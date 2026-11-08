/// Loads a compiled Rocq project into typed per-file models and project-wide symbol and reference tables.
module FSharp.Data.Rocq.Project

open System
open System.Collections.Generic
open System.IO
open FSharp.Data.Rocq.Source
open FSharp.Data.Rocq.Glob
open FSharp.Data.Rocq.Structure

type ProjectFile =
    {
        /// Path relative to the `_CoqProject` directory, with forward slashes.
        Path: string
        FullPath: string
        /// Logical module derived from the `-Q` and `-R` mappings.
        Module: string
    }

/// How a `.glob` relates to its source. Only `Current` files carry `.glob` facts.
type GlobStatus =
    /// The digest recorded in the `.glob` matches the source.
    | Current
    /// The digest recorded in the `.glob` differs from the source.
    | Stale
    /// The `.glob` records `DIGEST NO`.
    | NoDigest

type LoadOptions =
    {
        /// Keeps lexical data for a file whose `.glob` is stale, instead of refusing.
        AllowStale: bool
    }

/// A reason a project cannot be loaded.
type LoadFailure =
    | ProjectFileMissing of project: string
    | SourceMissing of file: string
    | SourceNotUtf8 of file: string
    | GlobMissing of file: string
    | GlobStaleForSource of file: string * recorded: string * actual: string
    | GlobHasNoDigest of file: string
    | ModuleMismatch of file: string * glob: string * expected: string

/// One source file with its digests and model.
type RocqFile =
    {
        Path: string
        FullPath: string
        Module: string
        /// MD5 of the source, in lowercase hexadecimal.
        Digest: string
        /// MD5 of the `.glob`, in lowercase hexadecimal.
        GlobDigest: string
        GlobStatus: GlobStatus
        /// The parsed `.glob` of a `Current` file.
        Glob: GlobFile option
        Model: FileModel
    }

type ProjectDeclaration =
    {
        /// Index into `RocqProject.Files`.
        File: int
        /// Index into the file's `Model.Declarations`.
        Local: int
        Declaration: Declaration
    }

type ProjectSymbol =
    {
        /// Index into `RocqProject.Files`.
        File: int
        /// Index into the file's `Model.Symbols`.
        Local: int
        /// Index into `RocqProject.Declarations`.
        Declaration: int option
        Symbol: Symbol
    }

/// A reference target outside `RocqProject.Symbols`, such as a name from the standard library.
type ExternalNode =
    {
        Module: string
        /// The name relative to `Module`.
        Qualified: string
        Kind: string
    }

/// The target of a reference.
[<Struct>]
type Node =
    /// An index into `RocqProject.Symbols`.
    | SymbolNode of symbol: int
    /// An index into `CrossReferences.Externals`.
    | ExternalSymbolNode of externalSymbol: int

/// The source of a reference.
[<Struct>]
type Owner =
    /// An index into `RocqProject.Declarations`.
    | DeclarationOwner of declaration: int
    /// An index into `RocqProject.Files`, for a directive outside every declaration.
    | FileOwner of file: int

/// Aggregated mentions of a target by an owner at one location.
[<Struct>]
type Edge =
    {
        Target: Node
        Owner: Owner
        /// 0 statement, 1 body, 2 proof, 3 top-level directive.
        Where: int
        Count: int
    }

/// The reference graph. `Edges` is sorted by target, with lookup tables in both directions.
type CrossReferences =
    {
        /// Sorted by module, name and kind.
        Externals: ExternalNode[]
        Edges: Edge[]
        /// `Edges[ToOffsets[n] .. ToOffsets[n + 1] - 1]` target node `n`. Symbols number first, then externals.
        ToOffsets: int[]
        /// Indexes into `Edges`, grouped by owner.
        ByFrom: int[]
        /// `ByFrom[FromOffsets[n] .. FromOffsets[n + 1] - 1]` belong to owner `n`. Declarations number first, then files.
        FromOffsets: int[]
    }

type FileReference =
    {
        /// Index into `RocqProject.Files`.
        From: int
        /// Index into `RocqProject.Files`.
        To: int
        Count: int
    }

