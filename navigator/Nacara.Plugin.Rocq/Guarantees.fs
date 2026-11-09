/// <summary>The index page: the project's titled theorems, sorted by what their proofs establish.</summary>
module Rocq.Navigator.Guarantees

open System
open System.Net
open System.Text
open System.Text.RegularExpressions
open FSharp.Data.Rocq.Project
open FSharp.Data.Rocq.Structure
open Nacara.Plugins
open Rocq.Navigator.Article

/// <summary>The status of a theorem, as its source and <c>navigator.json</c> state it.</summary>
type Group =
    /// The proof ends <c>Admitted</c>.
    | Open
    /// The name ends with the configured suffix.
    | Falsified
    /// An attached comment holds the configured marker.
    | Check
    /// Every other entry.
    | Guarantee

/// <summary>A theorem on the index page.</summary>
type Entry =
    {
        /// The opening bold line of the attached comment.
        Title: string
        Group: Group
        Module: string
        Href: string
        /// The attached documentation without its title.
        DocHtml: string
        /// <c>None</c> when Rocq reported nothing. An <c>Open</c> or <c>Falsified</c> entry never displays it.
        Axioms: Assumptions option
    }

let private withoutFirstLine (body: string) =
    let trimmed = body.TrimStart ()

    match trimmed.IndexOf '\n' with
    | -1 -> ""
    | index -> trimmed.Substring (index + 1)

/// <summary>An entry for each <c>Theorem</c> and <c>Corollary</c> whose attached <c>(**md</c> comment opens with a line wholly in bold, in project order.</summary>
/// <remarks>Without a suffix or marker in <c>config</c>, the <c>Falsified</c> or <c>Check</c> group stays empty. The first matching group in the order of <c>Group</c> wins.</remarks>
let entries (project: RocqProject) (links: Links) (config: Config) (facts: Facts option) : Entry[] =
    let href = declarationHref project links

    let symbolsByDeclaration =
        project.Symbols
        |> Array.filter _.Declaration.IsSome
        |> Array.groupBy _.Declaration.Value
        |> dict

    let axiomsOf (index: int) (declaration: Declaration) =
        match facts, symbolsByDeclaration.TryGetValue index with
        | Some facts, (true, symbols) ->
            symbols
            |> Array.tryFind (fun symbol -> Some symbol.Symbol.Name = declaration.Name)
            |> Option.bind (fun symbol -> facts.Symbols.TryFind (Facts.symbolKey project symbol))
            |> Option.bind _.Assumptions
        | _ -> None

    project.Declarations
    |> Array.mapi (fun index owned -> index, owned)
    |> Array.choose (fun (index, owned) ->
        let declaration = owned.Declaration
        let file = project.Files[owned.File]

        let docs =
            declaration.Docs
            |> Array.map (fun comment -> file.Model.Comments[comment])

        let title =
            docs
            |> Array.tryPick (fun comment ->
                if comment.Format = CommentFormat.Markdown then
                    Docs.firstBoldLine comment.Format (commentBody file comment)
                    |> Option.map (fun title -> comment.Index, title)
                else
                    None)

        match declaration.Keyword, title with
        | (Some "Theorem" | Some "Corollary"), Some (titled, title) ->
            let holds (test: string -> bool) (setting: string option) =
                setting |> Option.exists test

            let group =
                if
                    declaration.Proofs
                    |> Array.exists (fun proof -> proof.Ending = ProofEnding.Admitted)
                then
                    Open
                elif
                    config.FalsifiedSuffix
                    |> holds (fun suffix ->
                        declaration.Name
                        |> Option.exists _.EndsWith(suffix, StringComparison.Ordinal))
                then
                    Falsified
                elif
                    config.CheckMarker
                    |> holds (fun marker ->
                        docs
                        |> Array.exists (fun comment -> (commentBody file comment).Contains marker))
                then
                    Check
                else
                    Guarantee

            let resolve = resolver project links href owned.File

            let docHtml =
                docs
                |> Array.map (fun comment ->
                    let body = snd (docParts file comment)

                    Docs.render
                        links.Pipeline
                        resolve
                        comment.Format
                        (if comment.Index = titled then
                             withoutFirstLine body
                         else
                             body))
                |> String.concat ""

            Some
                {
                    Title = title
                    Group = group
                    Module = file.Module
                    Href = href index
                    DocHtml = docHtml
                    Axioms = axiomsOf index declaration
                }
        | _ -> None)

let private encode (value: string) =
    Source.encode value

let private badge (entry: Entry) =
    match entry.Group, entry.Axioms with
    | (Open | Falsified), _
    | _, None -> ""
    | _, Some Closed -> """<span class="badge pass">✓ No axioms</span>"""
    | _, Some (RestsOn axioms) ->
        let names = axioms |> Array.map encode |> String.concat ", "
        $"""<span class="badge axioms">Rests on {names}</span>"""

let private codeSpan = Regex "`([^`]+)`"

/// A title is one line of Markdown, of which code spans occur in practice.
let private titleHtml (title: string) =
    codeSpan.Replace (encode title, "<code>$1</code>")

/// <summary>The index page's HTML: each non-empty group under its heading, with entries under their module.</summary>
let toHtml (entries: Entry[]) : string =
    let out = StringBuilder ()

    for group, className, heading in
        [
            Open, "open", "Open"
            Falsified, "falsified", "Falsified"
            Check, "check", "Checks"
            Guarantee, "guarantee", "Guarantees"
        ] do
        let members =
            entries
            |> Array.filter (fun entry -> entry.Group = group)

        if members.Length > 0 then
            out.Append ($"""<section class="group group-{className}"><h2 id="group-{className}">{heading}</h2>""")
            |> ignore

            for moduleName, moduleEntries in members |> Array.groupBy _.Module do
                out.Append ($"""<h3 id="{className}-{encode moduleName}">{encode moduleName}</h3>""")
                |> ignore

                for entry in moduleEntries do
                    out.Append (
                        $"""<article class="entry"><h4><a href="{encode entry.Href}">{titleHtml entry.Title}</a></h4>{badge entry}{entry.DocHtml}</article>"""
                    )
                    |> ignore

            out.Append "</section>" |> ignore

    out.ToString ()
