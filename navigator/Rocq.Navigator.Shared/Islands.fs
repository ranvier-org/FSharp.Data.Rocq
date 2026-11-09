module Rocq.Navigator.Islands

open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar
open Rocq.Navigator.Model

#if FABLE_COMPILER
open Fable.Core
open Ranvier.Dom
#endif

/// The attribute that marks an element as the host of a hydrated island. Its value names the island.
[<Literal>]
let hostAttribute = "data-island"

/// The id prefix an island renders and hydrates with, distinct per island on a page.
let idPrefix (name: string) =
    "r" + name

/// The look of a page with nothing stored, and of the server's HTML.
[<Literal>]
let defaultLook = "family"

/// The mode of a page with nothing stored, and of the server's HTML.
[<Literal>]
let defaultMode = "system"

/// The `localStorage` key of the saved look.
[<Literal>]
let lookKey = "rocq-navigator-look"

/// The `localStorage` key of the saved mode.
[<Literal>]
let modeKey = "rocq-navigator-mode"

/// The values `data-look` accepts.
let looks = [ defaultLook; "journal" ]

/// The values `data-mode` accepts.
let modes = [ defaultMode; "light"; "dark" ]

/// The distance from the top of the viewport, in pixels, of the line that decides the section in view.
[<Literal>]
let private sectionLine = 48.0

/// The share of the viewport's height, from its top, that holds candidate sections.
[<Literal>]
let private bandShare = 0.4

/// The band between the line and its end, as an `IntersectionObserver` margin. A section whose top crosses the line
/// changes its ratio in the band from 1, and a section that leaves past the line changes it from above 0.
[<Literal>]
let private sectionBand = "-48px 0px -60% 0px"

#if FABLE_COMPILER
[<Global "document">]
let private document: Document = jsNative

[<Emit("localStorage.setItem($0, $1)")>]
let private setStored (key: string) (value: string) : unit = jsNative

[<Emit("Array.from(document.querySelectorAll($0))")>]
let private all (selector: string) : Element array = jsNative

[<Emit("$0.replaceChildren(...$1)")>]
let private replaceChildren (parent: Element) (children: Node array) : unit = jsNative

let private rootAttribute (name: string) (fallback: string) =
    match document.documentElement.getAttribute name with
    | null -> fallback
    | value -> value

let private save (key: string) (value: string) =
    try
        setStored key value
    with _ ->
        ()

[<AllowNullLiteral>]
type private SectionObserver =
    abstract observe: Element -> unit
    abstract disconnect: unit -> unit

[<Emit("new IntersectionObserver(() => $0(), { rootMargin: $1, threshold: [0, 1] })")>]
let private sectionObserver (callback: unit -> unit) (rootMargin: string) : SectionObserver = jsNative

/// The section in view: the last to start above a line near the top of the viewport or, at the top of the page, the first
/// in the band. Space after the article lets the last section reach the line.
let private sectionInView (sections: Element array) : Element option =
    let top (section: Element) =
        section.getBoundingClientRect().top

    let bandEnd =
        float document.documentElement.clientHeight
        * bandShare

    let inBand =
        sections
        |> Array.filter (fun section -> top section < bandEnd)

    let aboveLine =
        inBand
        |> Array.filter (fun section -> top section <= sectionLine)

    if inBand.Length = 0 then
        None
    elif aboveLine.Length > 0 then
        Some (aboveLine |> Array.maxBy top)
    else
        Some (inBand |> Array.minBy top)

/// Marks the outline links of a section in the pane and the drawer. A section with no outline link leaves the mark.
let private markOutline (section: string) =
    let links = all """.pane-item[href^="#"]"""
    let href = "#" + section

    if
        links
        |> Array.exists (fun link -> link.getAttribute "href" = href)
    then
        for link in links do
            if link.getAttribute "href" = href then
                link.classList.add "in-view"
                link.setAttribute ("aria-current", "location")
            else
                link.classList.remove "in-view"
                link.removeAttribute "aria-current"

/// Copies a declaration's lists into the right pane's slots. The drawer's slots stay empty: below 900 px the lists stay inline.
let private showLists (section: string) =
    match document.getElementById section with
    | null -> ()
    | target when target.matches ".decl" ->
        for slot in all ".pane-right [data-slot]" do
            let name = slot.getAttribute "data-slot"

            match target.querySelector $""".ref-list[data-list="%s{name}"]""" with
            | null -> replaceChildren slot [||]
            | list -> replaceChildren slot [| list.cloneNode true |]
    | _ -> ()
#endif

