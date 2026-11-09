/// <summary>The client data files: a card per reference key on a page, and the search index.</summary>
module Rocq.Navigator.Cards

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open FSharp.Data.Rocq.Project
open FSharp.Data.Rocq.Structure
open Nacara.Plugins
open Rocq.Navigator.Article
open Rocq.Navigator.Model

let private symbolsByDeclaration (project: RocqProject) =
    project.Symbols
    |> Array.filter _.Declaration.IsSome
    |> Array.groupBy _.Declaration.Value
    |> dict

let private namedSymbol (project: RocqProject) (symbols: IDictionary<int, ProjectSymbol[]>) (index: int) =
    match symbols.TryGetValue index with
    | true, candidates ->
        candidates
        |> Array.tryFind (fun symbol -> Some symbol.Symbol.Name = project.Declarations[index].Declaration.Name)
    | _ -> None

let private factOf (project: RocqProject) (facts: Facts option) (symbol: ProjectSymbol) =
    facts
    |> Option.bind (fun facts -> facts.Symbols.TryFind (Facts.symbolKey project symbol))

/// <summary>The badge of a declaration, by its index in <c>RocqProject.Declarations</c>.</summary>
/// <remarks>Only a theorem with a complete proof and a captured result has one.</remarks>
let badges (project: RocqProject) (facts: Facts option) : int -> Badge option =
    let symbols = symbolsByDeclaration project

    fun index ->
        let declaration = project.Declarations[index].Declaration

        if
            declaration.Category <> Category.Theorem
            || declaration.Proofs
               |> Array.exists (fun proof -> proof.Ending = ProofEnding.Admitted)
        then
            None
        else
            namedSymbol project symbols index
            |> Option.bind (factOf project facts)
            |> Option.bind _.Assumptions
            |> Option.map (fun assumptions ->
                match assumptions with
                | Closed -> NoAxioms
                | RestsOn axioms -> Rests axioms)

/// The declaration's text up to its body or proof.
let private headerOf (file: RocqFile) (declaration: Declaration) =
    let text = Source.text file (statementSpan declaration)
    text.TrimEnd().TrimEnd('=').TrimEnd(':').TrimEnd().TrimEnd('.')

let private docOf (project: RocqProject) (links: Links) (href: int -> string) (owned: ProjectDeclaration) =
    let file = project.Files[owned.File]
    let resolve = resolver project links href owned.File

    match
        docComments file owned.Declaration
        |> Array.map (fun comment -> Docs.render links.Pipeline resolve comment.Format (snd (docParts file comment)))
        |> String.concat ""
    with
    | "" -> None
    | html -> Some html

/// <summary>The type Rocq printed for a binder, by declaration index and binder name.</summary>
let printedBinder (project: RocqProject) (facts: Facts option) : int -> string -> string option =
    let symbols = symbolsByDeclaration project

    fun declaration name ->
        namedSymbol project symbols declaration
        |> Option.bind (factOf project facts)
        |> Option.bind (fun fact -> fact.Binders.TryFind name)

let private keyShape = Regex @"^([sxdf])(\d+)$|^b(\d+)\.(\d+)$"