type ExternalFileReference =
    {
        /// Index into `RocqProject.Files`.
        From: int
        Module: string
        Count: int
    }

/// Dependencies between files, as recorded in `.glob` files. Every array is sorted.
type FileGraph =
    {
        /// Required files, as pairs of indexes into `RocqProject.Files`.
        Requires: (int * int)[]
        /// Required modules outside the project, as an index into `RocqProject.Files` and a module.
        ExternalRequires: (int * string)[]
        References: FileReference[]
        ExternalReferences: ExternalFileReference[]
    }

/// A reference into a project file whose target has no matching symbol.
type UnresolvedTarget =
    {
        File: string
        Target: string
        Kind: string
    }

type RocqProject =
    {
        Files: RocqFile[]
        Declarations: ProjectDeclaration[]
        Symbols: ProjectSymbol[]
        CrossReferences: CrossReferences
        FileGraph: FileGraph
        /// Lines per `.glob` kind outside `Glob.knownKinds`.
        UnknownKinds: Map<string, int>
        /// Sorted by file, target and kind.
        Unresolved: UnresolvedTarget[]
    }

/// A sentence describing the failure and how to fix it.
let describeFailure (failure: LoadFailure) =
    match failure with
    | ProjectFileMissing project -> $"{project}: project file is missing"
    | SourceMissing file -> $"{file}: source file is missing"
    | SourceNotUtf8 file -> $"{file}: source is not valid UTF-8"
    | GlobMissing file -> $"{file}: no .glob file; compile the project first"
    | GlobStaleForSource (file, recorded, actual) ->
        $"{file}: stale .glob: it records digest {recorded}, and the source has {actual}. Recompile, or pass --allow-stale for lexical data only"
    | GlobHasNoDigest file -> $"{file}: .glob has no digest (DIGEST NO), so it cannot be matched to the source"
    | ModuleMismatch (file, glob, expected) -> $"{file}: .glob names module {glob}, but _CoqProject maps it to {expected}"

/// Reads `-Q`, `-R` and `.v` entries from a `_CoqProject` file.
let readProject (projectFile: string) : ProjectFile[] =
    let dir = Path.GetDirectoryName (Path.GetFullPath projectFile)

    let words =
        File.ReadAllText(projectFile).Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)

    let mappings = ResizeArray<string * string>()
    let files = ResizeArray<string>()
    let mutable i = 0

    while i < words.Length do
        match words[i] with
        | "-Q"
        | "-R" when i + 2 < words.Length ->
            mappings.Add (words[i + 1].TrimEnd('/').TrimStart('.', '/'), words[i + 2])
            i <- i + 3
        | "-I"
        | "-arg" -> i <- i + 2
        | w ->
            if w.EndsWith (".v", StringComparison.Ordinal) then
                files.Add (w.Replace ('\\', '/'))

            i <- i + 1

    let moduleOf (path: string) =
        let stem = path.Substring (0, path.Length - 2)

        mappings
        |> Seq.sortByDescending (fun (p, _) -> p.Length)
        |> Seq.tryPick (fun (physical, logical) ->
            if physical = "" then
                Some (logical + "." + stem.Replace ('/', '.'))
            elif stem.StartsWith (physical + "/", StringComparison.Ordinal) then
                Some (
                    logical
                    + "."
                    + stem.Substring(physical.Length + 1).Replace('/', '.')
                )
            else
                None)
        |> Option.defaultValue (stem.Replace ('/', '.'))

    files
    |> Seq.map (fun f ->
        {
            ProjectFile.Path = f
            FullPath = Path.Combine (dir, f)
            Module = moduleOf f
        })
    |> Seq.toArray

/// The targets of a file's `Uses`, numbered by first use, and each use's index into them. A target naming a symbol
/// under definition carries that symbol's path and name, so it resolves across files.
let useTargets (m: FileModel) : Target[] * int[] =
    let targets = List<Target>()
    let remap = Dictionary<int, int>()

    let column =
        m.Uses
        |> Array.map (fun u ->
            match remap.TryGetValue u.Target with
            | true, i -> i
            | _ ->
                let i = targets.Count
                let t = m.Targets[u.Target]

                targets.Add (
                    if t.Symbol >= 0 then
                        { t with
                            Path = m.Symbols[t.Symbol].Path
                            Name = m.Symbols[t.Symbol].Name
                        }
                    else
                        t
                )

                remap[u.Target] <- i
                i)

    targets.ToArray (), column

