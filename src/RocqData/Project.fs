/// Project discovery, digest checks, incremental extraction, and the cross-file index and graph.
module RocqData.Project

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open RocqData.Source
open RocqData.Glob
open RocqData.Structure
open RocqData.Shard

type Options =
    {
        /// Path of the `_CoqProject` file.
        Project: string
        OutDir: string
        /// Output of `rocq dep -f _CoqProject`, when captured.
        RocqDep: string option
        RocqVersion: string option
        /// Keeps lexical data for a file whose `.glob` is stale, instead of refusing.
        AllowStale: bool
        /// Fails on an unknown `.glob` kind as well as on an unparsed line.
        Strict: bool
        /// Re-extracts every file.
        Force: bool
    }

type ProjectFile =
    {
        /// Path relative to the `_CoqProject` directory, with forward slashes.
        Path: string
        FullPath: string
        /// Logical module derived from the `-Q` and `-R` mappings.
        Module: string
    }

type RunReport =
    {
        Extracted: string[]
        Reused: string[]
        Refused: string[]
        UnknownKinds: Map<string, int>
        UnparsedLines: int
        ExtractMs: int64
        GlobalMs: int64
        TotalMs: int64
        ExitCode: int
    }

/// Reads `-Q`, `-R` and `.v` entries from a `_CoqProject` file.
let readProject (projectFile: string) =
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
            if w.EndsWith ".v" then
                files.Add (w.Replace ('\\', '/'))

            i <- i + 1

    let moduleOf (path: string) =
        let stem = path.Substring (0, path.Length - 2)

        mappings
        |> Seq.sortByDescending (fun (p, _) -> p.Length)
        |> Seq.tryPick (fun (physical, logical) ->
            if physical = "" then
                Some (logical + "." + stem.Replace ('/', '.'))
            elif stem.StartsWith (physical + "/") then
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
            Path = f
            FullPath = Path.Combine (dir, f)
            Module = moduleOf f
        })
    |> Seq.toArray

let private shardName (moduleName: string) (layer: string) =
    $"files/{moduleName}.{layer}.json"

/// Reads the in-project edges from `rocq dep` output: `a.vo a.glob ...: a.v b.vo ...`.
let private readRocqDep (path: string) (files: ProjectFile[]) =
    let byPath = Dictionary<string, int>()

    files
    |> Array.iteri (fun i f -> byPath[f.Path.Substring (0, f.Path.Length - 2)] <- i)

    [|
        for line in File.ReadAllLines path do
            let colon = line.IndexOf ':'

            if colon > 0 then
                let left =
                    line.Substring(0, colon).Split(' ', StringSplitOptions.RemoveEmptyEntries)

                if left.Length > 0 && left[0].EndsWith ".vo" then
                    match byPath.TryGetValue (left[0].Substring(0, left[0].Length - 3)) with
                    | true, from ->
                        for word in line.Substring(colon + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries) do
                            if word.EndsWith ".vo" then
                                match byPath.TryGetValue (word.Substring (0, word.Length - 3)) with
                                | true, target -> yield struct (from, target)
                                | _ -> ()
                    | _ -> ()
    |]
    |> Array.distinct
    |> Array.sort

/// One file as `index.json` lists it. Read back from an earlier run to decide what can be reused.
type private PreviousEntry =
    {
        Digest: string
        GlobDigest: string
        GlobStatus: string
    }

let private readPreviousIndex (indexPath: string) =
    let result = Dictionary<string, PreviousEntry>()

    if File.Exists indexPath then
        try
            use doc = JsonDocument.Parse (File.ReadAllBytes indexPath)
            let root = doc.RootElement
            let schema = root.GetProperty "schema"
            let generator = root.GetProperty "generator"

            if
                schema.GetProperty("major").GetInt32() = SchemaMajor
                && schema.GetProperty("minor").GetInt32() = SchemaMinor
                && generator.GetProperty("version").GetString() = GeneratorVersion
            then
                for f in root.GetProperty("files").EnumerateArray() do
                    let g = f.GetProperty "globDigest"

                    result[f.GetProperty("path").GetString()] <-
                        {
                            Digest = f.GetProperty("digest").GetString()
                            GlobDigest =
                                if g.ValueKind = JsonValueKind.Null then
                                    null
                                else
                                    g.GetString ()
                            GlobStatus = f.GetProperty("globStatus").GetString()
                        }
        with
        | :? JsonException
        | :? KeyNotFoundException -> result.Clear ()

    result