/// The state of the look and mode switch.
type SwitchInstance =
    {
        Look: Signal<string>
        Mode: Signal<string>
    }

let private createSwitch (_: Setup) () =
#if FABLE_COMPILER
    let look = Api.createSignal (rootAttribute "data-look" defaultLook)
    let mode = Api.createSignal (rootAttribute "data-mode" defaultMode)
    Api.createEffect (fun () -> document.documentElement.setAttribute ("data-look", look.Value))
    Api.createEffect (fun () -> document.documentElement.setAttribute ("data-mode", mode.Value))
#else
    let look = Api.createSignal defaultLook
    let mode = Api.createSignal defaultMode
#endif
    { Look = look; Mode = mode }

let private choice (select: SwitchInstance -> Signal<string>) (key: string) (value: string) (label: string) =
    button (
        Attrs.attribute ("type", "button"),
        Attrs.aria ("pressed", (fun (instance: SwitchInstance) -> Some (if (select instance).Value = value then "true" else "false"))),
        Events.onClick (fun (instance: SwitchInstance) _ ->
#if FABLE_COMPILER
            save key value
#endif
            (select instance).Value <- value)
    ) {
        label
    }

/// The switch between the two looks and the three modes. Its choice applies to the root element and persists in `localStorage`.
let lookSwitch: Component<unit> =
    Component.template
        createSwitch
        (div (Attrs.className "switch") {
            span (Attrs.className "switch-group", Attrs.aria ("label", "Look")) {
                choice _.Look lookKey "family" "Family"
                choice _.Look lookKey "journal" "Journal"
            }

            span (Attrs.className "switch-group", Attrs.aria ("label", "Mode")) {
                choice _.Mode modeKey "system" "Auto"
                choice _.Mode modeKey "light" "Light"
                choice _.Mode modeKey "dark" "Dark"
            }
        })

let private createMarker (_: Setup) () =
#if FABLE_COMPILER
    let section = Api.createSignal ""

    let sections =
        Array.append
            (all ".decl[id]")
            (all """.pane-right .pane-item[href^="#"]"""
             |> Array.choose (fun link ->
                 document.getElementById ((link.getAttribute "href").Substring 1)
                 |> Option.ofObj))

    let observer =
        sectionObserver
            (fun () ->
                match sectionInView sections with
                | Some current -> section.Value <- current.id
                | None -> ())
            sectionBand

    for target in sections do
        observer.observe target

    Api.onCleanup observer.disconnect

    if (all ".pane-right [data-slot]").Length > 0 then
        document.documentElement.setAttribute ("data-lists", "pane")

    Api.createEffect (fun () ->
        if section.Value <> "" then
            markOutline section.Value
            showLists section.Value)
#endif
    ()

/// Marks the section in view in the outline, and shows the current declaration's lists in the right pane.
let sectionMarker: Component<unit> =
    Component.define createMarker (fun t -> t.fragment [||])

#if FABLE_COMPILER
[<Emit("$0.target.value")>]
let private inputValue (event: obj) : string = jsNative

[<Emit("$0.key")>]
let private keyOf (event: obj) : string = jsNative

[<Emit("$0.preventDefault()")>]
let private preventDefault (event: obj) : unit = jsNative

[<Emit("($0.ctrlKey || $0.metaKey)")>]
let private commandKey (event: obj) : bool = jsNative

[<Emit("($0.altKey || $0.ctrlKey || $0.metaKey)")>]
let private modified (event: obj) : bool = jsNative

[<Emit("(() => { const t = $0.target; return t instanceof Element && t.closest('input, textarea, select, [contenteditable]') !== null })()")>]
let private typedInField (event: obj) : bool = jsNative

[<Emit("($0.target === $0.currentTarget || ($0.target instanceof Element && $0.target.closest('a.hit') !== null))")>]
let private dismissesDialog (event: obj) : bool = jsNative

[<Emit("document.activeElement")>]
let private activeElement () : obj = jsNative

[<Emit("(($0 != null) && $0.isConnected && $0.focus())")>]
let private refocus (element: obj) : unit = jsNative

[<Emit("$0.open")>]
let private dialogIsOpen (dialog: obj) : bool = jsNative

[<Emit("$0.showModal()")>]
let private showModal (dialog: obj) : unit = jsNative

[<Emit("$0.close()")>]
let private closeDialog (dialog: obj) : unit = jsNative

[<Emit("(() => { const i = $0.querySelector('input'); i.focus(); i.select() })()")>]
let private focusInput (dialog: obj) : unit = jsNative

