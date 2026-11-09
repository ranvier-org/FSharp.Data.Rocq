/// <summary>The site's stylesheet: both looks in one file, and the shell, panes and article.</summary>
module Rocq.Navigator.Styles

#if !FABLE_COMPILER

open Ranvier.Css
open Ranvier.Css.Build
open Rocq.Navigator.Model

let private rule (selector: string) (declarations: (string * string) list) =
    Rule.style
        (Selector.unsafeFromText selector)
        (declarations
         |> List.map (fun (name, value) -> Declaration.create name value))

let private tokenDeclarations (scheme: string) (tokens: Tokens) =
    [
        "color-scheme", scheme
        "--bg", tokens.Bg
        "--side", tokens.Side
        "--line", tokens.Line
        "--ink", tokens.Ink
        "--soft", tokens.Soft
        "--dim", tokens.Dim
        "--selected", tokens.Selected
        "--keyword", tokens.Keyword
        "--binder", tokens.Binder
        "--link", tokens.Link
        "--pass", tokens.Pass
        "--warn", tokens.Warn
        "--radius", tokens.Radius
        "--body", tokens.Body
        "--title", tokens.Title
        "--title-size", tokens.TitleSize
        "--title-weight", string tokens.TitleWeight
    ]

let private lookRules (name: string) (look: Look) =
    let light = tokenDeclarations "light" (Tokens.``of`` look Light)
    let dark = tokenDeclarations "dark" (Tokens.``of`` look Dark)

    [
        rule $":root[data-look=\"{name}\"], :root[data-look=\"{name}\"][data-mode=\"light\"]" light
        rule $":root[data-look=\"{name}\"][data-mode=\"dark\"]" dark
        Rule.media "(prefers-color-scheme: dark)" [ rule $":root[data-look=\"{name}\"]:not([data-mode])" dark ]
        Rule.media "(prefers-color-scheme: dark)" [ rule $":root[data-look=\"{name}\"][data-mode=\"system\"]" dark ]
    ]

let private fontFace (family: string) (file: string) (weight: string) (style: string) =
    Rule.descriptors
        "font-face"
        ""
        [
            Declaration.create "font-family" $"\"{family}\""
            Declaration.create "src" $"url(\"fonts/{file}\") format(\"woff2\")"
            Declaration.create "font-weight" weight
            Declaration.create "font-style" style
            Declaration.create "font-display" "swap"
        ]

let private fonts =
    [
        fontFace "Inter" "Inter-wght.woff2" "100 900" "normal"
        fontFace "JetBrains Mono" "JetBrainsMono-Regular.woff2" "400" "normal"
        fontFace "JetBrains Mono" "JetBrainsMono-SemiBold.woff2" "600" "normal"
        fontFace "Source Serif 4" "SourceSerif4-wght.woff2" "200 900" "normal"
        fontFace "Source Serif 4" "SourceSerif4-wght-italic.woff2" "200 900" "italic"
    ]

let private ui = "Inter, \"JetBrains Mono\", system-ui, sans-serif"
let private mono = "\"JetBrains Mono\", Consolas, monospace"

let private shell =
    [
        rule "*, *::before, *::after" [ "box-sizing", "border-box" ]
        rule
            "html"
            [
                "background", "var(--bg)"
                "color", "var(--ink)"
                "font-family", "var(--body)"
                "-webkit-text-size-adjust", "100%"
            ]
        rule "body" [ "margin", "0"; "background", "var(--bg)" ]
        rule "a" [ "color", "var(--link)" ]
        rule "[data-island]" [ "display", "contents" ]
        rule
            ".shell"
            [
                "display", "grid"
                "grid-template-columns", "minmax(11rem, min(14vw, 15rem)) minmax(0, 1fr) minmax(11rem, min(14vw, 15rem))"
                "min-height", "100vh"
            ]
        rule ".center" [ "min-width", "0"; "padding", "0 2.25rem 6rem" ]
        rule
            ".topbar"
            [
                "display", "flex"
                "flex-wrap", "wrap"
                "align-items", "center"
                "gap", "0.75rem"
                "max-width", "60rem"
                "margin", "0 auto"
                "padding", "1.1rem 0 0.25rem"
            ]
        rule ".crumb" [ "flex", "1"; "margin", "0"; "font", $"12px/1.4 {ui}"; "color", "var(--dim)" ]
        rule ".page" [ "max-width", "60rem"; "margin", "0 auto" ]
    ]

