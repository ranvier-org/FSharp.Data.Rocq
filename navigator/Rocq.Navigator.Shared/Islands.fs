module Rocq.Navigator.Islands

open Ranvier.View

/// The attribute that marks an element as the host of a hydrated island. Its value names the island.
[<Literal>]
let hostAttribute = "data-island"

/// The id prefix an island renders and hydrates with, distinct per island on a page.
let idPrefix (name: string) =
    "r" + name

/// An island with no content, standing in until the real islands exist.
let stub: Component<unit> =
    Component.define (fun _ () -> ()) (fun t -> t.fragment [||])
