module Rocq.Navigator.Tests.FactsTests

open System
open System.IO
open System.Text.Json
open Expecto
open Rocq.Navigator
open Rocq.Navigator.Tests

let private transcript () =
    File.ReadAllText (Path.Combine (FixtureFiles.fixtureDir (), "transcript.txt"))

let private factsOf () =
    let project = FixtureFiles.fixtureProject ()
    Facts.parseTranscript project (transcript ())

let private fileFacts (symbols: Map<string, SymbolFact>) =
    let project = FixtureFiles.fixtureProject ()

    {
        RocqVersion = "9.1.0"
        Digests =
            project.Files
            |> Array.map (fun f -> f.Path, f.Digest)
            |> Map.ofArray
        Symbols = symbols
    }

let private unknownFact =
    {
        Type = Some "nat"
        Binders = Map.empty
        Assumptions = None
    }

let private captureTests =
    match Environment.GetEnvironmentVariable "ROCQ_NAVIGATOR_ROCQ" with
    | null
    | "" -> []
    | prefix ->
        [
            testCase "capture on the fixture reproduces the committed transcript's facts"
            <| fun () ->
                let rocq = JsonSerializer.Deserialize<string[]> prefix
                let project = FixtureFiles.fixtureProject ()

                let config =
                    match Config.read (Path.Combine (FixtureFiles.fixtureDir (), "navigator.json")) with
                    | Ok config -> config
                    | Result.Error message -> failtest message

                match Facts.capture rocq (FixtureFiles.fixtureDir ()) project config with
                | Result.Error message -> failtest message
                | Ok facts ->
                    Expect.equal facts.Symbols (Facts.parseTranscript project (transcript ())) "capture agrees with transcript.txt"

                    Expect.isSome facts.Symbols["Fixture.Guide.sum_refl"].Type "a theorem has a type"
                    Expect.isNotEmpty facts.RocqVersion "the version is read"
        ]

