/// Guarantees that hold for any compiled project. They run on the fixture, and on `ROCQDATA_CORPUS` when set.
module RocqData.Tests.CompletenessTests

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open Expecto
open RocqData.Project
open RocqData.Source
open RocqData.Tests.Support

let private isBlank (b: byte) =
    b = 32uy || b = 9uy || b = 10uy || b = 13uy

let private coverage (f: Loaded) =
    let sentences = f.Outline.GetProperty "sentences"
    let starts = ints sentences "start"
    let ends = ints sentences "end"
    let covered = Array.zeroCreate<bool> f.Bytes.Length

    for i in 0 .. starts.Length - 1 do
        if i > 0 then
            Expect.isGreaterThanOrEqual starts[i] ends[i - 1] $"{f.Path}: sentence {i} overlaps its predecessor"

        for b in starts[i] .. ends[i] - 1 do
            covered[b] <- true

    for c in (f.Outline.GetProperty "comments").EnumerateArray() do
        let s, e = spanOf (c.GetProperty "span")

        for b in s .. e - 1 do
            covered[b] <- true

    for b in 0 .. f.Bytes.Length - 1 do
        if not covered[b] && not (isBlank f.Bytes[b]) then
            failtest $"{f.Path}: byte {b} ({char f.Bytes[b]}) lies in no sentence or comment"

let private tokenPartition (f: Loaded) =
    let starts = ints f.Tokens "start"
    let u16 = ints f.Tokens "u16"
    let source = f.Tokens.GetProperty("source").GetString()
    let total = f.Bytes.Length
    Expect.equal (Encoding.UTF8.GetBytes source) f.Bytes $"{f.Path}: source round-trips"
    Expect.equal (f.Tokens.GetProperty("bytes").GetInt32()) total $"{f.Path}: byte length"
    Expect.equal (f.Tokens.GetProperty("utf16Length").GetInt32()) source.Length $"{f.Path}: UTF-16 length"
    let rebuilt = StringBuilder ()

    for i in 0 .. starts.Length - 1 do
        let stop = if i + 1 < starts.Length then starts[i + 1] else total
        let u16Stop = if i + 1 < u16.Length then u16[i + 1] else source.Length
        Expect.isGreaterThan stop starts[i] $"{f.Path}: token {i} is non-empty"
        let viaBytes = Encoding.UTF8.GetString (f.Bytes, starts[i], stop - starts[i])
        let viaUtf16 = source.Substring (u16[i], u16Stop - u16[i])
        Expect.equal viaUtf16 viaBytes $"{f.Path}: token {i} slices the same by bytes and by UTF-16"
        rebuilt.Append viaUtf16 |> ignore

    Expect.equal (rebuilt.ToString ()) source $"{f.Path}: tokens rebuild the source"
    Expect.equal (if starts.Length > 0 then starts[0] else 0) 0 $"{f.Path}: first token at 0"

let private everySpanFallsOnATokenBoundary (f: Loaded) =
    let boundaries = HashSet<int>(ints f.Tokens "start")
    boundaries.Add f.Bytes.Length |> ignore

    let check (what: string) (s: int, e: int) =
        Expect.isTrue (boundaries.Contains s) $"{f.Path}: {what} starts at {s}, between tokens"
        Expect.isTrue (boundaries.Contains e) $"{f.Path}: {what} ends at {e}, between tokens"

        Expect.isTrue
            (isCharBoundary f.Bytes s
             && isCharBoundary f.Bytes e)
            $"{f.Path}: {what} splits a character"

    let columns (e: JsonElement) (what: string) =
        Array.iter2 (fun s x -> check what (s, x)) (ints e "start") (ints e "end")

    columns (f.Outline.GetProperty "sentences") "sentence"
    columns (f.Outline.GetProperty "symbols") "symbol"
    columns (f.Refs.GetProperty "refs") "ref"
    columns (f.Refs.GetProperty "binders") "binder"

    for c in (f.Outline.GetProperty "comments").EnumerateArray() do
        check "comment" (spanOf (c.GetProperty "span"))

    for s in (f.Outline.GetProperty "scopes").EnumerateArray() do
        check "scope" (spanOf (s.GetProperty "span"))

    for d in (f.Outline.GetProperty "declarations").EnumerateArray() do
        for name in
            [
                "extent"
                "sentenceSpan"
                "keywordSpan"
                "nameSpan"
                "statement"
                "binders"
                "type"
                "body"
            ] do
            let v = d.GetProperty name

            if v.ValueKind = JsonValueKind.Array then
                check name (spanOf v)

        for p in (d.GetProperty "proofs").EnumerateArray() do
            check "proof" (spanOf (p.GetProperty "span"))
            check "proof body" (spanOf (p.GetProperty "bodySpan"))