type private Prepared =
    {
        File: ProjectFile
        Source: byte[]
        Digest: string
        GlobBytes: byte[]
        GlobDigest: string
        Glob: GlobFile option
        Status: string
    }

let private prepare (options: Options) (f: ProjectFile) : Result<Prepared, string> =
    if not (File.Exists f.FullPath) then
        Result.Error $"{f.Path}: source file is missing"
    else
        let source = File.ReadAllBytes f.FullPath
        let digest = md5Hex source
        let globPath = Path.ChangeExtension (f.FullPath, ".glob")

        if not (isValidUtf8 source) then
            Result.Error $"{f.Path}: source is not valid UTF-8"
        elif not (File.Exists globPath) then
            Result.Error $"{f.Path}: no .glob file; compile the project first"
        else
            let globBytes = File.ReadAllBytes globPath
            let glob = parse globBytes

            let status =
                match glob.Digest with
                | None -> "noDigest"
                | Some d when d <> digest -> "stale"
                | Some _ -> "ok"

            let prepared =
                {
                    File = f
                    Source = source
                    Digest = digest
                    GlobBytes = globBytes
                    GlobDigest = md5Hex globBytes
                    Glob = if status = "ok" then Some glob else None
                    Status = status
                }

            match status with
            | "ok" when glob.Module <> f.Module -> Result.Error $"{f.Path}: .glob names module {glob.Module}, but _CoqProject maps it to {f.Module}"
            | "ok" -> Result.Ok prepared
            | _ when options.AllowStale -> Result.Ok prepared
            | "stale" ->
                Result.Error
                    $"{f.Path}: stale .glob: it records digest {glob.Digest.Value}, and the source has {digest}. Recompile, or pass --allow-stale for lexical data only"
            | _ -> Result.Error $"{f.Path}: .glob has no digest (DIGEST NO), so it cannot be matched to the source"

/// A file's cross-file facts, read from its outline layer whether the file was re-extracted or reused.
type private OutlineFacts =
    {
        Path: string
        Module: string
        Digest: string
        GlobDigest: string
        GlobStatus: string
        Root: JsonElement
        Document: JsonDocument
    }

let private ints (e: JsonElement) (name: string) =
    let a = e.GetProperty name
    let result = Array.zeroCreate<int>(a.GetArrayLength ())
    let mutable i = 0

    for v in a.EnumerateArray () do
        result[i] <- v.GetInt32 ()
        i <- i + 1

    result

let private strings (e: JsonElement) (name: string) =
    [| for v in (e.GetProperty name).EnumerateArray() -> v.GetString () |]

let private qualifyTarget (path: string) (name: string) =
    match path, name with
    | "<>", n -> n
    | p, "<>" -> p
    | p, n -> p + "." + n

let private countOf (e: JsonElement) (name: string) =
    (e.GetProperty name).GetArrayLength()

let private fileSize (path: string) =
    if File.Exists path then FileInfo(path).Length else 0L