let tests =
    testList
        "facts"
        ([
            testCase "a symbol whose Check errored has no type, and its neighbours keep theirs"
            <| fun () ->
                let facts = factsOf ()
                Expect.isNone facts["Fixture.Guide.First.shared"].Type "a section variable has no global name"

                Expect.equal facts["Fixture.Guide.sum_refl"].Type (Some "∀ a b : nat, a + b = a + b") "the theorem before keeps its type"

                Expect.equal facts["Fixture.Guide.first_use"].Type (Some "nat -> nat") "the definition after keeps its type"

            testCase "Closed under the global context reads as Closed"
            <| fun () ->
                let facts = factsOf ()
                Expect.equal facts["Fixture.Guide.sum_refl"].Assumptions (Some Closed) "a Qed theorem with no axioms"
                Expect.equal facts["Fixture.Guide.first_use"].Assumptions None "a definition is not asked"

            testCase "named axioms read as RestsOn with each axiom"
            <| fun () ->
                let facts = factsOf ()

                Expect.equal facts["Fixture.Guide.rests_on_axiom"].Assumptions (Some (RestsOn [| "fixture_axiom" |])) "the axiom is named"

                Expect.equal facts["Fixture.Guide.open_goal"].Assumptions (Some (RestsOn [| "open_goal" |])) "an Admitted theorem rests on itself"

            testCase "binders come from the leading forall, and a generated binder name matches only itself"
            <| fun () ->
                let grouped =
                    Facts.parseBinders "forall (a b : list (nat * nat)) [c : T] {Stable0 : Stable a}, a = forall d : nat, d"

                Expect.equal
                    grouped
                    (Map.ofList
                        [
                            "a", "list (nat * nat)"
                            "b", "list (nat * nat)"
                            "c", "T"
                            "Stable0", "Stable a"
                        ])
                    "groups end at the first comma at depth 0"

                Expect.isFalse (grouped.ContainsKey "Stable") "the class name is not a binder"

                Expect.equal
                    (Facts.parseBinders "∀ a b : nat, a = b")
                    (Map.ofList [ "a", "nat"; "b", "nat" ])
                    "an unbracketed group and the Unicode quantifier"

                Expect.isEmpty (Facts.parseBinders "nat -> nat") "no leading forall, no binders"

                let facts = factsOf ()

                Expect.equal
                    facts["Fixture.Guide.stable_twice"].Binders
                    (Map.ofList [ "P", "Prop" ])
                    "an arrow-printed instance argument has no binder"

            testCase "a digest mismatch returns an error naming the file"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()
                let facts = fileFacts (factsOf ())

                let stale =
                    { facts with
                        Digests = facts.Digests |> Map.add "theories/Other.v" "0000"
                    }

                match Facts.reconcile false project stale with
                | Ok _ -> failtest "a stale digest must not reconcile"
                | Result.Error messages ->
                    Expect.equal messages.Length 1 "one stale file"
                    Expect.stringContains messages[0] "theories/Other.v" "the file is named"

            testCase "dropStale removes that file's facts and keeps the others"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()
                let facts = fileFacts (factsOf ())

                let stale =
                    { facts with
                        Digests = facts.Digests |> Map.add "theories/Other.v" "0000"
                    }

                match Facts.reconcile true project stale with
                | Result.Error messages -> failtestf "%A" messages
                | Ok kept ->
                    Expect.isFalse (kept.Symbols.ContainsKey "Fixture.Other.other_def") "the stale file's fact is gone"
                    Expect.isTrue (kept.Symbols.ContainsKey "Fixture.Guide.sum_refl") "the other file's fact stays"
                    Expect.isFalse (kept.Digests.ContainsKey "theories/Other.v") "the stale digest is gone"
                    Expect.isTrue (kept.Digests.ContainsKey "theories/Guide.v") "the other digest stays"

            testCase "a fact for an unknown symbol is ignored"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()

                let facts =
                    fileFacts (
                        factsOf ()
                        |> Map.add "Fixture.Guide.no_such_symbol" unknownFact
                    )

                match Facts.reconcile false project facts with
                | Result.Error messages -> failtestf "%A" messages
                | Ok kept -> Expect.isFalse (kept.Symbols.ContainsKey "Fixture.Guide.no_such_symbol") "the fact is ignored"

            testCase "facts survive a write and read"
            <| fun () ->
                let path =
                    Path.Combine (
                        Path.GetTempPath (),
                        "rocq-navigator-"
                        + Guid.NewGuid().ToString "N"
                        + ".json"
                    )

                let facts = fileFacts (factsOf ())

                try
                    Facts.write path facts
                    Expect.equal (Facts.read path) (Ok facts) "the round trip is lossless"
                finally
                    File.Delete path

            testCase "reading a missing or malformed file is an error"
            <| fun () ->
                let path =
                    Path.Combine (
                        Path.GetTempPath (),
                        "rocq-navigator-"
                        + Guid.NewGuid().ToString "N"
                        + ".json"
                    )

                Expect.isError (Facts.read path) "a missing facts file"
                Expect.isError (Config.read path) "a missing config file"
                File.WriteAllText (path, "{ not json")

                try
                    Expect.isError (Facts.read path) "malformed facts"
                    Expect.isError (Config.read path) "malformed config"
                finally
                    File.Delete path

            testCase "the fixture config reads its three fields"
            <| fun () ->
                let config =
                    Config.read (Path.Combine (FixtureFiles.fixtureDir (), "navigator.json"))

                Expect.equal
                    config
                    (Ok
                        {
                            FalsifiedSuffix = Some "_falsified"
                            CheckMarker = Some "@checked"
                            CaptureImports = [| "Fixture.Guide" |]
                        })
                    "every field is read"

            testCase "the capture script requires each file, imports the config's modules, and separates each command"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()

                let script =
                    Facts.captureScript
                        project
                        { Config.none with
                            CaptureImports = [| "Fixture.Guide" |]
                        }

                Expect.stringContains script "Require Fixture.Other." "each file is required"
                Expect.stringContains script "Require Import Fixture.Guide." "the import is present"
                Expect.stringContains script "Fail Check __nav_0." "commands are separated"
                Expect.stringContains script "Print Assumptions Fixture.Guide.sum_refl." "a theorem has assumptions"
                Expect.isFalse (script.Contains "Print Assumptions Fixture.Guide.first_use.") "a definition has none"
                Expect.isFalse (script.Contains "Check @Fixture.Guide.First.\n") "a section name has no Check"
         ]
         @ captureTests)