let private everyGlobLineIsAccounted (f: Loaded) (globBytes: byte[]) =
    let lines = Encoding.UTF8.GetString(globBytes).TrimEnd('\n').Split '\n'
    let glob = f.Outline.GetProperty "globLines"
    Expect.equal (glob.GetProperty("total").GetInt32()) lines.Length $"{f.Path}: line total"

    let seen =
        [|
            yield 0
            yield 1
            yield! ints (f.Outline.GetProperty "symbols") "line"
            yield! ints (f.Refs.GetProperty "refs") "line"
            yield! ints (f.Refs.GetProperty "binders") "line"
            for u in (glob.GetProperty "unparsed").EnumerateArray() do
                yield u.GetProperty("line").GetInt32()
        |]
        |> Array.sort

    Expect.equal seen [| 0 .. lines.Length - 1 |] $"{f.Path}: each .glob line lands in exactly one table"
    Expect.equal ((glob.GetProperty "unparsed").GetArrayLength()) 0 $"{f.Path}: no line left unparsed"

let private symbolTextMatches (f: Loaded) =
    let symbols = f.Outline.GetProperty "symbols"
    let names = strs symbols "name"
    let kinds = strs symbols "kind"
    let starts = ints symbols "start"
    let ends = ints symbols "end"

    for i in 0 .. names.Length - 1 do
        // Generated names and notation keys have no literal form in the source. A module's name includes its parents.
        match kinds[i] with
        | "scheme"
        | "not" -> ()
        | "mod"
        | "modtype" -> Expect.stringEnds names[i] (sliceBytes f.Bytes (starts[i], ends[i])) $"{f.Path}: module name"
        | _ when names[i] = "Unnamed_thm" -> ()
        | _ -> Expect.equal (sliceBytes f.Bytes (starts[i], ends[i])) names[i] $"{f.Path}: bytes at the symbol's span"

    let binders = f.Refs.GetProperty "binders"
    let binderNames = strs binders "name"
    let binderStarts = ints binders "start"
    let binderEnds = ints binders "end"

    for i in 0 .. binderNames.Length - 1 do
        let written = sliceBytes f.Bytes (binderStarts[i], binderEnds[i])

        // A pattern binder has a generated name, and its span covers the pattern.
        if not (written.StartsWith '(' || written.StartsWith '\'') then
            Expect.equal written binderNames[i] $"{f.Path}: bytes at the binder's span"

let private declarationKeywords =
    [
        "Theorem"
        "Lemma"
        "Fact"
        "Remark"
        "Corollary"
        "Proposition"
        "Property"
        "Goal"
        "Definition"
        "Example"
        "Fixpoint"
        "CoFixpoint"
        "Instance"
        "Inductive"
        "CoInductive"
        "Variant"
        "Record"
        "Structure"
        "Class"
        "Axiom"
        "Parameter"
        "Parameters"
        "Conjecture"
        "Variable"
        "Hypothesis"
        "Context"
        "Notation"
        "Infix"
        "Ltac"
        "Let"
    ]

/// Counts keywords at sentence starts in text stripped by the test's own scanner. A sentence start follows a dot and a
/// blank, the start of the file, or a bullet.
let private independentKeywordCount (bytes: byte[]) =
    let stripped = stripCommentsAndStrings (Encoding.UTF8.GetString bytes)

    let pattern =
        Regex (
            @"(?:^|\.\s)\s*(?:(?:[-+*]+|[{}])\s*)*(?:#\[[^\]]*\]\s*)?(?:(?:Local|Global|Program|Polymorphic|Monomorphic|Private)\s+)*("
            + String.Join ("|", declarationKeywords)
            + @")\b",
            RegexOptions.Compiled
        )

    let counts = Dictionary<string, int>()

    let mutable at = 0
    let mutable m = pattern.Match (stripped, at)

    while m.Success do
        let k = m.Groups[1].Value

        counts[k] <-
            (match counts.TryGetValue k with
             | true, c -> c + 1
             | _ -> 1)

        at <- m.Groups[1].Index + m.Groups[1].Length
        m <- pattern.Match (stripped, at)

    counts

