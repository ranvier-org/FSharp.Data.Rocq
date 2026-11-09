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

/// The `localStorage` key of the saved reference placement.
[<Literal>]
let referencesKey = "rocq-navigator-references"

/// The `localStorage` key of whether reference folds under definitions start open.
[<Literal>]
let referencesOpenKey = "rocq-navigator-references-open"

/// The `localStorage` key of the saved definition fold state.
[<Literal>]
let definitionsKey = "rocq-navigator-definitions"

/// The `localStorage` key of the line count above which "Close long ones" closes a definition.
[<Literal>]
let definitionsOverKey = "rocq-navigator-definitions-over"

/// The `localStorage` key of the saved proof fold state.
[<Literal>]
let proofsKey = "rocq-navigator-proofs"

/// The `localStorage` key of the saved imports fold state.
[<Literal>]
let importsKey = "rocq-navigator-imports"

/// The `localStorage` key of the saved argument list layout.
[<Literal>]
let argumentsKey = "rocq-navigator-args"

/// The root attribute holding the argument list layout, absent for names only.
[<Literal>]
let argumentsAttribute = "data-args"

/// The argument list with names and descriptions, above the code.
[<Literal>]
let argumentsNames = "names"

/// The argument list with types, above the code. Also its `data-args` value.
[<Literal>]
let argumentsTypes = "types"

/// The argument list with types, inside the code panel above the proofs. Also its `data-args` value.
[<Literal>]
let argumentsPanel = "panel"

/// The placement with the lists in the right pane. Also its `data-lists` value.
[<Literal>]
let placementPane = "pane"

/// The placement with a chip on each definition. Also its `data-lists` value.
[<Literal>]
let placementBadges = "badges"

/// The placement with the lists in a fold under each definition. Its `data-lists` is absent.
[<Literal>]
let placementUnder = "under"

/// A fold's saved state: open.
[<Literal>]
let foldOpen = "open"

/// A fold's saved state: closed.
[<Literal>]
let foldClosed = "closed"

/// The definition fold state that closes a definition longer than the saved line count.
[<Literal>]
let foldLong = "long"

/// The initial line count of "Close long ones".
[<Literal>]
let defaultDefinitionsOver = 40

/// The attribute marking a button that opens the settings.
[<Literal>]
let settingsTriggerAttribute = "data-settings-open"

/// The body attribute holding the site's `collapseCodeOver`, absent when the site has none.
[<Literal>]
let collapseOverAttribute = "data-collapse-over"

/// The percentage of the viewport's height, from its top, where a jump lands its target.
[<Literal>]
let private landingPercent = 5

/// The pixels a jump target lands above the landing percentage.
[<Literal>]
let private landingOffset = 4

/// The `scroll-margin-top` of a jump target.
let landingMargin = $"calc({landingPercent}vh - {landingOffset}px)"

/// The distance in pixels from a jump's landing position that still counts as the jump's target.
[<Literal>]
let private landingTolerance = 6.0

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
type private SizeObserver =
    abstract observe: Element -> unit
    abstract disconnect: unit -> unit

[<Emit("new ResizeObserver(() => $0())")>]
let private sizeObserver (callback: unit -> unit) : SizeObserver = jsNative

[<Emit("(() => { const h = () => $2(); $0.addEventListener($1, h, { passive: true }); return () => $0.removeEventListener($1, h) })()")>]
let private listenPassive (target: obj) (name: string) (handler: unit -> unit) : (unit -> unit) = jsNative

[<Emit("window")>]
let private windowObject () : obj = jsNative

[<Emit("document.documentElement.scrollTop")>]
let private pageScrollTop () : float = jsNative

[<Emit("(() => { try { return decodeURIComponent(location.hash.slice(1)) } catch { return location.hash.slice(1) } })()")>]
let private hashId () : string = jsNative

[<Emit("$0.open = $1")>]
let private setOpen (fold: Element) (value: bool) : unit = jsNative

/// The element a location's hash names, if any.
let private hashTarget () =
    match hashId () with
    | "" -> None
    | id -> document.getElementById id |> Option.ofObj

/// The section in view. The target of a jump holds while its top stays at the landing position. Otherwise the line runs from
/// the viewport's top at the page's start to its bottom at the page's end: the last section to start at or above the line
/// or, with none above it, the first to start inside the viewport.
let private sectionInView (isSection: Element -> bool) (sections: Element array) : Element option =
    let root = document.documentElement
    let height = float root.clientHeight
    let scrollable = float root.scrollHeight - height

    let progress =
        if scrollable <= 0.0 then
            0.0
        else
            min 1.0 (pageScrollTop () / scrollable)

    let line = height * progress

    let landing =
        height * float landingPercent / 100.0
        - float landingOffset

    let landed =
        hashTarget ()
        |> Option.filter (fun target ->
            isSection target
            && abs (target.getBoundingClientRect().top - landing)
               <= landingTolerance)

    match landed with
    | Some _ -> landed
    | None ->
        let mutable above = None
        let mutable inside = None

        for section in sections do
            let top = section.getBoundingClientRect().top

            if top <= line then
                above <- Some section
            elif top >= 0.0 && top < height && inside.IsNone then
                inside <- Some section

        match above with
        | Some _ -> above
        | None -> inside

