namespace Nacara.Plugins

open System
open System.Collections.Generic
open System.Net
open System.Reflection
open System.Text
open System.Text.RegularExpressions
open FSharp.Data.Rocq.Project
open FSharp.Data.Rocq.Structure
open Feliz.ViewEngine
open Nacara.Core
open Ranvier.View
open Rocq.Navigator
open Rocq.Navigator.Article
open Rocq.Navigator.Model

/// <summary>The front matter of a generated page.</summary>
type RocqPage =
    {
        Title: string
        /// <c>None</c> for the Guarantees page.
        Module: string option
        Description: string option
        /// The site's <c>collapseCodeOver</c>, from which the client restores the server's fold state.
        CollapseOver: int option
    }

/// <summary>Options of the Rocq plugin.</summary>
type RocqOptions =
    {
        /// The <c>_CoqProject</c> file, relative to the project root or absolute.
        Project: string
        /// The route prefix of the pages. Empty places them at the site root.
        Root: string
        /// The <c>facts.json</c> file. <c>None</c> builds with source headers and no badges.
        Facts: string option
        /// The <c>navigator.json</c> file. <c>None</c> leaves every setting absent.
        Config: string option
        /// Drops the facts of a file whose digest changed, rather than failing with <c>rocq/facts-stale</c>.
        DropStaleFacts: bool
        /// A link back to the host site: its label and URL.
        Home: (string * string) option
        /// The Markdig extensions applied to comments. Raw HTML stays disabled.
        Extensions: Markdig.IMarkdownExtension list
    }

