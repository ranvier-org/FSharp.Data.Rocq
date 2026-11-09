namespace Nacara.Plugins

open System.Net
open System.Text
open System.Text.RegularExpressions
open FSharp.Data.Rocq.Structure
open Markdig
open Markdig.Syntax
open Markdig.Syntax.Inlines

/// <summary>Lookups that turn names in a comment into links.</summary>
type Resolve =
    {
        /// Maps the text of an inline code span to a reference key and the URL of its page.
        Code: string -> (string * string) option
        /// Maps a relative link written as <c>Module.html#name</c> to the site URL of a project module.
        Link: string -> string option
    }

/// <summary>Renders comment bodies to HTML. Raw HTML in a body is displayed as text.</summary>
[<RequireQualifiedAccess>]
module Docs =

    /// <summary>A Markdig pipeline with raw HTML disabled and the given extensions applied.</summary>
    let pipeline (extensions: IMarkdownExtension list) : MarkdownPipeline =
        let builder = MarkdownPipelineBuilder().DisableHtml()

        for extension in extensions do
            builder.Extensions.AddIfNotAlready extension
            |> ignore

        builder.Build ()

    let private encode (text: string) =
        Rocq.Navigator.Source.encode text

    let private referenceHtml (text: string) (key: string) (href: string) =
        $"""<a class="ref" data-ref="%s{encode key}" href="%s{encode href}"><code>%s{encode text}</code></a>"""

    let private scheme = Regex @"^([a-zA-Z][a-zA-Z0-9+.\-]*):"

    /// Browsers drop control characters and spaces from a URL before reading its scheme.
    let private isSafeUrl (url: string) =
        let compact =
            url
            |> String.filter (fun c -> c > ' ' && c <> '\u007f')

        let m = scheme.Match compact

        not m.Success
        || List.contains (m.Groups[1].Value.ToLowerInvariant()) [ "http"; "https"; "mailto" ]

    let private unwrap (link: LinkInline) =
        for child in link |> Seq.toArray do
            child.Remove ()
            link.InsertBefore child

        link.Remove ()

    let rec private insideLink (node: Inline) =
        match node.Parent with
        | null -> false
        | :? LinkInline -> true
        | parent -> insideLink parent

    let private rewrite (resolve: Resolve) (document: MarkdownDocument) =
        // The tree is edited during the walk, so the nodes are collected first.
        for node in document.Descendants<Inline>() |> Seq.toArray do
            match node with
            | :? CodeInline as code when not (insideLink code) ->
                match resolve.Code code.Content with
                | Some (key, href) ->
                    code.ReplaceBy (HtmlInline (referenceHtml code.Content key href))
                    |> ignore
                | None -> ()
            | :? LinkInline as link when not (isNull link.Url) && not (isSafeUrl link.Url) -> unwrap link
            | :? LinkInline as link when not link.IsImage && not (isNull link.Url) ->
                match resolve.Link link.Url with
                | Some url -> link.Url <- url
                | None -> ()
            | :? AutolinkInline as link when not link.IsEmail && not (isSafeUrl link.Url) -> link.ReplaceBy (LiteralInline link.Url) |> ignore
            | _ -> ()

    let private escapedPrimeInHref = Regex "href=\"[^\"]*%27[^\"]*\""

    /// Markdig writes a prime in a URL as %27, which differs from the anchor as written.
    let private renderMarkdown (pipeline: MarkdownPipeline) (resolve: Resolve) (body: string) =
        let document = Markdown.Parse (body, pipeline)
        rewrite resolve document

        escapedPrimeInHref.Replace (document.ToHtml pipeline, (fun m -> m.Value.Replace ("%27", "'")))

    let private starHeading = Regex (@"^\s*(\*{1,4})\s+(.*?)\s*$")
    let private codeSpan = Regex @"\[([^\[\]]+)\]"

    let private coqdocInline (resolve: Resolve) (text: string) =
        let sb = StringBuilder ()
        let mutable last = 0

        for m in codeSpan.Matches text do
            sb.Append (encode (text.Substring (last, m.Index - last)))
            |> ignore

            let code = m.Groups[1].Value

            match resolve.Code code with
            | Some (key, href) -> sb.Append (referenceHtml code key href) |> ignore
            | None ->
                sb.Append ($"<code>%s{encode code}</code>")
                |> ignore

            last <- m.Index + m.Length

        sb.Append (encode (text.Substring last)) |> ignore
        sb.ToString ()

    let private renderCoqdoc (resolve: Resolve) (body: string) =
        let out = StringBuilder ()
        let paragraph = ResizeArray<string>()

        let flush () =
            if paragraph.Count > 0 then
                out.Append("<p>").Append(coqdocInline resolve (System.String.Join ("\n", paragraph))).Append("</p>\n")
                |> ignore

                paragraph.Clear ()

        for line in body.Replace("\r\n", "\n").Split '\n' do
            if System.String.IsNullOrWhiteSpace line then
                flush ()
            else
                let heading = starHeading.Match line

                if heading.Success then
                    flush ()
                    let level = heading.Groups[1].Length

                    out.Append($"<h%d{level}>").Append(coqdocInline resolve heading.Groups[2].Value).Append($"</h%d{level}>\n")
                    |> ignore
                else
                    paragraph.Add (line.Trim ())

        flush ()
        out.ToString ()

    /// <summary>Renders a comment body to HTML according to its format.</summary>
    /// <param name="pipeline">The Markdown pipeline for Markdown comments.</param>
    /// <param name="resolve">Turns code spans and relative links into references.</param>
    /// <param name="format">The comment's markup.</param>
    /// <param name="body">The comment's text.</param>
    let render (pipeline: MarkdownPipeline) (resolve: Resolve) (format: CommentFormat) (body: string) : string =
        match format with
        | CommentFormat.Markdown -> renderMarkdown pipeline resolve body
        | CommentFormat.Doc
        | CommentFormat.Plain -> renderCoqdoc resolve body

    let private boldLine = Regex (@"^\s*\*\*(.+?)\*\*\s*$")

    /// <summary>The text of the opening line when the body starts with a line wholly in bold.</summary>
    let firstBoldLine (format: CommentFormat) (body: string) : string option =
        ignore format

        body.Replace("\r\n", "\n").Split '\n'
        |> Array.tryFind (System.String.IsNullOrWhiteSpace >> not)
        |> Option.bind (fun line ->
            let m = boldLine.Match line
            if m.Success then Some m.Groups[1].Value else None)
