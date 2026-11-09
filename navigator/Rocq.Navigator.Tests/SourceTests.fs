module Rocq.Navigator.Tests.SourceTests

open System
open System.Text
open System.Text.RegularExpressions
open Expecto
open FSharp.Data.Rocq.Project
open FSharp.Data.Rocq.Structure
open Rocq.Navigator
open Rocq.Navigator.Tests

type private SourceSpan = FSharp.Data.Rocq.Source.Span

/// Keys every reference and binder the way a page would: project symbols link, and externals and binders do not.
let private refAtFor (project: RocqProject) (file: RocqFile) : SourceSpan -> Source.Ref option =
    let model = file.Model

    let symbolOf (target: Target) =
        let qualified =
            match target.Path, target.Name with
            | "<>", name -> name
            | path, name -> path + "." + name

        project.Symbols
        |> Array.tryFind (fun symbol ->
            project.Files[symbol.File].Module = target.Module
            && symbol.Symbol.Qualified = qualified)

    let externalOf (target: Target) =
        project.CrossReferences.Externals
        |> Array.tryFindIndex (fun node ->
            node.Module = target.Module
            && node.Kind = target.Kind
            && node.Qualified =
                (if target.Path = "<>" then
                     target.Name
                 else
                     target.Path + "." + target.Name))

    let binderKey (binder: int) : Source.Ref =
        { Key = $"b0.{binder}"; Href = None }

    let references =
        model.Refs
        |> Array.choose (fun reference ->
            let target = model.Targets[reference.Target]

            match target.Binder, symbolOf target, externalOf target with
            | Some binder, _, _ -> Some (reference.Span, binderKey binder)
            | None, Some symbol, _ ->
                let reference': Source.Ref =
                    {
                        Key = $"s{symbol.Index}"
                        Href = Some $"/{target.Module}.html#{symbol.Symbol.Qualified}"
                    }

                Some (reference.Span, reference')
            | None, None, Some index ->
                let reference': Source.Ref = { Key = $"x{index}"; Href = None }
                Some (reference.Span, reference')
            | None, None, None -> None)

    let binders =
        model.Binders
        |> Array.mapi (fun index binder -> binder.Span, binderKey index)

    // A repeated span keeps its first entry, as `Array.distinctBy` does.
    let table =
        Array.append references binders
        |> Array.distinctBy fst
        |> dict

    fun span ->
        match table.TryGetValue span with
        | true, reference -> Some reference
        | _ -> None

let private spansOf (declaration: Declaration) =
    [
        match declaration.Statement with
        | ValueSome span -> span
        | ValueNone -> ()

        declaration.Extent

        for proof in declaration.Proofs do
            proof.Span
    ]

let private bytesAt (file: RocqFile) (span: SourceSpan) =
    Array.sub file.Model.Bytes span.Start (span.End - span.Start)

let private expectExact (project: RocqProject) (file: RocqFile) (span: SourceSpan) =
    let html = Source.render file (refAtFor project file) span
    let decoded = Encoding.UTF8.GetBytes (Source.stripTags html)
    Expect.equal decoded (bytesAt file span) $"{file.Path} {span.Start}-{span.End} decodes to the source bytes"

let private wholeFile (file: RocqFile) : SourceSpan =
    {
        Start = 0
        End = file.Model.Bytes.Length
    }

let private fileNamed (project: RocqProject) (suffix: string) =
    project.Files
    |> Array.find (fun file -> file.Path.EndsWith suffix)

let private declarationNamed (file: RocqFile) (name: string) =
    file.Model.Declarations
    |> Array.find (fun declaration -> declaration.Name = Some name)

let tests =
    testList
        "source"
        [
            testCase "every declaration's rendered statement and proof decode to the exact source bytes"
            <| fun () ->
                for project in [ FixtureFiles.fixtureProject (); FixtureFiles.libraryProject () ] do
                    for file in project.Files do
                        for declaration in file.Model.Declarations do
                            for span in spansOf declaration do
                                expectExact project file span
                                Expect.equal (Encoding.UTF8.GetBytes (Source.text file span)) (bytesAt file span) "text is exact"

            testCase "a span over non-ASCII text and CRLF line ends round-trips"
            <| fun () ->
                let navigator = FixtureFiles.fixtureProject ()
                let library = FixtureFiles.libraryProject ()

                for project, file in
                    [
                        navigator, fileNamed navigator "Other.v"
                        library, fileNamed library "Crlf.v"
                    ] do
                    let text = Source.text file (wholeFile file)
                    Expect.stringContains text "\r\n" "the file has CRLF line ends"
                    Expect.stringContains text "µ" "the file has non-ASCII text"
                    expectExact project file (wholeFile file)

                let guide = fileNamed navigator "Guide.v"
                Expect.stringContains (Source.text guide (wholeFile guide)) "∀ (P : Prop)" "three-byte characters precede later spans"
                expectExact navigator guide (wholeFile guide)

            testCase "a reference is an anchor with data-ref, and an external is a span with data-ref"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()
                let guide = fileNamed project "Guide.v"
                let refAt = refAtFor project guide
                let plainDoc = declarationNamed guide "plain_doc"
                let statement = Source.render guide refAt plainDoc.Statement.Value

                let target =
                    project.Symbols
                    |> Array.find (fun symbol -> symbol.Symbol.Qualified = "other_def")

                Expect.stringContains
                    statement
                    $"""<a href="/Fixture.Other.html#other_def" data-ref="s{target.Index}">other_def</a>"""
                    "a project symbol links"

                let sumRefl = declarationNamed guide "sum_refl"
                let withExternal = Source.render guide refAt sumRefl.Statement.Value

                Expect.isTrue (Regex.IsMatch (withExternal, """<span data-ref="x\d+">nat</span>""")) "an external has no link"

            testCase "repeated references render one anchor, with no anchor inside another"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()
                let guide = fileNamed project "Guide.v"
                let html = Source.render guide (refAtFor project guide) (wholeFile guide)
                let mutable depth = 0

                for tag in Regex.Matches (html, "</?a[ >]") do
                    depth <- depth + (if tag.Value = "</a>" then -1 else 1)
                    Expect.isLessThanOrEqual depth 1 "anchors do not nest"

                let axiom = declarationNamed guide "rests_on_axiom"
                let proof = Source.render guide (refAtFor project guide) axiom.Proofs[0].Span
                Expect.equal (Regex.Matches (proof, ">fixture_axiom<")).Count 1 "the repeated span renders once"

            testCase "a binder in the statement carries its b key"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()
                let guide = fileNamed project "Guide.v"
                let sumRefl = declarationNamed guide "sum_refl"
                let html = Source.render guide (refAtFor project guide) sumRefl.Statement.Value

                let index =
                    guide.Model.Binders
                    |> Array.findIndex (fun binder ->
                        binder.Name = "a"
                        && binder.Span.Start >= sumRefl.Extent.Start
                        && binder.Span.End <= sumRefl.Extent.End)

                Expect.stringContains html $"""<span data-ref="b0.{index}">a</span>""" "the binder site is keyed"

            testCase "keywords are marked, and comments inside a span stay in place"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()
                let guide = fileNamed project "Guide.v"
                let plainDoc = declarationNamed guide "plain_doc"
                let html = Source.render guide (fun _ -> None) plainDoc.Extent
                Expect.stringContains html """<span class="kw">Lemma</span>""" "the keyword is marked"
                Expect.stringContains html """<span class="kw">Qed</span>""" "the ending is marked"

                Expect.stringContains
                    html
                    "<span class=\"comment\">(* a comment inside the proof *)</span>\n  reflexivity"
                    "the comment stays before the tactic it precedes"

            testCase "source text is escaped, and stripTags decodes it"
            <| fun () ->
                let project = FixtureFiles.fixtureProject ()
                let guide = fileNamed project "Guide.v"
                let html = Source.render guide (fun _ -> None) (wholeFile guide)
                Expect.stringContains html "&lt;script&gt;" "markup in a comment is escaped"
                Expect.isFalse (html.Contains "<script>") "no raw markup"
                Expect.stringContains html "&amp;" "an ampersand is escaped"
                Expect.equal (Source.stripTags "<a href=\"x\">&lt;b&gt; &amp;lt;</a>") "<b> &lt;" "entities decode once"
        ]