/// <summary>The panes, article and document of a page.</summary>
module internal RocqReader =

    let encode (value: string) =
        Source.encode value

    /// Separates the panes from the article in a generated body. Source text and comments on a page have `<` escaped.
    [<Literal>]
    let Split = "<!--rocq:split-->"

    /// The comment `Server.render` writes at the root of each island.
    [<Literal>]
    let RootMarker = "<!--[r-->"

    let embedded (name: string) : byte array =
        use stream =
            Assembly.GetExecutingAssembly().GetManifestResourceStream($"Nacara.Plugin.Rocq.assets.%s{name}")

        match stream with
        | null -> [||]
        | stream ->
            use memory = new IO.MemoryStream ()
            stream.CopyTo memory
            memory.ToArray ()

    let bundle =
        lazy
            (match embedded "navigator.js" with
             | [||] -> None
             | bytes -> Some bytes)

    let scriptAsset = "assets/navigator.js"
    let styleAsset = "assets/navigator.css"

    /// The fonts and their licences, written beside the stylesheet.
    let fontFiles =
        [
            "Inter-wght.woff2"
            "JetBrainsMono-Regular.woff2"
            "JetBrainsMono-SemiBold.woff2"
            "SourceSerif4-wght.woff2"
            "SourceSerif4-wght-italic.woff2"
            "OFL-Inter.txt"
            "OFL-JetBrainsMono.txt"
            "OFL-SourceSerif4.txt"
        ]

    let private serverOptions =
        { Server.Options.defaults with
            Policy = Omit
        }

    let private hostOf (name: string) =
        $"""<div %s{Islands.hostAttribute}="%s{name}"></div>"""

    /// <summary>Inserts text before the end of the head.</summary>
    /// <returns><c>Error</c> when the document has no head.</returns>
    let insertInHead (text: string) (html: string) : Result<string, string> =
        match html.IndexOf ("</head>", StringComparison.Ordinal) with
        | -1 -> Result.Error "The rendered document has no </head>."
        | index -> Ok (html.Insert (index, text))

    /// <summary>Removes the head marks hydration would remove the elements for, leaving plain elements.</summary>
    /// <returns><c>Error</c> when the document has no head.</returns>
    let unmarkHead (html: string) : Result<string, string> =
        match html.IndexOf ("</head>", StringComparison.Ordinal) with
        | -1 -> Result.Error "The rendered document has no </head>."
        | index ->
            Ok (
                Regex.Replace (html.Substring (0, index), " data-ranvier-head=\"[^\"]*\"", "")
                + html.Substring index
            )

    /// <summary>Places an island's server markup in its empty host.</summary>
    /// <returns><c>Error</c> naming the island when the document has no such host.</returns>
    let fillHost (name: string) (markup: string) (html: string) : Result<string, string> =
        let host = hostOf name

        match html.IndexOf (host, StringComparison.Ordinal) with
        | -1 -> Result.Error $"The rendered document has no host for the {name} island."
        | index -> Ok (html.Insert (index + host.Length - "</div>".Length, markup))

    /// <summary>Removes the doctype <c>Server.renderDocument</c> writes, since Nacara writes its own.</summary>
    let withoutDoctype (html: string) : string =
        Regex.Replace (html, @"^\s*<!doctype html>", "", RegexOptions.IgnoreCase)

    let private shell (title: string) =
        Component.define (fun _ (markup: UnsafeHtml) -> markup) (fun t ->
            t.fragment
                [|
                    t.Document.title title
                    t.element<obj> (Representation.HtmlTag "div") [| t.Unsafe.html<obj>(fun markup -> markup) |] [||]
                |])

    let private islandMarkup (name: string, island: Component<unit>) =
        name,
        Server.render
            { serverOptions with
                IdPrefix = Islands.idPrefix name
            }
            island
            ()

    /// <summary>The inline head script that writes the saved look, mode and argument list to the root before the stylesheet applies.</summary>
    /// <remarks>Storage that throws, or holds an unknown value, leaves the server's <c>data-look</c> in place.</remarks>
    let preloadScript: string =
        let known (values: string list) =
            values
            |> List.map (fun value -> $"\"{value}\"")
            |> String.concat ","

        String.concat
            ""
            [
                "(function(){var r=document.documentElement;"
                "function a(n,k,v){try{var s=localStorage.getItem(k);if(v.indexOf(s)>=0)r.setAttribute(n,s)}catch(e){}}"
                $"""a("data-look","{Islands.lookKey}",[{known Islands.looks}]);"""
                $"""a("data-mode","{Islands.modeKey}",[{known Islands.modes}]);"""
                $"""a("{Islands.argumentsAttribute}","{Islands.argumentsKey}",[{known [ Islands.argumentsTypes; Islands.argumentsPanel ]}])"""
                "})()"
            ]

    /// <summary>The inline head script of a page with a client: the lists' saved place on the root until a client fails to start, and the Journal look's font request.</summary>
    /// <remarks>Blocked storage or an unknown value leaves the lists pinned. Under definitions, <c>data-lists</c> is absent.</remarks>
    /// <param name="serifUrl">The site URL of the serif font.</param>
    let clientPreloadScript (serifUrl: string) : string =
        String.concat
            ""
            [
                "(function(){var r=document.documentElement,p=null;"
                $"""try{{p=localStorage.getItem("{Islands.referencesKey}")}}catch(e){{}}"""
                $"""if(p!=="{Islands.placementUnder}")r.setAttribute("data-lists",p==="{Islands.placementBadges}"?p:"{Islands.placementPane}");"""
                """addEventListener("load",function(){if(r.getAttribute("data-client")!=="ready")r.removeAttribute("data-lists")});"""
                """if(r.getAttribute("data-look")==="journal"){var l=document.createElement("link");"""
                """l.rel="preload";l.as="font";l.type="font/woff2";l.crossOrigin="";"""
                $"""l.href="{serifUrl}";document.head.appendChild(l)}}}}"""
                ")()"
            ]

    /// <summary>The inline script after the article: applies each saved fold state to the article's folds before first paint.</summary>
    /// <remarks>A key with nothing saved, or an unknown value, leaves the server's state.</remarks>
    let foldScript: string =
        String.concat
            ""
            [
                "(function(){"
                "function g(k){try{return localStorage.getItem(k)}catch(e){return null}}"
                """function e(q,f){document.querySelectorAll("article "+q).forEach(function(d){d.open=f(d)})}"""
                $"""function s(q,k){{var v=g(k);if(v==="{Islands.foldOpen}"||v==="{Islands.foldClosed}")e(q,function(){{return v==="{Islands.foldOpen}"}})}}"""
                $"""s("details.proof","{Islands.proofsKey}");"""
                $"""s("details.imports","{Islands.importsKey}");"""
                $"""s("details.refs","{Islands.referencesOpenKey}");"""
                $"""var d=g("{Islands.definitionsKey}"),o=g("{Islands.definitionsOverKey}"),n=/^[1-9][0-9]{{0,5}}$/.test(o)?parseInt(o,10):{Islands.defaultDefinitionsOver};"""
                $"""if(d==="{Islands.foldOpen}"||d==="{Islands.foldClosed}"||d==="{Islands.foldLong}")e("details.body",function(b){{"""
                $"""return d==="{Islands.foldLong}"?parseInt(b.querySelector("summary").getAttribute("data-lines"),10)<=n:d==="{Islands.foldOpen}"}})"""
                "})()"
            ]

    /// <summary>The button that opens the settings, in the pane and the drawer alike.</summary>
    let settingsTrigger: string =
        $"""<button type="button" class="settings-trigger" {Islands.settingsTriggerAttribute} aria-haspopup="dialog">⚙ Settings</button>"""

    /// <summary>The fonts of the default look, requested before the stylesheet's rules apply.</summary>
    let fontPreloads (urlOf: string -> string) : string =
        [
            "Inter-wght.woff2"
            "JetBrainsMono-Regular.woff2"
            "JetBrainsMono-SemiBold.woff2"
        ]
        |> List.map (fun file ->
            let url = encode (urlOf $"assets/fonts/{file}")
            $"""<link rel="preload" href="{url}" as="font" type="font/woff2" crossorigin>""")
        |> String.concat ""

    /// <summary>Inserts text at the start of the head, before any stylesheet.</summary>
    /// <returns><c>Error</c> when the document has no head.</returns>
    let insertAtHeadStart (text: string) (html: string) : Result<string, string> =
        match html.IndexOf ("<head>", StringComparison.Ordinal) with
        | -1 -> Result.Error "The rendered document has no <head>."
        | index -> Ok (html.Insert (index + "<head>".Length, text))

    let private orFail (result: Result<string, string>) =
        match result with
        | Ok html -> html
        | Result.Error message -> failwith $"rocq: {message}"

    /// <summary>The URL of a page's card data, relative to the output directory.</summary>
    let dataPath (root: string) (name: string) =
        match root.Trim '/' with
        | "" -> $"data/{name}"
        | prefix -> $"{prefix}/data/{name}"

    let private symbolsFile (page: RocqPage) =
        match page.Module with
        | Some name -> $"{name}.symbols.json"
        | None -> "index.symbols.json"

    /// <summary>The whole document of a page.</summary>
    /// <param name="stylesheets">The site URLs of the stylesheets.</param>
    /// <param name="options">The plugin's options.</param>
    /// <param name="context">The page, with its panes in its data.</param>
    let render (stylesheets: string list) (options: RocqOptions) (context: PageContext<RocqPage>) : ReactElement =
        let site = context.Site
        let page = context.FrontMatter

        let data (key: string) =
            context.Page.TryData<string> key
            |> Option.defaultValue ""

        let left = data "rocq/left"
        let right = data "rocq/right"

        let index =
            match options.Root.Trim '/' with
            | "" -> "index.html"
            | prefix -> $"{prefix}/index.html"

        let home =
            match options.Home with
            | Some (label, url) -> $"""<a class="home" href="{encode url}">{encode label}</a>"""
            | None -> ""

        let brand =
            $"""<div class="brand">{home}<a class="site" href="{encode (site.UrlOfAsset index)}">{encode site.Title}</a></div>"""

        let crumb =
            match page.Module with
            | Some name -> encode name
            | None -> "Guarantees"

        let foldState =
            match bundle.Value with
            | Some _ -> $"<script>{foldScript}</script>"
            | None -> ""

        let shellMarkup =
            String.concat
                ""
                [
                    """<div class="shell">"""
                    $"""<nav class="pane pane-left" aria-label="Modules">{brand}{left}<div class="pane-foot">{settingsTrigger}</div></nav>"""
                    """<div class="center"><header class="topbar">"""
                    $"""<p class="crumb">{crumb}</p>"""
                    hostOf "search"
                    "</header>"
                    hostOf "settings"
                    hostOf "section"
                    hostOf "cards"
                    $"""<details class="drawer"><summary>Modules</summary>{brand}{left}<div class="drawer-foot">{settingsTrigger}</div></details>"""
                    $"""<details class="drawer"><summary>On this page</summary>{right}</details>"""
                    $"""<article class="page"><h1 class="page-title">{(encode page.Title).Replace(".", ".<wbr>")}</h1>{context.Content}</article>"""
                    foldState
                    "</div>"
                    $"""<aside class="pane pane-right" aria-label="On this page">{right}</aside>"""
                    "</div>"
                ]

        let document =
            { Server.DocumentShell.defaults with
                Lang = Some "en"
                HtmlAttributes = [ "data-look", "family" ]
                BodyAttributes =
                    [
                        "data-symbols", site.UrlOfAsset (dataPath options.Root (symbolsFile page))
                        "data-search", site.UrlOfAsset (dataPath options.Root "search.json")

                        match page.CollapseOver with
                        | Some limit -> Islands.collapseOverAttribute, string limit
                        | None -> ()
                    ]
                Stylesheets = stylesheets
                Scripts =
                    match bundle.Value with
                    | Some _ -> [ Server.ModuleScript.Src (site.UrlOfAsset scriptAsset) ]
                    | None -> []
            }

        let head =
            let description =
                match page.Description with
                | Some text -> $"""<meta name="description" content="{encode text}">"""
                | None -> ""

            $"""<meta name="viewport" content="width=device-width, initial-scale=1">{description}{fontPreloads site.UrlOfAsset}"""

        let preload =
            let client =
                match bundle.Value with
                | Some _ ->
                    let serif =
                        clientPreloadScript (site.UrlOfAsset "assets/fonts/SourceSerif4-wght.woff2")

                    $"<script>{serif}</script>"
                | None -> ""

            $"<script>{preloadScript}</script>{client}"

        let html =
            Server.renderDocument serverOptions document (shell page.Title) (UnsafeHtml.Trust shellMarkup)
            |> unmarkHead
            |> Result.bind (insertInHead head)
            |> Result.bind (insertAtHeadStart preload)

        Islands.islands
        |> List.map islandMarkup
        |> List.fold (fun html (name, markup) -> html |> Result.bind (fillHost name markup)) html
        |> orFail
        |> withoutDoctype
        |> Html.rawText

    let private modulePath (moduleName: string) =
        let parts = moduleName.Split '.'
        // The first component is the logical root, and the last one the file.
        if parts.Length > 2 then
            String.Join (".", parts[1 .. parts.Length - 2])
        else
            ""

    let private shortName (moduleName: string) =
        moduleName.Substring (moduleName.LastIndexOf '.' + 1)

    /// <summary>The left pane: the Guarantees link, the root-level modules, then the directory modules in project order, with declaration counts.</summary>
    /// <remarks>A directory whose modules are interleaved with another directory's repeats its heading.</remarks>
    /// <param name="links">Maps a module to its page.</param>
    /// <param name="project">The project.</param>
    /// <param name="indexUrl">The URL of the Guarantees page.</param>
    /// <param name="current">The module of the page, or <c>None</c> on the Guarantees page.</param>
    let leftPane (links: Links) (project: RocqProject) (indexUrl: string) (current: string option) : string =
        let out = StringBuilder ()

        let counts =
            project.Declarations
            |> Array.countBy _.File
            |> dict

        let currentMark (isCurrent: bool) =
            if isCurrent then """ on" aria-current="page""" else ""

        out.Append ($"""<a class="pane-item{currentMark current.IsNone}" href="{encode indexUrl}">Guarantees</a>""")
        |> ignore

        out.Append """<p class="pane-label">Modules</p>"""
        |> ignore

        let rootLevel, inDirectories =
            project.Files
            |> Array.indexed
            |> Array.partition (fun (_, file) -> modulePath file.Module = "")

        let mutable group = ""

        for index, file in Array.append rootLevel inDirectories do
            let directory = modulePath file.Module

            if directory <> group then
                group <- directory

                out.Append ($"""<p class="pane-group">{encode directory}</p>""")
                |> ignore

            let count =
                match counts.TryGetValue index with
                | true, count -> count
                | _ -> 0

            out.Append (
                $"""<a class="pane-item{currentMark (current = Some file.Module)}" href="{encode (links.Module file.Module)}"><span>{encode (shortName file.Module)}</span><span class="count">{count}</span></a>"""
            )
            |> ignore

        out.ToString ()

    let private unnamedAnchor = Regex @"^d\d+$"

    /// <summary>The right pane of a module page: its outline, and the pinned region with the empty slots of the cross-reference lists.</summary>
    let rightPane (blocks: Block[]) : string =
        let out = StringBuilder ()

        out.Append """<p class="pane-label">On this page</p>"""
        |> ignore

        let item (className: string) (anchor: string) (text: string) =
            out.Append ($"""<a class="pane-item {className}" href="#{encode anchor}">{encode text}</a>""")
            |> ignore

        for block in blocks do
            match block with
            | Heading (level, text, anchor) -> item $"outline-heading depth-{level}" anchor text
            | Documented (_, anchor, _, _, _, _, _) when not (unnamedAnchor.IsMatch anchor) -> item "outline-documented" anchor anchor
            | Quiet (_, anchor, _, _, _) when not (unnamedAnchor.IsMatch anchor) -> item "outline-quiet" anchor anchor
            | _ -> ()

        out.Append
            """<div class="pinned" role="region" aria-label="This definition's references"><div class="lists-head"><small>This definition</small><span class="lists-name"></span></div><p class="lists-empty"></p><div class="xref" data-slot="uses"></div><div class="xref" data-slot="used-by"></div></div>"""
        |> ignore

        out.ToString ()

    /// <summary>The right pane of the Guarantees page: its groups and their modules.</summary>
    let guaranteesPane (entries: Guarantees.Entry[]) : string =
        let out = StringBuilder ()

        out.Append """<p class="pane-label">On this page</p>"""
        |> ignore

        for group, className, heading in
            [
                Guarantees.Open, "open", "Open"
                Guarantees.Falsified, "falsified", "Falsified"
                Guarantees.Check, "check", "Checks"
                Guarantees.Guarantee, "guarantee", "Guarantees"
            ] do
            let members =
                entries
                |> Array.filter (fun entry -> entry.Group = group)

            if members.Length > 0 then
                out.Append ($"""<a class="pane-item outline-heading depth-2" href="#group-{className}">{heading}</a>""")
                |> ignore

                for name in members |> Array.map _.Module |> Array.distinct do
                    out.Append ($"""<a class="pane-item outline-quiet" href="#{className}-{encode name}">{encode (shortName name)}</a>""")
                    |> ignore

        out.ToString ()

    /// <summary>The badge markup of a declaration.</summary>
    let badgeHtml (badge: Badge option) : string =
        match badge with
        | None -> ""
        | Some NoAxioms -> """<span class="badge pass">✓ No axioms</span>"""
        | Some (Rests axioms) ->
            let names = axioms |> Array.map encode |> String.concat ", "
            $"""<span class="badge axioms">Rests on {names}</span>"""