[<Emit("setTimeout(() => { const e = document.getElementById($0); if (e) e.scrollIntoView({ block: 'nearest' }) }, 0)")>]
let private revealRow (id: string) : unit = jsNative

[<Emit("window.location.assign($0)")>]
let private navigate (href: string) : unit = jsNative

[<Emit("document.body.getAttribute('data-search')")>]
let private searchUrl () : string = jsNative

[<Emit("fetch($0).then(r => { if (!r.ok) throw new Error(String(r.status)); return r.text() }).then($1, e => $2(String(e)))")>]
let private fetchText (url: string) (onBody: string -> unit) (onFailure: string -> unit) : unit = jsNative

[<Emit("(() => { const h = (e) => $0(e); document.addEventListener('keydown', h); return () => document.removeEventListener('keydown', h) })()")>]
let private listenKeys (handler: obj -> unit) : (unit -> unit) = jsNative
#endif

/// One line of the search results.
type SearchRow =
    {
        Entry: SearchEntry
        Index: int
        Active: bool
    }

/// The state and handlers of the search dialog.
type SearchInstance =
    {
        Text: Signal<string>
        IsOpen: Signal<bool>
        Status: Signal<string>
        Selected: Signal<int>
        Results: Memo<SearchEntry[]>
        Open: unit -> unit
        Attach: obj -> (unit -> unit)
        OnInput: obj -> unit
        OnKey: obj -> unit
        OnDialogClick: obj -> unit
        OnClosed: unit -> unit
    }

let private optionId (index: int) =
    "search-option-" + string index

[<Literal>]
let private statusLoading = "loading"

[<Literal>]
let private statusFailed = "failed"

[<Literal>]
let private statusReady = "ready"

let private searchRows (instance: SearchInstance) =
    let selected = instance.Selected.Value

    instance.Results.Value
    |> Array.mapi (fun index entry ->
        {
            Entry = entry
            Index = index
            Active = index = selected
        })

let private searchMessage (instance: SearchInstance) =
    match instance.Status.Value with
    | status when status = statusLoading -> "Loading the index"
    | status when status = statusFailed -> "The search index could not be loaded. Reload the page to try again."
    | _ when instance.Text.Value.Trim() <> "" && instance.Results.Value.Length = 0 -> "No matches"
    | _ -> ""

let private createSearch (_: Setup) () : SearchInstance =
    let text = Api.createSignal ""
    let isOpen = Api.createSignal false
    let status = Api.createSignal "idle"
    let selected = Api.createSignal 0
    let entries = Api.createSignal ([||]: SearchEntry[])

    let results =
        Api.createMemo (fun _ ->
            Ranking.rank entries.Value text.Value
            |> Array.truncate Ranking.maxRows)
#if FABLE_COMPILER
    let dialog = ref null
    let opener = ref null
    let wasOpen = ref false

    let load () =
        status.Value <- statusLoading

        let fail (_: string) =
            status.Value <- statusFailed

        match searchUrl () with
        | null -> fail "no index"
        | url ->
            fetchText
                url
                (fun body ->
                    match searchWire.Decode body with
                    | Ok decoded ->
                        entries.Value <- decoded
                        status.Value <- statusReady
                    | Result.Error _ -> fail "decode")
                fail

    let openDialog () =
        if not isOpen.Value then
            opener.Value <- activeElement ()
            isOpen.Value <- true

            if status.Value <> statusReady && status.Value <> statusLoading then
                load ()

    Api.createEffect (fun () ->
        let wanted = isOpen.Value

        if not (isNull dialog.Value) then
            if wanted && not (dialogIsOpen dialog.Value) then
                showModal dialog.Value
                focusInput dialog.Value
                wasOpen.Value <- true
            elif not wanted && dialogIsOpen dialog.Value then
                closeDialog dialog.Value

            if not wanted && wasOpen.Value then
                wasOpen.Value <- false
                refocus opener.Value)

    Api.createEffect (fun () ->
        if isOpen.Value && results.Value.Length > 0 then
            revealRow (optionId (min selected.Value (results.Value.Length - 1))))

    let removeKeys =
        listenKeys (fun event ->
            let key = keyOf event

            if (commandKey event && key.ToLowerInvariant() = "k")
               || (key = "/" && not (modified event) && not (typedInField event)) then
                preventDefault event
                openDialog ())

    Api.onCleanup removeKeys

    let onKey (event: obj) =
        let count = results.Value.Length

        match keyOf event with
        | "ArrowDown" ->
            preventDefault event
            selected.Value <- min (max 0 (count - 1)) (selected.Value + 1)
        | "ArrowUp" ->
            preventDefault event
            selected.Value <- max 0 (selected.Value - 1)
        | "Enter" when count > 0 ->
            preventDefault event
            let target = results.Value[min selected.Value (count - 1)]
            isOpen.Value <- false
            navigate target.Href
        | _ -> ()

    {
        Text = text
        IsOpen = isOpen
        Status = status
        Selected = selected
        Results = results
        Open = openDialog
        Attach =
            fun element ->
                dialog.Value <- element
                fun () -> dialog.Value <- null
        OnInput =
            fun event ->
                text.Value <- inputValue event
                selected.Value <- 0
        OnKey = onKey
        OnDialogClick = fun event -> if dismissesDialog event then isOpen.Value <- false
        OnClosed = fun () -> isOpen.Value <- false
    }
