module Docs.Site

open System
open System.IO
open Feliz.ViewEngine
open Nacara.Core
open Nacara.Plugins
open Partas.Nacara.Theme

let private baseUrl =
    match Environment.GetEnvironmentVariable "DOCS_BASE_URL" with
    | null
    | "" -> "/"
    | value -> value

let private rocqOptions =
    Rocq.defaults
    |> Rocq.project "example/_CoqProject"
    |> Rocq.facts (Some "example/facts.json")
    |> Rocq.config (Some "example/navigator.json")
    |> Rocq.root "example"
    |> Rocq.home (Some ("Documentation", baseUrl))

let private apiOptions =
    { FSharpApi.defaults with
        Root = "reference"
        Title = "API reference"
        Sources =
            [
                for name in [ "FSharp.Data.Rocq"; "Nacara.Plugin.Rocq" ] ->
                    FSharpApiSource.create (Path.Combine (AppContext.BaseDirectory, $"%s{name}.dll"))
            ]
    }

let private styles =
    """
.prose-rendered { margin-block: var(--nacara-space-4); padding: var(--nacara-space-4); border: 1px solid var(--nacara-border); border-radius: var(--nacara-radius); }
.prose-rendered > :first-child { margin-top: 0; }
.prose-rendered > :last-child { margin-bottom: 0; }
.prose-arguments dt { font-weight: 600; }
.prose-arguments dd { margin: 0 0 var(--nacara-space-2) var(--nacara-space-4); }
.rocq-frame { display: block; width: 100%; height: 34rem; margin-block: var(--nacara-space-4); border: 1px solid var(--nacara-border); border-radius: var(--nacara-radius); }
.rocq-hero { padding-block: var(--nacara-space-8); text-align: center; }
.rocq-hero__title { margin: 0; font-size: clamp(2.25rem, 7vw, 3.5rem); overflow-wrap: anywhere; }
.rocq-hero__line { max-width: 40rem; margin: var(--nacara-space-4) auto 0; font-size: var(--nacara-font-size-lg); color: var(--nacara-text-muted); }
.rocq-hero__actions { display: flex; flex-wrap: wrap; justify-content: center; gap: var(--nacara-space-2); margin-top: var(--nacara-space-8); }
.rocq-button { display: inline-flex; align-items: center; min-height: 2.75rem; padding-inline: var(--nacara-space-4); border: 1px solid var(--nacara-border); border-radius: var(--nacara-radius); background: var(--nacara-bg-raised); color: var(--nacara-text); font-weight: 600; text-decoration: none; }
.rocq-button:hover { border-color: var(--nacara-primary); color: var(--nacara-text); text-decoration: none; }
.rocq-button--primary, .rocq-button--primary:hover { border-color: var(--nacara-primary); background: var(--nacara-primary); color: var(--nacara-primary-contrast); }
.rocq-button--primary:hover { filter: brightness(1.1); }
.rocq-packages { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 20rem), 1fr)); gap: var(--nacara-space-4); margin-block: var(--nacara-space-8); }
.rocq-package { display: block; padding: var(--nacara-space-4); border: 1px solid var(--nacara-border); border-radius: var(--nacara-radius); background: var(--nacara-bg-subtle); color: var(--nacara-text); text-decoration: none; }
.rocq-package:hover { border-color: var(--nacara-primary); color: var(--nacara-text); text-decoration: none; }
.rocq-package__name { font-size: var(--nacara-font-size-lg); font-weight: 600; background: none; border: 0; padding: 0; }
.rocq-package p { margin: var(--nacara-space-2) 0 0; }
"""

let private theme =
    Theme.defaults
    |> Theme.navbar
        [
            NavbarSection ("Guide", "guide", "/guide/")
            NavbarLink ("Example site", "/example/index.html")
            NavbarSection ("API reference", "reference", "/reference/")
        ]
    |> Theme.navbarEnd
        [
            NavbarIcon ("GitHub", "https://github.com/ranvier-org/FSharp.Data.Rocq", Icons.github)
        ]
    |> Theme.menu
        "guide"
        [
            Menu.page "guide/index.md"
            Menu.section
                "Library"
                [
                    Menu.page "guide/library.md"
                    Menu.page "guide/references.md"
                    Menu.page "guide/json.md"
                    Menu.page "guide/schema.md"
                ]
            Menu.section
                "Navigator"
                [
                    Menu.page "guide/navigator.md"
                    Menu.page "guide/prose.md"
                    Menu.page "guide/guarantees.md"
                    Menu.page "guide/output.md"
                    Menu.page "guide/settings.md"
                ]
            Menu.section "Example" [ Menu.page "guide/example.md" ]
        ]
    |> Theme.css styles
    |> Theme.footer (Html.p [ Html.text "FSharp.Data.Rocq · Built with Nacara" ])

let private site () =
    let reference =
        FSharpApi.collection "reference" DocFrontMatter.decoder apiOptions
        |> Collection.title _.Title
        |> Collection.layout (Theme.layout theme)

    Site.create "FSharp.Data.Rocq"
    |> Site.baseUrl baseUrl
    |> Site.output "output"
    |> Site.noStaticFiles
    |> Markdown.register
    |> TreeSitter.register
    |> FSharpApi.register apiOptions
    |> Rocq.register rocqOptions
    |> LinkValidator.register
    |> Theme.register theme
    |> Site.collection (Theme.docs theme "woven")
    |> Site.collection reference
    |> Site.collection (
        Rocq.collection "example" rocqOptions
        |> Collection.layout (Rocq.reader rocqOptions)
    )

[<EntryPoint>]
let main argv =
    Directory.SetCurrentDirectory __SOURCE_DIRECTORY__

    match List.ofArray argv with
    | [ "minimal-site" ] -> Examples.buildSite $"{baseUrl}example/" [| "build" |]
    | "rocq-capture" :: _ -> Nacara.run (site ()) argv
    | _ ->
        Weave.run baseUrl
        Nacara.run (site ()) argv
