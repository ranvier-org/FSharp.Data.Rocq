module Rocq.Navigator.Site

open System
open Nacara.Core
open Nacara.Plugins

let private environment (name: string) =
    match Environment.GetEnvironmentVariable name with
    | null
    | "" -> None
    | value -> Some value

/// Takes `--project <path>` out of the arguments Nacara reads.
let rec private projectArgument (args: string list) =
    match args with
    | "--project" :: path :: rest -> Some path, rest
    | first :: rest ->
        let path, others = projectArgument rest
        path, first :: others
    | [] -> None, []

[<EntryPoint>]
let main args =
    let projectPath, rest = projectArgument (List.ofArray args)

    let options =
        Rocq.defaults
        |> Rocq.project (
            projectPath
            |> Option.orElse (environment "NAVIGATOR_PROJECT")
            |> Option.defaultValue "_CoqProject"
        )
        |> Rocq.facts (environment "NAVIGATOR_FACTS")
        |> Rocq.config (environment "NAVIGATOR_CONFIG")

    let site =
        Site.create "Rocq navigator"
        |> Site.baseUrl (
            environment "NAVIGATOR_BASE_URL"
            |> Option.defaultValue "/"
        )
        |> Site.output "output"
        |> Site.noStaticFiles
        |> Rocq.register options
        |> LinkValidator.register
        |> Site.collection (
            Rocq.collection "proofs" options
            |> Collection.layout (Rocq.reader options)
        )

    Nacara.run site (Array.ofList rest)