/// <summary>A lookup from a reference key to its card. <c>None</c> for a key that names nothing in the project.</summary>
let internal lookup (project: RocqProject) (links: Links) (facts: Facts option) : string -> CardData option =
    let href = declarationHref project links
    let symbols = symbolsByDeclaration project
    let badge = badges project facts
    let modules = project.Files |> Array.map _.Module |> HashSet

    let edgesOf (node: Node) =
        usesOf project node |> Array.sumBy _.Count

    let printed = printedBinder project facts

    let binderCounts =
        project.Files
        |> Array.map (fun file ->
            lazy
                (let model = file.Model

                 model.Refs
                 |> Array.choose (fun reference -> model.Targets[reference.Target].Binder)
                 |> Array.countBy id
                 |> dict))

    let symbolCard (key: string) (symbol: ProjectSymbol) =
        let owned =
            symbol.Declaration
            |> Option.map (fun index -> project.Declarations[index])

        let file = project.Files[symbol.File]
        let fact = factOf project facts symbol

        let captured = fact |> Option.bind _.Type

        let header =
            owned
            |> Option.map (fun owned -> headerOf file owned.Declaration)
            |> Option.defaultValue ""

        let name =
            match symbol.Symbol.Kind, owned with
            | "not", Some owned ->
                notationText file owned.Declaration
                |> Option.defaultValue symbol.Symbol.Name
            | _ -> symbol.Symbol.Name

        {
            Key = key
            Name = name
            Qualified = Facts.symbolKey project symbol
            Module = file.Module
            Href = Some (symbolHref project links href symbol)
            Signature = captured |> Option.defaultValue header
            SignatureIsType = captured.IsSome
            DocHtml = owned |> Option.bind (docOf project links href)
            Mentions = edgesOf (SymbolNode symbol.Index)
            Badge =
                symbol.Declaration
                |> Option.filter (fun index -> namedSymbol project symbols index = Some symbol)
                |> Option.bind badge
            External = false
        }

    let declarationCard (key: string) (index: int) =
        let owned = project.Declarations[index]
        let file = project.Files[owned.File]

        let name =
            if owned.Declaration.Category = Category.Notation then
                notationText file owned.Declaration
            else
                owned.Declaration.Name
            |> Option.defaultValue (headerOf file owned.Declaration)

        {
            Key = key
            Name = name
            Qualified = file.Module + "." + name
            Module = file.Module
            Href = Some (href index)
            Signature = headerOf file owned.Declaration
            SignatureIsType = false
            DocHtml = docOf project links href owned
            Mentions = 0
            Badge = None
            External = false
        }

    let externalCard (key: string) (index: int) =
        let node = project.CrossReferences.Externals[index]
        let inProject = modules.Contains node.Module

        {
            Key = key
            Name = node.Qualified.Substring (node.Qualified.LastIndexOf '.' + 1)
            Qualified = node.Module + "." + node.Qualified
            Module = node.Module
            Href = if inProject then Some (links.Module node.Module) else None
            Signature = ""
            SignatureIsType = false
            DocHtml = None
            Mentions = edgesOf (ExternalSymbolNode index)
            Badge = None
            External = not inProject
        }

    let fileCard (key: string) (index: int) =
        let file = project.Files[index]

        {
            Key = key
            Name = file.Module
            Qualified = file.Module
            Module = file.Module
            Href = Some (links.Module file.Module)
            Signature = ""
            SignatureIsType = false
            DocHtml = None
            Mentions = 0
            Badge = None
            External = false
        }

    let binderCard (key: string) (declaration: int) (binder: int) =
        let owned = project.Declarations[declaration]
        let file = project.Files[owned.File]
        let span = file.Model.Binders[binder].Span
        let name = Source.text file span
        let statement = statementSpan owned.Declaration

        let inStatement =
            statement.Start <= span.Start
            && span.End <= statement.End

        let kind = if inStatement then printed declaration name else None

        let written =
            writtenBinderType file (if inStatement then statement else owned.Declaration.Extent) span
            |> Option.map (fun written -> Regex.Replace (Source.text file written, @"\s+", " "))

        let entry =
            if inStatement then
                docComments file owned.Declaration
                |> Array.tryPick (fun comment ->
                    fst (docParts file comment)
                    |> Array.tryFind (fun entry -> entry.Name = name)
                    |> Option.map (fun entry ->
                        let text =
                            Docs.renderArgument links.Pipeline (resolver project links href owned.File) comment.Format entry.Text

                        $"<p>{text}</p>"))
            else
                None

        {
            Key = key
            Name = name
            Qualified = name
            Module = file.Module
            Href = None
            Signature =
                kind
                |> Option.orElse written
                |> Option.defaultValue ""
            SignatureIsType = kind.IsSome
            DocHtml = entry
            Mentions =
                match binderCounts[owned.File].Value.TryGetValue binder with
                | true, count -> count
                | _ -> 0
            Badge = None
            External = false
        }

    fun key ->
        let m = keyShape.Match key

        if not m.Success then
            None
        else
            let inRange (index: int) (length: int) =
                index >= 0 && index < length

            if m.Groups[3].Success then
                let declaration, binder = int m.Groups[3].Value, int m.Groups[4].Value

                if
                    inRange declaration project.Declarations.Length
                    && inRange binder project.Files[project.Declarations[declaration].File].Model.Binders.Length
                then
                    Some (binderCard key declaration binder)
                else
                    None
            else
                let index = int m.Groups[2].Value

                match m.Groups[1].Value with
                | "s" when inRange index project.Symbols.Length -> Some (symbolCard key project.Symbols[index])
                | "x" when inRange index project.CrossReferences.Externals.Length -> Some (externalCard key index)
                | "d" when inRange index project.Declarations.Length -> Some (declarationCard key index)
                | "f" when inRange index project.Files.Length -> Some (fileCard key index)
                | _ -> None

let private dataRef = Regex "data-ref=\"([^\"]*)\""

/// <summary>The distinct <c>data-ref</c> keys in an HTML document, in order of first appearance.</summary>
let internal keysIn (html: string) : string[] =
    dataRef.Matches html
    |> Seq.map (fun m -> System.Net.WebUtility.HtmlDecode m.Groups[1].Value)
    |> Seq.distinct
    |> Array.ofSeq

/// <summary>The contents of a module's <c>symbols.json</c>: a card for each symbol of the file and each reference on its page.</summary>
let forModule (project: RocqProject) (links: Links) (facts: Facts option) (fileIndex: int) : CardData[] =
    let card = lookup project links facts

    let own =
        project.Symbols
        |> Array.filter (fun symbol -> symbol.File = fileIndex)
        |> Array.map (fun symbol -> $"s{symbol.Index}")

    let onPage = blocks project links fileIndex |> toHtml |> keysIn

    Array.append own onPage
    |> Array.distinct
    |> Array.choose card

let private summaryLength = 160

let private summaryOf (card: CardData) =
    match card.DocHtml with
    | None -> ""
    | Some html ->
        let text = Regex.Replace(Source.stripTags html, @"\s+", " ").Trim()

        if text.Length > summaryLength then
            text.Substring(0, summaryLength).TrimEnd() + "…"
        else
            text

let private unsearchable = set [ "sec"; "mod"; "modtype"; "lib"; "binder"; "var" ]

/// <summary>The contents of <c>search.json</c>: each declared symbol of the project, in project order.</summary>
let search (project: RocqProject) (links: Links) (facts: Facts option) : SearchEntry[] =
    let card = lookup project links facts

    project.Symbols
    |> Array.filter (fun symbol ->
        symbol.Declaration.IsSome
        && not (unsearchable.Contains symbol.Symbol.Kind))
    |> Array.choose (fun symbol ->
        card $"s{symbol.Index}"
        |> Option.bind (fun card ->
            card.Href
            |> Option.map (fun href ->
                {
                    Name = card.Name
                    Qualified = card.Qualified
                    Module = card.Module
                    Kind = symbol.Symbol.Kind
                    Href = href
                    Summary = summaryOf card
                })))
