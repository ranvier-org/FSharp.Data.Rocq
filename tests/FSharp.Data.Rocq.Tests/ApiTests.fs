/// The public library API, and its agreement with the JSON written from the same model.
module FSharp.Data.Rocq.Tests.ApiTests

open System.IO
open Expecto
open FSharp.Data.Rocq.Extract
open FSharp.Data.Rocq.Project
open FSharp.Data.Rocq.Structure
open FSharp.Data.Rocq.Tests.Support

let private loaded (project: string) =
    match load project with
    | Result.Ok p -> p
    | Result.Error failures -> failtestf "load failed: %A" (failures |> Array.map describeFailure)

let private nodeCount (p: RocqProject) =
    p.Symbols.Length
    + p.CrossReferences.Externals.Length

let private allNodes (p: RocqProject) =
    [
        for s in 0 .. p.Symbols.Length - 1 -> SymbolNode s
        for e in 0 .. p.CrossReferences.Externals.Length - 1 -> ExternalSymbolNode e
    ]

/// Each project under test: the fixture, and the corpus when configured.
let private projects =
    [
        yield "fixture", fixtureProject

        match corpusProject with
        | Some corpus -> yield "corpus", corpus
        | None -> ()
    ]

[<Tests>]
let tests =
    testList
        "api"
        [
            test "load needs no output directory and returns every file's model" {
                let p = loaded fixtureProject
                Expect.equal p.Files.Length 3 "the fixture's files"

                for f in p.Files do
                    Expect.equal f.GlobStatus Current "digests match"
                    Expect.isSome f.Glob "the .glob is kept"
                    Expect.isGreaterThan f.Model.Sentences.Length 0 "sentences"
            }

            test "a stale .glob is a typed failure" {
                let project = copyFixture ()
                let v = Path.Combine (Path.GetDirectoryName project, "theories", "Base.v")
                File.AppendAllText (v, " ")

                match load project with
                | Result.Error [| GlobStaleForSource (file, recorded, actual) |] ->
                    Expect.equal file "theories/Base.v" "says which"
                    Expect.notEqual recorded actual "says how"
                | other -> failtestf "expected one stale failure, got %A" (Result.map (fun _ -> "a project") other)
            }

            test "every unreadable file is reported at once" {
                let project = copyFixture ()
                let theories = Path.Combine (Path.GetDirectoryName project, "theories")
                File.Delete (Path.Combine (theories, "Base.glob"))
                File.Delete (Path.Combine (theories, "Crlf.glob"))

                match load project with
                | Result.Error failures ->
                    Expect.sequenceEqual
                        (failures
                         |> Array.map (function
                             | GlobMissing f -> f
                             | other -> failtestf "unexpected %A" other)
                         |> Array.sort)
                        [| "theories/Base.v"; "theories/Crlf.v" |]
                        "both files"
                | Result.Ok _ -> failtest "expected failures"
            }

            test "a missing _CoqProject is a typed failure" {
                let missing = Path.Combine (tempDir (), "_CoqProject")

                match load missing with
                | Result.Error [| ProjectFileMissing path |] -> Expect.equal path missing "says which"
                | other -> failtestf "expected a missing project, got %A" (Result.map (fun _ -> "a project") other)
            }

            test "AllowStale keeps the model and drops .glob facts" {
                let project = copyFixture ()
                let v = Path.Combine (Path.GetDirectoryName project, "theories", "Base.v")
                File.AppendAllText (v, "\n(* new *)\n")

                match loadWith { AllowStale = true } project with
                | Result.Ok p ->
                    let b =
                        p.Files
                        |> Array.find (fun f -> f.Path = "theories/Base.v")

                    Expect.equal b.GlobStatus Stale "flagged"
                    Expect.isNone b.Glob "no .glob facts"
                    Expect.isEmpty b.Model.Refs "no references"
                    Expect.isGreaterThan b.Model.Sentences.Length 0 "sentences stay"
                | Result.Error failures -> failtestf "%A" failures
            }

            test "symbolsNamed matches short and fully qualified names" {
                let p = loaded fixtureProject
                let short = symbolsNamed p "base_id"
                Expect.isNonEmpty short "short name"
                Expect.equal (symbolsNamed p "Fixture.Base.base_id") short "fully qualified name"
                Expect.isEmpty (symbolsNamed p "no_such_symbol") "no match"
            }

            test "whoUses finds a declaration that mentions the symbol" {
                let p = loaded fixtureProject

                let owners =
                    whoUses p "base_id"
                    |> Array.choose (fun e ->
                        match e.Owner with
                        | DeclarationOwner d -> p.Declarations[d].Declaration.Name
                        | FileOwner _ -> None)

                Expect.contains owners "base_id_eq" "the lemma about it"
            }

            for (name, project) in projects do
                test $"usesOf and usesBy partition the edges of the {name}" {
                    let p = loaded project
                    let edges = p.CrossReferences.Edges

                    let byTarget =
                        allNodes p
                        |> List.collect (fun node ->
                            let found = usesOf p node

                            for e in found do
                                Expect.equal e.Target node "targets the node"

                            List.ofArray found)

                    Expect.equal byTarget.Length edges.Length "every edge, by target"
                    Expect.equal (nodeCount p + 1) p.CrossReferences.ToOffsets.Length "one offset per node"

                    let owners =
                        [
                            for d in 0 .. p.Declarations.Length - 1 -> DeclarationOwner d
                            for f in 0 .. p.Files.Length - 1 -> FileOwner f
                        ]

                    let byOwner =
                        owners
                        |> List.collect (fun owner ->
                            let found = usesBy p owner

                            for e in found do
                                Expect.equal e.Owner owner "belongs to the owner"

                            List.ofArray found)

                    Expect.equal byOwner.Length edges.Length "every edge, by owner"
                }

                test $"the JSON written for the {name} matches the typed tables" {
                    let p = loaded project
                    let out = tempDir ()
                    let report = run (options project out)
                    Expect.equal report.ExitCode 0 "ran"
                    let index, xref, files = loadOutput project out

                    let declarations = index.GetProperty "declarations"
                    let symbols = index.GetProperty "symbols"
                    let edges = xref.GetProperty "edges"

                    Expect.sequenceEqual (strs declarations "id") (p.Declarations |> Array.map _.Declaration.Id) "declaration ids"

                    Expect.sequenceEqual
                        (strs declarations "category")
                        (p.Declarations
                         |> Array.map (fun d -> categoryName d.Declaration.Category))
                        "declaration categories"

                    Expect.sequenceEqual (strs symbols "qualified") (p.Symbols |> Array.map _.Symbol.Qualified) "symbols"
                    Expect.equal (ints edges "to").Length p.CrossReferences.Edges.Length "edges"

                    Expect.sequenceEqual
                        (ints edges "where")
                        (p.CrossReferences.Edges
                         |> Array.map (fun e -> useLocationCode e.Where))
                        "edge locations"

                    Expect.sequenceEqual (ints xref "byFrom") p.CrossReferences.ByFrom "owner lookup"
                    Expect.equal files.Length p.Files.Length "files"
                }

                test $"absent fields of the {name} are None in the model, and -1 or null in the JSON" {
                    let p = loaded project
                    let out = tempDir ()
                    Expect.equal (run (options project out)).ExitCode 0 "ran"
                    let index, _, files = loadOutput project out

                    let optionalIndex (i: int) =
                        if i >= 0 then Some i else None

                    Expect.sequenceEqual
                        (strs (index.GetProperty "declarations") "name"
                         |> Array.map Option.ofObj)
                        (p.Declarations |> Array.map _.Declaration.Name)
                        "declaration names"

                    Array.iter2
                        (fun (f: RocqFile) (json: Loaded) ->
                            let sentences = json.Outline.GetProperty "sentences"
                            let keywords = strs sentences "keywords"

                            Expect.sequenceEqual
                                (ints sentences "decl" |> Array.map optionalIndex)
                                (f.Model.Sentences |> Array.map _.Decl)
                                $"{f.Path}: sentence declarations"

                            Expect.sequenceEqual
                                (ints sentences "keyword"
                                 |> Array.map (fun k -> if k >= 0 then Some keywords[k] else None))
                                (f.Model.Sentences |> Array.map _.Keyword)
                                $"{f.Path}: sentence keywords"

                            Expect.sequenceEqual
                                (ints (json.Outline.GetProperty "symbols") "decl"
                                 |> Array.map optionalIndex)
                                (f.Model.Symbols |> Array.map _.Decl)
                                $"{f.Path}: symbol declarations"

                            Expect.sequenceEqual
                                (ints (json.Outline.GetProperty "uses") "owner"
                                 |> Array.map optionalIndex)
                                (f.Model.Uses |> Array.map _.Owner)
                                $"{f.Path}: use owners"

                            let targets = json.Refs.GetProperty "targets"

                            Expect.sequenceEqual
                                (ints targets "binder" |> Array.map optionalIndex)
                                (f.Model.Targets |> Array.map _.Binder)
                                $"{f.Path}: target binders"

                            Expect.sequenceEqual
                                (ints targets "symbol" |> Array.map optionalIndex)
                                (f.Model.Targets |> Array.map _.Symbol)
                                $"{f.Path}: target symbols")
                        p.Files
                        files
                }
        ]