#else
    {
        Text = text
        IsOpen = isOpen
        Status = status
        Selected = selected
        Results = results
        Open = ignore
        Attach = fun _ -> ignore
        OnInput = ignore
        OnKey = ignore
        OnDialogClick = ignore
        OnClosed = ignore
    }
#endif

let private searchRow: TemplateView<unit -> SearchRow> =
    li (
        Attrs.role "option",
        Attrs.id (fun current -> optionId (current ()).Index),
        Attrs.aria ("selected", (fun current -> Some (if (current ()).Active then "true" else "false")))
    ) {
        a (Attrs.className "hit", Attrs.attribute ("href", (fun current -> Some (current ()).Entry.Href))) {
            span (Attrs.className "hit-name") { fun current -> (current ()).Entry.Name }
            span (Attrs.className "hit-kind") { fun current -> (current ()).Entry.Kind }
            span (Attrs.className "hit-module") { fun current -> (current ()).Entry.Module }
            span (Attrs.className "hit-summary") { fun current -> (current ()).Entry.Summary }
        }
    }

/// The search dialog. `Ctrl+K` or `/` opens it, and `search.json` loads on the first opening.
let search: Component<unit> =
    Component.template
        createSearch
        (fragment [|
            button (
                Attrs.attribute ("type", "button"),
                Attrs.className "search-trigger",
                Attrs.aria ("haspopup", "dialog"),
                Events.onClick (fun (instance: SearchInstance) _ -> instance.Open ())
            ) {
                span (Attrs.className "search-label") { "Search" }
                kbd (Attrs.className "search-hint") { "Ctrl K" }
            }

            dialog (
                Attrs.className "search-dialog",
                Attrs.aria ("label", "Search declarations"),
                Hooks.mounted (fun (instance: SearchInstance) element -> instance.Attach (box element)),
                Events.onClick (fun instance event -> instance.OnDialogClick (box event)),
                Events.onClose (fun instance _ -> instance.OnClosed ())
            ) {
                input (
                    Attrs.attribute ("type", "text"),
                    Attrs.className "search-input",
                    Attrs.role "combobox",
                    Attrs.attribute ("autocomplete", "off"),
                    Attrs.attribute ("spellcheck", "false"),
                    Attrs.aria ("label", "Search declarations"),
                    Attrs.aria ("controls", "search-list"),
                    Attrs.aria ("autocomplete", "list"),
                    Attrs.aria ("expanded", (fun (instance: SearchInstance) -> Some (if instance.Results.Value.Length > 0 then "true" else "false"))),
                    Attrs.aria (
                        "activedescendant",
                        (fun (instance: SearchInstance) ->
                            if instance.Results.Value.Length > 0 then
                                Some (optionId (min instance.Selected.Value (instance.Results.Value.Length - 1)))
                            else
                                None)
                    ),
                    Events.onInput (fun instance event -> instance.OnInput (box event)),
                    Events.onKeydown (fun instance event -> instance.OnKey (box event))
                )

                p (Attrs.className "search-message", Attrs.role "status") { searchMessage }

                ul (Attrs.id "search-list", Attrs.className "search-list", Attrs.role "listbox", Attrs.aria ("label", "Results")) {
                    forEach (fun (row: SearchRow) -> row.Entry.Href) searchRows searchRow
                }
            }
        |])

/// An island with no content, standing in until the real islands exist.
let stub: Component<unit> =
    Component.define (fun _ () -> ()) (fun t -> t.fragment [||])

/// The islands on a page and their components, in document order.
let islands =
    [
        "search", search
        "switch", lookSwitch
        "section", sectionMarker
        "cards", stub
    ]

/// The islands on a page, in document order.
let names = islands |> List.map fst
