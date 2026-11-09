/// <summary>The content of a module's page: its source file in source order, as blocks of prose and code.</summary>
module Rocq.Navigator.Article

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open FSharp.Data.Rocq.Project
open FSharp.Data.Rocq.Structure
open Markdig
open Nacara.Plugins

type private SourceSpan = FSharp.Data.Rocq.Source.Span

/// <summary>A proof, with any comments before the next proof.</summary>
type ProofBlock =
    {
        Ending: ProofEnding
        /// Decodes to the exact source text.
        Html: string
    }

/// <summary>An entry in a declaration's reference lists.</summary>
type RefItem =
    {
        Name: string
        /// A <c>data-ref</c> key, as in <c>Source.Ref</c>. <c>d</c> and an index keys a declaration with no symbol, and <c>f</c> and an index a file.
        Key: string
        /// <c>None</c> for a name outside the project.
        Href: string option
    }

/// <summary>The cross-references of a declaration. Each name appears once, and the declaration itself is absent.</summary>
type RefLists = { Uses: RefItem[]; UsedBy: RefItem[] }

/// <summary>One unit of a page. Source-bearing fields decode to the exact source text.</summary>
type Block =
    /// A heading comment's heading. Anchors are unique on the page.
    | Heading of level: int * text: string * anchor: string
    /// A declaration with attached documentation.
    | Documented of decl: int * anchor: string * docHtml: string * statementHtml: string * proofs: ProofBlock[] * refs: RefLists
    /// A declaration with no attached documentation.
    | Quiet of decl: int * anchor: string * statementHtml: string * proofs: ProofBlock[] * refs: RefLists
    /// A sentence outside any declaration, or the whitespace between two blocks.
    | Command of html: string
    /// A comment attached to no declaration, or the text around a comment's headings.
    | Prose of html: string

/// <summary>The site-specific inputs of a page.</summary>
type Links =
    {
        /// Maps a module name to the site URL of its page.
        Module: string -> string
        /// Renders Markdown comments.
        Pipeline: MarkdownPipeline
    }

/// <summary>The URL of a declaration, by its index in <c>RocqProject.Declarations</c>.</summary>
let internal declarationHref (project: RocqProject) (links: Links) : int -> string =
    let anchors =
        project.Files
        |> Array.map (fun file -> lazy (Anchors.forFile file))

    fun index ->
        let declaration = project.Declarations[index]
        let page = links.Module project.Files[declaration.File].Module

        page
        + "#"
        + anchors[declaration.File].Value[declaration.Local]

let internal symbolHref (project: RocqProject) (links: Links) (declarationHref: int -> string) (symbol: ProjectSymbol) =
    match symbol.Declaration with
    | Some declaration -> declarationHref declaration
    | None -> links.Module project.Files[symbol.File].Module

/// Accepts a full module name, or a dotted suffix naming one module.
let private resolveModuleLink (project: RocqProject) (links: Links) (url: string) =
    let path, fragment =
        match url.IndexOf '#' with
        | -1 -> url, ""
        | index -> url.Substring (0, index), url.Substring index

    if path.EndsWith (".html", StringComparison.Ordinal) then
        let name = path.Substring (0, path.Length - ".html".Length)

        let matches =
            project.Files
            |> Array.filter (fun file ->
                file.Module = name
                || file.Module.EndsWith ("." + name, StringComparison.Ordinal))

        match matches with
        | [| file |] -> Some (links.Module file.Module + fragment)
        | _ -> None
    else
        None

/// <summary>Resolves the names and links in a file's comments. A name matching several symbols resolves to one in the same file, or to nothing.</summary>
let internal resolver (project: RocqProject) (links: Links) (declarationHref: int -> string) (fileIndex: int) : Resolve =
    {
        Code =
            fun code ->
                let candidates = symbolsNamed project code

                let chosen =
                    match
                        candidates
                        |> Array.filter (fun symbol -> symbol.File = fileIndex)
                    with
                    | [||] when candidates.Length = 1 -> Some candidates[0]
                    | [||] -> None
                    | local -> Some local[0]

                chosen
                |> Option.map (fun symbol -> $"s{symbol.Index}", symbolHref project links declarationHref symbol)
        Link = resolveModuleLink project links
    }

