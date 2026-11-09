/// <summary>Turns <c>pages</c> into <c>woven</c>, the content of the site, by replacing the markers on a page with real output.</summary>
module Docs.Weave

open System
open System.IO
open System.Text
open System.Text.RegularExpressions
open FSharp.Data.Rocq.Project
open FSharp.Data.Rocq.Structure
open Nacara.Plugins
open Rocq.Navigator

let private fence (language: string) (text: string) =
    let longest =
        Regex.Matches (text, "`{3,}")
        |> Seq.map _.Length
        |> Seq.fold max 2

    let marks = String ('`', longest + 1)
    $"{marks}{language}\n{text.TrimEnd ()}\n{marks}"

let private region (name: string) =
    let text = (File.ReadAllText "Examples.fs").Replace("\r\n", "\n")

    let m =
        Regex.Match (text, $@"//#region {name}\n(.*?)//#endregion", RegexOptions.Singleline)

    if m.Success then
        m.Groups[1].Value
    else
        failwith $"Examples.fs has no region {name}"

let private capture (name: string) =
    match Examples.all.TryFind name with
    | None -> failwith $"Examples.all has no example {name}"
    | Some example ->
        let previous = Console.Out
        use writer = new StringWriter ()
        Console.SetOut writer

        try
            example ()
        finally
            Console.SetOut previous

        writer.ToString().Replace('\\', '/')

let private project =
    lazy
        (match load "example/_CoqProject" with
         | Ok project -> project
         | Error failures ->
             failwith (
                 failures
                 |> Array.map describeFailure
                 |> String.concat "\n"
             ))

let private rocqSource (reference: string) =
    let path, name =
        match reference.Split '#' with
        | [| path; name |] -> path, Some name
        | parts -> parts[0], None

    let file =
        project.Value.Files
        |> Array.find (fun file -> file.Path.EndsWith (path, StringComparison.Ordinal))

    match name with
    | None -> fence "rocq" (Encoding.UTF8.GetString file.Model.Bytes)
    | Some name ->
        let declaration =
            file.Model.Declarations
            |> Array.find (fun declaration -> declaration.Name = Some name)

        fence "rocq" (Source.text file declaration.Extent)

/// The comment's source, then what the plugin renders from it.
let private prose (baseUrl: string) (phrase: string) =
    let fileIndex =
        project.Value.Files
        |> Array.findIndex (fun file -> file.Module = "Tally.Prose")

    let file = project.Value.Files[fileIndex]

    let comment =
        file.Model.Comments
        |> Array.find (fun comment -> (Article.commentBody file comment).Contains phrase)

    let links: Article.Links =
        {
            Module = fun name -> $"{baseUrl}example/{name}.html"
            Pipeline = Docs.pipeline []
        }

    let resolve =
        Article.resolver project.Value links (Article.declarationHref project.Value links) fileIndex

    let entries, body = Article.docParts file comment
    let rendered = Docs.render links.Pipeline resolve comment.Format body

    let arguments =
        if entries.Length = 0 then
            ""
        else
            let rows =
                entries
                |> Array.map (fun entry ->
                    let text = Docs.renderArgument links.Pipeline resolve comment.Format entry.Text
                    $"<dt><code>{entry.Name}</code></dt><dd>{text}</dd>")

            $"""<dl class="prose-arguments">{String.concat "" rows}</dl>"""

    let html = (Regex.Replace ($"{rendered}{arguments}", @"\n\s*\n", "\n")).Trim()

    let source = fence "rocq" (Source.text file comment.Span)
    $"{source}\n\n<div class=\"prose-rendered\">\n{html}\n</div>"

let private frame (baseUrl: string) (target: string) =
    $"""<iframe class="rocq-frame" loading="lazy" title="The example site at {target}" src="{baseUrl}example/{target}"></iframe>"""

let private marker = Regex @"<!--\s*(source|output|rocq|prose|frame):\s*(.+?)\s*-->"

let private weave (baseUrl: string) (text: string) =
    let woven =
        marker.Replace (
            text.Replace ("\r\n", "\n"),
            fun m ->
                let argument = m.Groups[2].Value

                match m.Groups[1].Value with
                | "source" -> fence "fsharp" (region argument)
                | "output" -> fence "text" (capture argument)
                | "rocq" -> rocqSource argument
                | "prose" -> prose baseUrl argument
                | _ -> frame baseUrl argument
        )

    woven.Replace ("{{base}}", baseUrl)

/// <summary>Replaces <c>woven</c> with the pages, their markers replaced, and <c>docs/SCHEMA.md</c> as a page.</summary>
let run (baseUrl: string) =
    if Directory.Exists "woven" then
        Directory.Delete ("woven", true)

    for page in Directory.GetFiles ("pages", "*.md", SearchOption.AllDirectories) do
        let target = Path.Combine ("woven", Path.GetRelativePath ("pages", page))

        Directory.CreateDirectory (Path.GetDirectoryName target)
        |> ignore

        File.WriteAllText (target, weave baseUrl (File.ReadAllText page))

    let schema =
        File.ReadAllLines "../SCHEMA.md"
        |> Array.skip 1
        |> String.concat "\n"

    File.WriteAllText ("woven/guide/schema.md", $"---\ntitle: JSON schema\n---\n{schema}\n")