/// The libraries a file requires, sorted.
let requiredModules (m: FileModel) : string[] =
    m.Refs
    |> Seq.map (fun r -> m.Targets[r.Target])
    |> Seq.filter (fun t -> t.Kind = "lib")
    |> Seq.map _.Module
    |> Seq.distinct
    |> Seq.sort
    |> Seq.toArray

/// The count of references into each module, sorted by module.
let referencedModules (m: FileModel) : (string * int)[] =
    let counts = SortedDictionary<string, int>(StringComparer.Ordinal)

    for r in m.Refs do
        let t = m.Targets[r.Target]

        if t.Kind <> "lib" then
            counts[t.Module] <-
                (match counts.TryGetValue t.Module with
                 | true, c -> c + 1
                 | _ -> 1)

    [| for KeyValue (name, count) in counts -> name, count |]

let private qualifyTarget (path: string) (name: string) =
    match path, name with
    | "<>", n -> n
    | p, "<>" -> p
    | p, n -> p + "." + n

/// Builds the project-wide tables. File order determines numbering, and external nodes sort by name.
let private build (files: RocqFile[]) : RocqProject =
    let moduleToFile = Dictionary<string, int>()

    files
    |> Array.iteri (fun i f -> moduleToFile[f.Module] <- i)

    let declOffset = Array.zeroCreate<int> files.Length
    let declarations = List<ProjectDeclaration>()
    let symbols = List<ProjectSymbol>()
    let symbolIndex = Dictionary<string, int>()

    for fi in 0 .. files.Length - 1 do
        let f = files[fi]
        declOffset[fi] <- declarations.Count

        f.Model.Declarations
        |> Array.iteri (fun local d ->
            declarations.Add
                {
                    File = fi
                    Local = local
                    Declaration = d
                })

        f.Model.Symbols
        |> Array.iteri (fun local s ->
            let key =
                namespaceOf s.Kind
                + "|"
                + f.Module
                + "#"
                + s.Qualified

            if not (symbolIndex.ContainsKey key) then
                symbolIndex[key] <- symbols.Count

            symbols.Add
                {
                    File = fi
                    Local = local
                    Declaration = if s.Decl >= 0 then Some (declOffset[fi] + s.Decl) else None
                    Symbol = s
                })

    let nSymbols = symbols.Count
    let nDecls = declarations.Count

    // Nodes number symbols first, then externals. A target outside the symbol table becomes an external node.
    let externals = Dictionary<string, int>()
    let externalNodes = List<ExternalNode>()
    let edges = List<struct (int * int * int * int)>() // to, from, where, count
    let unresolved = List<string * string * string>()

    for fi in 0 .. files.Length - 1 do
        let f = files[fi]
        let targets, targetColumn = useTargets f.Model

        let nodes =
            targets
            |> Array.map (fun t ->
                let qualified = qualifyTarget t.Path t.Name

                let key =
                    namespaceOf t.Kind
                    + "|"
                    + t.Module
                    + "#"
                    + qualified

                match symbolIndex.TryGetValue key with
                | true, s -> s
                | _ ->
                    // `Import` of a whole library names the file itself, which has no symbol.
                    if
                        moduleToFile.ContainsKey t.Module
                        && qualified <> "<>"
                    then
                        unresolved.Add ((f.Path, t.Module + "#" + qualified, t.Kind))

                    let exKey = key + "|" + t.Kind

                    match externals.TryGetValue exKey with
                    | true, e -> nSymbols + e
                    | _ ->
                        let e = externalNodes.Count
                        externals[exKey] <- e

                        externalNodes.Add
                            {
                                Module = t.Module
                                Qualified = qualified
                                Kind = t.Kind
                            }

                        nSymbols + e)

        f.Model.Uses
        |> Array.iteri (fun i u ->
            let from = if u.Owner >= 0 then declOffset[fi] + u.Owner else ~~~fi
            edges.Add (struct (nodes[targetColumn[i]], from, u.Where, u.Count)))

    let externalOrder =
        [| 0 .. externalNodes.Count - 1 |]
        |> Array.sortBy (fun i -> externalNodes[i].Module, externalNodes[i].Qualified, externalNodes[i].Kind)

    let externalRank = Array.zeroCreate<int> externalNodes.Count

    externalOrder
    |> Array.iteri (fun rank old -> externalRank[old] <- rank)

    let sorted =
        edges.ToArray ()
        |> Array.map (fun (struct (t, a, b, c)) ->
            struct ((if t >= nSymbols then
                         nSymbols + externalRank[t - nSymbols]
                     else
                         t),
                    a,
                    b,
                    c))
        |> Array.sort

    let nNodes = nSymbols + externalNodes.Count
    let toOffsets = Array.zeroCreate<int>(nNodes + 1)

    for struct (t, _, _, _) in sorted do
        toOffsets[t + 1] <- toOffsets[t + 1] + 1

    for i in 0 .. nNodes - 1 do
        toOffsets[i + 1] <- toOffsets[i + 1] + toOffsets[i]

    let ownerSlot (from: int) =
        if from >= 0 then from else nDecls + ~~~from

    let nOwners = nDecls + files.Length

    let byFrom =
        [| 0 .. sorted.Length - 1 |]
        |> Array.sortBy (fun e ->
            let struct (_, from, _, _) = sorted[e]
            struct (ownerSlot from, e))

    let fromOffsets = Array.zeroCreate<int>(nOwners + 1)

    for struct (_, from, _, _) in sorted do
        let slot = ownerSlot from
        fromOffsets[slot + 1] <- fromOffsets[slot + 1] + 1

    for i in 0 .. nOwners - 1 do
        fromOffsets[i + 1] <- fromOffsets[i + 1] + fromOffsets[i]

    let requires = List<int * int>()
    let externalRequires = List<int * string>()
    let references = List<FileReference>()
    let externalReferences = List<ExternalFileReference>()

    for fi in 0 .. files.Length - 1 do
        let m = files[fi].Model

        for name in requiredModules m do
            match moduleToFile.TryGetValue name with
            | true, t -> requires.Add ((fi, t))
            | _ -> externalRequires.Add ((fi, name))

        for (name, count) in referencedModules m do
            match moduleToFile.TryGetValue name with
            | true, t -> references.Add { From = fi; To = t; Count = count }
            | _ ->
                externalReferences.Add
                    {
                        From = fi
                        Module = name
                        Count = count
                    }

    {
        Files = files
        Declarations = declarations.ToArray ()
        Symbols = symbols.ToArray ()
        CrossReferences =
            {
                Externals =
                    externalOrder
                    |> Array.map (fun i -> externalNodes[i])
                Edges =
                    sorted
                    |> Array.map (fun (struct (t, from, where, count)) ->
                        {
                            Target =
                                if t >= nSymbols then
                                    ExternalSymbolNode (t - nSymbols)
                                else
                                    SymbolNode t
                            Owner =
                                if from >= 0 then
                                    DeclarationOwner from
                                else
                                    FileOwner (~~~from)
                            Where = where
                            Count = count
                        })
                ToOffsets = toOffsets
                ByFrom = byFrom
                FromOffsets = fromOffsets
            }
        FileGraph =
            {
                Requires =
                    requires.ToArray ()
                    |> Array.distinct
                    |> Array.sort
                ExternalRequires =
                    externalRequires.ToArray ()
                    |> Array.distinct
                    |> Array.sort
                References =
                    references.ToArray ()
                    |> Array.sortBy (fun r -> r.From, r.To)
                ExternalReferences =
                    externalReferences.ToArray ()
                    |> Array.sortBy (fun r -> r.From, r.Module)
            }
        UnknownKinds =
            files
            |> Seq.collect (fun f ->
                match f.Glob with
                | Some g -> g.UnknownKinds |> Seq.map snd
                | None -> Seq.empty)
            |> Seq.countBy id
            |> Map.ofSeq
        Unresolved =
            unresolved
            |> Seq.distinct
            |> Seq.sort
            |> Seq.map (fun (file, target, kind) ->
                {
                    File = file
                    Target = target
                    Kind = kind
                })
            |> Seq.toArray
    }