/// <summary>The written form of a notation, read from its declaring sentence.</summary>
let internal notationText (file: RocqFile) (declaration: Declaration) : string option =
    let m = Regex.Match (Source.text file declaration.SentenceSpan, "\"([^\"]*)\"")
    if m.Success then Some m.Groups[1].Value else None

/// <summary>The comment's body as source text.</summary>
let internal commentBody (file: RocqFile) (comment: Comment) : string =
    Source.text file comment.BodySpan

/// The keyed reference and binder sites of a file. A site repeated in the `.glob` keeps its first entry.
let private referenceTable (project: RocqProject) (links: Links) (declarationHref: int -> string) (fileIndex: int) =
    let file = project.Files[fileIndex]
    let model = file.Model

    let bySymbolName = Dictionary<string, ProjectSymbol>()
    let byLocal = Dictionary<int, ProjectSymbol>()

    for symbol in project.Symbols do
        bySymbolName.TryAdd (
            namespaceOf symbol.Symbol.Kind
            + "|"
            + project.Files[symbol.File].Module
            + "#"
            + symbol.Symbol.Qualified,
            symbol
        )
        |> ignore

        if symbol.File = fileIndex then
            byLocal[symbol.Local] <- symbol

    let externals =
        project.CrossReferences.Externals
        |> Array.mapi (fun index node -> (node.Module, node.Qualified, node.Kind), index)
        |> dict

    let modules = project.Files |> Array.map _.Module |> HashSet

    let firstDeclaration =
        project.Declarations
        |> Array.tryFindIndex (fun declaration -> declaration.File = fileIndex)
        |> Option.defaultValue 0

    let binderRef (binder: int) : Source.Ref option =
        let span = model.Binders[binder].Span

        model.Declarations
        |> Array.tryFindIndex (fun declaration ->
            declaration.Extent.Start <= span.Start
            && span.End <= declaration.Extent.End)
        |> Option.map (fun local ->
            {
                Key = $"b{firstDeclaration + local}.{binder}"
                Href = None
            })

    let symbolRef (symbol: ProjectSymbol) : Source.Ref =
        {
            Key = $"s{symbol.Index}"
            Href = Some (symbolHref project links declarationHref symbol)
        }

    let targetRef (target: Target) : Source.Ref option =
        let qualified =
            match target.Path, target.Name with
            | "<>", name -> name
            | path, "<>" -> path
            | path, name -> path + "." + name

        let key =
            namespaceOf target.Kind
            + "|"
            + target.Module
            + "#"
            + qualified

        match target.Binder, target.Symbol with
        | Some binder, _ -> binderRef binder
        | None, Some symbol when byLocal.ContainsKey symbol -> Some (symbolRef byLocal[symbol])
        | _ ->
            match bySymbolName.TryGetValue key with
            | true, symbol -> Some (symbolRef symbol)
            | _ ->
                match externals.TryGetValue ((target.Module, qualified, target.Kind)) with
                // A notation from outside the project has no card, and its span crosses spaces and brackets.
                | true, index when target.Kind <> "not" ->
                    Some
                        {
                            Key = $"x{index}"
                            Href =
                                if modules.Contains target.Module then
                                    Some (links.Module target.Module)
                                else
                                    None
                        }
                | _ -> None

    let table = Dictionary<SourceSpan, Source.Ref>()

    let add (span: SourceSpan) (reference: Source.Ref option) =
        match reference with
        | Some reference -> table.TryAdd (span, reference) |> ignore
        | None -> ()

    for reference in model.Refs do
        add reference.Span (targetRef model.Targets[reference.Target])

    model.Binders
    |> Array.iteri (fun index binder -> add binder.Span (binderRef index))

    table

