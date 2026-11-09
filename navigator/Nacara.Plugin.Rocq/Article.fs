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

/// <summary>A row of a declaration's Arguments list.</summary>
type ArgumentRow =
    {
        Name: string
        /// The reference keys of the statement's binders with this name. Empty for a stale entry.
        Binders: string[]
        /// The binder's type as HTML, written with its references when the source has one.
        TypeHtml: string option
        TextHtml: string
    }

/// <summary>One unit of a page. Source-bearing fields decode to the exact source text.</summary>
type Block =
    /// A heading comment's heading. Anchors are unique on the page.
    | Heading of level: int * text: string * anchor: string
    /// A declaration with attached documentation.
    | Documented of
        decl: int *
        anchor: string *
        docHtml: string *
        arguments: ArgumentRow[] *
        statementHtml: string *
        proofs: ProofBlock[] *
        refs: RefLists
    /// A declaration with no attached documentation.
    | Quiet of decl: int * anchor: string * statementHtml: string * proofs: ProofBlock[] * refs: RefLists
    /// A sentence outside any declaration, or the whitespace between two blocks.
    | Command of html: string
    /// A sentence collected into the file's imports block. A page shows its imports together.
    | Import of html: string
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

let private bodyColumn (file: RocqFile) (comment: Comment) =
    let bytes = file.Model.Bytes
    let mutable start = comment.BodySpan.Start

    while start > 0 && bytes[start - 1] <> 10uy do
        start <- start - 1

    comment.BodySpan.Start - start

/// <summary>A comment's argument entries, and its body without them.</summary>
let internal docParts (file: RocqFile) (comment: Comment) : ArgumentEntry[] * string =
    Docs.splitArguments comment.Format (bodyColumn file comment) (commentBody file comment)

/// <summary>The comments documenting a declaration: its attached comments that hold no heading.</summary>
let internal docComments (file: RocqFile) (declaration: Declaration) : Comment[] =
    let hasHeading = file.Model.Headings |> Array.map _.Comment |> set

    declaration.Docs
    |> Array.filter (fun comment -> not (hasHeading.Contains comment))
    |> Array.map (fun comment -> file.Model.Comments[comment])

/// <summary>A declaration's statement: from its keyword to its body or proof.</summary>
let internal statementSpan (declaration: Declaration) : FSharp.Data.Rocq.Source.Span =
    let stop =
        match declaration.Statement, declaration.Body with
        | ValueSome statement, _ -> statement.End
        | ValueNone, ValueSome body -> body.Start
        | ValueNone, ValueNone -> declaration.SentenceSpan.End

    {
        Start = declaration.KeywordSpan.Start
        End = stop
    }

/// <summary>The type written in a binder's group, such as <c>nat</c> for <c>b</c> in <c>(a b : nat)</c> or <c>forall a b : nat,</c>.</summary>
/// <param name="bytes">The source.</param>
/// <param name="isBinder">Whether a binder's name starts at a source offset.</param>
/// <param name="scope">The source span holding the group.</param>
/// <param name="binder">The binder's name in the source.</param>
let internal writtenBinderTypeIn
    (bytes: byte[])
    (isBinder: int -> bool)
    (scope: FSharp.Data.Rocq.Source.Span)
    (binder: FSharp.Data.Rocq.Source.Span)
    : FSharp.Data.Rocq.Source.Span option =
    let isSpace (value: byte) =
        value = 32uy
        || value = 9uy
        || value = 10uy
        || value = 13uy

    let isOpen (value: byte) =
        value = byte '('
        || value = byte '{'
        || value = byte '['

    let isClose (value: byte) =
        value = byte ')'
        || value = byte '}'
        || value = byte ']'

    let isDelimiter (value: byte) =
        isOpen value
        || isClose value
        || value = byte ':'
        || value = byte ','
        || value = byte '='
        || value = byte '|'

    let isWord (value: byte) =
        not (isSpace value) && not (isDelimiter value)

    let quantifiers = set [ "forall"; "exists"; "fun"; "∀"; "∃"; "λ" ]

    // The bracket opening the group, or zero for a group opened by a quantifier.
    let rec opener (position: int) =
        if position <= scope.Start then
            None
        else
            let previous = bytes[position - 1]

            if isSpace previous then
                opener (position - 1)
            elif isOpen previous then
                Some previous
            elif isDelimiter previous then
                None
            else
                let mutable first = position - 1

                while first > scope.Start && isWord bytes[first - 1] do
                    first <- first - 1

                let word = Encoding.UTF8.GetString (bytes, first, position - first)

                if quantifiers.Contains word then
                    Some 0uy
                elif
                    word = "_"
                    || word = "!"
                    || isBinder first
                    || (word.StartsWith '!' && isBinder (first + 1))
                then
                    opener first
                else
                    None

    let startsAt (position: int) (text: string) =
        position + text.Length <= scope.End
        && Encoding.ASCII.GetString (bytes, position, text.Length) = text

    let rec afterColon (position: int) =
        if position >= scope.End then
            None
        elif isSpace bytes[position] || isWord bytes[position] then
            afterColon (position + 1)
        elif
            bytes[position] = byte ':'
            && not (startsAt position ":=")
        then
            Some (position + if startsAt position ":>" then 2 else 1)
        else
            None

    let rec afterComment (position: int) (depth: int) =
        if position >= scope.End then
            scope.End
        elif startsAt position "(*" then
            afterComment (position + 2) (depth + 1)
        elif startsAt position "*)" then
            if depth = 1 then
                position + 2
            else
                afterComment (position + 2) (depth - 1)
        else
            afterComment (position + 1) depth

    let rec typeEnd (group: byte) (position: int) (depth: int) =
        if position >= scope.End then
            None
        elif startsAt position "(*" then
            typeEnd group (afterComment position 0) depth
        elif isOpen bytes[position] then
            typeEnd group (position + 1) (depth + 1)
        elif isClose bytes[position] then
            match depth, group with
            | 0, 0uy -> None
            | 0, _ -> Some position
            | _ -> typeEnd group (position + 1) (depth - 1)
        elif depth > 0 then
            typeEnd group (position + 1) depth
        elif
            startsAt position ":="
            || (group = 0uy
                && (bytes[position] = byte ','
                    || startsAt position "=>"))
            || (group = byte '{'
                && (bytes[position] = byte '|'
                    || bytes[position] = byte '&'))
        then
            Some position
        else
            typeEnd group (position + 1) depth

    match opener binder.Start, afterColon binder.End with
    | Some group, Some start ->
        typeEnd group start 0
        |> Option.bind (fun stop ->
            let mutable first = start
            let mutable last = stop

            while first < last && isSpace bytes[first] do
                first <- first + 1

            while last > first && isSpace bytes[last - 1] do
                last <- last - 1

            if last > first then
                Some { Start = first; End = last }
            else
                None)
    | _ -> None

