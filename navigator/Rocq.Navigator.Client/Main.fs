module Main

open Fable.Core
open Ranvier.Dom
open global.Ranvier
open Ranvier.Anime
open Ranvier.View
open Ranvier.View.Dom
open Rocq.Navigator

[<Global "document">]
let private document: Document = jsNative

/// True when the page's text selection starts inside the element.
[<Emit "(getSelection() != null && !getSelection().isCollapsed && $0.contains(getSelection().anchorNode))">]
let private selectionStartsIn (element: Element) : bool = jsNative

/// The click count of a click event. Keyboard activation has 0.
[<Emit "$0.detail">]
let private clickCount (event: Event) : int = jsNative

/// Hydrates the island on the page, if any. Returns false when hydration reported errors.
let private hydrateIsland (graph: Graph) (name: string) (definition: Component<'props>) (props: 'props) =
    match
        document.querySelector $"""[%s{Islands.hostAttribute}="%s{name}"]"""
        |> Option.ofObj
    with
    | None -> ()
    | Some host ->
        let options =
            { Hydration.HydrationOptions.defaults with
                IdPrefix = Islands.idPrefix name
                Strict = true
            }

        let report (errors: exn list) =
            for error in errors do
                JS.console.warn ("Hydration", error)

        Hydration.hydrate graph None report options host definition props
        |> ignore

let private clearFold (fold: HTMLElement) =
    fold.style.removeProperty "height" |> ignore
    fold.style.removeProperty "overflow" |> ignore

/// Marks a fold whose closing height is running. It stays open until the height finishes.
let private closingAttribute = "data-closing"

/// Animates a fold's height both ways. A click during a run reverses it from the current height. Reduced motion leaves the native toggle.
let private animateFolds (graph: Graph) =
    graph.Run (fun () ->
        Api.createRoot (fun _ ->
            match
                document.querySelector "article.page"
                |> Option.ofObj
            with
            | None -> ()
            | Some article ->
                let policy = Reactive.policy ()

                let onClick (event: Event) =
                    let target = unbox<Element> event.target
                    let summary = target.closest "summary"

                    let isFold =
                        not (isNull summary)
                        && summary.parentElement.matches "details.proof, details.refs, details.more, details.body, details.imports"

                    if isFold && not (isNull (target.closest "a")) then
                        ()
                    elif
                        isFold
                        && (not (isNull (target.closest "[data-ref]"))
                            // A double click's first click toggled, so its second, which selected a word, toggles back.
                            || (clickCount event <> 0
                                && clickCount event <> 2
                                && selectionStartsIn summary))
                    then
                        event.preventDefault ()
                    elif isFold then
                        let fold = unbox<HTMLDetailsElement> summary.parentElement
                        let target: Targets = unbox fold

                        let closing =
                            fold.``open``
                            && not (fold.hasAttribute closingAttribute)

                        let current = fold.getBoundingClientRect().height
                        Utils.remove target
                        clearFold fold
                        fold.removeAttribute closingAttribute

                        if policy.Value = Reactive.Full then
                            event.preventDefault ()
                            fold.``open`` <- not closing
                            let goal = fold.getBoundingClientRect().height
                            fold.``open`` <- true
                            fold.style.setProperty ("overflow", "clip")

                            if closing then
                                fold.setAttribute (closingAttribute, "")

                            Anime.animate (
                                target,
                                Anim (
                                    height = Value.fromTo (Value.op_Implicit current, Value.op_Implicit goal),
                                    duration = 180,
                                    ease = Ease.outQuad,
                                    onComplete =
                                        fun _ ->
                                            if closing then
                                                fold.removeAttribute closingAttribute
                                                fold.``open`` <- false

                                            clearFold fold
                                )
                            )
                            |> ignore

                let listener: EventListener = unbox onClick
                article.addEventListener ("click", listener)
                Api.onCleanup (fun () -> article.removeEventListener ("click", listener))))

let start () =
    let graph = new Graph ()

    for name, island in Islands.islands do
        try
            hydrateIsland graph name island ()
        with error ->
            JS.console.warn ("Hydration", name, error)

    animateFolds graph
    document.documentElement.setAttribute ("data-client", "ready")