let private refLists
    (project: RocqProject)
    (links: Links)
    (declarationHref: int -> string)
    (symbolsByDeclaration: IDictionary<int, ProjectSymbol[]>)
    (index: int)
    =
    let own: ProjectSymbol[] =
        match symbolsByDeclaration.TryGetValue index with
        | true, symbols -> symbols
        | _ -> [||]

    let targetItem (node: Node) =
        match node with
        | SymbolNode symbol ->
            let symbol = project.Symbols[symbol]

            if Array.exists (fun (other: ProjectSymbol) -> other.Index = symbol.Index) own then
                None
            else
                let name =
                    match symbol.Symbol.Kind, symbol.Declaration with
                    | "not", Some declaration ->
                        let owned = project.Declarations[declaration]
                        notationText project.Files[owned.File] owned.Declaration
                    | "not", None -> None
                    | _ -> Some symbol.Symbol.Qualified

                name
                |> Option.map (fun name ->
                    {
                        Name = name
                        Key = $"s{symbol.Index}"
                        Href = Some (symbolHref project links declarationHref symbol)
                    })
        | ExternalSymbolNode node when project.CrossReferences.Externals[node].Kind = "not" -> None
        | ExternalSymbolNode node ->
            Some
                {
                    Name = project.CrossReferences.Externals[node].Qualified
                    Key = $"x{node}"
                    Href = None
                }

    let ownerItem (owner: Owner) =
        match owner with
        | DeclarationOwner other when other = index -> None
        | DeclarationOwner other ->
            let owned = project.Declarations[other]
            let declaration = owned.Declaration

            let name =
                if declaration.Category = Category.Notation then
                    notationText project.Files[owned.File] declaration
                    |> Option.defaultValue $"d{other}"
                else
                    declaration.Name
                    |> Option.defaultValue $"d{other}"

            Some
                {
                    Name = name
                    Key =
                        match symbolsByDeclaration.TryGetValue other with
                        | true, symbols -> $"s{symbols[0].Index}"
                        | _ -> $"d{other}"
                    Href = Some (declarationHref other)
                }
        | FileOwner file ->
            Some
                {
                    Name = project.Files[file].Module
                    Key = $"f{file}"
                    Href = Some (links.Module project.Files[file].Module)
                }

    {
        Uses =
            usesBy project (DeclarationOwner index)
            |> Array.choose (fun edge -> targetItem edge.Target)
            |> Array.distinctBy _.Key
        UsedBy =
            own
            |> Array.collect (fun symbol -> usesOf project (SymbolNode symbol.Index))
            |> Array.choose (fun edge -> ownerItem edge.Owner)
            |> Array.distinctBy _.Key
    }

/// The start of the indentation before `position`, or `position` when other text precedes it on the line.
let private lineStart (bytes: byte[]) (floor: int) (position: int) =
    let mutable start = position

    while start > floor
          && (bytes[start - 1] = 32uy || bytes[start - 1] = 9uy) do
        start <- start - 1

    if start = 0 || bytes[start - 1] = 10uy then
        start
    else
        position

/// The prose around a comment's headings, and the headings, in body order.
let private commentBlocks (file: RocqFile) (render: Comment -> string -> string) (uniqueHeading: string -> string) (comment: Comment) =
    let bytes = file.Model.Bytes
    let body = comment.BodySpan

    let prose (start: int) (stop: int) =
        [
            if stop > start then
                let text = Source.text file { Start = start; End = stop }

                if not (String.IsNullOrWhiteSpace text) then
                    Prose (render comment text)
        ]

    let mutable cursor = body.Start

    [
        for heading in file.Model.Headings do
            if heading.Comment = comment.Index then
                let mutable first = heading.Span.Start

                while first > body.Start && bytes[first - 1] <> 10uy do
                    first <- first - 1

                let mutable last = heading.Span.End

                while last < body.End && bytes[last] <> 10uy do
                    last <- last + 1

                yield! prose cursor first
                Heading (heading.Level, heading.Text, uniqueHeading (Anchors.slug heading.Text))
                cursor <- last

        yield! prose cursor body.End
    ]

type private Item =
    | DeclarationItem of Declaration
    | CommentItem of Comment
    | SentenceItem of Sentence

