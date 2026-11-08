module Rocq.Navigator.Tests.IslandTests

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Nacara.Core
open Nacara.Plugins

let private temporaryRoot () =
    let path =
        Path.Combine (Path.GetTempPath (), "rocq-navigator-" + Guid.NewGuid().ToString "N")

    Directory.CreateDirectory path |> ignore
    AbsolutePath.create path

let private build (configure: Site -> Site) =
    let root = temporaryRoot ()

    let site =
        Site.create "Spike"
        |> Site.baseUrl "/"
        |> Site.output "output"
        |> Site.noStaticFiles
        |> Markdown.register
        |> Spike.register
        |> configure

    root, Build.run root site

let private read (root: AbsolutePath) (path: string) =
    File.ReadAllText (Path.Combine (AbsolutePath.value root, "output", path))

let private count (pattern: string) (text: string) =
    Regex.Matches(text, pattern, RegexOptions.IgnoreCase).Count

let private articleOf (html: string) =
    let start = html.IndexOf "<article"
    html.Substring (start, html.IndexOf "</article>" - start)

let private ruleCodes (result: BuildResult) =
    result.Diagnostics |> List.map _.Code

let tests =
    testList
        "islands in a Nacara build"
        [
            testCase "a generated page is written at a literal .html route under the prefix"
            <| fun () ->
                let root, _ = build id

                Expect.isTrue
                    (File.Exists (Path.Combine (AbsolutePath.value root, "output", "proofs", "Mod.A.html")))
                    "the first page keeps its dotted, cased name"

            testCase "the head carries no mark island hydration would remove"
            <| fun () ->
                let root, _ = build id
                let html = read root "proofs/Mod.A.html"
                Expect.stringContains html "<title>Mod.A</title>" "the title is plain"
                Expect.isFalse (html.Contains "data-ranvier-head") "no head element is marked"

            testCase "the document has exactly one doctype, one html and one body"
            <| fun () ->
                let root, _ = build id
                let html = read root "proofs/Mod.A.html"
                Expect.equal (count "<!doctype" html) 1 "one doctype"
                Expect.equal (count "<html" html) 1 "one html"
                Expect.equal (count "<body" html) 1 "one body"

            testCase "each island host holds its own root marker, outside the article"
            <| fun () ->
                let root, _ = build id
                let html = read root "proofs/Mod.A.html"
                Expect.stringContains html """<div data-island="a"><!--[r""" "first island"
                Expect.stringContains html """<div data-island="b"><!--[r""" "second island"
                Expect.isFalse ((articleOf html).Contains "<!--[r") "the article has no markers"

            testCase "the Markdown transform leaves a .rocqpage body alone"
            <| fun () ->
                let root, _ = build id
                Expect.stringContains (read root "proofs/Mod.A.html") "*a* # b" "the body is byte-exact"

            testCase "LinkValidator accepts a site URL to an anchor on a literal .html route"
            <| fun () ->
                let _, result = build LinkValidator.register

                Expect.isEmpty
                    (ruleCodes result
                     |> List.filter (fun code -> code.StartsWith "link"))
                    "no link problems"

            testCase "LinkValidator rejects a missing anchor on a literal .html route"
            <| fun () ->
                let root = temporaryRoot ()

                let site =
                    Site.create "Spike"
                    |> Site.baseUrl "/"
                    |> Site.output "output"
                    |> Site.noStaticFiles
                    |> Spike.registerWith (Spike.brokenLink true)
                    |> LinkValidator.register

                let result = Build.run root site
                Expect.exists (ruleCodes result) (fun code -> code.EndsWith "anchor-missing") "the missing anchor"

            testCase "a site with no bundle warns and still writes its pages"
            <| fun () ->
                let root = temporaryRoot ()

                let site =
                    Site.create "Spike"
                    |> Site.baseUrl "/"
                    |> Site.output "output"
                    |> Site.noStaticFiles
                    |> Spike.registerWith (Spike.bundle None)

                let result = Build.run root site
                Expect.contains (ruleCodes result) "rocq/client-missing" "the warning"
                let html = read root "proofs/Mod.A.html"
                Expect.stringContains html "<article" "the page reads"
                Expect.isFalse (html.Contains "navigator.js") "no script points at a missing file"
        ]
