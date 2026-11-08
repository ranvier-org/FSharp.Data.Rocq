module Rocq.Navigator.Tests.DocsTests

open Expecto
open FSharp.Data.Rocq.Structure
open Markdig
open Markdig.Extensions.EmphasisExtras
open Nacara.Plugins

let private plain = Docs.pipeline []

let private noResolve =
    {
        Code = fun _ -> None
        Link = fun _ -> None
    }

let private resolver =
    {
        Code =
            function
            | "add_comm" -> Some ("Arith.add_comm", "/Arith.html#add_comm")
            | _ -> None
        Link =
            fun url ->
                if url.StartsWith "Arith.html" then
                    Some ("/site/" + url)
                else
                    None
    }

let tests =
    testList
        "docs"
        [
            testCase "a markdown comment renders headings, paragraphs, links and code"
            <| fun () ->
                let html =
                    Docs.render plain noResolve CommentFormat.Markdown "# Title\n\nSome *words* with `code` and [a link](https://example.org/x)."

                Expect.stringContains html "<h1>Title</h1>" "heading"
                Expect.stringContains html "<p>Some <em>words</em>" "paragraph"
                Expect.stringContains html "<code>code</code>" "code"
                Expect.stringContains html """<a href="https://example.org/x">a link</a>""" "link"

            testCase "a coqdoc comment renders headings, [code] and paragraphs"
            <| fun () ->
                let html =
                    Docs.render plain noResolve CommentFormat.Doc "** Section\n\nFirst [x + 0] line\ncontinues.\n\nSecond."

                Expect.stringContains html "<h2>Section</h2>" "heading"
                Expect.stringContains html "<p>First <code>x + 0</code> line\ncontinues.</p>" "paragraph with code"
                Expect.stringContains html "<p>Second.</p>" "second paragraph"

            testCase "angle brackets and ampersands in prose display literally"
            <| fun () ->
                let html =
                    Docs.render plain noResolve CommentFormat.Markdown "s < fuel & <script>alert(1)</script>"

                Expect.isFalse (html.Contains "<script") "no live script"
                Expect.stringContains html "s &lt; fuel &amp;" "literal text"

                let doc =
                    Docs.render plain noResolve CommentFormat.Doc "s < fuel & <script>alert(1)</script>"

                Expect.isFalse (doc.Contains "<script") "no live script in coqdoc"
                Expect.stringContains doc "s &lt; fuel &amp;" "literal text in coqdoc"

            testCase "code naming a symbol in scope becomes a reference, with data-ref and href"
            <| fun () ->
                let md = Docs.render plain resolver CommentFormat.Markdown "See `add_comm`."

                let expected =
                    """<a class="ref" data-ref="Arith.add_comm" href="/Arith.html#add_comm"><code>add_comm</code></a>"""

                Expect.stringContains md expected "markdown"
                let doc = Docs.render plain resolver CommentFormat.Doc "See [add_comm]."
                Expect.stringContains doc expected "coqdoc"

            testCase "code naming nothing stays plain code"
            <| fun () ->
                let html = Docs.render plain resolver CommentFormat.Markdown "See `other`."
                Expect.stringContains html "<code>other</code>" "plain code"
                Expect.isFalse (html.Contains "data-ref") "no reference"

            testCase "a relative link to a project module becomes its site URL, keeping the anchor"
            <| fun () ->
                let html =
                    Docs.render plain resolver CommentFormat.Markdown "[lemma](Arith.html#add_comm)"

                Expect.stringContains html "href=\"/site/Arith.html#add_comm\"" "rewritten"

            testCase "an external link and a link to an unknown page stay as written"
            <| fun () ->
                let html =
                    Docs.render plain resolver CommentFormat.Markdown "[a](https://example.org) [b](Other.html#x)"

                Expect.stringContains html "href=\"https://example.org\"" "external"
                Expect.stringContains html "href=\"Other.html#x\"" "unknown"

            testCase "a javascript link renders as plain text"
            <| fun () ->
                for source, text in
                    [
                        "[click](javascript:alert(1))", "click"
                        "[click](JaVaScRiPt:alert(1))", "click"
                        "[click](java&#9;script:alert(1))", "click"
                        "[click](data:text/html,x)", "click"
                        "[click][r]\n\n[r]: javascript:alert(1)", "click"
                        "<javascript:alert(1)>", "javascript:alert(1)"
                        "![pic](javascript:alert(1))", "pic"
                    ] do
                    let html = Docs.render plain resolver CommentFormat.Markdown source
                    Expect.isFalse (html.Contains "href") $"{source} has no link: {html}"
                    Expect.isFalse (html.Contains "src") $"{source} has no image: {html}"
                    Expect.stringContains html text $"{source} keeps its text"

            testCase "http, https, mailto, relative and anchor links stay links"
            <| fun () ->
                for url in
                    [
                        "http://example.org/a"
                        "https://example.org/a"
                        "mailto:a@example.org"
                        "page.html?a=b:c"
                        "#section"
                    ] do
                    let html = Docs.render plain noResolve CommentFormat.Markdown $"[x]({url})"
                    Expect.stringContains html $"""<a href="{url}">x</a>""" url

            testCase "an extension the site registered is applied"
            <| fun () ->
                let strike = EmphasisExtraExtension EmphasisExtraOptions.Strikethrough

                let html =
                    Docs.render (Docs.pipeline [ strike ]) noResolve CommentFormat.Markdown "~~gone~~"

                Expect.stringContains html "<del>gone</del>" "extension applied"
                let without = Docs.render plain noResolve CommentFormat.Markdown "~~gone~~"
                Expect.isFalse (without.Contains "<del>") "absent without the extension"

            testCase "firstBoldLine reads the opening bold line and returns None without one"
            <| fun () ->
                Expect.equal (Docs.firstBoldLine CommentFormat.Markdown "\n**Adds zero.**\n\nMore.") (Some "Adds zero.") "bold"
                Expect.equal (Docs.firstBoldLine CommentFormat.Markdown "Plain first.\n**Later.**") None "not opening"
                Expect.equal (Docs.firstBoldLine CommentFormat.Markdown "") None "empty"
        ]