let private panes =
    [
        rule
            ".pane"
            [
                "position", "sticky"
                "top", "0"
                "height", "100vh"
                "overflow-y", "auto"
                "padding", "14px 12px 2rem"
                "scrollbar-width", "thin"
                "font-family", ui
            ]
        rule ".pane-left" [ "background", "var(--side)"; "border-right", "1px solid var(--line)" ]
        rule ".pane-right" [ "border-left", "1px solid var(--line)" ]
        rule
            ".brand"
            [
                "display", "flex"
                "flex-direction", "column"
                "gap", "2px"
                "margin", "0 7px 12px"
            ]
        rule
            ".brand .site"
            [
                "font", $"600 14px/1.4 {ui}"
                "color", "var(--ink)"
                "text-decoration", "none"
            ]
        rule ".brand .home" [ "font", $"11.5px/1.4 {ui}"; "color", "var(--dim)"; "text-decoration", "none" ]
        rule
            ".pane-label"
            [
                "font", $"600 10px/1.4 {ui}"
                "letter-spacing", "0.09em"
                "text-transform", "uppercase"
                "color", "var(--dim)"
                "margin", "14px 7px 6px"
            ]
        rule
            ".pane-group"
            [
                "font", $"600 11.5px/1.4 {ui}"
                "color", "var(--soft)"
                "margin", "12px 7px 2px"
            ]
        rule
            ".pane-item"
            [
                "display", "flex"
                "justify-content", "space-between"
                "gap", "0.5rem"
                "font", $"12.5px/1.8 {ui}"
                "color", "var(--dim)"
                "padding", "0 7px"
                "border-radius", "5px"
                "white-space", "nowrap"
                "overflow", "hidden"
                "text-overflow", "ellipsis"
                "text-decoration", "none"
            ]
        rule ".pane-item:hover" [ "color", "var(--ink)"; "background", "var(--selected)" ]
        rule ".pane-item.on" [ "background", "var(--selected)"; "color", "var(--ink)"; "font-weight", "600" ]
        rule
            ".count"
            [
                "font-size", "11px"
                "font-weight", "400"
                "font-variant-numeric", "tabular-nums"
            ]
        rule
            ".outline-heading"
            [
                "display", "block"
                "color", "var(--soft)"
                "font-weight", "600"
                "margin-top", "8px"
            ]
        rule ".outline-heading.depth-3, .outline-heading.depth-4" [ "font-weight", "500"; "margin-top", "4px" ]
        rule ".outline-documented, .outline-quiet" [ "display", "block"; "font", $"11.5px/1.85 {mono}" ]
        rule ".outline-documented" [ "color", "var(--soft)" ]
        rule
            ".drawer"
            [
                "display", "none"
                "margin", "0.5rem 0"
                "padding", "0.25rem 0.75rem"
                "background", "var(--side)"
                "border", "1px solid var(--line)"
                "border-radius", "var(--radius)"
                "font-family", ui
            ]
        rule ".drawer > summary" [ "font", $"600 13px/2 {ui}"; "color", "var(--soft)"; "cursor", "pointer" ]
    ]

let private prose =
    [
        rule
            ".page-title"
            [
                "font-family", "var(--title)"
                "font-weight", "var(--title-weight)"
                "font-size", "1.65rem"
                "line-height", "1.25"
                "margin", "0.25rem 0 1.25rem"
                "overflow-wrap", "anywhere"
            ]
        rule
            ".page h1:not(.page-title), .page h2, .page h3, .page h4"
            [
                "font-family", "var(--title)"
                "font-weight", "var(--title-weight)"
                "line-height", "1.3"
                "color", "var(--ink)"
                "scroll-margin-top", Islands.landingMargin
            ]
        rule ".page h1:not(.page-title)" [ "font-size", "1.4rem"; "margin", "2.5rem 0 0.75rem" ]
        rule
            ".page h2"
            [
                "font-size", "1.2rem"
                "margin", "2.75rem 0 0.75rem"
                "padding-bottom", "0.35rem"
                "border-bottom", "1px solid var(--line)"
            ]
        rule ".page h3" [ "font-size", "1.05rem"; "margin", "2rem 0 0.5rem" ]
        rule ".page h4" [ "font-size", "1rem"; "margin", "1.25rem 0 0.25rem" ]
        rule
            ".doc, .prose, .entry"
            [
                "max-width", "46rem"
                "font-size", "0.92rem"
                "line-height", "1.6"
                "color", "var(--soft)"
                "overflow-wrap", "anywhere"
            ]
        rule ".doc p, .prose p, .entry p" [ "margin", "0 0 0.75rem" ]
        rule ".doc ul, .doc ol, .prose ul, .prose ol, .entry ul, .entry ol" [ "margin", "0 0 0.75rem"; "padding-left", "1.4rem" ]
        rule ".doc code, .prose code, .entry p code, .entry li code, .decl-title code" [ "font", $"0.9em {mono}"; "color", "var(--link)" ]
        rule ".doc a code, .prose a code, .entry a code" [ "border-bottom", "1px dotted var(--link)" ]
        rule ".doc a.ref, .prose a.ref, .entry a.ref" [ "text-decoration", "none" ]
        rule
            ".decl-title"
            [
                "font-family", "var(--title)"
                "font-size", "var(--title-size)"
                "font-weight", "var(--title-weight)"
                "line-height", "1.3"
                "color", "var(--ink)"
                "margin", "0 0 0.4rem !important"
            ]
        rule ".prose" [ "margin", "1rem 0" ]
    ]

