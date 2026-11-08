/// <summary>Renders byte ranges of a Rocq source file as HTML. Decoding the HTML gives back the exact source text.</summary>
module Rocq.Navigator.Source

open System.Net
open System.Text
open System.Text.RegularExpressions
open FSharp.Data.Rocq.Lexer
open FSharp.Data.Rocq.Project

/// <summary>A reference site on a page.</summary>
type Ref =
    {
        /// <c>s</c> and a symbol index, <c>x</c> and an external index, or <c>b</c>, a declaration index, a dot and a binder index.
        Key: string
        /// The link target. <c>None</c> renders a span instead of an anchor.
        Href: string option
    }

/// <summary>Encodes text for an element or a double-quoted attribute. A prime stays literal, so an anchor and its links compare equal as written.</summary>
let encode (value: string) : string =
    (WebUtility.HtmlEncode value).Replace("&#39;", "'")

/// <summary>The exact source text of <c>span</c>.</summary>
let text (file: RocqFile) (span: FSharp.Data.Rocq.Source.Span) : string =
    FSharp.Data.Rocq.Source.text file.Model.Bytes span

let private escape (builder: StringBuilder) (value: string) =
    for c in value do
        match c with
        | '&' -> builder.Append "&amp;"
        | '<' -> builder.Append "&lt;"
        | '>' -> builder.Append "&gt;"
        | '"' -> builder.Append "&quot;"
        | c -> builder.Append c
        |> ignore

/// Appends a range with its keyword and comment tokens marked. A token crossing the range's edge is clipped to it.
let private appendMarked (file: RocqFile) (builder: StringBuilder) (start: int) (stop: int) =
    let mutable cursor = start

    let appendRange (from: int) (until: int) =
        if until > from then
            escape builder (text file { Start = from; End = until })

    let tokens = file.Model.Tokens
    let mutable low = 0
    let mutable high = tokens.Length

    // Tokens are sorted by position, and a page renders many short ranges of a long file.
    while low < high do
        let middle = (low + high) / 2

        if tokens[middle].End <= start then
            low <- middle + 1
        else
            high <- middle

    let mutable index = low

    while index < tokens.Length
          && tokens[index].Start < stop do
        let token = tokens[index]

        let className =
            match token.Kind with
            | TokenKind.Keyword -> Some "kw"
            | TokenKind.Comment -> Some "comment"
            | _ -> None

        match className with
        | Some name ->
            let from = max token.Start start
            let until = min token.End stop
            appendRange cursor from

            builder.Append ($"<span class=\"{name}\">")
            |> ignore

            appendRange from until
            builder.Append "</span>" |> ignore
            cursor <- until
        | None -> ()

        index <- index + 1

    appendRange cursor stop

/// <summary>
/// Renders <c>span</c>, with keywords and comments marked and a <c>data-ref</c> element at each reference and binder site
/// <c>refAt</c> keys. An overlapping site after the first is rendered as plain text.
/// </summary>
/// <param name="file">The file holding the span.</param>
/// <param name="refAt">The key of a reference or binder site, or <c>None</c> to render it as plain text.</param>
/// <param name="span">The byte range. It must fall on UTF-8 character boundaries.</param>
let render (file: RocqFile) (refAt: FSharp.Data.Rocq.Source.Span -> Ref option) (span: FSharp.Data.Rocq.Source.Span) : string =
    let builder = StringBuilder ()

    let sites =
        Seq.append
            (file.Model.Refs
             |> Seq.map (fun reference -> reference.Span))
            (file.Model.Binders
             |> Seq.map (fun binder -> binder.Span))
        |> Seq.filter (fun site -> site.Start >= span.Start && site.End <= span.End)
        |> Seq.sortBy (fun site -> site.Start, site.End)

    let mutable cursor = span.Start

    for site in sites do
        if site.Start >= cursor then
            match refAt site with
            | None -> ()
            | Some reference ->
                appendMarked file builder cursor site.Start

                match reference.Href with
                | Some href ->
                    builder.Append "<a href=\"" |> ignore
                    escape builder href
                    builder.Append "\" " |> ignore
                | None -> builder.Append "<span " |> ignore

                builder.Append "data-ref=\"" |> ignore
                escape builder reference.Key
                builder.Append "\">" |> ignore
                appendMarked file builder site.Start site.End

                builder.Append (if reference.Href.IsSome then "</a>" else "</span>")
                |> ignore

                cursor <- site.End

    appendMarked file builder cursor span.End
    builder.ToString ()

let private tag = Regex "<[^>]*>"

/// <summary>The text of an HTML fragment: tags removed and entities decoded once.</summary>
let stripTags (html: string) : string =
    WebUtility.HtmlDecode (tag.Replace (html, ""))