/// Scrolls an outline link's pane, and the pane alone, to bring the link inside the pane's visible rectangle, above any pinned lists.
let private keepInPane (link: Element) =
    match link.closest ".pane" with
    | null -> ()
    | pane ->
        let paneBox = pane.getBoundingClientRect ()
        let linkBox = link.getBoundingClientRect ()

        let bottom =
            match pane.querySelector ".pinned" with
            | null -> paneBox.bottom
            | pinned ->
                let pinnedBox = pinned.getBoundingClientRect ()

                if pinnedBox.height > 0.0 then
                    pinnedBox.top
                else
                    paneBox.bottom

        if linkBox.top < paneBox.top then
            pane.scrollTop <-
                pane.scrollTop
                + int (linkBox.top - paneBox.top - markMargin)
        elif linkBox.bottom > bottom then
            pane.scrollTop <-
                pane.scrollTop
                + int (linkBox.bottom - bottom + markMargin)

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

/// Moves `.current-decl` to the named section. An empty name leaves no section marked.
let private markSection (section: string) =
    match document.querySelector ".current-decl" with
    | null -> ()
    | previous -> previous.classList.remove "current-decl"

    match document.getElementById section with
    | null -> ()
    | current -> current.classList.add "current-decl"

/// The name a declaration shows beside its lists.
let private declarationName (declaration: Element) =
    match declaration.getAttribute "data-name" with
    | null -> declaration.id
    | name -> name

/// Copies a declaration's lists into their named slots. A declaration lacking a list empties its slot.
let private copyLists (declaration: Element option) (slots: Element array) =
    for slot in slots do
        let name = slot.getAttribute "data-slot"

        match declaration with
        | Some owner ->
            match owner.querySelector $""".ref-list[data-list="%s{name}"]""" with
            | null -> replaceChildren slot [||]
            | list -> replaceChildren slot [| list.cloneNode true |]
        | None -> replaceChildren slot [||]

/// Writes the head of a list region: the declaration's name, and a quiet line for a declaration with no lists or none at all.
let private writeListsHead (region: Element) (declaration: Element option) =
    match region.querySelector ".lists-name", region.querySelector ".lists-empty" with
    | null, _
    | _, null -> ()
    | name, empty ->
        match declaration with
        | Some owner ->
            name.textContent <- declarationName owner

            empty.textContent <-
                if isNull (owner.querySelector ".ref-list") then
                    "No uses or used-by entries."
                else
                    ""
        | None ->
            name.textContent <- ""
            empty.textContent <- "No definition in view."

/// The declaration owning the lists for a section in view: the section itself when a declaration. For a heading, the previous
/// declaration while any of it is on screen, otherwise the first declaration with any part in the viewport.
let private listsOwnerOf (declarations: Element array) (section: Element option) =
    let onScreen (declaration: Element) =
        let box = declaration.getBoundingClientRect ()

        box.bottom > 0.0
        && box.top < float document.documentElement.clientHeight

    let rec before (element: Element) =
        match element.previousElementSibling with
        | null -> None
        | previous when previous.matches ".decl" -> Some previous
        | previous -> before previous

    match section with
    | None -> None
    | Some current when current.matches ".decl" -> section
    | Some heading ->
        before heading
        |> Option.filter onScreen
        |> Option.orElseWith (fun () -> declarations |> Array.tryFind onScreen)

/// Copies the named declaration's lists into the right pane's pinned region. The drawer's slots stay empty: below 900 px the lists stay inline.
let private showLists (declaration: string) =
    match document.querySelector ".pane-right .pinned" with
    | null -> ()
    | pinned ->
        let owner =
            document.getElementById declaration
            |> Option.ofObj

        writeListsHead pinned owner
        copyLists owner (all ".pane-right [data-slot]")

        match document.querySelector cardHost with
        | null -> ()
        | host -> dispatch host listsReplaced

/// Keeps each outline mark in its pane, above the pinned region.
let private keepMarkInPane () =
    for mark in all ".pane-right .pane-item.in-view" do
        keepInPane mark

[<Emit("(() => { const t = $0.target; const a = t instanceof Element ? t.closest('a[href]') : null; return !$0.ctrlKey && !$0.metaKey && !$0.shiftKey && a !== null && a.hash !== '' && a.pathname === location.pathname && a.hash === location.hash })()")>]
let private linksToCurrentHash (event: obj) : bool = jsNative

/// Opens every fold holding the location's hash target. A declaration's own definition fold opens with it.
let private revealTarget () =
    let rec openFolds (element: Element) =
        match element.closest "details" with
        | null -> ()
        | fold ->
            setOpen fold true

            match fold.parentElement with
            | null -> ()
            | parent -> openFolds parent

    match hashTarget () with
    | None -> ()
    | Some target ->
        openFolds target

        if target.matches ".decl" then
            match target.querySelector ".panel > details.body" with
            | null -> ()
            | fold -> setOpen fold true
#endif

