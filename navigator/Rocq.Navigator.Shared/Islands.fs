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

/// The percentage of the viewport's height, from its top, above the line that decides the section in view.
[<Literal>]
let private linePercent = 5

/// The `scroll-margin-top` of a jump target. Its top lands just above the line at every viewport height.
let landingMargin = $"calc({linePercent}vh - 4px)"

/// The `IntersectionObserver` margin of the area above the line. A section's state changes whenever its top crosses the line.
let private aboveLineMargin = $"0px 0px -{100 - linePercent}%% 0px"

/// The share of the viewport's height, from its top, that holds the first section of a page.
[<Literal>]
let private bandShare = 0.4

/// The element that holds every reference a card opens for.
[<Literal>]
let private cardHost = ".shell"

/// The event the section marker dispatches on the card host after it replaces the right pane's lists.
[<Literal>]
let private listsReplaced = "rocq-lists-replaced"

/// The distance in pixels between a marked outline item and its pane's edge, after the pane scrolls.
[<Literal>]
let private markMargin = 32.0

#if FABLE_COMPILER
[<Global "document">]
let private document: Document = jsNative

[<Emit("localStorage.setItem($0, $1)")>]
let private setStored (key: string) (value: string) : unit = jsNative

[<Emit("Array.from(document.querySelectorAll($0))")>]
let private all (selector: string) : Element array = jsNative

[<Emit("$0.replaceChildren(...$1)")>]
let private replaceChildren (parent: Element) (children: Node array) : unit = jsNative

[<Emit("$0.dispatchEvent(new Event($1))")>]
let private dispatch (target: Element) (name: string) : unit = jsNative

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

[<Emit("new IntersectionObserver(() => $0(), { rootMargin: $1 })")>]
let private sectionObserver (callback: unit -> unit) (rootMargin: string) : SectionObserver = jsNative

