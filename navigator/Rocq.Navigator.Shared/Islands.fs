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

/// An island with no content, standing in until the real islands exist.
let stub: Component<unit> =
    Component.define (fun _ () -> ()) (fun t -> t.fragment [||])

/// The islands on a page and their components, in document order.
let islands =
    [
        "search", stub
        "switch", lookSwitch
        "section", sectionMarker
        "cards", stub
    ]

/// The islands on a page, in document order.
let names = islands |> List.map fst