let private code =
    [
        rule
            ".page pre"
            [
                "font", $"12.5px/1.65 {mono}"
                "background", "var(--side)"
                "border", "1px solid var(--line)"
                "border-radius", "var(--radius)"
                "padding", "10px 12px"
                "margin", "0 0 8px"
                "white-space", "pre-wrap"
                "overflow-wrap", "anywhere"
                "color", "var(--ink)"
            ]
        rule "pre code" [ "font", "inherit"; "color", "inherit" ]
        rule
            ".l"
            [
                "display", "block"
                "--hang", "min(calc((var(--i, 0) + 4) * 1ch), 30vw)"
                "padding-left", "var(--hang)"
                "text-indent", "calc(-1 * var(--hang))"
            ]
        rule ".kw" [ "color", "var(--keyword)"; "font-weight", "600" ]
        rule ".comment" [ "color", "var(--dim)" ]
        rule "[data-ref^=\"b\"]" [ "color", "var(--binder)" ]
        rule
            "pre a[data-ref]"
            [
                "color", "var(--link)"
                "text-decoration", "none"
                "border-bottom", "1px dotted var(--link)"
            ]
        rule "pre a[data-ref]:hover" [ "border-bottom-style", "solid" ]
        rule ".decl" [ "margin", "1.9rem 0"; "scroll-margin-top", Islands.landingMargin ]
        rule ".decl.quiet" [ "margin", "0.2rem 0" ]
        rule
            ".page pre.command"
            [
                "background", "transparent"
                "border", "0"
                "padding", "3px 0"
                "margin", "0"
                "font-size", "12px"
                "color", "var(--soft)"
            ]
        rule
            ".panel"
            [
                "background", "var(--side)"
                "border", "1px solid var(--line)"
                "border-radius", "var(--radius)"
                "margin", "0 0 8px"
            ]
        rule
            ".page .panel pre"
            [
                "background", "transparent"
                "border", "0"
                "border-radius", "0"
                "margin", "0"
            ]
        rule ".decl:target > .panel" [ "border-color", "var(--link)" ]
    ]

