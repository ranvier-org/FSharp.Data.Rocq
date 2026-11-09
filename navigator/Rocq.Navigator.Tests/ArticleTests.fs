module Rocq.Navigator.Tests.ArticleTests

open System
open System.IO
open System.Net
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions
open Expecto
open FSharp.Data.Rocq.Project
open FSharp.Data.Rocq.Structure
open Nacara.Core
open Nacara.Plugins
open Rocq.Navigator
open Rocq.Navigator.Article
open Rocq.Navigator.Tests

let private links =
    {
        Module = fun name -> $"/site/{name}.html"
        Pipeline = Docs.pipeline []
    }

let private collected (text: string) =
    let model = analyze "M" (Encoding.UTF8.GetBytes text) None
    let chosen = collectedImports model

    [
        for sentence in model.Sentences do
            if chosen.Contains sentence.Index then
                Encoding.UTF8.GetString (model.Bytes, sentence.Span.Start, sentence.Span.End - sentence.Span.Start)
    ]

let private guideIndex (project: RocqProject) =
    project.Files
    |> Array.findIndex (fun file -> file.Path.EndsWith "Guide.v")

let private guideBlocks () =
    let project = FixtureFiles.fixtureProject ()
    project, blocks project links (guideIndex project)

let private sourceHtml (block: Block) =
    match block with
    | Documented (_, _, _, _, statement, proofs, _)
    | Quiet (_, _, statement, proofs, _) ->
        statement
        :: [
            for proof in proofs do
                proof.Html
        ]
    | Command html
    | Import html -> [ html ]
    | Heading _
    | Prose _ -> []

let private decodedSource (blocks: Block[]) =
    blocks
    |> Array.toList
    |> List.collect sourceHtml
    |> List.map Source.stripTags
    |> String.concat ""

/// The file's bytes with each comment outside every declaration and sentence removed.
let private expectedSource (file: RocqFile) =
    let model = file.Model

    let insideDeclaration (comment: Comment) =
        model.Declarations
        |> Array.exists (fun declaration ->
            declaration.Extent.Start <= comment.Span.Start
            && comment.Span.End <= declaration.Extent.End)

    let removed =
        model.Comments
        |> Array.filter (fun comment ->
            comment.Sentence.IsNone
            && not (insideDeclaration comment))
        |> Array.map _.Span

    let out = ResizeArray<byte>()
    let mutable cursor = 0

    for span in removed do
        out.AddRange model.Bytes[cursor .. span.Start - 1]
        cursor <- span.End

    out.AddRange model.Bytes[cursor..]
    Encoding.UTF8.GetString (out.ToArray ())

let private declarationBlock (blocks: Block[]) (anchor: string) =
    blocks
    |> Array.find (function
        | Documented (_, name, _, _, _, _, _)
        | Quiet (_, name, _, _, _) -> name = anchor
        | _ -> false)

let private noRefs = { Uses = [||]; UsedBy = [||] }

let private item (index: int) =
    {
        Name = $"name{index}"
        Key = $"s{index}"
        Href = Some $"/p.html#name{index}"
    }