/// <summary>The type written in a binder's group, as <see cref="writtenBinderTypeIn"/> reads it from a file.</summary>
/// <param name="file">The binder's file.</param>
/// <param name="scope">The source span holding the group.</param>
/// <param name="binder">The binder's name in the source.</param>
let internal writtenBinderType
    (file: RocqFile)
    (scope: FSharp.Data.Rocq.Source.Span)
    (binder: FSharp.Data.Rocq.Source.Span)
    : FSharp.Data.Rocq.Source.Span option =
    let isBinder (start: int) =
        file.Model.Binders
        |> Array.exists (fun known -> known.Span.Start = start)

    writtenBinderTypeIn file.Model.Bytes isBinder scope binder

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

/// <summary>True for a sentence the imports block collects. A locality modifier such as <c>Local</c> is outside the keyword.</summary>
let internal collectsAsImport (category: Category) (keyword: string option) : bool =
    category = Category.Import
    || keyword = Some "Open Scope"

/// <summary>The sentences collected into a file's imports block: imports and scope opens at file top level, before the first scope or declaration.</summary>
let internal collectedImports (model: FileModel) : Set<int> =
    let boundaries =
        [
            for declaration in model.Declarations do
                declaration.Extent.Start

            for sentence in model.Sentences do
                if
                    sentence.Category = Category.Section
                    || sentence.Category = Category.Module
                then
                    sentence.Span.Start
        ]

    let boundary =
        if boundaries.IsEmpty then
            Int32.MaxValue
        else
            List.min boundaries

    model.Sentences
    |> Array.filter (fun sentence ->
        collectsAsImport sentence.Category sentence.Keyword
        && sentence.Span.Start < boundary)
    |> Array.map _.Index
    |> Set.ofArray

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

    let argumentRow (index: int) (statement: SourceSpan) (comment: Comment) (entry: ArgumentEntry) =
        let binders =
            model.Binders
            |> Array.indexed
            |> Array.filter (fun (_, binder) ->
                statement.Start <= binder.Span.Start
                && binder.Span.End <= statement.End
                && Source.text file binder.Span = entry.Name)

        let written =
            binders
            |> Array.map (fun (_, binder) -> writtenBinderType file statement binder.Span)

        {
            Name = entry.Name
            Binders =
                binders
                |> Array.map (fun (binder, _) -> $"b{index}.{binder}")
            TypeHtml =
                match
                    written
                    |> Array.map (Option.map (Source.text file))
                    |> Array.distinct
                with
                | [| Some _ |] ->
                    written[0]
                    |> Option.map (fun span -> source span.Start span.End)
                | _ -> None
            TextHtml = Docs.renderArgument links.Pipeline resolve comment.Format entry.Text
        }

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

        let docs = docComments file declaration

        if docs.Length > 0 then
            let docHtml =
                docs
                |> Array.map (fun comment -> render comment (snd (docParts file comment)))
                |> String.concat ""

            let arguments =
                docs
                |> Array.collect (fun comment ->
                    fst (docParts file comment)
                    |> Array.map (argumentRow index (statementSpan declaration) comment))

            Documented (index, anchors[declaration.Index], docHtml, arguments, statement, proofs, refs)
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

    let collected = collectedImports model
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
                let html = source cursor sentence.Span.End

                result.Add (
                    if collected.Contains sentence.Index then
                        Import html
                    else
                        Command html
                )

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