/// Writes `index.json` and `xref.json` from the outline layers.
let private writeGlobal (options: Options) (facts: OutlineFacts[]) =
    let moduleToFile = Dictionary<string, int>()

    facts
    |> Array.iteri (fun i f -> moduleToFile[f.Module] <- i)

    // Declarations and symbols, numbered across the project.
    let declOffset = Array.zeroCreate<int> facts.Length
    let symbolOffset = Array.zeroCreate<int> facts.Length
    let declId = List<string>()
    let declName = List<string>()
    let declKeyword = List<string>()
    let declCategory = List<string>()
    let declFile = List<int>()
    let declLocal = List<int>()
    let declStart = List<int>()
    let declEnd = List<int>()
    let declEnding = List<string>()
    let declDocs = List<int>()

    let symQualified = List<string>()
    let symKind = List<string>()
    let symFile = List<int>()
    let symDecl = List<int>()
    let symLocal = List<int>()
    let symbolIndex = Dictionary<string, int>()

    for fi in 0 .. facts.Length - 1 do
        let f = facts[fi]
        declOffset[fi] <- declId.Count
        symbolOffset[fi] <- symQualified.Count
        let mutable local = 0

        for d in (f.Root.GetProperty "declarations").EnumerateArray() do
            declId.Add (d.GetProperty("id").GetString())
            declName.Add (d.GetProperty("name").GetString())
            declKeyword.Add (d.GetProperty("keyword").GetString())
            declCategory.Add (d.GetProperty("category").GetString())
            declFile.Add fi
            declLocal.Add local
            let extent = d.GetProperty "extent"
            declStart.Add (extent[0].GetInt32())
            declEnd.Add (extent[1].GetInt32())
            let proofs = d.GetProperty "proofs"

            declEnding.Add (
                if proofs.GetArrayLength () = 0 then
                    null
                else
                    proofs[proofs.GetArrayLength () - 1].GetProperty("ending").GetString()
            )

            declDocs.Add ((d.GetProperty "docs").GetArrayLength())
            local <- local + 1

        let symbols = f.Root.GetProperty "symbols"
        let qualified = strings symbols "qualified"
        let kinds = strings symbols "kind"
        let decls = ints symbols "decl"

        for i in 0 .. qualified.Length - 1 do
            let key =
                namespaceOf kinds[i]
                + "|"
                + f.Module
                + "#"
                + qualified[i]

            if not (symbolIndex.ContainsKey key) then
                symbolIndex[key] <- symQualified.Count

            symQualified.Add qualified[i]
            symKind.Add kinds[i]
            symFile.Add fi
            symLocal.Add i
            symDecl.Add (if decls[i] >= 0 then declOffset[fi] + decls[i] else -1)

    let nSymbols = symQualified.Count
    let nDecls = declId.Count

    // Edges. A target outside the symbol table becomes an external node.
    let externals = Dictionary<string, int>()
    let extModule = List<string>()
    let extQualified = List<string>()
    let extKind = List<string>()
    let edges = List<struct (int * int * int * int)>() // to, from, where, count
    let unresolved = List<string * string * string>()

    for fi in 0 .. facts.Length - 1 do
        let f = facts[fi]
        let uses = f.Root.GetProperty "uses"
        let targets = uses.GetProperty "targets"
        let tModule = strings targets "module"
        let tPath = strings targets "path"
        let tName = strings targets "name"
        let tKind = strings targets "kind"

        let nodes =
            Array.init tModule.Length (fun i ->
                let qualified = qualifyTarget tPath[i] tName[i]

                let key =
                    namespaceOf tKind[i]
                    + "|"
                    + tModule[i]
                    + "#"
                    + qualified

                match symbolIndex.TryGetValue key with
                | true, s -> s
                | _ ->
                    // `Import` of a whole library names the file itself, which has no symbol.
                    if
                        moduleToFile.ContainsKey tModule[i]
                        && qualified <> "<>"
                    then
                        unresolved.Add ((f.Path, tModule[i] + "#" + qualified, tKind[i]))

                    let exKey = key + "|" + tKind[i]

                    match externals.TryGetValue exKey with
                    | true, e -> nSymbols + e
                    | _ ->
                        let e = extModule.Count
                        externals[exKey] <- e
                        extModule.Add tModule[i]
                        extQualified.Add qualified
                        extKind.Add tKind[i]
                        nSymbols + e)

        let owner = ints uses "owner"
        let target = ints uses "target"
        let where = ints uses "where"
        let count = ints uses "count"

        for i in 0 .. owner.Length - 1 do
            let from = if owner[i] >= 0 then declOffset[fi] + owner[i] else ~~~fi
            edges.Add (struct (nodes[target[i]], from, where[i], count[i]))

    // External ids sort by name, so the output is independent of file order.
    let externalOrder =
        [| 0 .. extModule.Count - 1 |]
        |> Array.sortBy (fun i -> extModule[i], extQualified[i], extKind[i])

    let externalRank = Array.zeroCreate<int> extModule.Count

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

    let nNodes = nSymbols + extModule.Count
    let toOffsets = Array.zeroCreate<int>(nNodes + 1)

    for struct (t, _, _, _) in sorted do
        toOffsets[t + 1] <- toOffsets[t + 1] + 1

    for i in 0 .. nNodes - 1 do
        toOffsets[i + 1] <- toOffsets[i + 1] + toOffsets[i]

    // Forward lookup: owners numbered declarations first, then one slot per file for top-level directives.
    let ownerSlot (from: int) =
        if from >= 0 then from else nDecls + ~~~from

    let nOwners = nDecls + facts.Length

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

    writeJsonFile (Path.Combine (options.OutDir, "xref.json")) (fun w ->
        writeSchema w "xref"
        w.WriteNumber ("symbolCount", nSymbols)
        w.WriteNumber ("declarationCount", nDecls)
        w.WriteNumber ("fileCount", facts.Length)
        w.WriteStartObject "externals"
        writeStrings w "module" (externalOrder |> Seq.map (fun i -> extModule[i]))

        writeStrings
            w
            "qualified"
            (externalOrder
             |> Seq.map (fun i -> extQualified[i]))

        writeStrings w "kind" (externalOrder |> Seq.map (fun i -> extKind[i]))
        w.WriteEndObject ()
        w.WriteStartObject "edges"
        writeInts w "to" (sorted |> Seq.map (fun (struct (t, _, _, _)) -> t))
        writeInts w "from" (sorted |> Seq.map (fun (struct (_, a, _, _)) -> a))
        writeInts w "where" (sorted |> Seq.map (fun (struct (_, _, b, _)) -> b))
        writeInts w "count" (sorted |> Seq.map (fun (struct (_, _, _, c)) -> c))
        w.WriteEndObject ()
        writeInts w "toOffsets" toOffsets
        writeInts w "byFrom" byFrom
        writeInts w "fromOffsets" fromOffsets)

    // File graph.
    let requires = SortedSet<struct (int * int)>()
    let externalRequires = SortedSet<struct (int * string)>()
    let references = SortedDictionary<struct (int * int), int>()
    let externalReferences = SortedDictionary<struct (int * string), int>()

    for fi in 0 .. facts.Length - 1 do
        for m in strings facts[fi].Root "requires" do
            match moduleToFile.TryGetValue m with
            | true, t -> requires.Add (struct (fi, t)) |> ignore
            | _ -> externalRequires.Add (struct (fi, m)) |> ignore

        let refd = facts[fi].Root.GetProperty "referencedModules"
        let names = strings refd "module"
        let counts = ints refd "count"

        for i in 0 .. names.Length - 1 do
            match moduleToFile.TryGetValue names[i] with
            | true, t -> references[struct (fi, t)] <- counts[i]
            | _ -> externalReferences[struct (fi, names[i])] <- counts[i]

    let projectFiles =
        facts
        |> Array.map (fun f ->
            {
                Path = f.Path
                FullPath = ""
                Module = f.Module
            })

    let rocqDep =
        options.RocqDep
        |> Option.map (fun p -> readRocqDep p projectFiles)

    let unknownKinds = SortedDictionary<string, int>(StringComparer.Ordinal)

    for f in facts do
        for u in (f.Root.GetProperty("globLines").GetProperty "unknownKinds").EnumerateArray() do
            let k = u.GetProperty("kind").GetString()

            unknownKinds[k] <-
                (match unknownKinds.TryGetValue k with
                 | true, c -> c + 1
                 | _ -> 1)

    writeJsonFile (Path.Combine (options.OutDir, "index.json")) (fun w ->
        writeSchema w "index"
        w.WriteStartObject "generator"
        w.WriteString ("name", "rocq-data")
        w.WriteString ("version", GeneratorVersion)
        w.WriteEndObject ()

        match options.RocqVersion with
        | Some v -> w.WriteString ("rocqVersion", v)
        | None -> w.WriteNull "rocqVersion"

        w.WriteStartArray "files"

        for f in facts do
            let root = f.Root
            let lines = root.GetProperty "globLines"

            let proofs =
                root.GetProperty("declarations").EnumerateArray()
                |> Seq.sumBy (fun d -> d.GetProperty("proofs").GetArrayLength())

            w.WriteStartObject ()
            w.WriteString ("path", f.Path)
            w.WriteString ("module", f.Module)
            w.WriteString ("digest", f.Digest)

            if isNull f.GlobDigest then
                w.WriteNull "globDigest"
            else
                w.WriteString ("globDigest", f.GlobDigest)

            w.WriteString ("globStatus", f.GlobStatus)
            w.WriteNumber ("bytes", root.GetProperty("bytes").GetInt32())
            w.WriteStartObject "shards"

            for layer in [ "outline"; "refs"; "tokens" ] do
                let name = shardName f.Module layer
                w.WriteStartObject layer
                w.WriteString ("path", name)
                w.WriteNumber ("bytes", fileSize (Path.Combine (options.OutDir, name)))
                w.WriteEndObject ()

            w.WriteEndObject ()
            w.WriteStartObject "counts"
            w.WriteNumber ("sentences", countOf (root.GetProperty "sentences") "start")
            w.WriteNumber ("comments", countOf root "comments")
            w.WriteNumber ("headings", countOf root "headings")
            w.WriteNumber ("declarations", countOf root "declarations")
            w.WriteNumber ("proofs", proofs)
            w.WriteNumber ("symbols", lines.GetProperty("symbols").GetInt32())
            w.WriteNumber ("refs", lines.GetProperty("refs").GetInt32())
            w.WriteNumber ("binders", lines.GetProperty("binders").GetInt32())
            w.WriteNumber ("globLines", lines.GetProperty("total").GetInt32())
            w.WriteNumber ("diagnostics", countOf root "diagnostics")
            w.WriteEndObject ()
            w.WriteEndObject ()

        w.WriteEndArray ()

        w.WriteStartObject "declarations"
        writeStrings w "id" declId
        writeStrings w "name" declName
        writeStrings w "keyword" declKeyword
        writeStrings w "category" declCategory
        writeInts w "file" declFile
        writeInts w "local" declLocal
        writeInts w "start" declStart
        writeInts w "end" declEnd
        writeStrings w "ending" declEnding
        writeInts w "docCount" declDocs
        w.WriteEndObject ()

        w.WriteStartObject "symbols"
        writeStrings w "qualified" symQualified
        writeStrings w "kind" symKind
        writeInts w "file" symFile
        writeInts w "local" symLocal
        writeInts w "decl" symDecl
        w.WriteEndObject ()

        w.WriteStartObject "fileGraph"
        w.WriteStartObject "requires"
        writeInts w "from" (requires |> Seq.map (fun (struct (a, _)) -> a))
        writeInts w "to" (requires |> Seq.map (fun (struct (_, b)) -> b))
        w.WriteEndObject ()
        w.WriteStartObject "externalRequires"

        writeInts
            w
            "from"
            (externalRequires
             |> Seq.map (fun (struct (a, _)) -> a))

        writeStrings
            w
            "module"
            (externalRequires
             |> Seq.map (fun (struct (_, b)) -> b))

        w.WriteEndObject ()
        w.WriteStartObject "references"

        writeInts
            w
            "from"
            (references.Keys
             |> Seq.map (fun (struct (a, _)) -> a))

        writeInts
            w
            "to"
            (references.Keys
             |> Seq.map (fun (struct (_, b)) -> b))

        writeInts w "count" references.Values
        w.WriteEndObject ()
        w.WriteStartObject "externalReferences"

        writeInts
            w
            "from"
            (externalReferences.Keys
             |> Seq.map (fun (struct (a, _)) -> a))

        writeStrings
            w
            "module"
            (externalReferences.Keys
             |> Seq.map (fun (struct (_, b)) -> b))

        writeInts w "count" externalReferences.Values
        w.WriteEndObject ()

        match rocqDep with
        | Some edges ->
            w.WriteStartObject "rocqDep"
            writeInts w "from" (edges |> Seq.map (fun (struct (a, _)) -> a))
            writeInts w "to" (edges |> Seq.map (fun (struct (_, b)) -> b))
            w.WriteEndObject ()
        | None -> w.WriteNull "rocqDep"

        w.WriteEndObject ()

        w.WriteStartObject "unknownKinds"

        for KeyValue (k, c) in unknownKinds do
            w.WriteNumber (k, c)

        w.WriteEndObject ()

        w.WriteStartArray "unresolved"

        for (file, target, kind) in unresolved |> Seq.distinct |> Seq.sort do
            w.WriteStartObject ()
            w.WriteString ("file", file)
            w.WriteString ("target", target)
            w.WriteString ("kind", kind)
            w.WriteEndObject ()

        w.WriteEndArray ())