let private createMarker (_: Setup) () =
#if FABLE_COMPILER
    let section = Api.createSignal ""
    let listsOwner = Api.createSignal ""

    let outlined =
        all """.pane-right .pane-item[href^="#"]"""
        |> Array.map (fun link -> (link.getAttribute "href").Substring 1)
        |> Set.ofArray

    let isSection (element: Element) =
        element.matches ".decl"
        || outlined.Contains element.id

    let sections = all "article.page [id]" |> Array.filter isSection

    let declarations =
        sections
        |> Array.filter (fun element -> element.matches ".decl")

    let idOf (element: Element option) =
        match element with
        | Some element -> element.id
        | None -> ""

    let update () =
        let current = sectionInView isSection sections
        section.Value <- idOf current
        listsOwner.Value <- idOf (listsOwnerOf declarations current)

    let onHash () =
        revealTarget ()
        update ()

    for name, handler in [ "scroll", update; "resize", update; "hashchange", onHash ] do
        Api.onCleanup (listenPassive (windowObject ()) name handler)

    // A link to the hash already in the URL fires no `hashchange`. A touch preview's `preventDefault` leaves the fold closed.
    let onClick: EventListener =
        unbox (fun (event: Event) ->
            if
                not event.defaultPrevented
                && linksToCurrentHash event
            then
                revealTarget ())

    document.addEventListener ("click", onClick)
    Api.onCleanup (fun () -> document.removeEventListener ("click", onClick))

    // The pinned region's height changes with its lists, its placement and the viewport.
    match document.querySelector ".pane-right .pinned" with
    | null -> ()
    | pinned ->
        let observer = sizeObserver keepMarkInPane
        observer.observe pinned
        Api.onCleanup observer.disconnect

    // A fold changing the page's height moves the line with no scroll event.
    match document.querySelector "article.page" with
    | null -> ()
    | article ->
        let observer = sizeObserver update
        observer.observe article
        Api.onCleanup observer.disconnect

    Api.createEffect (fun () ->
        let current = section.Value

        Api.untrack (fun () ->
            if current <> "" then
                markOutline current

            keepMarkInPane ()))

    Api.createEffect (fun () ->
        let owner = listsOwner.Value

        Api.untrack (fun () ->
            markSection owner
            showLists owner
            keepMarkInPane ()))

    onHash ()
#endif
    ()

/// Marks the section in view on the page and in the outline, and shows the current declaration's lists in the right pane.
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

/// The id of the dialog a reference chip opens.
[<Literal>]
let private chipDialogId = "rocq-chip-dialog"

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
        /// True while a chip's dialog is open.
        ChipOpen: unit -> bool
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

[<Emit("Array.from($0.querySelectorAll($1))")>]
let private allIn (parent: Element) (selector: string) : Element array = jsNative

/// Marks or clears the statement's occurrences of an argument row's binders.
let private markBinders (row: Element) (marked: bool) =
    match row.closest ".decl" with
    | null -> ()
    | declaration ->
        for key in (row.getAttribute "data-binders").Split ' ' do
            if key <> "" then
                for occurrence in allIn declaration $"""pre.statement [data-ref="{key}"]""" do
                    if marked then
                        occurrence.classList.add "arg-hot"
                    else
                        occurrence.classList.remove "arg-hot"

[<Emit("$0.pointerType === 'touch'")>]
let private isTouch (event: obj) : bool = jsNative

[<Emit("document.activeElement")>]
let private activeElement () : obj = jsNative

[<Emit("$0.contains($1)")>]
let private elementContains (parent: Element) (child: obj) : bool = jsNative

[<Emit("$0.focus({ preventScroll: true })")>]
let private focus (element: Element) : unit = jsNative

[<Emit("$0?.querySelector('a[href], summary')?.focus({ preventScroll: true })")>]
let private focusFirstIn (parent: Element) : unit = jsNative

[<Emit("$0.detail")>]
let private clickDetail (event: obj) : int = jsNative

[<Emit("($0.relatedTarget instanceof Node && $1?.contains($0.relatedTarget) === true)")>]
let private focusMovesInto (event: obj) (parent: Element) : bool = jsNative

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

/// Moves a layer beside its anchor: below it, else above it, else against the viewport's bottom, and always inside the
/// viewport. Coordinates are in the document, so the layer scrolls with the anchor.
let private placeLayer (id: string) (endsAtRight: bool) (anchor: Element) =
    match document.getElementById id with
    | null -> ()
    | layer ->
        let layer = unbox<HTMLElement> layer
        let rect = anchor.getBoundingClientRect ()
        let viewWidth = float document.documentElement.clientWidth
        let viewHeight = float document.documentElement.clientHeight
        let width = float layer.offsetWidth
        let height = float layer.offsetHeight

        let within (limit: float) (size: float) (value: float) =
            max cardMargin (min (limit - size - cardMargin) value)

        let below = rect.bottom + cardMargin
        let above = rect.top - cardMargin - height

        let top =
            if below + height + cardMargin <= viewHeight then below
            elif above >= cardMargin then above
            else within viewHeight height below

        let left = if endsAtRight then rect.right - width else rect.left
        layer.style.setProperty ("left", $"%f{within viewWidth width left + scrollX ()}px")
        layer.style.setProperty ("top", $"%f{top + scrollY ()}px")