/// <summary>A file's blocks in source order. The source-bearing fields, read in order, give every byte of the file apart from the comments rendered as prose.</summary>
/// <param name="project">The project holding the file.</param>
/// <param name="links">Builds the URLs of references.</param>
/// <param name="fileIndex">The index in <c>RocqProject.Files</c>.</param>
let blocks (project: RocqProject) (links: Links) (fileIndex: int) : Block[] =
    let file = project.Files[fileIndex]
    let model = file.Model
    let href = declarationHref project links
    let resolve = resolver project links href fileIndex
    let anchors = Anchors.forFile file
    let references = referenceTable project links href fileIndex
    let uniqueHeading = Anchors.uniquifier ()

    let refAt (span: SourceSpan) =
        match references.TryGetValue span with
        | true, reference -> Some reference
        | _ -> None

    let source (start: int) (stop: int) =
        Source.render file refAt { Start = start; End = stop }

    let render (comment: Comment) (body: string) =
        Docs.render links.Pipeline resolve comment.Format body

    let hasHeading = model.Headings |> Array.map _.Comment |> set

    let firstDeclaration =
        project.Declarations
        |> Array.tryFindIndex (fun declaration -> declaration.File = fileIndex)
        |> Option.defaultValue 0

    let symbolsByDeclaration =
        project.Symbols
        |> Array.filter _.Declaration.IsSome
        |> Array.groupBy _.Declaration.Value
        |> dict

    let declarationBlock (declaration: Declaration) =
        let starts =
            declaration.Proofs
            |> Array.map (fun proof -> lineStart model.Bytes declaration.Extent.Start proof.Span.Start)

        let statementEnd =
            if starts.Length > 0 then
                starts[0]
            else
                declaration.Extent.End

        let proofs =
            declaration.Proofs
            |> Array.mapi (fun index proof ->
                let stop =
                    if index + 1 < starts.Length then
                        starts[index + 1]
                    else
                        declaration.Extent.End

                {
                    Ending = proof.Ending
                    Html = source starts[index] stop
                })

        let statement = source declaration.Extent.Start statementEnd
        let index = firstDeclaration + declaration.Index
        let refs = refLists project links href symbolsByDeclaration index

        let docs =
            declaration.Docs
            |> Array.filter (fun comment -> not (hasHeading.Contains comment))

        if docs.Length > 0 then
            let docHtml =
                docs
                |> Array.map (fun comment -> render model.Comments[comment] (commentBody file model.Comments[comment]))
                |> String.concat ""

            Documented (index, anchors[declaration.Index], docHtml, statement, proofs, refs)
        else
            Quiet (index, anchors[declaration.Index], statement, proofs, refs)

    let items =
        [|
            for declaration in model.Declarations do
                declaration.Extent.Start, 0, DeclarationItem declaration

            for comment in model.Comments do
                if comment.Sentence.IsNone then
                    comment.Span.Start, 1, CommentItem comment

            for sentence in model.Sentences do
                sentence.Span.Start, 2, SentenceItem sentence
        |]
        |> Array.sortBy (fun (start, rank, _) -> start, rank)

    let result = ResizeArray<Block>()
    let mutable cursor = 0

    let appendGap (stop: int) =
        if stop > cursor then
            result.Add (Command (source cursor stop))
            cursor <- stop

    for start, _, item in items do
        if start >= cursor then
            match item with
            | DeclarationItem declaration ->
                appendGap (lineStart model.Bytes cursor start)

                result.Add (
                    declarationBlock
                        { declaration with
                            Extent =
                                { declaration.Extent with
                                    Start = cursor
                                }
                        }
                )

                cursor <- declaration.Extent.End
            | SentenceItem sentence ->
                appendGap (lineStart model.Bytes cursor start)
                result.Add (Command (source cursor sentence.Span.End))
                cursor <- sentence.Span.End
            | CommentItem comment ->
                appendGap start

                if
                    comment.AttachedTo.IsNone
                    || hasHeading.Contains comment.Index
                then
                    result.AddRange (commentBlocks file render uniqueHeading comment)

                cursor <- comment.Span.End

    appendGap model.Bytes.Length
    result.ToArray ()

let private encode (value: string) =
    Source.encode value