let private folds =
    [
        rule
            "details.proof > summary, details.refs > summary, details.more > summary, details.imports > summary"
            [
                "font", $"12px/1.8 {ui}"
                "color", "var(--dim)"
                "cursor", "pointer"
                "list-style", "none"
                "user-select", "none"
            ]
        rule
            "details.proof > summary::-webkit-details-marker, details.refs > summary::-webkit-details-marker, details.body > summary::-webkit-details-marker, details.imports > summary::-webkit-details-marker"
            [ "display", "none" ]
        rule "details.proof > summary::before" [ "content", "\"▸ Proof · \"" ]
        rule "details.proof[open]:not([data-closing]) > summary::before" [ "content", "\"▾ Proof · \"" ]
        rule "details.refs > summary::before" [ "content", "\"▸ \"" ]
        rule "details.refs[open]:not([data-closing]) > summary::before" [ "content", "\"▾ \"" ]
        rule "details.proof.warning > summary" [ "color", "var(--warn)" ]
        rule ".panel > details.proof" [ "border-top", "1px solid var(--line)" ]
        rule ".panel > details > summary" [ "padding", "2px 12px" ]
        rule ".panel > details > summary:hover" [ "color", "var(--ink)" ]
        rule "details.imports" [ "margin", "0.5rem 0 1rem" ]
        rule "details.imports > summary::before" [ "content", "\"▸ Imports · \"" ]
        rule "details.imports[open]:not([data-closing]) > summary::before" [ "content", "\"▾ Imports · \"" ]
        rule "details.body > summary" [ "display", "flex"; "padding", "0"; "cursor", "pointer"; "list-style", "none" ]
        rule "details.body" [ "margin-bottom", "10px" ]
        rule "details.body > summary > pre" [ "flex", "1"; "min-width", "0" ]
        rule ".page .panel details.body > summary > pre" [ "padding-bottom", "0" ]
        rule
            "details.body > summary::before"
            [
                "content", "\"▸\""
                "order", "1"
                "flex", "none"
                "padding", "10px 12px 0 0"
                "font", $"12px/20px {ui}"
                "color", "var(--dim)"
            ]
        rule "details.body[open]:not([data-closing]) > summary::before" [ "content", "\"▾\"" ]
        rule "details.body > summary:hover::before" [ "color", "var(--ink)" ]
        rule
            "details.body > summary::after"
            [
                "content", "attr(data-lines)"
                "flex", "none"
                "padding", "10px 6px 0 0"
                "font", $"11px/20px {ui}"
                "color", "var(--dim)"
            ]
        rule "details.body[open]:not([data-closing]) > summary:not(:hover)::after" [ "visibility", "hidden" ]
        rule ".page .panel details.body > pre" [ "padding-top", "0"; "padding-bottom", "0" ]
        rule ".warning-text" [ "font-weight", "600"; "margin-left", "0.35rem" ]
        rule ".decl.quiet details.proof, .decl.quiet details.refs" [ "margin-left", "0" ]
        rule ".ref-list" [ "margin", "0.25rem 0 0.5rem" ]
        rule
            ".ref-list > p"
            [
                "font", $"600 10px/1.8 {ui}"
                "letter-spacing", "0.09em"
                "text-transform", "uppercase"
                "color", "var(--dim)"
                "margin", "0"
            ]
        rule
            ".ref-list ul"
            [
                "display", "flex"
                "flex-wrap", "wrap"
                "gap", "0 0.9rem"
                "list-style", "none"
                "margin", "0"
                "padding", "0"
                "font", $"11.5px/1.7 {mono}"
                "color", "var(--soft)"
            ]
        rule ".ref-list a" [ "text-decoration", "none" ]
    ]

let private badges =
    [
        rule
            ".badge"
            [
                "display", "inline-block"
                "font", $"11px/1.6 {ui}"
                "border", "1px solid"
                "border-radius", "10px"
                "padding", "0 9px"
                "margin", "0 0 8px"
            ]
        rule
            ".badge.pass"
            [
                "color", "var(--pass)"
                "border-color", "color-mix(in srgb, var(--pass) 40%, transparent)"
                "background", "color-mix(in srgb, var(--pass) 10%, transparent)"
            ]
        rule
            ".badge.axioms"
            [
                "color", "var(--warn)"
                "border-color", "color-mix(in srgb, var(--warn) 40%, transparent)"
                "background", "color-mix(in srgb, var(--warn) 10%, transparent)"
            ]
    ]

let private guarantees =
    [
        rule ".group-open > h2, .group-falsified > h2" [ "color", "var(--warn)" ]
        rule
            ".group > h3"
            [
                "font", $"600 12px/1.4 {ui}"
                "color", "var(--dim)"
                "margin", "1.75rem 0 0.6rem"
            ]
        rule
            ".entry"
            [
                "border", "1px solid var(--line)"
                "border-radius", "var(--radius)"
                "padding", "0.8rem 1rem 0.3rem"
                "margin", "0 0 0.6rem"
            ]
        rule ".entry .badge" [ "margin", "0.1rem 0 0.5rem" ]
        rule ".entry h4" [ "margin", "0 0 0.3rem !important"; "font-size", "1.02rem !important" ]
        rule ".entry h4 a" [ "color", "var(--ink)"; "text-decoration", "none" ]
        rule ".entry h4 a:hover" [ "color", "var(--link)" ]
    ]