/// Operators holding a hyphen, where a browser may otherwise break the line.
let private unbreakable = Regex @"<[^>]*>|(?:&lt;)?-+[nd]?(?:&gt;|\*|∗)"

/// Each line is a block carrying its source indentation, which sets where its wrapped text hangs.
let private lineHtml (line: string) =
    let body =
        unbreakable.Replace (
            line,
            (fun m ->
                if m.Value[0] = '<' then
                    m.Value
                else
                    $"""<span class="nb">{m.Value}</span>""")
        )

    match Source.indent line with
    | 0 -> $"""<span class="l">{body}</span>"""
    | indent -> $"""<span class="l" style="--i:{indent}">{body}</span>"""

let private linesHtml (lines: string[]) =
    lines |> Array.map lineHtml |> String.concat ""

let private codeHtml (html: string) =
    linesHtml (Source.lines html)

let private appendProof (out: StringBuilder) (proof: ProofBlock) =
    let name = proofEndingName proof.Ending

    if proof.Ending = ProofEnding.Admitted then
        out.Append ($"""<details class="proof warning"><summary>{name} <span class="warning-text">Not proved</span></summary>""")
        |> ignore
    else
        out.Append ($"""<details class="proof"><summary>{name}</summary>""")
        |> ignore

    out.Append ($"""<pre><code>{codeHtml proof.Html}</code></pre></details>""")
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

let private appendStatement (out: StringBuilder) (config: Config) (statement: string) =
    let statementPre (lines: string[]) =
        $"""<pre class="statement"><code>{linesHtml lines}</code></pre>"""

    let lines = Source.lines statement

    let hasText (line: string) =
        not (String.IsNullOrWhiteSpace (Source.stripTags line))

    let first =
        lines
        |> Array.tryFindIndex hasText
        |> Option.defaultValue 0

    let last =
        lines
        |> Array.tryFindIndexBack hasText
        |> Option.defaultValue 0

    let count = last - first + 1

    let long =
        config.CollapseCodeOver
        |> Option.exists (fun limit -> count > limit)

    if count > 1 && (config.CollapsibleCode || long) then
        let state = if long then "" else " open"

        out.Append (
            $"""<details class="body"{state}><summary data-lines="{count} lines">{statementPre lines[..first]}</summary>{statementPre lines[first + 1 ..]}</details>"""
        )
        |> ignore
    else
        out.Append (statementPre lines) |> ignore

let private appendImports (out: StringBuilder) (imports: string[]) =
    if imports.Length > 0 then
        let code =
            imports
            |> Array.map _.TrimStart()
            |> String.concat "\n"

        out.Append (
            $"""<details class="imports"><summary>{imports.Length}</summary><pre class="command"><code>{codeHtml code}</code></pre></details>"""
        )
        |> ignore

/// The index of the block after a page's opening heading and its prose.
let private leadEnd (blocks: Block[]) =
    let rec find (index: int) (seenHeading: bool) =
        if index = blocks.Length then
            index
        else
            match blocks[index] with
            | Heading _ when seenHeading -> index
            | Heading _ -> find (index + 1) true
            | Prose _
            | Import _ -> find (index + 1) seenHeading
            | Command html when String.IsNullOrWhiteSpace html -> find (index + 1) seenHeading
            | _ -> index

    find 0 false

let private generatedAnchor = Regex @"^d\d+$"
let private repeatSuffix = Regex @"~\d+$"

/// The name a declaration shows beside its lists. A notation's anchor is generated, so it shows its first source line.
let private displayName (anchor: string) (statement: string) =
    if generatedAnchor.IsMatch anchor then
        Source.lines statement
        |> Array.map (fun line -> (Source.stripTags line).Trim())
        |> Array.tryFind (fun line -> line <> "")
        |> Option.defaultValue anchor
    else
        repeatSuffix.Replace (anchor, "")

/// A button placed outside the statement's `pre`.
let private appendChip (out: StringBuilder) (refs: RefLists) =
    if refs.Uses.Length + refs.UsedBy.Length > 0 then
        out.Append (
            $"""<button type="button" class="refs-chip" aria-haspopup="dialog">uses {refs.Uses.Length} · used by {refs.UsedBy.Length}</button>"""
        )
        |> ignore