let tests =
    testList
        "article"
        [
            testCase "anchors: a primed name keeps its prime and resolves through getElementById-style matching"
            <| fun () ->
                let project, blocks = guideBlocks ()
                let anchors = Anchors.forFile project.Files[guideIndex project]
                Expect.contains (anchors |> Map.values) "sum_refl'" "the prime stays"
                let html = toHtml blocks

                let ids =
                    [
                        for m in Regex.Matches (html, "\\bid=\"([^\"]*)\"") do
                            WebUtility.HtmlDecode m.Groups[1].Value
                    ]

                Expect.contains ids "sum_refl'" "the decoded id equals the anchor"

            testCase "anchors: a repeated name keeps #name first, then #name~2"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()
                let file = project.Files[guideIndex project]
                let anchors = Anchors.forFile file

                let shared =
                    file.Model.Declarations
                    |> Array.filter (fun declaration -> declaration.Name = Some "shared")
                    |> Array.map (fun declaration -> anchors[declaration.Index])

                Expect.equal shared [| "shared"; "shared~2" |] "in source order"
                Expect.equal (Seq.length (Seq.distinct (Map.values anchors))) anchors.Count "every anchor is unique"

            testCase "anchors: an unnamed declaration gets #d<index>, and a Unicode name stays readable"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()
                let file = project.Files[guideIndex project]

                let unnamed =
                    { file with
                        Model =
                            { file.Model with
                                Declarations =
                                    file.Model.Declarations
                                    |> Array.mapi (fun index declaration ->
                                        if index = 2 then
                                            { declaration with Name = None }
                                        else
                                            declaration)
                            }
                    }

                let anchors = Anchors.forFile unnamed
                Expect.equal anchors[2] "d2" "by index"
                Expect.equal anchors[0] "d0" "a notation's encoded name is not an anchor"
                Expect.contains (anchors |> Map.values) "αβ_law" "readable"

            testCase "anchors: a heading slug is lowercase words joined by dashes, and a route sits under the root"
            <| fun () ->
                Expect.equal (Anchors.slug "The Sum, of αβ!") "h-the-sum-of-αβ" "slug"
                let locale = Locale.root "en"
                Expect.equal (Anchors.routeOf "" locale "A.B") (Route.file locale "A.B.html") "no root"
                Expect.equal (Anchors.routeOf "proofs/" locale "A.B") (Route.file locale "proofs/A.B.html") "a root"

            testCase "blocks: source order, and every sentence appears exactly once"
            <| fun () ->
                let project, blocks = guideBlocks ()
                let file = project.Files[guideIndex project]
                let decoded = decodedSource blocks
                let mutable position = 0

                for sentence in file.Model.Sentences do
                    let text = Source.text file sentence.Span
                    let found = decoded.IndexOf (text, position, System.StringComparison.Ordinal)
                    Expect.isGreaterThanOrEqual found 0 $"sentence {sentence.Index} follows the one before it"
                    position <- found + text.Length

                let declarations =
                    blocks
                    |> Array.choose (function
                        | Documented (decl, _, _, _, _, _, _)
                        | Quiet (decl, _, _, _, _) -> Some decl
                        | _ -> None)

                Expect.equal declarations.Length file.Model.Declarations.Length "one block per declaration"
                Expect.equal declarations (Array.sort declarations) "in source order"

                let proofStarts =
                    blocks
                    |> Array.filter (function
                        | Command html -> html.Contains "reflexivity"
                        | _ -> false)

                Expect.isEmpty proofStarts "tactics are inside proof blocks, not commands"

            testCase "blocks: an attached doc sits on its declaration, an unattached comment becomes Prose"
            <| fun () ->
                let _, blocks = guideBlocks ()

                match declarationBlock blocks "sum_refl" with
                | Documented (_, _, doc, _, statement, proofs, _) ->
                    Expect.stringContains doc "Reflexivity of sums" "the doc is on the declaration"
                    Expect.stringStarts (Source.stripTags statement) "Theorem sum_refl" "the statement follows"
                    Expect.equal proofs.Length 1 "one proof"
                | other -> failtestf "expected a documented block, got %A" other

                Expect.isTrue
                    (match declarationBlock blocks "sum_refl'" with
                     | Quiet _ -> true
                     | _ -> false)
                    "no comment, no prose"

                let prose =
                    blocks
                    |> Array.choose (function
                        | Prose html -> Some html
                        | _ -> None)
                    |> String.concat ""

                Expect.stringContains prose "An unattached comment." "the comment is prose"
                Expect.stringContains prose "An introduction" "the text after a heading is prose"
                Expect.stringContains prose "/site/Fixture.Other.html#other_def" "a module link resolves"

                let headings =
                    blocks
                    |> Array.choose (function
                        | Heading (level, text, anchor) -> Some (level, text, anchor)
                        | _ -> None)

                Expect.equal headings [| 1, "Guide", "h-guide"; 2, "Theorems", "h-theorems" |] "headings from the file model"

            testCase "toHtml: a proof is a closed details labelled with its ending"
            <| fun () ->
                let _, blocks = guideBlocks ()
                let html = toHtml [| declarationBlock blocks "sum_refl" |]
                Expect.stringContains html """<details class="proof"><summary>Qed</summary>""" "closed, with the ending"
                Expect.isFalse (html.Contains "<details open") "nothing starts open"

            testCase "toHtml: a statement and its proofs share a pane, with the proofs last"
            <| fun () ->
                let _, blocks = guideBlocks ()
                let html = toHtml [| declarationBlock blocks "sum_refl" |]
                let pane = html.IndexOf """<div class="panel"><pre class="statement">"""
                Expect.isGreaterThan pane -1 "the statement opens the pane"
                Expect.isGreaterThan (html.IndexOf """<details class="proof">""") pane "the proof is after the statement"
                Expect.stringContains html "</details></div>" "the proof closes the pane"

            testCase "toHtmlWith: code bodies stay plain unless an option asks otherwise"
            <| fun () ->
                let html =
                    toHtmlWith Config.none (fun _ -> "") [| Quiet (0, "a", "one\ntwo\nthree\n", [||], noRefs) |]

                Expect.isFalse (html.Contains "details") "no fold"

            testCase "toHtmlWith: collapsibleCode makes the first line the fold, starting open"
            <| fun () ->
                let config =
                    { Config.none with
                        CollapsibleCode = true
                    }

                let html =
                    toHtmlWith config (fun _ -> "") [| Quiet (0, "a", "one &lt; x\n  two\n", [||], noRefs) |]

                Expect.stringContains
                    html
                    """<details class="body" open><summary data-lines="2 lines"><pre class="statement"><code><span class="l">one &lt; x"""
                    "an open fold, with the first line as its summary"

                Expect.stringContains
                    html
                    """</summary><pre class="statement"><code><span class="l" style="--i:2">  two"""
                    "the rest follows, carrying its indent"

            testCase "toHtmlWith: a one-line body has nothing to fold"
            <| fun () ->
                let config =
                    { Config.none with
                        CollapsibleCode = true
                        CollapseCodeOver = Some 1
                    }

                let html =
                    toHtmlWith config (fun _ -> "") [| Quiet (0, "a", "one.\n", [||], noRefs) |]

                Expect.isFalse (html.Contains "details") "no fold"

            testCase "toHtml: a page's imports collect in one closed block after its opening prose"
            <| fun () ->
                let html =
                    toHtml
                        [|
                            Import "From A Require Import B."
                            Command "\n"
                            Heading (1, "Title", "h-title")
                            Prose "intro"
                            Quiet (0, "a", "one.", [||], noRefs)
                            Import "  Import C."
                        |]

                Expect.stringContains
                    html
                    """<details class="imports"><summary>2</summary><pre class="command"><code><span class="l">From A Require Import B."""
                    "closed, with the count"

                Expect.stringContains html """<span class="l">Import C.</span></code></pre></details>""" "a later import joins them"
                Expect.isLessThan (html.IndexOf "intro") (html.IndexOf "details") "after the opening prose"
                Expect.isLessThan (html.IndexOf "details") (html.IndexOf "one.") "before the first declaration"

            testCase "collectsAsImport: imports and scope opens, and nothing else"
            <| fun () ->
                Expect.isTrue (collectsAsImport Category.Import (Some "From")) "an import"
                Expect.isTrue (collectsAsImport Category.Directive (Some "Open Scope")) "a scope open"
                Expect.isFalse (collectsAsImport Category.Directive (Some "Close Scope")) "a scope close"
                Expect.isFalse (collectsAsImport Category.Directive None) "no keyword"

            testCase "toHtml: a page with no imports has no imports block"
            <| fun () -> Expect.isFalse ((toHtml [| Prose "intro" |]).Contains "imports") "no block"

            testCase "toHtml: an empty page puts a lone import's block at the end"
            <| fun () -> Expect.stringContains (toHtml [| Import "Import A." |]) "Import A." "shown"

            testCase "collectedImports: a top-level block before the first declaration, and only that"
            <| fun () ->
                Expect.equal
                    (collected "From A Require Import B.\nImport C.\nOpen Scope nat_scope.\nDefinition x := 1.\nImport D.\nOpen Scope list_scope.")
                    [ "From A Require Import B."; "Import C."; "Open Scope nat_scope." ]
                    "the block before the declaration"

            testCase "collectedImports: an import inside a section stays in place"
            <| fun () -> Expect.isEmpty (collected "Section S. Import L. End S.") "inside the section"

            testCase "collectedImports: an import after a module stays in place"
            <| fun () -> Expect.isEmpty (collected "Module M. End M. Import M.") "after the module"

            testCase "collectedImports: a local scope open inside a section stays in place, and at top level it joins"
            <| fun () ->
                Expect.isEmpty (collected "Section S.\n  Local Open Scope nat_scope.\nEnd S.") "inside the section"

                Expect.equal
                    (collected "Local Open Scope x.\nGlobal Open Scope y.\nDefinition z := 1.")
                    [ "Local Open Scope x."; "Global Open Scope y." ]
                    "at top level"

            testCase "blocks: an import after a declaration stays a command with its indent bytes"
            <| fun () ->
                let root = Path.Combine (Path.GetTempPath (), Path.GetRandomFileName ())
                Directory.CreateDirectory root |> ignore

                try
                    let source = Encoding.UTF8.GetBytes "Import A.\nDefinition x := 1.\n  Import B.\n"
                    File.WriteAllText (Path.Combine (root, "_CoqProject"), "-Q . T\nM.v\n")
                    File.WriteAllBytes (Path.Combine (root, "M.v"), source)
                    let digest = (Convert.ToHexString (MD5.HashData source)).ToLowerInvariant()
                    File.WriteAllText (Path.Combine (root, "M.glob"), $"DIGEST {digest}\nFT.M\n")

                    match load (Path.Combine (root, "_CoqProject")) with
                    | Result.Error failures -> failwith $"%A{failures}"
                    | Ok project ->
                        let found = blocks project links 0

                        let imports =
                            found
                            |> Array.choose (function
                                | Import html -> Some (Source.stripTags html)
                                | _ -> None)

                        let commands =
                            found
                            |> Array.choose (function
                                | Command html when html.Contains "Import" -> Some (Source.stripTags html)
                                | _ -> None)

                        Expect.equal imports [| "Import A." |] "before the declaration"
                        Expect.equal commands [| "  Import B." |] "in place, with its indent"
                finally
                    Directory.Delete (root, true)

            testCase "toHtml: the fixture's import sentence is an Import block"
            <| fun () ->
                let _, blocks = guideBlocks ()

                let imports =
                    blocks
                    |> Array.choose (function
                        | Import html -> Some (Source.stripTags html)
                        | _ -> None)

                Expect.equal imports [| "From Fixture Require Import Other." |] "the import"

            testCase "Source.lines balances each line's tags and keeps the text"
            <| fun () ->
                let html = "  <span class=\"comment\">a &lt;\n  <a href=\"x\">b</a></span> c\n"

                let lines = Source.lines html

                Expect.equal
                    lines
                    [|
                        "  <span class=\"comment\">a &lt;\n</span>"
                        "<span class=\"comment\">  <a href=\"x\">b</a></span> c\n"
                    |]
                    "tags close and reopen at the newline"

                Expect.equal (lines |> Array.map Source.indent) [| 2; 2 |] "the indent reads through tags"
                Expect.equal (Source.stripTags (String.concat "" lines)) (Source.stripTags html) "the text is unchanged"

            testCase "toHtmlWith: collapseCodeOver starts a body closed only past the limit"
            <| fun () ->
                let config =
                    { Config.none with
                        CollapseCodeOver = Some 2
                    }

                let page (statement: string) =
                    toHtmlWith config (fun _ -> "") [| Quiet (0, "a", statement, [||], noRefs) |]

                Expect.isFalse ((page "one\ntwo\n").Contains "details") "at the limit"
                Expect.stringContains (page "one\ntwo\nthree\n") """<details class="body"><summary data-lines=""" "past the limit"

            testCase "toHtmlWith: a long body starts closed when every body is collapsible"
            <| fun () ->
                let config =
                    { Config.none with
                        CollapsibleCode = true
                        CollapseCodeOver = Some 1
                    }

                let html =
                    toHtmlWith config (fun _ -> "") [| Quiet (0, "a", "one\ntwo\n", [||], noRefs) |]

                Expect.stringContains html """<details class="body"><summary data-lines=""" "closed"

            testCase "toHtml: Admitted carries the warning class and text"
            <| fun () ->
                let _, blocks = guideBlocks ()
                let html = toHtml [| declarationBlock blocks "open_goal" |]
                Expect.stringContains html """<details class="proof warning"><summary>Admitted""" "the class and the label"
                Expect.stringContains html "Not proved" "the warning text"

            testCase "toHtml: reference lists show the first 8, with the rest in a nested details"
            <| fun () ->
                let refs =
                    {
                        Uses = Array.init 10 item
                        UsedBy = [| item 20 |]
                    }

                let html = toHtml [| Quiet (0, "a", "s", [||], refs) |]

                let first, nested =
                    let split = html.IndexOf """<details class="more">"""
                    html.Substring (0, split), html.Substring split

                Expect.stringContains first ">name7<" "the eighth shows"
                Expect.isFalse (first.Contains ">name8<") "the ninth folds"
                Expect.stringContains nested "2 more" "the count"
                Expect.stringContains nested ">name9<" "the rest is nested"
                Expect.stringContains nested "Used by" "the second list follows"

                let empty = toHtml [| Quiet (0, "a", "s", [||], { Uses = [||]; UsedBy = [||] }) |]

                Expect.isFalse (empty.Contains "refs") "no empty lists"

            testCase "blocks: reference lists come from the cross-references, without the declaration itself"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()
                let otherIndex = 1 - guideIndex project
                let others = blocks project links otherIndex

                match declarationBlock others "other_def" with
                | Documented (_, _, _, _, _, _, refs) ->
                    let names = refs.UsedBy |> Array.map _.Name
                    Expect.contains names "plain_doc" "a later file's use"
                    Expect.contains names "other_lemma" "a use in the same file"
                    Expect.isFalse (Array.contains "other_def" names) "not itself"

                    let plainDoc =
                        refs.UsedBy
                        |> Array.find (fun entry -> entry.Name = "plain_doc")

                    Expect.equal plainDoc.Href (Some "/site/Fixture.Guide.html#plain_doc") "linked through the module URL"
                    Expect.equal (Array.distinctBy _.Key refs.UsedBy).Length refs.UsedBy.Length "each name once"
                | other -> failtestf "expected a documented block, got %A" other

                let _, guide = guideBlocks ()

                match declarationBlock guide "sum_refl" with
                | Documented (_, _, _, _, statement, _, refs) ->
                    Expect.isTrue (Regex.IsMatch (statement, """<span data-ref="b\d+\.\d+">a</span>""")) "a binder is keyed"
                    Expect.isTrue (Regex.IsMatch (statement, """<span data-ref="x\d+">nat</span>""")) "an external is a span"
                    Expect.exists refs.Uses (fun entry -> entry.Name = "nat" && entry.Href.IsNone) "an external has no link"
                | other -> failtestf "expected a documented block, got %A" other

            testCase "every byte of the file appears on the page, apart from comments rendered as prose"
            <| fun () ->
                for project in [ FixtureFiles.fixtureProject (); FixtureFiles.libraryProject () ] do
                    project.Files
                    |> Array.iteri (fun index file ->
                        let blocks = blocks project links index
                        Expect.equal (decodedSource blocks) (expectedSource file) file.Path

                        let page = Source.stripTags (toHtml blocks)

                        for html in blocks |> Array.toList |> List.collect sourceHtml do
                            Expect.stringContains page ((Source.stripTags html).TrimStart()) $"{file.Path}: the page holds its blocks")
        ]
