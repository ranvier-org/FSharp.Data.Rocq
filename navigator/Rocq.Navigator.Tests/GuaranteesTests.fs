module Rocq.Navigator.Tests.GuaranteesTests

open System.IO
open System.Text.RegularExpressions
open Expecto
open FSharp.Data.Rocq.Project
open Nacara.Plugins
open Rocq.Navigator
open Rocq.Navigator.Article
open Rocq.Navigator.Guarantees
open Rocq.Navigator.Tests

let private links =
    {
        Module = fun name -> $"/site/{name}.html"
        Pipeline = Docs.pipeline []
    }

let private fixtureConfig =
    {
        FalsifiedSuffix = Some "_falsified"
        CheckMarker = Some "@checked"
        CaptureImports = [||]
        CollapseCodeOver = None
    }

let private titled (project: RocqProject) (config: Config) (facts: Facts option) =
    entries project links config facts
    |> Array.map (fun entry -> entry.Title, entry)
    |> Map.ofArray

/// Every symbol of the project recorded with `assumptions`.
let private factsWith (project: RocqProject) (assumptions: Assumptions) =
    {
        RocqVersion = "9.1.0"
        Digests = Map.empty
        Symbols =
            project.Symbols
            |> Array.map (fun symbol ->
                Facts.symbolKey project symbol,
                {
                    Type = None
                    Binders = Map.empty
                    Assumptions = Some assumptions
                })
            |> Map.ofArray
    }

let private guaranteesPage (project: RocqProject) (facts: Facts option) =
    entries project links fixtureConfig facts

/// The fixture's titled theorems, read from the source text alone.
let private scanFixture () =
    let source =
        File.ReadAllText (Path.Combine (FixtureFiles.fixtureDir (), "theories", "Guide.v"))

    let pattern =
        Regex (
            @"\(\*\*md[ \t]*\r?\n[ \t]*\*\*([^\r\n]+)\*\*[ \t]*\r?\n((?:(?!\*\)).)*)\*\)\s*(Theorem|Corollary)\s+([^\s(:]+)",
            RegexOptions.Singleline
        )

    [
        for m in pattern.Matches source do
            m.Groups[1].Value, m.Groups[4].Value
    ]
    |> List.sort

let tests =
    testList
        "guarantees"
        [
            testCase "only Theorem and Corollary with an opening bold line become entries"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()
                let all = titled project fixtureConfig None
                Expect.isTrue (all.ContainsKey "Reflexivity of sums.") "a theorem"
                Expect.isTrue (all.ContainsKey "Sum is zero on zero.") "a corollary"
                Expect.isFalse (all.ContainsKey "A lemma title.") "a lemma stays out"
                Expect.isFalse (all.ContainsKey "Stable twice.") "a bold phrase followed by prose is not a title"
                Expect.equal all.Count 5 "nothing else qualifies"

                let entry = all["Reflexivity of sums."]
                Expect.equal entry.Module "Fixture.Guide" "under its module"
                Expect.equal entry.Href "/site/Fixture.Guide.html#sum_refl" "links to the anchor"
                Expect.stringContains entry.DocHtml "A theorem with a bold first line." "the doc is kept"
                Expect.isFalse (entry.DocHtml.Contains "Reflexivity") "the title is not repeated"

            testCase "first match wins: an Admitted _falsified theorem is Open"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()
                let all = titled project fixtureConfig None
                Expect.equal all["An open statement."].Group Open "Admitted"
                Expect.equal all["Zero absorbs everything."].Group Falsified "the suffix"
                Expect.equal all["A checked law."].Group Check "the marker"
                Expect.equal all["Reflexivity of sums."].Group Guarantee "every other entry"

                let overlapping =
                    titled
                        project
                        { fixtureConfig with
                            FalsifiedSuffix = Some "_goal"
                            CheckMarker = Some "admitted"
                        }
                        None

                Expect.equal overlapping["An open statement."].Group Open "Admitted beats the suffix and the marker"

                let suffixAndMarker =
                    titled
                        project
                        { fixtureConfig with
                            CheckMarker = Some "The claim is false"
                        }
                        None

                Expect.equal suffixAndMarker["Zero absorbs everything."].Group Falsified "the suffix beats the marker"

            testCase "without a config only Open and Guarantee exist"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()

                let groups =
                    entries project links Config.none None
                    |> Array.map _.Group
                    |> Array.distinct
                    |> Array.sortBy string

                Expect.equal groups [| Guarantee; Open |] "the config-driven groups are empty"

            testCase "an Open or Falsified entry never renders the passing badge, even with Closed facts"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()
                let page = guaranteesPage project (Some (factsWith project Closed))

                for entry in page do
                    Expect.equal entry.Axioms (Some Closed) $"{entry.Title} has the fact"

                let html (group: Group) =
                    page
                    |> Array.filter (fun entry -> entry.Group = group)
                    |> toHtml

                Expect.isFalse ((html Open).Contains "badge") "Open"
                Expect.isFalse ((html Falsified).Contains "badge") "Falsified"
                Expect.stringContains (html Guarantee) "badge pass" "a guarantee shows it"
                Expect.stringContains (html Check) "badge pass" "a check shows it"

            testCase "facts are found by the symbol's full name, and axioms are named"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()

                let transcript =
                    File.ReadAllText (Path.Combine (FixtureFiles.fixtureDir (), "transcript.txt"))

                let facts =
                    {
                        RocqVersion = "9.1.0"
                        Digests = Map.empty
                        Symbols = Facts.parseTranscript project transcript
                    }

                let all = titled project fixtureConfig (Some facts)
                Expect.equal all["Reflexivity of sums."].Axioms (Some Closed) "from the transcript"
                Expect.equal all["An open statement."].Axioms (Some (RestsOn [| "open_goal" |])) "recorded, not displayed"

                let page =
                    toHtml
                        [|
                            { all["Reflexivity of sums."] with
                                Axioms = Some (RestsOn [| "a<b" |])
                            }
                        |]

                Expect.stringContains page "Rests on a&lt;b" "axioms display, escaped"
                Expect.isFalse (page.Contains "badge pass") "an axiom is not a pass"

            testCase "a Guarantee without facts renders no badge"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()
                let html = toHtml (guaranteesPage project None)
                Expect.isFalse (html.Contains "badge") "a missing fact shows as absent"

            testCase "toHtml: groups in rule order, with entries under their module"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()
                let html = toHtml (guaranteesPage project None)

                let position (text: string) =
                    html.IndexOf text

                Expect.isGreaterThan (position ">Falsified<") (position ">Open<") "Open first"
                Expect.isGreaterThan (position ">Checks<") (position ">Falsified<") "Falsified second"
                Expect.isGreaterThan (position ">Guarantees<") (position ">Checks<") "Checks third"
                Expect.stringContains html ">Fixture.Guide</h3>" "the module"
                Expect.stringContains html """<a href="/site/Fixture.Guide.html#sum_refl">Reflexivity of sums.</a>""" "the title links"

            testCase "entries match an independent regex scan of the fixture's comments"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()

                let found =
                    entries project links fixtureConfig None
                    |> Array.map (fun entry -> entry.Title, entry.Href.Substring (entry.Href.IndexOf '#' + 1))
                    |> Array.toList
                    |> List.sort

                let scanned = scanFixture ()
                Expect.isNonEmpty scanned "the scan finds entries"
                Expect.equal found scanned "the same titles and names"
        ]
