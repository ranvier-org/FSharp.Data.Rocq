/// A sample consumer. It reads the output with `System.Text.Json` alone and shares no code with the extractor.
///
///   rocq-data-sample who-uses <out> <name>    who mentions a symbol, grouped by where
///   rocq-data-sample show <out> <name>        a declaration's keyword, docs, statement and proof, sliced from the source
///   rocq-data-sample measure <out>            load and query times
///   rocq-data-sample formats <out>            size and read time of candidate formats
module RocqData.Sample.Program

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.IO.Compression
open System.Text
open System.Text.Json

let private parse (path: string) =
    JsonDocument.Parse (File.ReadAllBytes path)

let private ints (e: JsonElement) (name: string) =
    let a = e.GetProperty name
    let result = Array.zeroCreate<int>(a.GetArrayLength ())
    let mutable i = 0

    for v in a.EnumerateArray () do
        result[i] <- v.GetInt32 ()
        i <- i + 1

    result

let private strs (e: JsonElement) (name: string) =
    let a = e.GetProperty name
    let result = Array.zeroCreate<string>(a.GetArrayLength ())
    let mutable i = 0

    for v in a.EnumerateArray () do
        result[i] <- v.GetString ()
        i <- i + 1

    result

/// What a cross-file query needs from `index.json` and `xref.json`.
type Graph =
    {
        DeclId: string[]
        DeclKeyword: string[]
        Qualified: string[]
        FileModule: string[]
        SymbolFile: int[]
        ToOffsets: int[]
        From: int[]
        Where: int[]
        Count: int[]
        /// Symbol nodes by short and qualified name.
        ByName: Dictionary<string, List<int>>
    }

let private loadGraph (out: string) =
    use index = parse (Path.Combine (out, "index.json"))
    use xref = parse (Path.Combine (out, "xref.json"))
    let i = index.RootElement
    let x = xref.RootElement
    let symbols = i.GetProperty "symbols"
    let decls = i.GetProperty "declarations"
    let edges = x.GetProperty "edges"
    let qualified = strs symbols "qualified"
    let byName = Dictionary<string, List<int>>()

    let add (key: string) (node: int) =
        match byName.TryGetValue key with
        | true, l -> l.Add node
        | _ -> byName[key] <- List [ node ]

    qualified
    |> Array.iteri (fun n q ->
        add q n
        let dot = q.LastIndexOf '.'

        if dot > 0 then
            add (q.Substring (dot + 1)) n)

    {
        DeclId = strs decls "id"
        DeclKeyword = strs decls "keyword"
        Qualified = qualified
        FileModule =
            [|
                for f in (i.GetProperty "files").EnumerateArray() -> f.GetProperty("module").GetString()
            |]
        SymbolFile = ints symbols "file"
        ToOffsets = ints x "toOffsets"
        From = ints edges "from"
        Where = ints edges "where"
        Count = ints edges "count"
        ByName = byName
    }

let private whereNames = [| "statement"; "body"; "proof"; "directive" |]

let private whoUses (g: Graph) (name: string) =
    match g.ByName.TryGetValue name with
    | true, nodes ->
        [|
            for n in nodes do
                for e in g.ToOffsets[n] .. g.ToOffsets[n + 1] - 1 do
                    let owner =
                        if g.From[e] >= 0 then
                            g.DeclId[g.From[e]]
                        else
                            g.FileModule[~~~g.From[e]] + " (top level)"

                    yield owner, whereNames[g.Where[e]], g.Count[e]
        |]
    | _ -> [||]

let private show (out: string) (name: string) =
    use index = parse (Path.Combine (out, "index.json"))
    let decls = index.RootElement.GetProperty "declarations"
    let names = strs decls "name"
    let files = ints decls "file"
    let locals = ints decls "local"

    let filesTable =
        [| for f in (index.RootElement.GetProperty "files").EnumerateArray() -> f |]

    match Array.tryFindIndex (fun n -> n = name) names with
    | None -> printfn "no declaration named %s" name
    | Some d ->
        let shard (layer: string) =
            filesTable[files[d]].GetProperty("shards").GetProperty(layer).GetProperty("path").GetString()

        use outline = parse (Path.Combine (out, shard "outline"))
        use tokens = parse (Path.Combine (out, shard "tokens"))

        let bytes =
            Encoding.UTF8.GetBytes (tokens.RootElement.GetProperty("source").GetString())

        let decl = outline.RootElement.GetProperty("declarations")[locals[d]]
        let comments = outline.RootElement.GetProperty "comments"

        let slice (e: JsonElement) =
            if e.ValueKind = JsonValueKind.Null then
                "(none)"
            else
                Encoding.UTF8.GetString (bytes, e[0].GetInt32(), e[1].GetInt32() - e[0].GetInt32())

        printfn "%s %s" (decl.GetProperty("keyword").GetString()) name

        for c in (decl.GetProperty "docs").EnumerateArray() do
            printfn "doc:       %s" (comments[c.GetInt32 ()].GetProperty("text").GetString())

        printfn "binders:   %s" (slice (decl.GetProperty "binders"))
        printfn "type:      %s" (slice (decl.GetProperty "type"))
        printfn "body:      %s" (slice (decl.GetProperty "body"))

        for p in (decl.GetProperty "proofs").EnumerateArray() do
            let body = p.GetProperty "bodySpan"
            let size = body[1].GetInt32() - body[0].GetInt32()

            printfn
                "proof:     %s, %d tactic sentences, %d bytes"
                (p.GetProperty("ending").GetString())
                (p.GetProperty("tacticSentences").GetInt32())
                size