let private visibleCount = 8

let private appendProof (out: StringBuilder) (proof: ProofBlock) =
    let name = proofEndingName proof.Ending

    if proof.Ending = ProofEnding.Admitted then
        out.Append ($"""<details class="proof warning"><summary>{name} <span class="warning-text">Not proved</span></summary>""")
        |> ignore
    else
        out.Append ($"""<details class="proof"><summary>{name}</summary>""")
        |> ignore

    out.Append ($"""<pre><code>{proof.Html}</code></pre></details>""")
    |> ignore

let private itemHtml (item: RefItem) =
    match item.Href with
    | Some href -> $"""<li><a href="{encode href}" data-ref="{encode item.Key}">{encode item.Name}</a></li>"""
    | None -> $"""<li><span data-ref="{encode item.Key}">{encode item.Name}</span></li>"""

let private listHtml (items: RefItem[]) =
    "<ul>"
    + (items |> Array.map itemHtml |> String.concat "")
    + "</ul>"

let private appendRefList (out: StringBuilder) (list: string) (label: string) (items: RefItem[]) =
    if items.Length > 0 then
        out.Append ($"""<div class="ref-list" data-list="{list}"><p>{label}</p>""")
        |> ignore

        out.Append (listHtml (Array.truncate visibleCount items))
        |> ignore

        if items.Length > visibleCount then
            let rest = Array.skip visibleCount items

            out.Append ($"""<details class="more"><summary>{rest.Length} more</summary>{listHtml rest}</details>""")
            |> ignore

        out.Append "</div>" |> ignore

let private appendRefs (out: StringBuilder) (refs: RefLists) =
    if refs.Uses.Length + refs.UsedBy.Length > 0 then
        out.Append """<details class="refs"><summary>References</summary>"""
        |> ignore

        appendRefList out "uses" "Uses" refs.Uses
        appendRefList out "used-by" "Used by" refs.UsedBy
        out.Append "</details>" |> ignore

let private appendDeclaration
    (out: StringBuilder)
    (className: string)
    (anchor: string)
    (badge: string)
    (doc: string)
    (statement: string)
    (proofs: ProofBlock[])
    (refs: RefLists)
    =
    out.Append ($"""<section class="decl {className}" id="{encode anchor}">{badge}{doc}<pre class="statement"><code>{statement}</code></pre>""")
    |> ignore

    for proof in proofs do
        appendProof out proof

    appendRefs out refs
    out.Append "</section>" |> ignore

let private leadingBold = Regex @"^<p><strong>(.*?)</strong></p>"

/// <summary>The HTML of a page's blocks. Proofs and reference lists fold with <c>details</c> and need no script.</summary>
/// <param name="badge">The badge markup of a documented declaration, by its index in <c>RocqProject.Declarations</c>. Empty for none.</param>
/// <param name="blocks">The page's blocks in order.</param>
let toHtmlWith (badge: int -> string) (blocks: Block[]) : string =
    let out = StringBuilder ()

    for block in blocks do
        match block with
        | Heading (level, text, anchor) ->
            out.Append ($"""<h{level} id="{encode anchor}">{encode text}</h{level}>""")
            |> ignore
        | Documented (decl, anchor, doc, statement, proofs, refs) ->
            let titled = leadingBold.Replace (doc, """<p class="decl-title">$1</p>""")
            appendDeclaration out "documented" anchor (badge decl) $"""<div class="doc">{titled}</div>""" statement proofs refs
        | Quiet (_, anchor, statement, proofs, refs) -> appendDeclaration out "quiet" anchor "" "" statement proofs refs
        | Command html when String.IsNullOrWhiteSpace html -> out.Append html |> ignore
        | Command html ->
            out.Append ($"""<pre class="command"><code>{html}</code></pre>""")
            |> ignore
        | Prose html ->
            out.Append ($"""<div class="prose">{html}</div>""")
            |> ignore

    out.ToString ()

/// <summary>The HTML of a page's blocks, with no badges.</summary>
let toHtml (blocks: Block[]) : string =
    toHtmlWith (fun _ -> "") blocks
