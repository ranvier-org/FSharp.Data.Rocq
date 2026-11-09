namespace Nacara.Plugins

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.RegularExpressions
open FSharp.Data.Rocq.Project
open FSharp.Data.Rocq.Structure
open Nacara.Core
open Rocq.Navigator
open Rocq.Navigator.Article

/// <summary>Documentation pages from a compiled Rocq project: a page per module, and the Guarantees page.</summary>
[<RequireQualifiedAccess>]
module Rocq =

    /// <summary>A project at <c>_CoqProject</c> published at the site root, with no facts or configuration.</summary>
    let defaults =
        {
            Project = "_CoqProject"
            Root = ""
            Facts = None
            Config = None
            DropStaleFacts = false
            Home = None
            Extensions = []
        }

    /// <summary>Set <c>Project</c>.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let project value (options: RocqOptions) =
        { options with Project = value }

    /// <summary>Set <c>Root</c>.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let root value (options: RocqOptions) =
        { options with Root = value }

    /// <summary>Set <c>Facts</c>.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let facts value (options: RocqOptions) =
        { options with Facts = value }

    /// <summary>Set <c>Config</c>.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let config value (options: RocqOptions) =
        { options with Config = value }

    /// <summary>Set <c>DropStaleFacts</c>.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let dropStaleFacts value (options: RocqOptions) =
        { options with DropStaleFacts = value }

    /// <summary>Set <c>Home</c>.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let home value (options: RocqOptions) =
        { options with Home = value }

    /// <summary>Set <c>Extensions</c>.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let extensions value (options: RocqOptions) =
        { options with Extensions = value }

    let private indexPath (root: string) =
        match root.Trim '/' with
        | "" -> "index.html"
        | prefix -> $"{prefix}/index.html"

    let private resolvePath (projectRoot: AbsolutePath) (path: string) =
        Path.GetFullPath (Path.Combine (AbsolutePath.value projectRoot, path))

    let private linksFor (site: SiteInfo) (options: RocqOptions) =
        {
            Module = fun name -> site.UrlOf (Anchors.routeOf options.Root site.RootLocale name)
            Pipeline = Docs.pipeline options.Extensions
        }

    /// Removes the verdicts of theorems that disprove a claim, so no surface shows them passing.
    let private withoutFalsified (project: RocqProject) (config: Config) (facts: Facts) =
        match config.FalsifiedSuffix with
        | None -> facts
        | Some suffix ->
            let falsified =
                project.Symbols
                |> Array.filter (fun symbol ->
                    symbol.Declaration.IsSome
                    && symbol.Symbol.Name.EndsWith (suffix, StringComparison.Ordinal))
                |> Array.map (Facts.symbolKey project)
                |> set

            { facts with
                Symbols =
                    facts.Symbols
                    |> Map.map (fun name fact ->
                        if falsified.Contains name then
                            { fact with Assumptions = None }
                        else
                            fact)
            }

    /// A file held open by a running compile is a load failure rather than a crash.
    let private loadProject (projectFile: string) =
        try
            load projectFile
            |> Result.mapError (Array.map describeFailure)
        with
        | :? AggregateException as failure when
            (failure.InnerExceptions
             |> Seq.forall (fun inner -> inner :? IOException))
            ->
            failure.InnerExceptions
            |> Seq.map _.Message
            |> Array.ofSeq
            |> Result.Error
        | :? IOException as failure -> Result.Error [| failure.Message |]

    type private Loaded =
        {
            Project: RocqProject option
            Facts: Facts option
            Config: Config
            Links: Links
            Pages: GeneratedContent list
            Found: Diagnostic list
        }

    let private frontMatter (page: RocqPage) =
        let quoted (value: string) =
            "\""
            + value.Replace("\\", "\\\\").Replace("\"", "\\\"")
            + "\""

        let description =
            match page.Description with
            | Some text -> $"description: {quoted text}\n"
            | None -> ""

        let moduleLine =
            match page.Module with
            | Some name -> $"module: {quoted name}\n"
            | None -> ""

        $"---\ntitle: {quoted page.Title}\n{moduleLine}{description}---\n"

    let private document (path: string) (page: RocqPage) (left: string) (right: string) (article: string) =
        GeneratedContent.create
            path
            (frontMatter page
             + left
             + RocqReader.Split
             + right
             + RocqReader.Split
             + article)

    let private globOf (file: RocqFile) =
        AbsolutePath.create (Path.ChangeExtension (file.FullPath, ".glob"))

    let private producePages
        (site: SiteInfo)
        (options: RocqOptions)
        (links: Links)
        (project: RocqProject)
        (facts: Facts option)
        (config: Config)
        (factsFile: AbsolutePath list)
        =
        let indexUrl = site.UrlOfAsset (indexPath options.Root)
        let badge = Cards.badges project facts >> RocqReader.badgeHtml

        let guarantees = Guarantees.entries project links config facts

        [
            document
                "index.rocqpage"
                {
                    Title = "Guarantees"
                    Module = None
                    Description = Some "What the proofs promise, grouped by module."
                }
                (RocqReader.leftPane links project indexUrl None)
                (RocqReader.guaranteesPane guarantees)
                (Guarantees.toHtml guarantees)
            |> GeneratedContent.dependsOn (
                [
                    for file in project.Files do
                        AbsolutePath.create file.FullPath
                        globOf file
                ]
                @ factsFile
            )

            for index, file in Array.indexed project.Files do
                let blocks = Article.blocks project links index

                document
                    $"{file.Module}.rocqpage"
                    {
                        Title = file.Module
                        Module = Some file.Module
                        Description = None
                    }
                    (RocqReader.leftPane links project indexUrl (Some file.Module))
                    (RocqReader.rightPane blocks)
                    (Article.toHtmlWith config badge blocks)
                |> GeneratedContent.dependsOn (
                    [ AbsolutePath.create file.FullPath; globOf file ]
                    @ factsFile
                )
        ]

    let private loadFresh (site: SiteInfo) (projectRoot: AbsolutePath) (options: RocqOptions) =
        let found = ResizeArray<Diagnostic>()
        let links = linksFor site options

        let config =
            match options.Config with
            | None -> Config.none
            | Some path ->
                match Config.read (resolvePath projectRoot path) with
                | Ok config -> config
                | Result.Error message ->
                    found.Add (
                        Diagnostic.error "config-unreadable" message
                        |> Diagnostic.withHint "The path is resolved from the project root"
                    )

                    Config.none

        match loadProject (resolvePath projectRoot options.Project) with
        | Result.Error failures ->
            for failure in failures do
                found.Add (
                    Diagnostic.error "project-load-failed" failure
                    |> Diagnostic.withHint "Compile the Rocq project first, so each source has a current .glob beside it"
                )

            {
                Project = None
                Facts = None
                Config = config
                Links = links
                Pages = []
                Found = List.ofSeq found
            }
        | Ok project ->
            let facts =
                match options.Facts with
                | None -> None
                | Some path ->
                    match Facts.read (resolvePath projectRoot path) with
                    | Result.Error message ->
                        found.Add (
                            Diagnostic.error "facts-unreadable" message
                            |> Diagnostic.withHint "Run rocq-capture to write the file"
                        )

                        None
                    | Ok facts ->
                        match Facts.reconcile options.DropStaleFacts project facts with
                        | Ok facts -> Some (withoutFalsified project config facts)
                        | Result.Error stale ->
                            for message in stale do
                                found.Add (
                                    Diagnostic.error "facts-stale" message
                                    |> Diagnostic.withHint "Run rocq-capture again, or set DropStaleFacts to build without those files' facts"
                                )

                            None

            let factsFile =
                options.Facts
                |> Option.map (resolvePath projectRoot >> AbsolutePath.create)
                |> Option.toList

            {
                Project = Some project
                Facts = facts
                Config = config
                Links = links
                Pages = producePages site options links project facts config factsFile
                Found = List.ofSeq found
            }

    let private stampOf (projectRoot: AbsolutePath) (options: RocqOptions) =
        let stamp (path: string) =
            let file = FileInfo path

            if file.Exists then
                path, file.Length, file.LastWriteTimeUtc
            else
                path, -1L, DateTime.MinValue

        let projectFile = resolvePath projectRoot options.Project

        [
            stamp projectFile

            if File.Exists projectFile then
                for file in readProject projectFile do
                    stamp file.FullPath
                    stamp (Path.ChangeExtension (file.FullPath, ".glob"))

            for path in
                Option.toList options.Facts
                @ Option.toList options.Config do
                stamp (resolvePath projectRoot path)
        ]

    let private cacheGate = obj ()
    let private cache = Dictionary<string, (string * int64 * DateTime) list * Loaded>()

    /// The project and its pages, loaded again only when one of the inputs has changed.
    let private loadCached (site: SiteInfo) (projectRoot: AbsolutePath) (options: RocqOptions) =
        lock cacheGate (fun () ->
            let key =
                $"{AbsolutePath.value projectRoot}|{site.UrlOfAsset String.Empty}|{site.Title}|{options}"

            let stamp = stampOf projectRoot options

            match cache.TryGetValue key with
            | true, (cachedStamp, loaded) when cachedStamp = stamp -> loaded
            | _ ->
                let loaded = loadFresh site projectRoot options
                cache[key] <- (stamp, loaded)
                loaded)

    let private relativeHref = Regex "(?:href|src)\\s*=\\s*\"([^\"#/][^\"]*)\""
    let private urlScheme = Regex @"^[a-zA-Z][a-zA-Z0-9+.\-]*:"

    let private checkPage (context: HookContext) (loaded: Loaded) (project: RocqProject) (page: Page) (html: string) =
        let card = Cards.lookup project loaded.Links loaded.Facts

        let moduleName =
            match page.FrontMatter with
            | :? RocqPage as frontMatter -> frontMatter.Module
            | _ -> None

        let name = moduleName |> Option.defaultValue "Guarantees"

        for reference in relativeHref.Matches page.Html do
            let url = reference.Groups[1].Value

            if not (urlScheme.IsMatch url) then
                context.Diagnostics.Add (
                    Diagnostic.error "comment-link-broken" $"{name}: the comment link {url} names no module of the project"
                    |> Diagnostic.withHint "Write a comment link as <Module>.html#<declaration>, with a module from _CoqProject"
                )

        let index =
            moduleName
            |> Option.bind (fun moduleName ->
                project.Files
                |> Array.tryFindIndex (fun file -> file.Module = moduleName))

        let keys = Cards.keysIn html

        let cards =
            match index with
            | Some index -> Cards.forModule project loaded.Links loaded.Facts index
            | None -> keys |> Array.choose card

        let known = cards |> Array.map _.Key |> set

        for key in keys do
            if not (known.Contains key) then
                context.Diagnostics.Add (
                    Diagnostic.error "ref-without-card" $"{name}: the data-ref {key} has no card"
                    |> Diagnostic.withHint "A reference key names a symbol, external, binder, declaration or file of the project"
                )

        cards

    /// <summary>Reports written pages with no island root. The hook's <c>Rendered</c> text precedes asset transforms, so the check reads the files.</summary>
    let private checkHydrationMarkers (context: HookContext) =
        let stripped =
            context.Rendered
            |> List.filter (fun (page, _) -> page.Format = ".rocqpage")
            |> List.map (fun (page, _) -> RelativePath.value (Url.outputPath page.Route))
            |> List.filter (fun path ->
                let file =
                    AbsolutePath.value (AbsolutePath.combine context.OutputDirectory [ path ])

                File.Exists file
                && not ((File.ReadAllText file).Contains RocqReader.RootMarker))

        match stripped with
        | [] -> ()
        | first :: _ ->
            context.Diagnostics.Add (
                Diagnostic.error
                    "hydration-markers-stripped"
                    $"{stripped.Length} written pages, the first {first}, have no hydration root comments. Cards, search, the look switch and the right pane's lists require them."
                |> Diagnostic.withHint "Keep the comments: Nuglify.minifyHtmlWith (Nuglify.htmlRemoveComments false)"
            )

    let private writeData (options: RocqOptions) (context: HookContext) =
        checkHydrationMarkers context
        let loaded = loadCached context.Site context.ProjectRoot options

        match loaded.Project with
        | None -> ()
        | Some project ->
            for page, html in context.Rendered do
                if page.Format = ".rocqpage" then
                    let cards = checkPage context loaded project page html

                    let file =
                        match page.FrontMatter with
                        | :? RocqPage as frontMatter when frontMatter.Module.IsSome -> $"{frontMatter.Module.Value}.symbols.json"
                        | _ -> "index.symbols.json"

                    context.Write (RocqReader.dataPath options.Root file) (Model.cardsWire.Encode cards)
                    |> ignore

            context.Write (RocqReader.dataPath options.Root "search.json") (Model.searchWire.Encode (Cards.search project loaded.Links loaded.Facts))
            |> ignore

    /// Splits a command line, honouring single and double quotes.
    let private splitCommand (command: string) =
        let words = ResizeArray<string>()
        let word = StringBuilder ()
        let mutable quote = None
        let mutable started = false

        for c in command do
            match quote with
            | Some q when c = q -> quote <- None
            | Some _ -> word.Append c |> ignore
            | None when c = '"' || c = '\'' ->
                quote <- Some c
                started <- true
            | None when Char.IsWhiteSpace c ->
                if started then
                    words.Add (word.ToString ())
                    word.Clear () |> ignore
                    started <- false
            | None ->
                word.Append c |> ignore
                started <- true

        if started then
            words.Add (word.ToString ())

        words.ToArray ()

    let private captureHelp =
        """Runs Rocq over the compiled project and writes its types and assumptions to a facts file.

Arguments:
  --rocq "<command>"   The command that starts rocq, split on spaces with quotes honoured
  --out <path>         The facts file to write, relative to the project root

The project and navigator.json come from the plugin's options. The project must be compiled.

  dotnet run -- rocq-capture --rocq "rocq" --out facts.json"""

    let private capture (options: RocqOptions) (context: CommandContext) =
        let rec parse (arguments: string list) (rocq: string option) (out: string option) =
            match arguments with
            | "--rocq" :: value :: rest -> parse rest (Some value) out
            | "--out" :: value :: rest -> parse rest rocq (Some value)
            | [] -> rocq, out
            | other :: _ ->
                eprintfn $"rocq-capture: unknown argument {other}"
                None, None

        match parse context.Arguments None None with
        | Some rocq, Some out ->
            let projectFile = resolvePath context.ProjectRoot options.Project

            let config =
                options.Config
                |> Option.map (resolvePath context.ProjectRoot >> Config.read)
                |> Option.defaultValue (Ok Config.none)

            match loadProject projectFile, config with
            | Result.Error failures, _ ->
                for failure in failures do
                    eprintfn $"rocq-capture: {failure}"

                1
            | _, Result.Error message ->
                eprintfn $"rocq-capture: {message}"
                1
            | Ok project, Ok config ->
                match Facts.capture (splitCommand rocq) (Path.GetDirectoryName projectFile) project config with
                | Result.Error message ->
                    eprintfn $"rocq-capture: {message}"
                    1
                | Ok facts ->
                    Facts.write (resolvePath context.ProjectRoot out) facts
                    printfn $"rocq-capture: {facts.Symbols.Count} symbols written to {out}"
                    0
        | _ ->
            eprintfn "%s" captureHelp
            2

    type private RocqPlugin(options: RocqOptions) =
        interface IPlugin with
            member _.Name = "rocq"

            member _.Configure registry =
                let registry =
                    registry
                    |> Registry.frontMatter
                        {
                            Name = "rocqpage"
                            Extensions = [ ".rocqpage" ]
                            Opening = "---"
                            Closing = "---"
                            Wrapper = None
                        }
                    |> Registry.transform
                        {
                            Name = "rocqpage"
                            Extensions = [ ".rocqpage" ]
                            Transform =
                                fun _ page ->
                                    match page.Body.Split RocqReader.Split with
                                    | [| left; right; article |] ->
                                        { page.WithData("rocq/left", left).WithData("rocq/right", right) with
                                            Html = article
                                        }
                                    | _ -> { page with Html = page.Body }
                        }
                    |> Registry.asset (WriteText (snd (Styles.compiled ()), RelativePath.create RocqReader.styleAsset))
                    |> Registry.onBuildComplete (writeData options)
                    |> Registry.command (
                        PluginCommand.create "rocq-capture" "Capture types and assumptions from a compiled Rocq project" (capture options)
                        |> PluginCommand.help captureHelp
                    )

                let registry =
                    RocqReader.fontFiles
                    |> List.fold
                        (fun registry name ->
                            registry
                            |> Registry.asset (WriteBytes (RocqReader.embedded $"fonts.{name}", RelativePath.create $"assets/fonts/{name}")))
                        registry

                match RocqReader.bundle.Value with
                | Some bytes ->
                    registry
                    |> Registry.asset (WriteBytes (bytes, RelativePath.create RocqReader.scriptAsset))
                | None ->
                    registry
                    |> Registry.onPagesRouted (fun context ->
                        context.Diagnostics.Add (
                            Diagnostic.warning "client-missing" "The site was built with no client bundle"
                            |> Diagnostic.withHint "Run navigator/build-client.sh and rebuild; the pages read without it"
                        ))

    /// <summary>The plugin itself: the page format, assets, data files and <c>rocq-capture</c>. The collection produces the pages.</summary>
    /// <param name="options">The same options the collection was given.</param>
    let create (options: RocqOptions) : IPlugin =
        RocqPlugin options

    /// <summary>Adds the plugin to a site.</summary>
    /// <param name="options">The same options the collection was given.</param>
    /// <param name="site">The site being described.</param>
    let register (options: RocqOptions) (site: Site) : Site =
        Site.plugin (create options) site

    /// <summary>The Reader layout: modules, article and outline, with the client islands. It renders the whole document.</summary>
    /// <param name="options">The same options the collection was given.</param>
    /// <param name="context">The page being rendered.</param>
    let reader (options: RocqOptions) (context: PageContext<RocqPage>) =
        RocqReader.render [ context.Site.UrlOfAsset RocqReader.styleAsset ] options context

    let private decoder: Decoder<RocqPage> =
        Decode.object (fun get ->
            {
                Title = get.Required.Field "title" Decode.string
                Module = get.Optional.Field "module" Decode.string
                Description = get.Optional.Field "description" Decode.string
            })

    /// <summary>A collection whose pages are the project's modules and its Guarantees page.</summary>
    /// <remarks>Load problems are the diagnostics <c>rocq/project-load-failed</c> and <c>rocq/facts-stale</c>. A build never runs Rocq.</remarks>
    /// <param name="name">What the site calls the collection.</param>
    /// <param name="options">Which project to read, and where to publish it.</param>
    let collection (name: string) (options: RocqOptions) : Collection<RocqPage> =
        Collection.create name decoder
        |> Collection.title _.Title
        |> Collection.route (fun info ->
            match info.FrontMatter.Module with
            | Some moduleName -> Anchors.routeOf options.Root info.Locale moduleName
            | None -> Route.file info.Locale (indexPath options.Root))
        |> Collection.layout (reader options)
        |> Collection.producer "rocq" (fun context ->
            let loaded = loadCached context.Site context.ProjectRoot options

            for diagnostic in loaded.Found do
                context.Diagnostics.Add diagnostic

            loaded.Pages)