let private islands =
    [
        rule
            ".switch"
            [
                "display", "flex"
                "flex-wrap", "wrap"
                "gap", "0.5rem"
                "font", $"11.5px/1.5 {ui}"
            ]
        rule
            ".switch-group"
            [
                "display", "inline-flex"
                "border", "1px solid var(--line)"
                "border-radius", "var(--radius)"
                "overflow", "hidden"
            ]
        rule
            ".switch button"
            [
                "appearance", "none"
                "border", "0"
                "background", "transparent"
                "color", "var(--dim)"
                "font", "inherit"
                "padding", "2px 8px"
                "min-height", "24px"
                "cursor", "pointer"
            ]
        rule ".switch button:hover" [ "color", "var(--ink)" ]
        rule ".switch button[aria-pressed=\"true\"]" [ "background", "var(--selected)"; "color", "var(--ink)"; "font-weight", "600" ]
        rule ":root:not([data-client=\"ready\"]) .switch, :root:not([data-client=\"ready\"]) .search-trigger" [ "visibility", "hidden" ]
        rule ".pane-item.in-view" [ "color", "var(--ink)"; "background", "var(--selected)" ]
        rule ".xref:not(:empty)" [ "margin-top", "1rem" ]
        rule ":root[data-client=\"ready\"] .page::after" [ "content", "\"\""; "display", "block"; "height", "calc(100vh - 9rem)" ]
        Rule.media "(min-width: 901px)" [ rule ":root[data-lists=\"pane\"] .decl details.refs" [ "display", "none" ] ]
        rule
            ".card"
            [
                "position", "absolute"
                "z-index", "40"
                "box-sizing", "border-box"
                "width", "min(22rem, calc(100vw - 12px))"
                "max-height", "min(60vh, 24rem)"
                "overflow", "auto"
                "padding", "10px 12px"
                "background", "var(--bg)"
                "color", "var(--ink)"
                "border", "1px solid var(--line)"
                "border-radius", "var(--radius)"
                "box-shadow", "0 12px 30px #0005"
                "font", "13px/1.5 var(--body)"
                "overflow-wrap", "anywhere"
            ]
        rule ".card [hidden]" [ "display", "none" ]
        rule ".card p" [ "margin", "0" ]
        rule
            ".card-head"
            [
                "display", "flex"
                "flex-wrap", "wrap"
                "gap", "0.25rem 0.5rem"
                "align-items", "baseline"
            ]
        rule ".card-name" [ "font-weight", "600"; "color", "var(--ink)" ]
        rule ".card-name[href]" [ "color", "var(--link)" ]
        rule ".card .badge" [ "margin", "0" ]
        rule
            ".card-signature"
            [
                "margin", "0.375rem 0 0"
                "padding", "0"
                "font", $"11.5px/1.5 {mono}"
                "white-space", "pre-wrap"
                "color", "var(--binder)"
            ]
        rule ".card-doc" [ "margin-top", "0.375rem"; "color", "var(--soft)" ]
        rule ".card-doc > :first-child" [ "margin-top", "0" ]
        rule ".card-doc > :last-child" [ "margin-bottom", "0" ]
        rule
            ".card-quiet"
            [
                "margin-top", "0.375rem !important"
                "color", "var(--dim)"
                "font-style", "italic"
            ]
        rule
            ".card-meta"
            [
                "margin-top", "0.375rem !important"
                "color", "var(--dim)"
                "font", $"11px/1.5 {ui}"
            ]
    ]