/// Moves the card beside the reference, starting from the reference's left edge.
let private placeCard (reference: Element) =
    placeLayer cardId false reference

/// Moves a chip's dialog beside the chip, ending at the chip's right edge, clear of the definition's caret.
let private placeChipDialog (chip: Element) =
    placeLayer chipDialogId true chip

/// Shows a value after the hover delay, whenever the wanted value differs from the shown one.
let private followAfterDelay (wanted: Signal<Element option>) (shown: Signal<Element option>) =
    Api.createEffect (fun () ->
        let next = wanted.Value

        if next <> shown.Value then
            let timer = setTimeout (fun () -> shown.Value <- next) hoverDelay
            Api.onCleanup (fun () -> clearTimeout timer))

/// Fills a chip's dialog with the name and lists of the chip's declaration.
let private fillChipDialog (chip: Element) =
    match document.getElementById chipDialogId with
    | null -> ()
    | dialog ->
        let declaration = chip.closest ".decl" |> Option.ofObj
        writeListsHead dialog declaration
        copyLists declaration (all $"#{chipDialogId} [data-slot]")
#endif

let private createCards (_: Setup) () =
#if FABLE_COMPILER
    let file = Api.createSignal NotRequested
    let wanted = Api.createSignal (None: Element option)
    let shown = Api.createSignal (None: Element option)
    let chipWanted = Api.createSignal (None: Element option)
    let chipShown = Api.createSignal (None: Element option)

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

    followAfterDelay wanted shown
    followAfterDelay chipWanted chipShown

    Api.createEffect (fun () ->
        face.Value |> ignore

        match shown.Value with
        | Some reference -> afterFlush (fun () -> placeCard reference)
        | None -> ())

    Api.createEffect (fun () ->
        match shown.Value with
        | None -> ()
        | Some reference ->
            reference.setAttribute ("aria-describedby", cardId)
            Api.onCleanup (fun () -> reference.removeAttribute "aria-describedby"))

    let chipDialog () =
        document.getElementById chipDialogId

    let focusInChipDialog () =
        match chipDialog () with
        | null -> false
        | dialog -> elementContains dialog (activeElement ())

    // Focus moves back first: the chip's own `focusin` would otherwise reopen the dialog.
    let closeChip () =
        match chipShown.Value with
        | Some chip when focusInChipDialog () -> focus chip
        | _ -> ()

        chipShown.Value <- None
        chipWanted.Value <- None

    let openChip (chip: Element) =
        chipShown.Value <- Some chip
        chipWanted.Value <- Some chip

    Api.createEffect (fun () ->
        match chipShown.Value with
        | None -> ()
        | Some chip ->
            Api.untrack (fun () -> fillChipDialog chip)
            chip.setAttribute ("aria-expanded", "true")
            afterFlush (fun () -> placeChipDialog chip)
            let stopListening = listenPassive (windowObject ()) "resize" closeChip

            Api.onCleanup (fun () ->
                stopListening ()
                chip.removeAttribute "aria-expanded"))

    // One Esc closes the top layer: a card over a chip's dialog closes first.
    Api.createEffect (fun () ->
        if shown.Value.IsSome || chipShown.Value.IsSome then
            let onKey: EventListener =
                unbox (fun (event: KeyboardEvent) ->
                    match event.key with
                    | "Escape" when shown.Value.IsSome -> close ()
                    | "Escape" -> closeChip ()
                    | "Tab" when focusInChipDialog () ->
                        // A Tab past the dialog's last link returns to the chip, so the reader keeps their place in the article.
                        setTimeout
                            (fun () ->
                                if not (focusInChipDialog ()) then
                                    chipShown.Value |> Option.iter focus
                                    closeChip ())
                            0
                        |> ignore
                    | _ -> ())

            document.addEventListener ("keydown", onKey)
            Api.onCleanup (fun () -> document.removeEventListener ("keydown", onKey)))

    // A link inside the card's doc is the card's own content, not a reference to open.
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
        | name, _ when name = listsReplaced -> closeIfDisconnected ()
        | "pointerover", None -> closeIfDisconnected ()
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
            close ()
        | _ -> ()

    let chipOf (event: Event) =
        (unbox<Element> event.target).closest ".refs-chip"
        |> Option.ofObj

    let onArgumentEvent (event: Event) =
        match event.``type``, (unbox<Element> event.target).closest ".args-row" with
        | "pointerover", row when not (isNull row) -> markBinders row true
        | "pointerout", row when not (isNull row) -> markBinders row false
        | _ -> ()

    // The dialog and any card over it hold the dialog open.
    let inChipLayer (event: Event) =
        let target = unbox<Element> event.target

        not (isNull (target.closest ".chip-dialog"))
        || not (isNull (target.closest ".card"))

    let onChipEvent (event: Event) =
        match event.``type``, chipOf event with
        | "pointerover", Some chip when not (isTouch event) -> chipWanted.Value <- Some chip
        | "pointerover", None when
            chipShown.Value.IsSome
            && inChipLayer event
            && not (isTouch event)
            ->
            chipWanted.Value <- chipShown.Value
        | "pointerout", chip when
            not (isTouch event)
            && (chip.IsSome || inChipLayer event)
            && not (focusInChipDialog ())
            ->
            chipWanted.Value <- None
        | "focusin", Some chip when (unbox<Element> event.target).matches ":focus-visible" -> openChip chip
        | "focusout", Some _ when not (focusMovesInto event (chipDialog ())) -> chipWanted.Value <- None
        | "click", Some chip ->
            if isTouch event && chipShown.Value = Some chip then
                closeChip ()
            else
                openChip chip

                // A keyboard's click has no pointer position, and the dialog's links come next.
                if clickDetail event = 0 then
                    setTimeout (fun () -> chipDialog () |> focusFirstIn) 0
                    |> ignore
        | "click", None when chipShown.Value.IsSome ->
            let target = unbox<Element> event.target

            if
                isNull (target.closest ".chip-dialog")
                && isNull (target.closest ".card")
            then
                closeChip ()
            elif
                not event.defaultPrevented
                && not (isNull (target.closest ".chip-dialog a[href]"))
            then
                closeChip ()
        | _ -> ()

    match document.querySelector cardHost with
    | null -> ()
    | host ->
        let listener: EventListener =
            unbox (fun (event: Event) ->
                onEvent event
                onChipEvent event
                onArgumentEvent event)

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
        ChipOpen = fun () -> chipShown.Value.IsSome
    }
