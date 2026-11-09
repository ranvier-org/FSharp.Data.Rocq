module Rocq.Navigator.Islands

open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar

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

/// The element that holds every reference a card opens for.
[<Literal>]
let private cardHost = "article.page"

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
        (unbox<Element> event.target).closest "[data-ref]"
        |> Option.ofObj

    let onEvent (event: Event) =
        match event.``type``, referenceOf event with
        | "pointerover", Some reference when not (isTouch event) -> hover reference
        | "pointerout", Some _ when not (isTouch event) -> wanted.Value <- None
        | "focusin", Some reference when (unbox<Element> event.target).matches ":focus-visible" -> openNow reference
        | "focusout", Some _ -> wanted.Value <- None
        | "click", Some reference when isTouch event ->
            if shown.Value <> Some reference then
                event.preventDefault ()
                openNow reference
        | "click", None when isTouch event -> close ()
        | _ -> ()

    match document.querySelector cardHost with
    | null -> ()
    | host ->
        let listener: EventListener = unbox onEvent
        let events = [ "pointerover"; "pointerout"; "focusin"; "focusout"; "click" ]

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
        $"{place} · mentioned by {face.Card.Mentions}"

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

/// An island with no content, standing in until the real islands exist.
let stub: Component<unit> =
    Component.define (fun _ () -> ()) (fun t -> t.fragment [||])

/// The islands on a page and their components, in document order.
let islands =
    [
        "search", stub
        "switch", lookSwitch
        "section", sectionMarker
        "cards", cardLayer
    ]

/// The islands on a page, in document order.
let names = islands |> List.map fst