let private search =
    [
        rule
            ".search-trigger"
            [
                "appearance", "none"
                "display", "inline-flex"
                "gap", "0.6rem"
                "align-items", "center"
                "border", "1px solid var(--line)"
                "border-radius", "var(--radius)"
                "background", "transparent"
                "color", "var(--dim)"
                "font", $"11.5px/1.5 {ui}"
                "padding", "2px 8px"
                "cursor", "pointer"
            ]
        rule ".search-trigger:hover" [ "color", "var(--ink)" ]
        rule ".search-hint" [ "font", "inherit"; "opacity", "0.8" ]
        rule
            ".search-dialog"
            [
                "width", "min(40rem, calc(100vw - 2rem))"
                "max-height", "min(32rem, calc(100vh - 6rem))"
                "margin", "4rem auto auto"
                "padding", "0"
                "border", "1px solid var(--line)"
                "border-radius", "var(--radius)"
                "background", "var(--bg)"
                "color", "var(--ink)"
                "overflow", "hidden"
            ]
        rule ".search-dialog[open]" [ "display", "flex"; "flex-direction", "column" ]
        rule ".search-dialog::backdrop" [ "background", "rgba(0, 0, 0, 0.4)" ]
        rule
            ".search-input"
            [
                "appearance", "none"
                "border", "0"
                "border-bottom", "1px solid var(--line)"
                "background", "transparent"
                "color", "var(--ink)"
                "font", $"15px/1.5 {ui}"
                "padding", "0.8rem 1rem"
                "outline", "none"
            ]
        rule ".search-input:focus-visible" [ "box-shadow", "inset 0 -2px 0 var(--link)" ]
        rule
            ".search-message"
            [
                "margin", "0"
                "padding", "0.5rem 1rem"
                "color", "var(--dim)"
                "font", $"12px/1.5 {ui}"
            ]
        rule ".search-message:empty" [ "display", "none" ]
        rule ".search-list" [ "list-style", "none"; "margin", "0"; "padding", "0"; "overflow", "auto" ]
        rule ".search-list li" [ "margin", "0"; "padding", "0" ]
        rule
            ".search-list .hit"
            [
                "display", "grid"
                "grid-template-columns", "1fr auto"
                "gap", "0 1rem"
                "padding", "0.45rem 1rem"
                "color", "var(--ink)"
                "text-decoration", "none"
            ]
        rule ".search-list li[aria-selected=\"true\"] .hit" [ "background", "var(--selected)" ]
        rule ".hit-name" [ "font-weight", "600"; "overflow-wrap", "anywhere" ]
        rule ".hit-kind, .hit-module" [ "color", "var(--dim)"; "font", $"11.5px/1.6 {ui}" ]
        rule ".hit-kind" [ "grid-column", "2"; "grid-row", "1"; "text-align", "right" ]
        rule ".hit-module" [ "grid-column", "1" ]
        rule ".hit-summary" [ "grid-column", "1 / -1"; "color", "var(--soft)"; "font", $"12px/1.5 {ui}" ]
        rule ".hit-summary:empty" [ "display", "none" ]
    ]

let private motion =
    [
        Rule.keyframes
            "rocq-settle"
            [
                "from",
                [
                    Declaration.create "box-shadow" "0 0 0 3px color-mix(in srgb, var(--link) 35%, transparent)"
                ]
                "to", [ Declaration.create "box-shadow" "0 0 0 3px transparent" ]
            ]
        Rule.keyframes
            "rocq-card-in"
            [
                "from", [ Declaration.create "opacity" "0"; Declaration.create "translate" "0 3px" ]
                "to", [ Declaration.create "opacity" "1"; Declaration.create "translate" "0 0" ]
            ]
        rule ".decl:target > .panel" [ "animation", "rocq-settle 700ms ease-out" ]
        rule ".card:not([hidden])" [ "animation", "rocq-card-in 120ms ease-out" ]
        Rule.media
            "(prefers-reduced-motion: reduce)"
            [
                rule "*, *::before, *::after" [ "animation", "none !important"; "transition", "none !important" ]
            ]
    ]

let private narrow =
    [
        Rule.media
            "(max-width: 900px)"
            [
                rule ".shell" [ "display", "block" ]
                rule ".pane" [ "display", "none" ]
                rule
                    ".drawer"
                    [
                        "display", "block"
                        "max-width", "60rem"
                        "margin-left", "auto"
                        "margin-right", "auto"
                    ]
                rule ".drawer .brand" [ "margin", "0.25rem 7px 0.5rem" ]
                rule ".center" [ "padding", "0 1rem 3rem" ]
                rule ".pane-item" [ "line-height", "2.1" ]
            ]
    ]

/// <summary>The stylesheet's modules, for <c>Compiler.compile</c> with <c>SingleFile</c>.</summary>
let modules: CssModule list =
    [
        {
            Name = "navigator"
            Dependencies = []
            Styles = []
            Rules =
                fonts
                @ lookRules "family" Family
                @ lookRules "journal" Journal
                @ shell
                @ panes
                @ prose
                @ code
                @ folds
                @ badges
                @ guarantees
                @ islands
                @ search
                @ motion
                @ narrow
        }
    ]

/// <summary>The compiled stylesheet's file name and text.</summary>
let compiled () : string * string =
    let compilation =
        Compiler.compile
            { Options.defaults with
                Mode = OutputMode.SingleFile "navigator.css"
                ContentHash = false
            }
            modules

    match compilation with
    | Ok compilation ->
        let asset = List.exactlyOne compilation.Assets
        asset.FileName, asset.Content
    | Error problems -> failwith $"The stylesheet did not compile: %A{problems}"

#endif