#else
    {
        Face = Api.createMemoWithComparer sameFace (fun _ -> None)
        Hold = ignore
        Release = ignore
        ChipOpen = fun () -> false
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

/// A card for a declaration or a file carries no count of mentions.
let private hasMentions (face: CardFace) =
    not (
        face.Card.Key.StartsWith "d"
        || face.Card.Key.StartsWith "f"
    )

let private metaOf (face: CardFace) =
    if not face.FromFile then
        ""
    else
        let place = if face.Card.External then "external" else face.Card.Module

        if hasMentions face then
            $"{place} · mentioned by {face.Card.Mentions}"
        else
            place

/// The hover card, and the dialog a reference chip opens: each one element in the server's HTML, hidden until opened. A card's
/// name and signature are text, and its doc is HTML the build rendered with raw HTML disabled.
let cardLayer: Component<unit> =
    Component.define createCards (fun t ->
        fragment
            [|
                div (
                    Attrs.className "card",
                    Attrs.attribute ("id", cardId),
                    Attrs.attribute ("role", "tooltip"),
                    shownWhen (fun _ -> true),
                    Events.onPointerenter (fun (instance: CardLayerInstance) _ -> instance.Hold ()),
                    Events.onPointerleave (fun (instance: CardLayerInstance) event -> instance.Release (box event))
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
                }

                div (
                    Attrs.className "chip-dialog",
                    Attrs.attribute ("id", chipDialogId),
                    Attrs.attribute ("role", "dialog"),
                    Attrs.aria ("label", "References"),
                    Attrs.attribute ("hidden", (fun (instance: CardLayerInstance) -> if instance.ChipOpen () then None else Some ""))
                ) {
                    div (Attrs.className "lists-head") {
                        small () { "This definition" }
                        span (Attrs.className "lists-name") { () }
                    }

                    p (Attrs.className "lists-empty") { () }
                    div (Attrs.className "xref", Attrs.attribute ("data-slot", "uses")) { () }
                    div (Attrs.className "xref", Attrs.attribute ("data-slot", "used-by")) { () }
                }
            |])
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

    Api.createEffectOn (fun () -> isOpen.Value) (fun wanted ->
        if not (isNull dialog.Value) then
            if wanted then
                if not (dialogIsOpen dialog.Value) then
                    showModal dialog.Value
                    focusInput dialog.Value
            else
                if dialogIsOpen dialog.Value then
                    closeDialog dialog.Value

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

/// The state and handlers of the settings surface.
type SettingsInstance =
    {
        Look: Signal<string>
        Mode: Signal<string>
        References: Signal<string>
        ReferencesOpen: Signal<string>
        /// Empty while the site's own fold state stands.
        Definitions: Signal<string>
        DefinitionsOver: Signal<int>
        Proofs: Signal<string>
        Imports: Signal<string>
        Arguments: Signal<string>
        /// Writes a fold setting's chosen value to its folds, by the setting's key. Other keys change no fold.
        WriteFolds: string -> string -> unit
        Attach: obj -> (unit -> unit)
        Close: unit -> unit
        OnClosed: unit -> unit
        OnDialogClick: obj -> unit
        OnOver: obj -> unit
        Reset: unit -> unit
    }

[<Literal>]
let private surfacePopover = "popover"

[<Literal>]
let private surfacePanel = "panel"

[<Literal>]
let private surfaceDialog = "dialog"

#if FABLE_COMPILER
[<Emit("localStorage.getItem($0)")>]
let private getStored (key: string) : string = jsNative

[<Emit("localStorage.removeItem($0)")>]
let private removeStored (key: string) : unit = jsNative

[<Emit("matchMedia($0).matches")>]
let private mediaMatches (query: string) : bool = jsNative

[<Emit("$0.show()")>]
let private showDialog (dialog: obj) : unit = jsNative

[<Emit("($0.checkVisibility ? $0.checkVisibility() : $0.getClientRects().length > 0)")>]
let private isVisible (element: Element) : bool = jsNative

[<Emit("$0.target === $0.currentTarget")>]
let private targetIsCurrent (event: obj) : bool = jsNative

[<Emit("$0.value = $1")>]
let private setInputValue (input: Element) (value: string) : unit = jsNative

[<Emit("$0.value")>]
let private inputText (input: Element) : string = jsNative

[<Emit("(/^[1-9][0-9]{0,5}$/.test($0))")>]
let private isLineCount (text: string) : bool = jsNative

[<Emit("parseInt($0, 10)")>]
let private parseCount (text: string) : int = jsNative

let private stored (key: string) =
    try
        getStored key |> Option.ofObj
    with _ ->
        None

let private forget (key: string) =
    try
        removeStored key
    with _ ->
        ()

/// A saved value when it is one of the known values, else the fallback.
let private storedChoice (key: string) (known: string list) (fallback: string) =
    match stored key with
    | Some value when List.contains value known -> value
    | _ -> fallback

let private storedOver () =
    match stored definitionsOverKey with
    | Some value when isLineCount value -> parseCount value
    | _ -> defaultDefinitionsOver

let private setFolds (selector: string) (value: string) =
    for fold in all selector do
        setOpen fold (value = foldOpen)

/// The line count of a definition's fold.
let private foldLines (fold: Element) =
    match fold.querySelector "summary" with
    | null -> 0
    | summary -> parseCount (summary.getAttribute "data-lines")

/// Writes the definition folds' state. An empty state restores the server's, which closes a fold longer than the site's limit.
let private setDefinitions (state: string) (over: int) =
    let serverLimit =
        match document.body.getAttribute collapseOverAttribute with
        | null -> None
        | limit -> Some (parseCount limit)

    for fold in all "article details.body" do
        setOpen
            fold
            (match state with
             | state when state = foldOpen -> true
             | state when state = foldClosed -> false
             | state when state = foldLong -> foldLines fold <= over
             | _ ->
                 serverLimit
                 |> Option.forall (fun limit -> foldLines fold <= limit))

let private surfaceNow () =
    if mediaMatches "(max-width: 600px)" then surfaceDialog
    elif mediaMatches "(pointer: coarse)" then surfacePanel
    elif mediaMatches "(min-width: 901px)" then surfacePopover
    else surfaceDialog

/// Focuses the opener or, when a resize has hidden it, the visible settings trigger or else the Modules drawer's summary.
let private focusSettingsTrigger (opener: obj) =
    [
        if not (isNull opener) then
            unbox<Element> opener

        yield! all $"[{settingsTriggerAttribute}]"
        yield! all ".drawer > summary"
    ]
    |> List.tryFind isVisible
    |> Option.iter focus
#endif

let private createSettings (_: Setup) () : SettingsInstance =
#if FABLE_COMPILER
    let look = Api.createSignal (rootAttribute "data-look" defaultLook)
    let mode = Api.createSignal (rootAttribute "data-mode" defaultMode)
    let references = Api.createSignal (rootAttribute "data-lists" placementUnder)
    let foldStates = [ foldOpen; foldClosed ]

    let referencesOpen =
        Api.createSignal (storedChoice referencesOpenKey foldStates foldClosed)

    let definitions =
        Api.createSignal (storedChoice definitionsKey [ foldOpen; foldClosed; foldLong ] "")

    let definitionsOver = Api.createSignal (storedOver ())
    let proofs = Api.createSignal (storedChoice proofsKey foldStates foldClosed)
    let imports = Api.createSignal (storedChoice importsKey foldStates foldClosed)
    let arguments = Api.createSignal (rootAttribute argumentsAttribute argumentsNames)
    let isOpen = Api.createSignal false
    let dialog = ref null
    let opener = ref null

    Api.createEffect (fun () ->
        if arguments.Value = argumentsNames then
            document.documentElement.removeAttribute argumentsAttribute
        else
            document.documentElement.setAttribute (argumentsAttribute, arguments.Value))

    Api.createEffect (fun () -> document.documentElement.setAttribute ("data-look", look.Value))
    Api.createEffect (fun () -> document.documentElement.setAttribute ("data-mode", mode.Value))

    Api.createEffect (fun () ->
        if references.Value = placementUnder then
            document.documentElement.removeAttribute "data-lists"
        else
            document.documentElement.setAttribute ("data-lists", references.Value))

    let numberInput () =
        if isNull dialog.Value then
            None
        else
            (unbox<Element> dialog.Value).querySelector "input"
            |> Option.ofObj

    // A typed value leaves the attribute behind, so the property carries Reset.
    Api.createEffectOn (fun () -> definitionsOver.Value) (fun over ->
        numberInput ()
        |> Option.iter (fun input ->
            if parseCount (inputText input) <> over then
                setInputValue input (string over)))

    let surface () =
        (unbox<Element> dialog.Value).getAttribute "data-surface"

    let present () =
        let next = surfaceNow ()
        (unbox<Element> dialog.Value).setAttribute("data-surface", next)

        if next = surfacePopover then
            showDialog dialog.Value
        else
            showModal dialog.Value

    Api.createEffectOn (fun () -> isOpen.Value) (fun wanted ->
        if not (isNull dialog.Value) then
            if wanted then
                if not (dialogIsOpen dialog.Value) then
                    present ()
            else
                if dialogIsOpen dialog.Value then
                    closeDialog dialog.Value

                focusSettingsTrigger opener.Value)

    // A rotation or resize can change the surface, and each surface opens differently.
    Api.createEffect (fun () ->
        if isOpen.Value then
            let onResize () =
                if
                    surfaceNow () <> surface ()
                    && dialogIsOpen dialog.Value
                then
                    closeDialog dialog.Value
                    present ()

            let onKey: EventListener =
                unbox (fun (event: KeyboardEvent) ->
                    if event.key = "Escape" then
                        isOpen.Value <- false)

            let stopListening = listenPassive (windowObject ()) "resize" onResize
            document.addEventListener ("keydown", onKey)

            Api.onCleanup (fun () ->
                stopListening ()
                document.removeEventListener ("keydown", onKey)))

    let onClick (event: Event) =
        let target = unbox<Element> event.target

        match target.closest $"[{settingsTriggerAttribute}]" with
        | null ->
            if
                isOpen.Value
                && surface () = surfacePopover
                && isNull (target.closest ".settings")
            then
                isOpen.Value <- false
        | trigger ->
            if isOpen.Value then
                isOpen.Value <- false
            else
                opener.Value <- box trigger
                isOpen.Value <- true

    let listener: EventListener = unbox onClick
    document.addEventListener ("click", listener)
    Api.onCleanup (fun () -> document.removeEventListener ("click", listener))

    {
        Look = look
        Mode = mode
        References = references
        ReferencesOpen = referencesOpen
        Definitions = definitions
        DefinitionsOver = definitionsOver
        Proofs = proofs
        Imports = imports
        Arguments = arguments
        WriteFolds =
            fun key value ->
                match key with
                | key when key = proofsKey -> setFolds "article details.proof" value
                | key when key = importsKey -> setFolds "article details.imports" value
                | key when key = referencesOpenKey -> setFolds "article details.refs" value
                | key when key = definitionsKey -> setDefinitions value definitionsOver.Value
                | _ -> ()
        Attach =
            fun element ->
                dialog.Value <- element
                fun () -> dialog.Value <- null
        Close = fun () -> isOpen.Value <- false
        OnClosed =
            fun () ->
                if
                    isNull dialog.Value
                    || not (dialogIsOpen dialog.Value)
                then
                    isOpen.Value <- false
        OnDialogClick =
            fun event ->
                if targetIsCurrent event then
                    isOpen.Value <- false
        OnOver =
            fun event ->
                let text = inputValue event

                if isLineCount text then
                    save definitionsOverKey text
                    definitionsOver.Value <- parseCount text
                    setDefinitions definitions.Value definitionsOver.Value
        Reset =
            fun () ->
                for key in
                    [
                        lookKey
                        modeKey
                        referencesKey
                        referencesOpenKey
                        definitionsKey
                        definitionsOverKey
                        proofsKey
                        importsKey
                        argumentsKey
                    ] do
                    forget key

                look.Value <- defaultLook
                mode.Value <- defaultMode
                references.Value <- placementPane
                referencesOpen.Value <- foldClosed
                definitions.Value <- ""
                definitionsOver.Value <- defaultDefinitionsOver
                proofs.Value <- foldClosed
                imports.Value <- foldClosed
                arguments.Value <- argumentsNames
                setFolds "article details.proof" foldClosed
                setFolds "article details.imports" foldClosed
                setFolds "article details.refs" foldClosed
                setDefinitions "" defaultDefinitionsOver
    }
#else
    {
        Look = Api.createSignal defaultLook
        Mode = Api.createSignal defaultMode
        References = Api.createSignal placementPane
        ReferencesOpen = Api.createSignal foldClosed
        Definitions = Api.createSignal ""
        DefinitionsOver = Api.createSignal defaultDefinitionsOver
        Proofs = Api.createSignal foldClosed
        Imports = Api.createSignal foldClosed
        Arguments = Api.createSignal argumentsNames
        WriteFolds = fun _ _ -> ()
        Attach = fun _ -> ignore
        Close = ignore
        OnClosed = ignore
        OnDialogClick = ignore
        OnOver = ignore
        Reset = ignore
    }
#endif

let private choice (select: SettingsInstance -> Signal<string>) (key: string) (value: string) (label: string) =
    button (
        Attrs.attribute ("type", "button"),
        Attrs.aria ("pressed", (fun (instance: SettingsInstance) -> Some (if (select instance).Value = value then "true" else "false"))),
        Events.onClick (fun (instance: SettingsInstance) _ ->
#if FABLE_COMPILER
            save key value
#endif
            (select instance).Value <- value
            instance.WriteFolds key value)
    ) {
        label
    }

/// The reading settings: one dialog whose surface follows the viewport at each opening, and whose controls apply live and persist in `localStorage`.
let settings: Component<unit> =
    Component.template
        createSettings
        (dialog (
            Attrs.className "settings",
            Attrs.aria ("label", "Reading settings"),
            Hooks.mounted (fun (instance: SettingsInstance) element -> instance.Attach (box element)),
            Events.onClick (fun instance event -> instance.OnDialogClick (box event)),
            Events.onClose (fun instance _ -> instance.OnClosed ())
        ) {
            div (Attrs.className "settings-body") {
                div (Attrs.className "settings-head") {
                    h2 () { "Reading settings" }

                    button (
                        Attrs.attribute ("type", "button"),
                        Attrs.className "settings-close",
                        Attrs.aria ("label", "Close settings"),
                        Events.onClick (fun (instance: SettingsInstance) _ -> instance.Close ())
                    ) {
                        "✕"
                    }
                }

                div (Attrs.className "switch") {
                    div (Attrs.className "setting") {
                        p (Attrs.className "setting-label") { "Look" }

                        span (Attrs.className "switch-group", Attrs.attribute ("role", "group"), Attrs.aria ("label", "Look")) {
                            choice _.Look lookKey "family" "Family"
                            choice _.Look lookKey "journal" "Journal"
                        }
                    }

                    div (Attrs.className "setting") {
                        p (Attrs.className "setting-label") { "Mode" }

                        span (Attrs.className "switch-group", Attrs.attribute ("role", "group"), Attrs.aria ("label", "Mode")) {
                            choice _.Mode modeKey "system" "Auto"
                            choice _.Mode modeKey "light" "Light"
                            choice _.Mode modeKey "dark" "Dark"
                        }
                    }

                    div (Attrs.className "setting") {
                        p (Attrs.className "setting-label") { "Definitions" }

                        span (Attrs.className "switch-group", Attrs.attribute ("role", "group"), Attrs.aria ("label", "Definitions")) {
                            choice _.Definitions definitionsKey foldOpen "Open"
                            choice _.Definitions definitionsKey foldClosed "Closed"
                            choice _.Definitions definitionsKey foldLong "Close long ones"
                        }

                        input (
                            Attrs.attribute ("type", "number"),
                            Attrs.attribute ("min", "1"),
                            Attrs.className "setting-lines",
                            Attrs.aria ("label", "Close definitions longer than this many lines"),
                            Attrs.attribute ("value", (fun (instance: SettingsInstance) -> Some (string instance.DefinitionsOver.Value))),
                            Attrs.attribute (
                                "hidden",
                                (fun (instance: SettingsInstance) ->
                                    if instance.Definitions.Value = foldLong then
                                        None
                                    else
                                        Some "")
                            ),
                            Events.onInput (fun instance event -> instance.OnOver (box event))
                        )
                    }

                    div (Attrs.className "setting") {
                        p (Attrs.className "setting-label") { "Proofs" }

                        span (Attrs.className "switch-group", Attrs.attribute ("role", "group"), Attrs.aria ("label", "Proofs")) {
                            choice _.Proofs proofsKey foldClosed "Closed"
                            choice _.Proofs proofsKey foldOpen "Open"
                        }
                    }

                    div (Attrs.className "setting") {
                        p (Attrs.className "setting-label") { "Imports" }

                        span (Attrs.className "switch-group", Attrs.attribute ("role", "group"), Attrs.aria ("label", "Imports")) {
                            choice _.Imports importsKey foldClosed "Closed"
                            choice _.Imports importsKey foldOpen "Open"
                        }
                    }

                    div (Attrs.className "setting") {
                        p (Attrs.className "setting-label") { "Arguments" }

                        span (Attrs.className "switch-group", Attrs.attribute ("role", "group"), Attrs.aria ("label", "Arguments")) {
                            choice _.Arguments argumentsKey argumentsNames "Names only"
                            choice _.Arguments argumentsKey argumentsTypes "With types"
                            choice _.Arguments argumentsKey argumentsPanel "In the panel"
                        }
                    }

                    div (Attrs.className "setting setting-references") {
                        p (Attrs.className "setting-label") { "References" }

                        span (Attrs.className "switch-group", Attrs.attribute ("role", "group"), Attrs.aria ("label", "References")) {
                            choice _.References referencesKey placementPane "Pinned"
                            choice _.References referencesKey placementBadges "Badges"
                            choice _.References referencesKey placementUnder "Under definitions"
                        }
                    }

                    div (Attrs.className "setting setting-references-open") {
                        p (Attrs.className "setting-label") { "References open" }

                        span (Attrs.className "switch-group", Attrs.attribute ("role", "group"), Attrs.aria ("label", "References open")) {
                            choice _.ReferencesOpen referencesOpenKey foldClosed "Closed"
                            choice _.ReferencesOpen referencesOpenKey foldOpen "Open"
                        }
                    }
                }

                button (
                    Attrs.attribute ("type", "button"),
                    Attrs.className "settings-reset",
                    Events.onClick (fun (instance: SettingsInstance) _ -> instance.Reset ())
                ) {
                    "Reset to the site's defaults"
                }

                p (Attrs.className "settings-note") { "Saved in this browser." }
            }
        })

/// The islands on a page and their components, in document order.
let islands =
    [
        "search", search
        "settings", settings
        "section", sectionMarker
        "cards", cardLayer
    ]

/// The islands on a page, in document order.
let names = islands |> List.map fst