let private loadFile (options: LoadOptions) (f: ProjectFile) : Result<RocqFile, LoadFailure> =
    let globPath = Path.ChangeExtension (f.FullPath, ".glob")

    if not (File.Exists f.FullPath) then
        Result.Error (SourceMissing f.Path)
    else
        let source = File.ReadAllBytes f.FullPath

        if not (isValidUtf8 source) then
            Result.Error (SourceNotUtf8 f.Path)
        elif not (File.Exists globPath) then
            Result.Error (GlobMissing f.Path)
        else
            let digest = md5Hex source
            let globBytes = File.ReadAllBytes globPath
            let parsed = parse globBytes

            let status =
                match parsed.Digest with
                | None -> NoDigest
                | Some d when d <> digest -> Stale
                | Some _ -> Current

            let loaded () =
                let glob = if status = Current then Some parsed else None

                Result.Ok
                    {
                        Path = f.Path
                        FullPath = f.FullPath
                        Module = f.Module
                        Digest = digest
                        GlobDigest = md5Hex globBytes
                        GlobStatus = status
                        Glob = glob
                        Model = analyze f.Module source glob
                    }

            match status with
            | Current when parsed.Module <> f.Module -> Result.Error (ModuleMismatch (f.Path, parsed.Module, f.Module))
            | Current -> loaded ()
            | _ when options.AllowStale -> loaded ()
            | Stale -> Result.Error (GlobStaleForSource (f.Path, parsed.Digest.Value, digest))
            | NoDigest -> Result.Error (GlobHasNoDigest f.Path)