/// Median of `runs` timed calls after the first. Returns the first and the median, in milliseconds.
let private time (runs: int) (f: unit -> unit) =
    let once () =
        let sw = Stopwatch.StartNew ()
        f ()
        sw.Elapsed.TotalMilliseconds

    let first = once ()
    let rest = Array.init runs (fun _ -> once ()) |> Array.sort
    first, rest[runs / 2]

let private row (label: string) (first: float, median: float) =
    printfn "| %s | %.2f | %.2f |" label first median

let private largestFile (out: string) =
    use index = parse (Path.Combine (out, "index.json"))

    [|
        for f in (index.RootElement.GetProperty "files").EnumerateArray() -> f.GetProperty("bytes").GetInt32(), f.GetProperty("module").GetString()
    |]
    |> Array.maxBy fst
    |> snd

/// The symbol with the most mentions, so the query depends on no name in the proofs.
let private busiestSymbol (g: Graph) =
    [| 0 .. g.Qualified.Length - 1 |]
    |> Array.maxBy (fun n -> g.ToOffsets[n + 1] - g.ToOffsets[n])
    |> fun n -> g.Qualified[n]

let private readLayer (out: string) (moduleName: string) (layer: string) =
    use doc = parse (Path.Combine (out, "files", $"{moduleName}.{layer}.json"))
    doc.RootElement.GetProperty "schema" |> ignore

let private materialiseRefs (out: string) (moduleName: string) =
    use doc = parse (Path.Combine (out, "files", $"{moduleName}.refs.json"))
    let refs = doc.RootElement.GetProperty "refs"
    let targets = doc.RootElement.GetProperty "targets"

    (ints refs "start", ints refs "end", ints refs "target", strs targets "name", strs targets "module")
    |> ignore

let private measure (out: string) =
    let runs = 31
    let large = largestFile out
    printfn "Largest file: %s. First call includes JIT; the median is of %d further calls.\n" large runs
    printfn "| Operation | First (ms) | Median (ms) |"
    printfn "|---|---|---|"
    row "parse index.json" (time runs (fun () -> (parse (Path.Combine (out, "index.json"))).Dispose()))
    row "parse xref.json" (time runs (fun () -> (parse (Path.Combine (out, "xref.json"))).Dispose()))
    row "load the graph (index + xref, into arrays and a name table)" (time runs (fun () -> loadGraph out |> ignore))
    row "parse the largest file's outline layer" (time runs (fun () -> readLayer out large "outline"))
    row "parse the largest file's refs layer" (time runs (fun () -> readLayer out large "refs"))
    row "parse the largest file's tokens layer" (time runs (fun () -> readLayer out large "tokens"))
    row "refs layer into arrays" (time runs (fun () -> materialiseRefs out large))
    let g = loadGraph out
    let name = busiestSymbol g
    let hits = whoUses g name
    row $"who-uses on a loaded graph ({hits.Length} mentions)" (time runs (fun () -> whoUses g name |> ignore))

    row
        "who-uses from nothing (load the graph, answer)"
        (time runs (fun () ->
            let g = loadGraph out
            whoUses g name |> ignore))

let private gzipSize (bytes: byte[]) =
    use ms = new MemoryStream ()

    do
        use z = new GZipStream (ms, CompressionLevel.Optimal, true)
        z.Write (bytes, 0, bytes.Length)

    int ms.Length

let private brotliSize (bytes: byte[]) =
    use ms = new MemoryStream ()

    do
        use z = new BrotliStream (ms, CompressionLevel.Optimal, true)
        z.Write (bytes, 0, bytes.Length)

    int ms.Length

let private writeVarint (w: BinaryWriter) (v: int) =
    w.Write7BitEncodedInt v

/// A binary prototype of the refs layer: delta-coded starts, lengths, target indexes and a string table.
let private toBinary (starts: int[]) (ends: int[]) (target: int[]) (line: int[]) (tables: string[][]) =
    use ms = new MemoryStream ()
    use w = new BinaryWriter (ms, Encoding.UTF8)
    writeVarint w starts.Length
    let mutable prev = 0

    for i in 0 .. starts.Length - 1 do
        writeVarint w (starts[i] - prev)
        prev <- starts[i]
        writeVarint w (ends[i] - starts[i])
        writeVarint w target[i]
        writeVarint w line[i]

    for table in tables do
        writeVarint w table.Length

        for s in table do
            w.Write s

    w.Flush ()
    ms.ToArray ()

