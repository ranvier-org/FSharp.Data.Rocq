module RocqData.Tests.Support

open System
open System.IO
open System.Text
open System.Text.Json
open RocqData.Project

/// The compiled fixture project, committed with its `.glob` files.
let fixtureProject = Path.Combine (__SOURCE_DIRECTORY__, "fixtures", "_CoqProject")
let fixtureOracle = Path.Combine (__SOURCE_DIRECTORY__, "fixtures", "oracle")
let fixtureDep = Path.Combine (__SOURCE_DIRECTORY__, "fixtures", "rocqdep.txt")

let private envPath (name: string) =
    match Environment.GetEnvironmentVariable name with
    | null
    | "" -> None
    | v -> Some v

/// A compiled real project named by `ROCQDATA_CORPUS`, the path of its `_CoqProject`.
let corpusProject = envPath "ROCQDATA_CORPUS"

/// `rocq compile -time-file` outputs for the corpus, one `<path with / as _>.time` per file.
let corpusOracle = envPath "ROCQDATA_ORACLE"

/// `rocq dep -f _CoqProject` output for the corpus.
let corpusDep = envPath "ROCQDATA_DEP"

let tempDir () =
    let dir =
        Path.Combine (Path.GetTempPath (), "rocq-data-tests", Guid.NewGuid().ToString "N")

    Directory.CreateDirectory dir |> ignore
    dir

let options project out =
    {
        Project = project
        OutDir = out
        RocqDep = None
        RocqVersion = None
        AllowStale = false
        Strict = false
        Force = false
    }

let copyDirectory (source: string) (target: string) =
    for dir in Directory.GetDirectories (source, "*", SearchOption.AllDirectories) do
        Directory.CreateDirectory (dir.Replace (source, target))
        |> ignore

    Directory.CreateDirectory target |> ignore

    for file in Directory.GetFiles (source, "*", SearchOption.AllDirectories) do
        File.Copy (file, file.Replace (source, target), true)

/// A copy of the fixture project in a fresh directory; returns the copied `_CoqProject` path.
let copyFixture () =
    let dir = tempDir ()
    copyDirectory (Path.GetDirectoryName fixtureProject) dir
    Path.Combine (dir, "_CoqProject")

let ints (e: JsonElement) (name: string) =
    [| for v in (e.GetProperty name).EnumerateArray() -> v.GetInt32 () |]

let strs (e: JsonElement) (name: string) =
    [| for v in (e.GetProperty name).EnumerateArray() -> v.GetString () |]

let spanOf (e: JsonElement) =
    e[0].GetInt32(), e[1].GetInt32()

/// One extracted file, read back through JSON the way a consumer reads it.
type Loaded =
    {
        Path: string
        Module: string
        Bytes: byte[]
        Outline: JsonElement
        Refs: JsonElement
        Tokens: JsonElement
    }

let load (project: string) (out: string) =
    let root = Path.GetDirectoryName (Path.GetFullPath project)

    let doc (p: string) =
        JsonDocument.Parse(File.ReadAllBytes p).RootElement

    let index = doc (Path.Combine (out, "index.json"))

    let files =
        [|
            for f in (index.GetProperty "files").EnumerateArray() do
                let shards = f.GetProperty "shards"

                let shard (l: string) =
                    doc (Path.Combine (out, shards.GetProperty(l).GetProperty("path").GetString()))

                let path = f.GetProperty("path").GetString()

                {
                    Path = path
                    Module = f.GetProperty("module").GetString()
                    Bytes = File.ReadAllBytes (Path.Combine (root, path))
                    Outline = shard "outline"
                    Refs = shard "refs"
                    Tokens = shard "tokens"
                }
        |]

    index, doc (Path.Combine (out, "xref.json")), files

let sliceBytes (bytes: byte[]) (s: int, e: int) =
    Encoding.UTF8.GetString (bytes, s, e - s)

/// Blanks comments and strings with a second, independent scanner, keeping line structure. Tests count keywords in
/// the result, so a defect in the extractor's lexer cannot hide itself.
let stripCommentsAndStrings (text: string) =
    let sb = StringBuilder (text.Length)
    let mutable i = 0
    let mutable depth = 0
    let mutable inString = false

    while i < text.Length do
        let c = text[i]
        let next = if i + 1 < text.Length then text[i + 1] else '\000'

        if inString then
            if c = '"' && next = '"' then
                i <- i + 2
            else
                if c = '"' then
                    inString <- false

                i <- i + 1
        elif c = '"' then
            inString <- true
            i <- i + 1

            if depth = 0 then
                sb.Append "S" |> ignore
        elif c = '(' && next = '*' then
            depth <- depth + 1
            i <- i + 2
        elif depth > 0 && c = '*' && next = ')' then
            depth <- depth - 1
            i <- i + 2
        elif depth > 0 then
            i <- i + 1
        else
            sb.Append c |> ignore
            i <- i + 1

    sb.ToString ()