/// Loads a compiled project from its `_CoqProject`, reading each `.v` and `.glob` it lists. Returns every failure when
/// a file cannot be trusted.
let loadWith (options: LoadOptions) (projectFile: string) : Result<RocqProject, LoadFailure[]> =
    if not (File.Exists projectFile) then
        Result.Error [| ProjectFileMissing projectFile |]
    else
        let results =
            readProject projectFile
            |> Array.Parallel.map (loadFile options)

        let failures =
            results
            |> Array.choose (function
                | Result.Error e -> Some e
                | Result.Ok _ -> None)

        if failures.Length > 0 then
            Result.Error failures
        else
            results
            |> Array.choose (function
                | Result.Ok f -> Some f
                | Result.Error _ -> None)
            |> build
            |> Result.Ok

/// Loads a compiled project from its `_CoqProject`, refusing any file with a stale `.glob`.
let load (projectFile: string) : Result<RocqProject, LoadFailure[]> =
    loadWith { AllowStale = false } projectFile

/// Symbols whose name, module-relative qualified name, or fully qualified name equals `name`.
let symbolsNamed (project: RocqProject) (name: string) : ProjectSymbol[] =
    project.Symbols
    |> Array.filter (fun s ->
        s.Symbol.Name = name
        || s.Symbol.Qualified = name
        || project.Files[s.File].Module
           + "."
           + s.Symbol.Qualified
            =
            name)

/// Index of a symbol's node, for `usesOf`.
let symbolNode (project: RocqProject) (symbol: ProjectSymbol) : Node =
    let first =
        project.Symbols
        |> Array.findIndex (fun s -> s.File = symbol.File && s.Local = symbol.Local)

    SymbolNode first

/// The edges targeting a node.
let usesOf (project: RocqProject) (node: Node) : Edge[] =
    let x = project.CrossReferences

    let id =
        match node with
        | SymbolNode s -> s
        | ExternalSymbolNode e -> project.Symbols.Length + e

    x.Edges[x.ToOffsets[id] .. x.ToOffsets[id + 1] - 1]

/// The edges from an owner.
let usesBy (project: RocqProject) (owner: Owner) : Edge[] =
    let x = project.CrossReferences

    let slot =
        match owner with
        | DeclarationOwner d -> d
        | FileOwner f -> project.Declarations.Length + f

    [|
        for i in x.FromOffsets[slot] .. x.FromOffsets[slot + 1] - 1 -> x.Edges[x.ByFrom[i]]
    |]