/// Extracts a project. Refuses, with exit code 2 and no writes, when any file's `.glob` cannot be trusted.
let run (options: Options) : RunReport =
    let total = Stopwatch.StartNew ()
    let files = readProject options.Project

    let previous =
        if options.Force then
            Dictionary ()
        else
            readPreviousIndex (Path.Combine (options.OutDir, "index.json"))

    let prepared = files |> Array.Parallel.map (prepare options)

    let refused =
        prepared
        |> Array.choose (function
            | Result.Error e -> Some e
            | Result.Ok _ -> None)

    if refused.Length > 0 then
        {
            Extracted = [||]
            Reused = [||]
            Refused = refused
            UnknownKinds = Map.empty
            UnparsedLines = 0
            ExtractMs = 0L
            GlobalMs = 0L
            TotalMs = total.ElapsedMilliseconds
            ExitCode = 2
        }
    else
        let ready =
            prepared
            |> Array.map (function
                | Result.Ok p -> p
                | Result.Error _ -> failwith "unreachable")

        let isReusable (p: Prepared) =
            match previous.TryGetValue p.File.Path with
            | true, e ->
                e.Digest = p.Digest
                && e.GlobDigest = p.GlobDigest
                && e.GlobStatus = p.Status
                && [ "outline"; "refs"; "tokens" ]
                   |> List.forall (fun l -> File.Exists (Path.Combine (options.OutDir, shardName p.File.Module l)))
            | _ -> false

        let header (p: Prepared) : FileHeader =
            {
                Path = p.File.Path
                Module = p.File.Module
                Digest = p.Digest
                GlobDigest = p.GlobDigest
                GlobStatus = p.Status
            }

        let extract = Stopwatch.StartNew ()
        let toExtract = ready |> Array.filter (isReusable >> not)

        toExtract
        |> Array.Parallel.iter (fun p ->
            let model = analyze p.File.Module p.Source p.Glob
            let h = header p

            let path (layer: string) =
                Path.Combine (options.OutDir, shardName p.File.Module layer)

            writeOutline (path "outline") h model p.Glob
            writeRefs (path "refs") h model
            writeTokens (path "tokens") h model)

        extract.Stop ()

        // Shards of files no longer in the project.
        let filesDir = Path.Combine (options.OutDir, "files")

        if Directory.Exists filesDir then
            let current =
                HashSet<string>(
                    ready
                    |> Seq.collect (fun p ->
                        [ "outline"; "refs"; "tokens" ]
                        |> List.map (fun l -> Path.GetFileName (shardName p.File.Module l)))
                )

            for path in Directory.GetFiles (filesDir, "*.json") do
                if not (current.Contains (Path.GetFileName path)) then
                    File.Delete path

        let globalStage = Stopwatch.StartNew ()

        let facts =
            ready
            |> Array.Parallel.map (fun p ->
                let bytes =
                    File.ReadAllBytes (Path.Combine (options.OutDir, shardName p.File.Module "outline"))

                let doc = JsonDocument.Parse bytes

                {
                    Path = p.File.Path
                    Module = p.File.Module
                    Digest = p.Digest
                    GlobDigest = p.GlobDigest
                    GlobStatus = p.Status
                    Root = doc.RootElement
                    Document = doc
                })

        writeGlobal options facts

        let unknown =
            ready
            |> Seq.collect (fun p ->
                match p.Glob with
                | Some g -> g.UnknownKinds |> Seq.map snd
                | None -> Seq.empty)
            |> Seq.countBy id
            |> Map.ofSeq

        let unparsed =
            ready
            |> Seq.sumBy (fun p ->
                match p.Glob with
                | Some g -> g.Unparsed.Length
                | None -> 0)

        for f in facts do
            f.Document.Dispose ()

        globalStage.Stop ()

        {
            Extracted = toExtract |> Array.map _.File.Path
            Reused =
                ready
                |> Array.filter isReusable
                |> Array.map _.File.Path
            Refused = [||]
            UnknownKinds = unknown
            UnparsedLines = unparsed
            ExtractMs = extract.ElapsedMilliseconds
            GlobalMs = globalStage.ElapsedMilliseconds
            TotalMs = total.ElapsedMilliseconds
            ExitCode =
                if
                    unparsed > 0
                    || (options.Strict && not unknown.IsEmpty)
                then
                    3
                else
                    0
        }
