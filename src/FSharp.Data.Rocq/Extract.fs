/// Writes a project as JSON: `index.json`, `xref.json`, and three layers per file. `docs/SCHEMA.md` is the reference.
module FSharp.Data.Rocq.Extract

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text.Json
open FSharp.Data.Rocq.Structure
open FSharp.Data.Rocq.Project
open FSharp.Data.Rocq.Shard

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
        /// Rewrites every file's layers.
        Force: bool
    }

type RunReport =
    {
        /// Files whose layers were written.
        Extracted: string[]
        /// Files whose layers already matched their source and `.glob`.
        Reused: string[]
        Refused: string[]
        UnknownKinds: Map<string, int>
        UnparsedLines: int
        /// Loading and writing the per-file layers.
        ExtractMs: int64
        /// Writing `index.json` and `xref.json`.
        GlobalMs: int64
        TotalMs: int64
        ExitCode: int
    }

let private shardName (moduleName: string) (layer: string) =
    $"files/{moduleName}.{layer}.json"

/// Reads the in-project edges from `rocq dep` output: `a.vo a.glob ...: a.v b.vo ...`.
let private readRocqDep (path: string) (files: RocqFile[]) =
    let byPath = Dictionary<string, int>()

    files
    |> Array.iteri (fun i f -> byPath[f.Path.Substring (0, f.Path.Length - 2)] <- i)

    [|
        for line in File.ReadAllLines path do
            let colon = line.IndexOf ':'

            if colon > 0 then
                let left =
                    line.Substring(0, colon).Split(' ', StringSplitOptions.RemoveEmptyEntries)

                if
                    left.Length > 0
                    && left[0].EndsWith(".vo", StringComparison.Ordinal)
                then
                    match byPath.TryGetValue (left[0].Substring(0, left[0].Length - 3)) with
                    | true, from ->
                        for word in line.Substring(colon + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries) do
                            if word.EndsWith (".vo", StringComparison.Ordinal) then
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

let private fileSize (path: string) =
    if File.Exists path then FileInfo(path).Length else 0L

let private nodeId (project: RocqProject) (node: Node) =
    match node with
    | SymbolNode s -> s
    | ExternalSymbolNode e -> project.Symbols.Length + e

let private ownerId (owner: Owner) =
    match owner with
    | DeclarationOwner d -> d
    | FileOwner f -> ~~~f

let private writeXref (outDir: string) (project: RocqProject) =
    let x = project.CrossReferences

    writeJsonFile (Path.Combine (outDir, "xref.json")) (fun w ->
        writeSchema w "xref"
        w.WriteNumber ("symbolCount", project.Symbols.Length)
        w.WriteNumber ("declarationCount", project.Declarations.Length)
        w.WriteNumber ("fileCount", project.Files.Length)
        w.WriteStartObject "externals"
        writeStrings w "module" (x.Externals |> Seq.map _.Module)
        writeStrings w "qualified" (x.Externals |> Seq.map _.Qualified)
        writeStrings w "kind" (x.Externals |> Seq.map _.Kind)
        w.WriteEndObject ()
        w.WriteStartObject "edges"

        writeInts
            w
            "to"
            (x.Edges
             |> Seq.map (fun e -> nodeId project e.Target))

        writeInts w "from" (x.Edges |> Seq.map (fun e -> ownerId e.Owner))

        writeInts
            w
            "where"
            (x.Edges
             |> Seq.map (fun e -> useLocationCode e.Where))

        writeInts w "count" (x.Edges |> Seq.map _.Count)
        w.WriteEndObject ()
        writeInts w "toOffsets" x.ToOffsets
        writeInts w "byFrom" x.ByFrom
        writeInts w "fromOffsets" x.FromOffsets)

let private writeIndex (options: Options) (project: RocqProject) =
    let rocqDep =
        options.RocqDep
        |> Option.map (fun p -> readRocqDep p project.Files)

    let graph = project.FileGraph

    writeJsonFile (Path.Combine (options.OutDir, "index.json")) (fun w ->
        writeSchema w "index"
        w.WriteStartObject "generator"
        w.WriteString ("name", "FSharp.Data.Rocq")
        w.WriteString ("version", GeneratorVersion)
        w.WriteEndObject ()

        match options.RocqVersion with
        | Some v -> w.WriteString ("rocqVersion", v)
        | None -> w.WriteNull "rocqVersion"

        w.WriteStartArray "files"

        for f in project.Files do
            let m = f.Model

            w.WriteStartObject ()
            w.WriteString ("path", f.Path)
            w.WriteString ("module", f.Module)
            w.WriteString ("digest", f.Digest)
            w.WriteString ("globDigest", f.GlobDigest)
            w.WriteString ("globStatus", globStatusName f.GlobStatus)
            w.WriteNumber ("bytes", m.Bytes.Length)
            w.WriteStartObject "shards"

            for layer in [ "outline"; "refs"; "tokens" ] do
                let name = shardName f.Module layer
                w.WriteStartObject layer
                w.WriteString ("path", name)
                w.WriteNumber ("bytes", fileSize (Path.Combine (options.OutDir, name)))
                w.WriteEndObject ()

            w.WriteEndObject ()
            w.WriteStartObject "counts"
            w.WriteNumber ("sentences", m.Sentences.Length)
            w.WriteNumber ("comments", m.Comments.Length)
            w.WriteNumber ("headings", m.Headings.Length)
            w.WriteNumber ("declarations", m.Declarations.Length)
            w.WriteNumber ("proofs", m.Declarations |> Array.sumBy _.Proofs.Length)
            w.WriteNumber ("symbols", m.Symbols.Length)
            w.WriteNumber ("refs", m.Refs.Length)
            w.WriteNumber ("binders", m.Binders.Length)
            w.WriteNumber ("globLines", m.GlobLineCount)
            w.WriteNumber ("diagnostics", m.Diagnostics.Length)
            w.WriteEndObject ()
            w.WriteEndObject ()

        w.WriteEndArray ()

        let decls = project.Declarations

        w.WriteStartObject "declarations"
        writeStrings w "id" (decls |> Seq.map _.Declaration.Id)

        writeStrings
            w
            "name"
            (decls
             |> Seq.map (fun d -> Option.toObj d.Declaration.Name))

        writeStrings
            w
            "keyword"
            (decls
             |> Seq.map (fun d -> Option.toObj d.Declaration.Keyword))

        writeStrings
            w
            "category"
            (decls
             |> Seq.map (fun d -> categoryName d.Declaration.Category))

        writeInts w "file" (decls |> Seq.map _.File)
        writeInts w "local" (decls |> Seq.map _.Local)
        writeInts w "start" (decls |> Seq.map _.Declaration.Extent.Start)
        writeInts w "end" (decls |> Seq.map _.Declaration.Extent.End)

        writeStrings
            w
            "ending"
            (decls
             |> Seq.map (fun d ->
                 let proofs = d.Declaration.Proofs

                 if proofs.Length = 0 then
                     null
                 else
                     proofEndingName proofs[proofs.Length - 1].Ending))

        writeInts w "docCount" (decls |> Seq.map _.Declaration.Docs.Length)
        w.WriteEndObject ()

        let symbols = project.Symbols

        w.WriteStartObject "symbols"
        writeStrings w "qualified" (symbols |> Seq.map _.Symbol.Qualified)
        writeStrings w "kind" (symbols |> Seq.map _.Symbol.Kind)
        writeInts w "file" (symbols |> Seq.map _.File)
        writeInts w "local" (symbols |> Seq.map _.Local)

        writeInts
            w
            "decl"
            (symbols
             |> Seq.map (fun s -> s.Declaration |> Option.defaultValue -1))

        w.WriteEndObject ()

        w.WriteStartObject "fileGraph"
        w.WriteStartObject "requires"
        writeInts w "from" (graph.Requires |> Seq.map fst)
        writeInts w "to" (graph.Requires |> Seq.map snd)
        w.WriteEndObject ()
        w.WriteStartObject "externalRequires"
        writeInts w "from" (graph.ExternalRequires |> Seq.map fst)
        writeStrings w "module" (graph.ExternalRequires |> Seq.map snd)
        w.WriteEndObject ()
        w.WriteStartObject "references"
        writeInts w "from" (graph.References |> Seq.map _.From)
        writeInts w "to" (graph.References |> Seq.map _.To)
        writeInts w "count" (graph.References |> Seq.map _.Count)
        w.WriteEndObject ()
        w.WriteStartObject "externalReferences"
        writeInts w "from" (graph.ExternalReferences |> Seq.map _.From)
        writeStrings w "module" (graph.ExternalReferences |> Seq.map _.Module)
        writeInts w "count" (graph.ExternalReferences |> Seq.map _.Count)
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

        for KeyValue (k, c) in project.UnknownKinds do
            w.WriteNumber (k, c)

        w.WriteEndObject ()

        w.WriteStartArray "unresolved"

        for u in project.Unresolved do
            w.WriteStartObject ()
            w.WriteString ("file", u.File)
            w.WriteString ("target", u.Target)
            w.WriteString ("kind", u.Kind)
            w.WriteEndObject ()

        w.WriteEndArray ())

/// Writes a project's JSON. Refuses, with exit code 2 and no writes, when any file's `.glob` cannot be trusted. A file
/// whose layers already match its source and `.glob` keeps them unchanged.
let run (options: Options) : RunReport =
    let total = Stopwatch.StartNew ()

    let previous =
        if options.Force then
            Dictionary ()
        else
            readPreviousIndex (Path.Combine (options.OutDir, "index.json"))

    let extract = Stopwatch.StartNew ()

    match loadWith { AllowStale = options.AllowStale } options.Project with
    | Result.Error failures ->
        {
            Extracted = [||]
            Reused = [||]
            Refused = failures |> Array.map describeFailure
            UnknownKinds = Map.empty
            UnparsedLines = 0
            ExtractMs = 0L
            GlobalMs = 0L
            TotalMs = total.ElapsedMilliseconds
            ExitCode = 2
        }
    | Result.Ok project ->
        let isReusable (f: RocqFile) =
            match previous.TryGetValue f.Path with
            | true, e ->
                e.Digest = f.Digest
                && e.GlobDigest = f.GlobDigest
                && e.GlobStatus = globStatusName f.GlobStatus
                && [ "outline"; "refs"; "tokens" ]
                   |> List.forall (fun l -> File.Exists (Path.Combine (options.OutDir, shardName f.Module l)))
            | _ -> false

        let reused, toWrite = project.Files |> Array.partition isReusable

        toWrite
        |> Array.Parallel.iter (fun f ->
            let path (layer: string) =
                Path.Combine (options.OutDir, shardName f.Module layer)

            let header: FileHeader =
                {
                    Path = f.Path
                    Module = f.Module
                    Digest = f.Digest
                    GlobDigest = f.GlobDigest
                    GlobStatus = globStatusName f.GlobStatus
                }

            writeOutline (path "outline") header f.Model f.Glob
            writeRefs (path "refs") header f.Model
            writeTokens (path "tokens") header f.Model)

        extract.Stop ()

        // Shards of files no longer in the project.
        let filesDir = Path.Combine (options.OutDir, "files")

        if Directory.Exists filesDir then
            let current =
                HashSet<string>(
                    project.Files
                    |> Seq.collect (fun f ->
                        [ "outline"; "refs"; "tokens" ]
                        |> List.map (fun l -> Path.GetFileName (shardName f.Module l)))
                )

            for path in Directory.GetFiles (filesDir, "*.json") do
                if not (current.Contains (Path.GetFileName path)) then
                    File.Delete path

        let globalStage = Stopwatch.StartNew ()
        writeXref options.OutDir project
        writeIndex options project
        globalStage.Stop ()

        let unparsed =
            project.Files
            |> Array.sumBy (fun f ->
                match f.Glob with
                | Some g -> g.Unparsed.Length
                | None -> 0)

        {
            Extracted = toWrite |> Array.map _.Path
            Reused = reused |> Array.map _.Path
            Refused = [||]
            UnknownKinds = project.UnknownKinds
            UnparsedLines = unparsed
            ExtractMs = extract.ElapsedMilliseconds
            GlobalMs = globalStage.ElapsedMilliseconds
            TotalMs = total.ElapsedMilliseconds
            ExitCode =
                if
                    unparsed > 0
                    || (options.Strict && not project.UnknownKinds.IsEmpty)
                then
                    3
                else
                    0
        }