let private readBinary (bytes: byte[]) (tableCount: int) =
    use ms = new MemoryStream (bytes)
    use r = new BinaryReader (ms, Encoding.UTF8)
    let n = r.Read7BitEncodedInt ()
    let starts = Array.zeroCreate<int> n
    let ends = Array.zeroCreate<int> n
    let target = Array.zeroCreate<int> n
    let line = Array.zeroCreate<int> n
    let mutable prev = 0

    for i in 0 .. n - 1 do
        prev <- prev + r.Read7BitEncodedInt ()
        starts[i] <- prev
        ends[i] <- prev + r.Read7BitEncodedInt ()
        target[i] <- r.Read7BitEncodedInt ()
        line[i] <- r.Read7BitEncodedInt ()

    for _ in 1..tableCount do
        let m = r.Read7BitEncodedInt ()

        for _ in 1..m do
            r.ReadString () |> ignore

/// The refs layer as one JSON object per reference, the shape a columnar layout replaces.
let private toObjects (starts: int[]) (ends: int[]) (target: int[]) (line: int[]) (tables: string[][]) =
    use ms = new MemoryStream ()

    do
        use w =
            new Utf8JsonWriter (ms, JsonWriterOptions (Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping))

        w.WriteStartArray ()

        for i in 0 .. starts.Length - 1 do
            w.WriteStartObject ()
            w.WriteNumber ("start", starts[i])
            w.WriteNumber ("end", ends[i])
            w.WriteNumber ("line", line[i])
            w.WriteString ("module", tables[0][target[i]])
            w.WriteString ("path", tables[1][target[i]])
            w.WriteString ("name", tables[2][target[i]])
            w.WriteString ("kind", tables[3][target[i]])
            w.WriteEndObject ()

        w.WriteEndArray ()

    ms.ToArray ()

let private readObjects (bytes: byte[]) =
    use doc = JsonDocument.Parse bytes
    let n = doc.RootElement.GetArrayLength ()
    let starts = Array.zeroCreate<int> n
    let names = Array.zeroCreate<string> n
    let mutable i = 0

    for e in doc.RootElement.EnumerateArray () do
        starts[i] <- e.GetProperty("start").GetInt32()
        names[i] <- e.GetProperty("name").GetString()
        i <- i + 1

let private formats (out: string) =
    let large = largestFile out
    let refsPath = Path.Combine (out, "files", $"{large}.refs.json")
    let columnar = File.ReadAllBytes refsPath
    use doc = JsonDocument.Parse columnar
    let refs = doc.RootElement.GetProperty "refs"
    let targets = doc.RootElement.GetProperty "targets"
    let starts = ints refs "start"
    let ends = ints refs "end"
    let target = ints refs "target"
    let line = ints refs "line"

    let tables =
        [|
            strs targets "module"
            strs targets "path"
            strs targets "name"
            strs targets "kind"
        |]

    let objects = toObjects starts ends target line tables
    let binary = toBinary starts ends target line tables
    let runs = 31

    printfn "The references of %s (%d entries), three ways. Read = into arrays.\n" large starts.Length
    printfn "| Format | Bytes | gzip | brotli | Read, median (ms) |"
    printfn "|---|---|---|---|---|"

    let line' (label: string) (bytes: byte[]) (read: unit -> unit) =
        let _, median = time runs read
        printfn "| %s | %d | %d | %d | %.2f |" label bytes.Length (gzipSize bytes) (brotliSize bytes) median

    line' "JSON, one object per reference" objects (fun () -> readObjects objects)

    line' "JSON, columnar with a target table (chosen)" columnar (fun () ->
        use d = JsonDocument.Parse columnar
        let r = d.RootElement.GetProperty "refs"

        (ints r "start", ints r "end", ints r "target", ints r "line")
        |> ignore)

    line' "Binary prototype, varints with a target table" binary (fun () -> readBinary binary tables.Length)

    let all =
        Directory.GetFiles (out, "*.json", SearchOption.AllDirectories)
        |> Array.map (fun p -> Path.GetRelativePath (out, p), File.ReadAllBytes p)

    printfn
        "\nWhole output: %d files, %d bytes, %d gzip, %d brotli."
        all.Length
        (all |> Array.sumBy (fun (_, b) -> b.Length))
        (all |> Array.sumBy (fun (_, b) -> gzipSize b))
        (all |> Array.sumBy (fun (_, b) -> brotliSize b))

    printfn "\n| Output file | Bytes | gzip |"
    printfn "|---|---|---|"

    for (name, b) in all |> Array.sortBy fst do
        printfn "| %s | %d | %d |" (name.Replace ('\\', '/')) b.Length (gzipSize b)

[<EntryPoint>]
let main argv =
    match argv with
    | [| "who-uses"; out; name |] ->
        let sw = Stopwatch.StartNew ()
        let g = loadGraph out
        let hits = whoUses g name

        for (owner, where, count) in hits do
            printfn "%s  %s x%d" owner where count

        printfn "%d mentions in %d ms" hits.Length sw.ElapsedMilliseconds
        0
    | [| "show"; out; name |] ->
        show out name
        0
    | [| "measure"; out |] ->
        measure out
        0
    | [| "formats"; out |] ->
        formats out
        0
    | _ ->
        eprintfn "usage: who-uses <out> <name> | show <out> <name> | measure <out> | formats <out>"
        1
