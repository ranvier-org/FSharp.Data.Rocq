module Rocq.Navigator.Tests.PluginTests

open System
open System.IO
open System.Net
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions
open Expecto
open Feliz.ViewEngine
open FSharp.Data.Rocq.Project
open Nacara.Core
open Nacara.Plugins
open Rocq.Navigator
open Rocq.Navigator.Article
open Rocq.Navigator.Model
open Rocq.Navigator.Tests

let private withRoot (run: AbsolutePath -> 'a) : 'a =
    let path =
        Path.Combine (Path.GetTempPath (), "rocq-navigator-" + Guid.NewGuid().ToString "N")

    Directory.CreateDirectory path |> ignore

    try
        run (AbsolutePath.create path)
    finally
        Directory.Delete (path, true)

let private fixtureOptions () =
    Rocq.defaults
    |> Rocq.project (Path.Combine (FixtureFiles.fixtureDir (), "_CoqProject"))

let private siteWith (options: RocqOptions) =
    Site.create "Test"
    |> Site.baseUrl "/"
    |> Site.output "output"
    |> Site.noStaticFiles
    |> Rocq.register options
    |> Site.collection (Rocq.collection "proofs" options)

let private buildAt (root: AbsolutePath) (site: Site) =
    Build.run root site

let private read (root: AbsolutePath) (path: string) =
    File.ReadAllText (Path.Combine (AbsolutePath.value root, "output", path))

let private codes (result: BuildResult) =
    result.Diagnostics |> List.map _.Code

let private errors (result: BuildResult) =
    result.Diagnostics
    |> List.filter (fun diagnostic -> diagnostic.Code <> "rocq/client-missing")

/// A copy of the fixture project whose named source has text appended, with the `.glob` digest updated to match.
let private fixtureCopy (root: AbsolutePath) (appendTo: (string * string) option) =
    let target = Path.Combine (AbsolutePath.value root, "project")
    let source = FixtureFiles.fixtureDir ()

    for file in Directory.GetFiles (source, "*", SearchOption.AllDirectories) do
        let relative = Path.GetRelativePath (source, file)

        if
            relative = "_CoqProject"
            || relative.EndsWith ".v"
            || relative.EndsWith ".glob"
        then
            let copied = Path.Combine (target, relative)

            Directory.CreateDirectory (Path.GetDirectoryName copied)
            |> ignore

            File.Copy (file, copied)

    match appendTo with
    | None -> ()
    | Some (name, text) ->
        let vFile = Path.Combine (target, "theories", name + ".v")
        let bytes = Array.append (File.ReadAllBytes vFile) (Encoding.UTF8.GetBytes text)
        File.WriteAllBytes (vFile, bytes)
        let digest = (Convert.ToHexString (MD5.HashData bytes)).ToLowerInvariant()
        let glob = Path.ChangeExtension (vFile, ".glob")

        File.WriteAllText (glob, Regex.Replace (File.ReadAllText glob, @"^DIGEST \S+", $"DIGEST {digest}"))

    Path.Combine (target, "_CoqProject")

let private links =
    {
        Module = fun name -> $"/{name}.html"
        Pipeline = Docs.pipeline []
    }

let private guideIndex (project: RocqProject) =
    project.Files
    |> Array.findIndex (fun file -> file.Module.EndsWith "Guide")

let private fact (kind: string option) =
    {
        Type = kind
        Binders = Map.empty
        Assumptions = None
    }

let private factsOf (project: RocqProject) (symbols: (string * SymbolFact) list) =
    {
        RocqVersion = "test"
        Digests =
            project.Files
            |> Array.map (fun file -> file.Path, file.Digest)
            |> Map.ofArray
        Symbols = Map.ofList symbols
    }

let private cardsOf (root: AbsolutePath) (moduleName: string) =
    match cardsWire.Decode (read root $"data/{moduleName}.symbols.json") with
    | Ok cards -> cards
    | Result.Error error -> failwith $"{error}"

let private urls (html: string) =
    Regex.Matches (html, "(?:href|src)=\"([^\"]*)\"")
    |> Seq.map _.Groups[1].Value
    |> List.ofSeq

let private tests' =
    [
        testCase "codecs round-trip CardData and SearchEntry, including text with quotes and Unicode"
        <| fun () ->
            let card =
                {
                    Key = "s1"
                    Name = "a\"∀"
                    Qualified = "M.a\"∀"
                    Module = "M"
                    Href = Some "/M.html#a"
                    Signature = "∀ x : \"∅\", x"
                    SignatureIsType = true
                    DocHtml = Some "<p>\"q\" → </p>"
                    Mentions = 3
                    Badge = Some (Rests [| "ax" |])
                    External = false
                }

            let cards =
                [|
                    card
                    { card with
                        Href = None
                        DocHtml = None
                        Badge = Some NoAxioms
                    }
                    { card with Badge = None }
                |]

            Expect.equal (cardsWire.Decode (cardsWire.Encode cards)) (Ok cards) "the cards"

            let entries =
                [|
                    {
                        Name = "a\"∘"
                        Qualified = "M.a"
                        Module = "M"
                        Kind = "prf"
                        Href = "/M.html#a"
                        Summary = "∧ \"s\""
                    }
                |]

            Expect.equal (searchWire.Decode (searchWire.Encode entries)) (Ok entries) "the entries"

        testCase "a card prefers the captured type, and falls back to the source header"
        <| fun () ->
            let project = FixtureFiles.fixtureProject ()

            let facts =
                factsOf project [ "Fixture.Guide.sum_refl", fact (Some "forall a b : nat, a + b = a + b") ]

            let cards = Cards.forModule project links (Some facts) (guideIndex project)

            let named (name: string) =
                cards |> Array.find (fun card -> card.Name = name)

            Expect.equal (named "sum_refl").Signature "forall a b : nat, a + b = a + b" "the captured type"
            Expect.isTrue (named "sum_refl").SignatureIsType "a type"
            Expect.equal (named "sum_zero").Signature "Corollary sum_zero : 0 + 0 = 0" "the source header"
            Expect.isFalse (named "sum_zero").SignatureIsType "source text"

        testCase "an external card has no Href and External = true"
        <| fun () ->
            let project = FixtureFiles.fixtureProject ()
            let cards = Cards.forModule project links None (guideIndex project)

            let nat =
                cards
                |> Array.find (fun card -> card.Name = "nat")

            Expect.isNone nat.Href "no link"
            Expect.isTrue nat.External "external"
            Expect.equal nat.Qualified "Corelib.Init.Datatypes.nat" "qualified"

        testCase "an admitted theorem has no badge, even with an assumption fact"
        <| fun () ->
            let project = FixtureFiles.fixtureProject ()

            let facts =
                factsOf
                    project
                    [
                        "Fixture.Guide.open_goal",
                        { fact None with
                            Assumptions = Some Closed
                        }
                        "Fixture.Guide.sum_zero",
                        { fact None with
                            Assumptions = Some Closed
                        }
                    ]

            let cards = Cards.forModule project links (Some facts) (guideIndex project)

            let named (name: string) =
                cards |> Array.find (fun card -> card.Name = name)

            Expect.isNone (named "open_goal").Badge "no pass for an open proof"
            Expect.equal (named "sum_zero").Badge (Some NoAxioms) "a pass for a finished proof"

        testCase "reference lists show written notations and leave raw names out"
        <| fun () ->
            let project = FixtureFiles.fixtureProject ()

            let names =
                blocks project links (guideIndex project)
                |> Array.collect (fun block ->
                    match block with
                    | Documented (_, _, _, _, _, refs)
                    | Quiet (_, _, _, _, refs) -> Array.append refs.Uses refs.UsedBy
                    | _ -> [||])
                |> Array.map _.Name

            Expect.isFalse
                (names
                 |> Array.exists (fun name -> name.Contains "::" || name.Contains "_'"))
                "no raw notation name"

            Expect.contains names "∅" "the notation's written text"

        testCase "build: all modules render, and every declaration has its anchor"
        <| fun () ->
            withRoot (fun root ->
                let result = buildAt root (siteWith (fixtureOptions ()))

                Expect.equal
                    (errors result
                     |> List.map (fun d -> d.Code + ": " + d.Message))
                    []
                    "no diagnostics"

                let project = FixtureFiles.fixtureProject ()

                for file in project.Files do
                    let html = read root $"{file.Module}.html"

                    for _, anchor in Map.toList (Anchors.forFile file) do
                        Expect.stringContains html ("id=\"" + WebUtility.HtmlEncode anchor + "\"") $"{file.Module}#{anchor}"

                Expect.stringContains (read root "index.html") "Reflexivity of sums" "the Guarantees page")

        testCase "build: the document has one doctype and an unmarked head, and islands sit in their hosts outside the article"
        <| fun () ->
            withRoot (fun root ->
                buildAt root (siteWith (fixtureOptions ()))
                |> ignore

                let html = read root "Fixture.Guide.html"

                let count (pattern: string) =
                    Regex.Matches(html, pattern, RegexOptions.IgnoreCase).Count

                Expect.equal (count "<!doctype") 1 "one doctype"
                Expect.equal (count "<html") 1 "one html"
                Expect.stringContains html "<title>Fixture.Guide</title>" "the title is plain"
                Expect.isFalse (html.Contains "data-ranvier-head") "no head mark"

                for name in Islands.names do
                    Expect.stringContains html $"""<div data-island="{name}"><!--[r""" name

                let start = html.IndexOf "<article"
                let article = html.Substring (start, html.LastIndexOf "</article>" - start)
                Expect.isFalse (article.Contains "<!--[r") "the article has no markers")

        testCase "the bundle decides the script tag and the warning"
        <| fun () ->
            withRoot (fun root ->
                let result = buildAt root (siteWith (fixtureOptions ()))
                let html = read root "Fixture.Guide.html"

                match RocqReader.bundle.Value with
                | Some _ ->
                    Expect.stringContains html "navigator.js" "the script"
                    Expect.isFalse (List.contains "rocq/client-missing" (codes result)) "no warning"
                | None ->
                    Expect.isFalse (html.Contains "navigator.js") "no script points at a missing file"
                    Expect.contains (codes result) "rocq/client-missing" "the warning"
                    Expect.stringContains html "<article" "the page reads")

        testCase "build: LinkValidator reports nothing, and every data-ref has a card"
        <| fun () ->
            withRoot (fun root ->
                let result =
                    buildAt
                        root
                        (siteWith (fixtureOptions ())
                         |> LinkValidator.register)

                Expect.equal
                    (errors result
                     |> List.map (fun d -> d.Code + ": " + d.Message))
                    []
                    "no diagnostics"

                let project = FixtureFiles.fixtureProject ()

                for file in project.Files do
                    let cards = cardsOf root file.Module |> Array.map _.Key |> set
                    let keys = Cards.keysIn (read root $"{file.Module}.html")
                    Expect.isNonEmpty keys $"{file.Module} has references"

                    for key in keys do
                        Expect.isTrue (cards.Contains key) $"{file.Module}: {key}"

                Expect.isTrue (File.Exists (Path.Combine (AbsolutePath.value root, "output", "data", "search.json"))) "search.json")

        testCase "build: a broken comment link is a diagnostic naming the page and link"
        <| fun () ->
            withRoot (fun root ->
                let project =
                    fixtureCopy root (Some ("Guide", "\n(**md A [bad](Missing.html#nope) link and [gone](Fixture.Other.html#gone). *)\n"))

                let result =
                    buildAt
                        root
                        (siteWith (fixtureOptions () |> Rocq.project project)
                         |> LinkValidator.register)

                let broken =
                    result.Diagnostics
                    |> List.find (fun diagnostic -> diagnostic.Code = "rocq/comment-link-broken")

                Expect.stringContains broken.Message "Fixture.Guide" "the page"
                Expect.stringContains broken.Message "Missing.html#nope" "the link"

                Expect.exists
                    result.Diagnostics
                    (fun diagnostic ->
                        diagnostic.Code.EndsWith "anchor-missing"
                        && diagnostic.Message.Contains "gone")
                    "the missing anchor")

        testCase "build: a data-ref with no card is the diagnostic rocq/ref-without-card"
        <| fun () ->
            withRoot (fun root ->
                let options = fixtureOptions ()

                let site =
                    Site.create "Test"
                    |> Site.baseUrl "/"
                    |> Site.output "output"
                    |> Site.noStaticFiles
                    |> Rocq.register options
                    |> Site.collection (
                        Rocq.collection "proofs" options
                        |> Collection.layout (fun _ -> Html.rawText """<html><body><span data-ref="s999999">x</span></body></html>""")
                    )

                let result = buildAt root site

                let missing =
                    result.Diagnostics
                    |> List.find (fun diagnostic -> diagnostic.Code = "rocq/ref-without-card")

                Expect.stringContains missing.Message "s999999" "the key")

        testCase "build: a non-empty Root and a subpath baseUrl put every URL under them"
        <| fun () ->
            withRoot (fun root ->
                let options = fixtureOptions () |> Rocq.root "proofs"

                let site =
                    Site.create "Test"
                    |> Site.baseUrl "/sub/"
                    |> Site.output "output"
                    |> Site.noStaticFiles
                    |> Rocq.register options
                    |> LinkValidator.register
                    |> Site.collection (Rocq.collection "proofs" options)

                let result = buildAt root site

                Expect.equal
                    (errors result
                     |> List.map (fun d -> d.Code + ": " + d.Message))
                    []
                    "no diagnostics"

                for page in [ "proofs/Fixture.Guide.html"; "proofs/index.html" ] do
                    let html = read root page

                    for url in urls html do
                        if not (url.StartsWith "#") then
                            Expect.stringStarts url "/sub/" $"{page}: {url}"

                            if url.Contains ".html" then
                                Expect.stringStarts url "/sub/proofs/" $"{page}: {url}"

                    Expect.stringContains html "data-symbols=\"/sub/proofs/data/" "the card data URL"

                Expect.isTrue (File.Exists (Path.Combine (AbsolutePath.value root, "output", "proofs", "data", "search.json"))) "the data files")

        testCase "build: without facts the site builds, with no badges"
        <| fun () ->
            withRoot (fun root ->
                let result = buildAt root (siteWith (fixtureOptions ()))

                Expect.equal
                    (errors result
                     |> List.map (fun d -> d.Code + ": " + d.Message))
                    []
                    "no diagnostics"

                Expect.isFalse ((read root "Fixture.Guide.html").Contains "class=\"badge") "no badge in the article"
                Expect.isFalse ((read root "index.html").Contains "class=\"badge") "no badge on Guarantees")

        testCase "build: facts give badges, and a falsified theorem never passes"
        <| fun () ->
            withRoot (fun root ->
                let project = FixtureFiles.fixtureProject ()

                let closed =
                    { fact None with
                        Assumptions = Some Closed
                    }

                let factsPath = Path.Combine (AbsolutePath.value root, "facts.json")

                Facts.write
                    factsPath
                    (factsOf
                        project
                        [
                            "Fixture.Guide.sum_refl", closed
                            "Fixture.Guide.zero_absorbs_falsified", closed
                        ])

                let options =
                    fixtureOptions ()
                    |> Rocq.facts (Some factsPath)
                    |> Rocq.config (Some (Path.Combine (FixtureFiles.fixtureDir (), "navigator.json")))

                let result = buildAt root (siteWith options)

                Expect.equal
                    (errors result
                     |> List.map (fun d -> d.Code + ": " + d.Message))
                    []
                    "no diagnostics"

                let badges =
                    Regex.Matches(read root "Fixture.Guide.html", "class=\"badge pass\"").Count

                Expect.equal badges 1 "the one finished theorem"

                let falsified =
                    cardsOf root "Fixture.Guide"
                    |> Array.find (fun card -> card.Name = "zero_absorbs_falsified")

                Expect.isNone falsified.Badge "the card")

        testCase "build: stale facts give rocq/facts-stale, and DropStaleFacts proceeds"
        <| fun () ->
            withRoot (fun root ->
                let project = FixtureFiles.fixtureProject ()
                let factsPath = Path.Combine (AbsolutePath.value root, "facts.json")

                Facts.write
                    factsPath
                    { factsOf project [] with
                        Digests =
                            project.Files
                            |> Array.map (fun file -> file.Path, "0000")
                            |> Map.ofArray
                    }

                let options = fixtureOptions () |> Rocq.facts (Some factsPath)
                let stale = buildAt root (siteWith options)
                Expect.contains (codes stale) "rocq/facts-stale" "the error"

                let dropped = buildAt root (siteWith (options |> Rocq.dropStaleFacts true))
                Expect.isFalse (List.contains "rocq/facts-stale" (codes dropped)) "no error"
                Expect.stringContains (read root "Fixture.Guide.html") "<article" "the page")

        testCase "build: a missing .glob gives rocq/project-load-failed, not an exception"
        <| fun () ->
            withRoot (fun root ->
                let project = fixtureCopy root None
                File.Delete (Path.Combine (Path.GetDirectoryName project, "theories", "Other.glob"))
                let result = buildAt root (siteWith (fixtureOptions () |> Rocq.project project))
                Expect.contains (codes result) "rocq/project-load-failed" "the error")

        testCase "build: a second run with no changes rewrites nothing"
        <| fun () ->
            withRoot (fun root ->
                let site = siteWith (fixtureOptions ())
                Expect.isGreaterThan (buildAt root site).WrittenFiles 0 "the first run writes"
                Expect.equal (buildAt root site).WrittenFiles 0 "the second run does not")

        testCase "host: Nuglify.minifyHtml on, and written statements still equal the .v bytes"
        <| fun () ->
            withRoot (fun root ->
                let result = buildAt root (siteWith (fixtureOptions ()) |> Nuglify.minifyHtml)

                Expect.equal
                    (errors result
                     |> List.map (fun d -> d.Code + ": " + d.Message))
                    []
                    "no diagnostics"

                let project = FixtureFiles.fixtureProject ()

                // Nuglify writes a source CRLF as LF, which is also how an HTML parser reads a CRLF.
                let text (html: string) =
                    (Source.stripTags html).Replace("\r\n", "\n")

                for index, file in Array.indexed project.Files do
                    let html = read root $"{file.Module}.html"

                    let written =
                        Regex.Matches (html, "<pre class=\"?(statement|command)\"?><code>(.*?)</code></pre>", RegexOptions.Singleline)
                        |> Seq.map (fun m -> Source.stripTags m.Groups[2].Value)
                        |> Set.ofSeq

                    let proofs =
                        Regex.Matches (html, "</summary><pre><code>(.*?)</code></pre>", RegexOptions.Singleline)
                        |> Seq.map (fun m -> Source.stripTags m.Groups[1].Value)
                        |> Set.ofSeq

                    for block in blocks project links index do
                        match block with
                        | Documented (_, anchor, _, statement, blockProofs, _)
                        | Quiet (_, anchor, statement, blockProofs, _) ->
                            Expect.isTrue (written.Contains (text statement)) $"{file.Module}#{anchor} statement"

                            for proof in blockProofs do
                                Expect.isTrue (proofs.Contains (text proof.Html)) $"{file.Module}#{anchor} proof"
                        | _ -> ())

        testCase "host: a Markdown collection beside this one keeps its pages and its layout"
        <| fun () ->
            withRoot (fun root ->
                let content = Path.Combine (AbsolutePath.value root, "guide")
                Directory.CreateDirectory content |> ignore
                File.WriteAllText (Path.Combine (content, "hello.md"), "---\ntitle: Hello\n---\n# Hi *there*\n")

                let options = fixtureOptions () |> Rocq.root "proofs"

                let guide =
                    Collection.create "guide" (Decode.object (fun get -> get.Required.Field "title" Decode.string))
                    |> Collection.title id
                    |> Collection.layout (fun context -> Html.rawText $"""<html><body><main class="host">{context.Content}</main></body></html>""")

                let site =
                    Site.create "Test"
                    |> Site.baseUrl "/"
                    |> Site.output "output"
                    |> Site.noStaticFiles
                    |> Markdown.register
                    |> Rocq.register options
                    |> Site.collection guide
                    |> Site.collection (Rocq.collection "proofs" options)

                let result = buildAt root site

                Expect.equal
                    (errors result
                     |> List.map (fun d -> d.Code + ": " + d.Message))
                    []
                    "no diagnostics"

                let hello = read root "hello/index.html"
                Expect.stringContains hello "<main class=\"host\">" "the host's layout"
                Expect.stringContains hello "<em>there</em>" "Markdown still runs"
                Expect.isFalse (hello.Contains "data-island") "no islands"

                let page = read root "proofs/Fixture.Guide.html"
                Expect.isFalse (page.Contains "class=\"host\"") "the plugin's layout"
                Expect.stringContains page "&lt;script&gt;" "the page is the plugin's own")

        testCase "rocq-capture --help prints its arguments"
        <| fun () ->
            let registry = (Rocq.create (fixtureOptions ())).Configure Registry.empty

            let command =
                registry.Commands
                |> List.find (fun command -> command.Name = "rocq-capture")

            let help = command.Help |> Option.defaultValue ""
            Expect.stringContains help "--rocq" "the command argument"
            Expect.stringContains help "--out" "the output argument"

        testCase "string edits report a missing host or head, and the doctype goes by match"
        <| fun () ->
            Expect.isError (RocqReader.fillHost "search" "x" "<html></html>") "a missing host"
            Expect.isError (RocqReader.unmarkHead "<html></html>") "a missing head"
            Expect.isError (RocqReader.insertInHead "x" "<html></html>") "a missing head"
            Expect.equal (RocqReader.withoutDoctype "<!DOCTYPE html><html>") "<html>" "the doctype"
            Expect.equal (RocqReader.withoutDoctype "<html>") "<html>" "nothing to remove"
    ]

let tests = testList "plugin" tests'