/// The list holds every type. The stylesheet hides or shows them, and places the list, from `data-args`.
let private argumentsHtml (rows: ArgumentRow[]) =
    let rowHtml (row: ArgumentRow) =
        let kind =
            match row.TypeHtml with
            | Some html -> $"""<span class="args-type"> : {html}</span>"""
            | None -> ""

        let binders = encode (String.concat " " row.Binders)

        $"""<div class="args-row" data-binders="{binders}"><div class="args-sig"><span class="args-name">{encode row.Name}</span>{kind}</div><div class="args-text">{row.TextHtml}</div></div>"""

    if rows.Length = 0 then
        ""
    else
        let inner = rows |> Array.map rowHtml |> String.concat ""
        $"""<div class="args"><p class="args-label">Arguments</p><div class="args-rows">{inner}</div></div>"""

/// <summary>Fills each argument row with no written type from Rocq's printed binders, for a row matching a statement binder.</summary>
/// <param name="printed">The printed type, by declaration index and binder name.</param>
/// <param name="blocks">The page's blocks.</param>
let withPrintedTypes (printed: int -> string -> string option) (blocks: Block[]) : Block[] =
    blocks
    |> Array.map (function
        | Documented (decl, anchor, doc, arguments, statement, proofs, refs) ->
            let filled =
                arguments
                |> Array.map (fun row ->
                    if row.TypeHtml.IsNone && row.Binders.Length > 0 then
                        { row with
                            TypeHtml = printed decl row.Name |> Option.map encode
                        }
                    else
                        row)

            Documented (decl, anchor, doc, filled, statement, proofs, refs)
        | other -> other)

let private appendDeclaration
    (out: StringBuilder)
    (config: Config)
    (className: string)
    (anchor: string)
    (badge: string)
    (doc: string)
    (arguments: string)
    (statement: string)
    (proofs: ProofBlock[])
    (refs: RefLists)
    =
    let name =
        match displayName anchor statement with
        | same when same = anchor -> ""
        | other -> " data-name=\"" + encode other + "\""

    out.Append ($"""<section class="decl {className}" id="{encode anchor}"{name}>{badge}{doc}{arguments}<div class="panel">""")
    |> ignore

    appendStatement out config statement
    appendChip out refs
    out.Append arguments |> ignore

    for proof in proofs do
        appendProof out proof

    out.Append "</div>" |> ignore
    appendRefs out refs
    out.Append "</section>" |> ignore

let private leadingBold = Regex @"^<p><strong>(.*?)</strong></p>"

/// <summary>The HTML of a page's blocks. Proofs, imports and reference lists fold with <c>details</c> and need no script. The imports appear together, closed, after the page's opening heading and prose.</summary>
/// <param name="config">The settings selecting which code bodies fold.</param>
/// <param name="badge">The badge markup of a documented declaration, by its index in <c>RocqProject.Declarations</c>. Empty for none.</param>
/// <param name="blocks">The page's blocks in order.</param>
let toHtmlWith (config: Config) (badge: int -> string) (blocks: Block[]) : string =
    let out = StringBuilder ()

    let imports =
        blocks
        |> Array.choose (function
            | Import html -> Some html
            | _ -> None)

    let importsAt = leadEnd blocks

    for index, block in Array.indexed blocks do
        if index = importsAt then
            appendImports out imports

        match block with
        | Heading (level, text, anchor) ->
            out.Append ($"""<h{level} id="{encode anchor}">{encode text}</h{level}>""")
            |> ignore
        | Documented (decl, anchor, doc, arguments, statement, proofs, refs) ->
            let titled = leadingBold.Replace (doc, """<p class="decl-title">$1</p>""")

            appendDeclaration
                out
                config
                "documented"
                anchor
                (badge decl)
                $"""<div class="doc">{titled}</div>"""
                (argumentsHtml arguments)
                statement
                proofs
                refs
        | Quiet (_, anchor, statement, proofs, refs) -> appendDeclaration out config "quiet" anchor "" "" "" statement proofs refs
        | Command html when String.IsNullOrWhiteSpace html -> out.Append html |> ignore
        | Command html ->
            out.Append ($"""<pre class="command"><code>{codeHtml html}</code></pre>""")
            |> ignore
        | Import _ -> ()
        | Prose html ->
            out.Append ($"""<div class="prose">{html}</div>""")
            |> ignore

    if importsAt = blocks.Length then
        appendImports out imports

    out.ToString ()

/// <summary>The HTML of a page's blocks, with no badges and every code body plain.</summary>
let toHtml (blocks: Block[]) : string =
    toHtmlWith Config.none (fun _ -> "") blocks