let private declarationCountsMatch (f: Loaded) =
    let independent = independentKeywordCount f.Bytes
    let extracted = Dictionary<string, int>()

    for d in (f.Outline.GetProperty "declarations").EnumerateArray() do
        let k = d.GetProperty("keyword").GetString()

        if List.contains k declarationKeywords then
            extracted[k] <-
                (match extracted.TryGetValue k with
                 | true, c -> c + 1
                 | _ -> 1)

    let render (c: Dictionary<string, int>) =
        c
        |> Seq.sortBy _.Key
        |> Seq.map (fun kv -> $"{kv.Key}={kv.Value}")
        |> String.concat " "

    Expect.equal (render extracted) (render independent) $"{f.Path}: declaration counts against an independent scan"

let private proofEndingsMatch (f: Loaded) =
    let stripped = stripCommentsAndStrings (Encoding.UTF8.GetString f.Bytes)

    for ending in [ "Qed"; "Defined"; "Admitted"; "Abort" ] do
        let independent =
            Regex.Count (stripped, @"(?:^|\.\s|\s)" + ending + @"\s*\.(?:\s|$)")

        let extracted =
            (f.Outline.GetProperty "declarations").EnumerateArray()
            |> Seq.sumBy (fun d ->
                (d.GetProperty "proofs").EnumerateArray()
                |> Seq.filter (fun p -> p.GetProperty("ending").GetString() = ending)
                |> Seq.length)

        Expect.equal extracted independent $"{f.Path}: {ending} count against an independent scan"

let private theoremsMatchGlob (f: Loaded) =
    let symbols = f.Outline.GetProperty "symbols"

    let prf =
        strs symbols "kind"
        |> Array.filter (fun k -> k = "prf")
        |> Array.length

    let theoremNames =
        (f.Outline.GetProperty "declarations").EnumerateArray()
        |> Seq.filter (fun d -> d.GetProperty("category").GetString() = "theorem")
        |> Seq.length

    Expect.equal theoremNames prf $"{f.Path}: theorem-keyword declarations against .glob prf lines"

/// Rocq's own sentence ranges, from `-time-file`: `Chars <start> - <end> [...]`.
let private readOracle (path: string) =
    [|
        for line in File.ReadAllLines path do
            let m = Regex.Match (line, @"^Chars (\d+) - (\d+) ")

            if m.Success then
                yield int m.Groups[1].Value, int m.Groups[2].Value
    |]

let private matchesOracle (f: Loaded) (oraclePath: string) =
    let oracle =
        readOracle oraclePath
        |> Array.distinct
        |> Array.sort

    let sentences = f.Outline.GetProperty "sentences"

    let ours =
        Array.zip (ints sentences "start") (ints sentences "end")
        |> Array.sort

    let missing = Set.difference (set oracle) (set ours)
    let extra = Set.difference (set ours) (set oracle)

    let show (s: Set<int * int>) =
        s
        |> Seq.truncate 8
        |> Seq.map (fun (a, b) -> $"{a}-{b}: {sliceBytes f.Bytes (a, b)}")
        |> String.concat "\n"

    Expect.isTrue missing.IsEmpty $"{f.Path}: Rocq sentences absent here:\n{show missing}"
    Expect.isTrue extra.IsEmpty $"{f.Path}: sentences here absent from Rocq:\n{show extra}"

let private fileGraphMatchesRocqDep (index: JsonElement) =
    let graph = index.GetProperty "fileGraph"
    let requires = graph.GetProperty "requires"
    let dep = graph.GetProperty "rocqDep"

    let edges (e: JsonElement) =
        Array.zip (ints e "from") (ints e "to")
        |> Array.sort

    Expect.equal (edges requires) (edges dep) "file edges from .glob lib references against rocq dep"

