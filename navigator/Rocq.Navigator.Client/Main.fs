module Main

open Fable.Core
open Ranvier.Dom
open global.Ranvier
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

let start () =
    let graph = new Graph ()

    for name in Islands.names do
        hydrateIsland graph name Islands.stub ()
