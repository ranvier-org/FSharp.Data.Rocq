module Rocq.Navigator.Site

open Nacara.Core
open Nacara.Plugins

[<EntryPoint>]
let main args =
    let site =
        Site.create "Rocq navigator"
        |> Site.baseUrl "/"
        |> Site.output "output"
        |> Site.noStaticFiles
        |> Markdown.register
        |> Spike.register
        |> LinkValidator.register

    Nacara.run site args