let private inProjectTargetsResolve (index: JsonElement) (allowed: Set<string>) =
    let unresolved =
        [
            for u in (index.GetProperty "unresolved").EnumerateArray() do
                let target = u.GetProperty("target").GetString()

                if not (allowed.Contains target) then
                    yield $"""{u.GetProperty("file").GetString()}: {target} ({u.GetProperty("kind").GetString()})"""
        ]

    Expect.equal (String.concat "\n" unresolved) "" "references into the project with no symbol"

let private noStructuralDiagnostics (f: Loaded) =
    let found =
        [
            for d in (f.Outline.GetProperty "diagnostics").EnumerateArray() ->
                $"""{d.GetProperty("code").GetString()} at {d.GetProperty("offset").GetInt32()}: {d.GetProperty("message").GetString()}"""
        ]

    Expect.equal (String.concat "\n" found) "" $"{f.Path}: diagnostics"

/// The tests for one project.
let projectTests (label: string) (project: string) (oracleFor: (string -> string option)) (dep: string option) (allowedUnresolved: string list) =
    let loaded =
        lazy
            (let out = tempDir ()

             let report =
                 run
                     { options project out with
                         RocqDep = dep
                         RocqVersion = Some "test"
                     }

             Expect.equal report.ExitCode 0 $"extraction: {report.Refused}"
             load project out)

    let root = Path.GetDirectoryName (Path.GetFullPath project)

    let each (name: string) (check: Loaded -> unit) =
        testCase name (fun () ->
            let _, _, files = loaded.Value
            Expect.isNonEmpty files "files"
            files |> Array.iter check)

    testList
        label
        [
            each "every byte lies in a sentence, a comment or whitespace" coverage
            each "tokens partition the file and slice alike by bytes and UTF-16" tokenPartition
            each "every span falls on token and character boundaries" everySpanFallsOnATokenBoundary
            each "every .glob line is consumed or reported" (fun f ->
                everyGlobLineIsAccounted f (File.ReadAllBytes (Path.ChangeExtension (Path.Combine (root, f.Path), ".glob"))))
            each "symbol and binder spans hold the names .glob gives" symbolTextMatches
            each "declaration counts match an independent scan" declarationCountsMatch
            each "proof endings match an independent scan" proofEndingsMatch
            each "theorem declarations match .glob prf lines" theoremsMatchGlob
            each "a compiled file yields no diagnostics" noStructuralDiagnostics

            testCase "sentence spans match Rocq's own" (fun () ->
                let _, _, files = loaded.Value

                for f in files do
                    match oracleFor f.Path with
                    | Some oracle when File.Exists oracle -> matchesOracle f oracle
                    | _ -> skiptest $"no oracle for {f.Path}")

            testCase "the file graph matches rocq dep" (fun () ->
                let index, _, _ = loaded.Value

                match dep with
                | Some _ -> fileGraphMatchesRocqDep index
                | None -> skiptest "no rocq dep output")

            testCase "references into the project resolve" (fun () ->
                let index, _, _ = loaded.Value
                inProjectTargetsResolve index (set allowedUnresolved))

            testCase "no .glob kind is unknown" (fun () ->
                let index, _, _ = loaded.Value

                Expect.equal
                    ((index.GetProperty "unknownKinds").EnumerateObject()
                     |> Seq.length)
                    0
                    "unknown kinds")
        ]

[<Tests>]
let tests =
    let fixtureOracleFor (path: string) =
        Some (Path.Combine (fixtureOracle, Path.GetFileNameWithoutExtension path + ".time"))

    testList
        "completeness"
        [ // `.glob` writes no line for a record's constructor.
            yield projectTests "fixture" fixtureProject fixtureOracleFor (Some fixtureDep) [ "Fixture.Kitchen#mkPoint" ]

            match corpusProject with
            | Some project ->
                let oracleFor (path: string) =
                    corpusOracle
                    |> Option.map (fun d -> Path.Combine (d, path.Replace ('/', '_') + ".time"))

                yield projectTests "corpus" project oracleFor corpusDep []
            | None -> ()
        ]
