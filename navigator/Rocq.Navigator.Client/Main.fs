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

/// Animates a fold's height as it opens. A fold closes at once. Reduced motion leaves the native toggle.
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
                    let summary = (unbox<Element> event.target).closest "summary"

                    if
                        not (isNull summary)
                        && summary.parentElement.matches "details.proof, details.refs, details.more"
                    then
                        let fold = unbox<HTMLDetailsElement> summary.parentElement
                        let target: Targets = unbox fold

                        if fold.``open`` then
                            Utils.remove target
                            clearFold fold
                        elif policy.Value = Reactive.Full then
                            event.preventDefault ()
                            let closed = fold.getBoundingClientRect().height
                            fold.``open`` <- true
                            let opened = fold.getBoundingClientRect().height
                            fold.style.setProperty ("overflow", "clip")

                            Anime.animate (
                                target,
                                Anim (
                                    height = Value.fromTo (Value.op_Implicit closed, Value.op_Implicit opened),
                                    duration = 180,
                                    ease = Ease.outQuad,
                                    onComplete = fun _ -> clearFold fold
                                )
                            )
                            |> ignore

                let listener: EventListener = unbox onClick
                article.addEventListener ("click", listener)
                Api.onCleanup (fun () -> article.removeEventListener ("click", listener))))

let start () =
    let graph = new Graph ()

    for name in Islands.names do
        hydrateIsland graph name Islands.stub ()

    animateFolds graph
    document.documentElement.setAttribute ("data-client", "ready")