/// The section in view: the last to start above the line or, with none above it, the first in the band.
let private sectionInView (sections: Element array) : Element option =
    let top (section: Element) =
        section.getBoundingClientRect().top

    let height = float document.documentElement.clientHeight

    let inBand =
        sections
        |> Array.filter (fun section -> top section < height * bandShare)

    let aboveLine =
        inBand
        |> Array.filter (fun section -> top section <= height * float linePercent / 100.0)

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

        match document.querySelector cardHost with
        | null -> ()
        | host -> dispatch host listsReplaced
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
            span (Attrs.className "switch-group", Attrs.attribute ("role", "group"), Attrs.aria ("label", "Look")) {
                choice _.Look lookKey "family" "Family"
                choice _.Look lookKey "journal" "Journal"
            }

            span (Attrs.className "switch-group", Attrs.attribute ("role", "group"), Attrs.aria ("label", "Mode")) {
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

    let update () =
        match sectionInView sections with
        | Some current -> section.Value <- current.id
        | None -> ()

    // A jump between two gaps can leave the area above the line unchanged. It moves sections across the viewport's edges.
    for margin in [ aboveLineMargin; "0px" ] do
        let observer = sectionObserver update margin

        for target in sections do
            observer.observe target

        Api.onCleanup observer.disconnect

    Api.createEffect (fun () ->
        if section.Value <> "" then
            let current = section.Value
            markOutline current
            Api.untrack (fun () -> showLists current))
#endif
    ()

/// Marks the section in view in the outline, and shows the current declaration's lists in the right pane.
let sectionMarker: Component<unit> =
    Component.define createMarker (fun t -> t.fragment [||])

/// The id of the card element, which an open reference names in `aria-describedby`.
[<Literal>]
let private cardId = "rocq-card"

/// The delay in milliseconds before a hovered reference opens its card, and before a card closes after the pointer leaves.
[<Literal>]
let private hoverDelay = 120

/// The distance in pixels between a card and its reference, and between a card and the viewport's edge.
[<Literal>]
let private cardMargin = 6.0

/// What the card layer shows. `FromFile` is false for a card built from the reference alone, while the data file is
/// loading or after it fails.
type CardFace =
    { Card: Model.CardData; FromFile: bool }

/// The state of the card layer.
type CardLayerInstance =
    {
        Face: Memo<CardFace option>
        /// Keeps the open card while the pointer is on it.
        Hold: unit -> unit
        /// Closes the card after the delay, given the `pointerleave` event.
        Release: obj -> unit
    }

let private sameFace =
    { new System.Collections.Generic.IEqualityComparer<CardFace option> with
        member _.Equals(first, second) =
            match first, second with
            | Some first, Some second ->
                first.Card.Key = second.Card.Key
                && first.FromFile = second.FromFile
            | None, None -> true
            | _ -> false

        member _.GetHashCode _ = 0
    }

#if FABLE_COMPILER
type private CardFile =
    | NotRequested
    | Requested
    | Loaded of Map<string, Model.CardData>
    | Unavailable

[<Emit("fetch($0).then(r => r.ok ? r.text() : Promise.reject(r.status)).then($1, $2)")>]
let private fetchText (url: string) (onText: string -> unit) (onFailure: obj -> unit) : unit = jsNative

[<Emit("setTimeout($0, $1)")>]
let private setTimeout (callback: unit -> unit) (delay: int) : int = jsNative

[<Emit("clearTimeout($0)")>]
let private clearTimeout (timer: int) : unit = jsNative

[<Emit("queueMicrotask($0)")>]
let private afterFlush (callback: unit -> unit) : unit = jsNative

[<Emit("window.scrollX")>]
let private scrollX () : float = jsNative

[<Emit("window.scrollY")>]
let private scrollY () : float = jsNative

[<Emit("$0.pointerType === 'touch'")>]
let private isTouch (event: obj) : bool = jsNative

/// A card holding what the reference itself carries.
let private referenceFace (reference: Element) =
    let name = reference.textContent

    {
        Card =
            {
                Key = reference.getAttribute "data-ref"
                Name = name
                Qualified = name
                Module = ""
                Href = reference.getAttribute "href" |> Option.ofObj
                Signature = ""
                SignatureIsType = false
                DocHtml = None
                Mentions = 0
                Badge = None
                External = false
            }
        FromFile = false
    }

/// Moves the card beside the reference: below it, else above it, else against the viewport's bottom, and always inside
/// the viewport. Coordinates are in the document, so the card scrolls with the reference.
let private placeCard (reference: Element) =
    match document.getElementById cardId with
    | null -> ()
    | card ->
        let card = unbox<HTMLElement> card
        let rect = reference.getBoundingClientRect ()
        let viewWidth = float document.documentElement.clientWidth
        let viewHeight = float document.documentElement.clientHeight
        let width = float card.offsetWidth
        let height = float card.offsetHeight

        let within (limit: float) (size: float) (value: float) =
            max cardMargin (min (limit - size - cardMargin) value)

        let below = rect.bottom + cardMargin
        let above = rect.top - cardMargin - height

        let top =
            if below + height + cardMargin <= viewHeight then below
            elif above >= cardMargin then above
            else within viewHeight height below

        card.style.setProperty ("left", $"%f{within viewWidth width rect.left + scrollX ()}px")
        card.style.setProperty ("top", $"%f{top + scrollY ()}px")
#endif

let private createCards (_: Setup) () =
#if FABLE_COMPILER
    let file = Api.createSignal NotRequested
    let wanted = Api.createSignal (None: Element option)
    let shown = Api.createSignal (None: Element option)

    let load () =
        match file.Value, document.body.getAttribute "data-symbols" with
        | NotRequested, null -> file.Value <- Unavailable
        | NotRequested, url ->
            file.Value <- Requested

            fetchText
                url
                (fun text ->
                    match Model.cardsWire.Decode text with
                    | Ok cards ->
                        file.Value <-
                            cards
                            |> Array.map (fun card -> card.Key, card)
                            |> Map.ofArray
                            |> Loaded
                    | Error _ -> file.Value <- Unavailable)
                (fun _ -> file.Value <- Unavailable)
        | _ -> ()

    let hover (reference: Element) =
        load ()
        wanted.Value <- Some reference

    let openNow (reference: Element) =
        load ()
        shown.Value <- Some reference
        wanted.Value <- Some reference

    let close () =
        shown.Value <- None
        wanted.Value <- None

    let face =
        Api.createMemoWithComparer sameFace (fun _ ->
            shown.Value
            |> Option.map (fun reference ->
                match file.Value with
                | Loaded cards ->
                    match cards.TryFind (reference.getAttribute "data-ref") with
                    | Some card -> { Card = card; FromFile = true }
                    | None -> referenceFace reference
                | _ -> referenceFace reference))

    Api.createEffect (fun () ->
        let next = wanted.Value

        if next <> shown.Value then
            let timer = setTimeout (fun () -> shown.Value <- next) hoverDelay
            Api.onCleanup (fun () -> clearTimeout timer))

    Api.createEffect (fun () ->
        face.Value |> ignore

        match shown.Value with
        | Some reference -> afterFlush (fun () -> placeCard reference)
        | None -> ())

    Api.createEffect (fun () ->
        match shown.Value with
        | None -> ()
        | Some reference ->
            let onKey: EventListener =
                unbox (fun (event: KeyboardEvent) ->
                    if event.key = "Escape" then
                        close ())

            reference.setAttribute ("aria-describedby", cardId)
            document.addEventListener ("keydown", onKey)

            Api.onCleanup (fun () ->
                reference.removeAttribute "aria-describedby"
                document.removeEventListener ("keydown", onKey)))

    let referenceOf (event: Event) =
        let target = unbox<Element> event.target

        if isNull (target.closest ".card") then
            target.closest "[data-ref]" |> Option.ofObj
        else
            None

    let closeIfDisconnected () =
        let disconnected (reference: Element option) =
            reference
            |> Option.exists (fun element -> not element.isConnected)

        if
            (disconnected shown.Value
             || disconnected wanted.Value)
            && isNull (document.querySelector ".card:hover")
        then
            close ()

    let onEvent (event: Event) =
        match event.``type``, referenceOf event with
        | "pointerover", Some reference when not (isTouch event) -> hover reference
        | "pointerout", Some _ when not (isTouch event) -> wanted.Value <- None
        | "focusin", Some reference when (unbox<Element> event.target).matches ":focus-visible" -> openNow reference
        | "focusout", Some _ when isNull (document.querySelector ".card:hover") -> wanted.Value <- None
        | "click", Some reference when isTouch event ->
            if shown.Value <> Some reference then
                event.preventDefault ()
                openNow reference
        | "click", None when
            isTouch event
            && isNull ((unbox<Element> event.target).closest ".card")
            ->
    // A link inside the card's doc is the card's own content, not a reference to open.
            close ()
        | _ -> ()

    match document.querySelector cardHost with
    | null -> ()
    | host ->
        | name, _ when name = listsReplaced -> closeIfDisconnected ()
        | "pointerover", None -> closeIfDisconnected ()
        let listener: EventListener = unbox onEvent

        let events =
            [ "pointerover"; "pointerout"; "focusin"; "focusout"; "click"; listsReplaced ]

        for name in events do
            host.addEventListener (name, listener)

        Api.onCleanup (fun () ->
            for name in events do
                host.removeEventListener (name, listener))

    {
        Face = face
        Hold = fun () -> wanted.Value <- shown.Value
        Release =
            fun event ->
                if not (isTouch event) then
                    wanted.Value <- None
    }
#else
    {
        Face = Api.createMemoWithComparer sameFace (fun _ -> None)
        Hold = ignore
        Release = ignore
    }
#endif

let private cardText (read: CardFace -> string) (instance: CardLayerInstance) =
    match instance.Face.Value with
    | Some face -> read face
    | None -> ""

let private shownWhen (condition: CardFace -> bool) : Part<'native, CardLayerInstance> =
    Attrs.attribute (
        "hidden",
        fun (instance: CardLayerInstance) ->
            match instance.Face.Value with
            | Some face when condition face -> None
            | _ -> Some ""
    )

let private isBinder (face: CardFace) =
    face.Card.Key.StartsWith "b"

let private signatureOf (face: CardFace) =
    if face.Card.External then "" else face.Card.Signature

let private nameOf (face: CardFace) =
    if face.Card.External then
        face.Card.Qualified
    else
        face.Card.Name

let private badgeText (face: CardFace) =
    match face.Card.Badge with
    | Some Model.NoAxioms -> "✓ No axioms"
    | Some (Model.Rests axioms) -> "Rests on " + String.concat ", " axioms
    | None -> ""

let private badgeClass (face: CardFace) =
    match face.Card.Badge with
    | Some Model.NoAxioms -> "badge pass"
    | Some (Model.Rests _) -> "badge axioms"
    | None -> "badge"

let private metaOf (face: CardFace) =
    if not face.FromFile then
        ""
    else
        let place = if face.Card.External then "external" else face.Card.Module

        if hasMentions face then
            $"{place} · mentioned by {face.Card.Mentions}"
        else
            place

/// The hover card: one element in the server's HTML, hidden until a reference in the article opens it. Name and signature
/// are text. The doc is HTML the build rendered with raw HTML disabled.
let cardLayer: Component<unit> =
    Component.define createCards (fun t ->
        div (
            Attrs.className "card",
            Attrs.attribute ("id", cardId),
            Attrs.attribute ("role", "tooltip"),
            shownWhen (fun _ -> true),
            Events.onPointerenter (fun (instance: CardLayerInstance) _ -> instance.Hold ()),
            Events.onPointerleave (fun (instance: CardLayerInstance) event -> instance.Release (box event))
/// A card for a declaration or a file carries no count of mentions.
let private hasMentions (face: CardFace) =
    not (
        face.Card.Key.StartsWith "d"
        || face.Card.Key.StartsWith "f"
    )

        ) {
            p (Attrs.className "card-head") {
                a (
                    Attrs.className "card-name",
                    Attrs.attribute ("href", (fun (instance: CardLayerInstance) -> instance.Face.Value |> Option.bind _.Card.Href))
                ) {
                    cardText nameOf
                }

                span (
                    Attrs.attribute ("class", (fun (instance: CardLayerInstance) -> Some (cardText badgeClass instance))),
                    shownWhen _.Card.Badge.IsSome
                ) {
                    cardText badgeText
                }
            }

            pre (Attrs.className "card-signature", shownWhen (fun face -> signatureOf face <> "")) { cardText signatureOf }

            div (
                Attrs.className "card-doc",
                shownWhen _.Card.DocHtml.IsSome,
                t.Unsafe.html (fun (instance: CardLayerInstance) ->
                    instance.Face.Value
                    |> Option.bind _.Card.DocHtml
                    |> Option.defaultValue ""
                    |> UnsafeHtml.Trust)
            ) {
                ()
            }

            p (
                Attrs.className "card-quiet",
                shownWhen (fun face ->
                    face.FromFile
                    && face.Card.DocHtml.IsNone
                    && not face.Card.External
                    && not (isBinder face))
            ) {
                "No documentation"
            }

            p (Attrs.className "card-meta", shownWhen _.FromFile) { cardText metaOf }
        })
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

[<Emit("($0.contains(document.activeElement) && document.activeElement.blur(), $0.close())")>]
let private closeDialog (dialog: obj) : unit = jsNative

[<Emit("(() => { const i = $0.querySelector('input'); i.focus(); i.select() })()")>]
let private focusInput (dialog: obj) : unit = jsNative

[<Emit("setTimeout(() => { const e = document.getElementById($0); if (e) e.scrollIntoView({ block: 'nearest' }) }, 0)")>]
let private revealRow (id: string) : unit = jsNative

[<Emit("window.location.assign($0)")>]
let private navigate (href: string) : unit = jsNative

[<Emit("document.body.getAttribute('data-search')")>]
let private searchUrl () : string = jsNative

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
    | _ when
        instance.Text.Value.Trim () <> ""
        && instance.Results.Value.Length = 0
        ->
        "No matches"
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
                (fun failure -> fail (string failure))

    let openDialog () =
        if not isOpen.Value then
            opener.Value <- activeElement ()
            isOpen.Value <- true

            if
                status.Value <> statusReady
                && status.Value <> statusLoading
            then
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

            if
                (commandKey event && key.ToLowerInvariant () = "k")
                || (key = "/"
                    && not (modified event)
                    && not (typedInField event))
            then
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
        | "Escape" ->
            preventDefault event
            isOpen.Value <- false
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
        OnDialogClick =
            fun event ->
                if dismissesDialog event then
                    isOpen.Value <- false
        OnClosed =
            fun () ->
                if
                    isNull dialog.Value
                    || not (dialogIsOpen dialog.Value)
                then
                    isOpen.Value <- false
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
        (fragment
            [|
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
                        Attrs.aria (
                            "expanded",
                            (fun (instance: SearchInstance) ->
                                Some (
                                    if instance.Results.Value.Length > 0 then
                                        "true"
                                    else
                                        "false"
                                ))
                        ),
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
        "cards", cardLayer
    ]

/// The islands on a page, in document order.
let names = islands |> List.map fst
