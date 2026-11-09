/// <summary>Page routes and the anchors on a module page.</summary>
module Rocq.Navigator.Anchors

open System.Collections.Generic
open System.Text.RegularExpressions
open FSharp.Data.Rocq.Project
open FSharp.Data.Rocq.Structure
open Nacara.Core

/// <summary>The route of a module's page, <c>&lt;root&gt;/&lt;Module&gt;.html</c>.</summary>
/// <param name="root">The route prefix. Empty places the page at the site root.</param>
/// <param name="locale">The page's locale.</param>
/// <param name="moduleName">The module's full name.</param>
let routeOf (root: string) (locale: Locale) (moduleName: string) : Route =
    match root.Trim '/' with
    | "" -> Route.file locale $"{moduleName}.html"
    | prefix -> Route.file locale $"{prefix}/{moduleName}.html"

let private notWordCharacters = Regex @"[^\p{L}\p{N}]+"

/// <summary>The anchor of a heading: <c>h-</c> and its lowercased words joined by dashes.</summary>
let slug (heading: string) : string =
    let words = notWordCharacters.Replace (heading.ToLowerInvariant (), "-")

    "h-" + words.Trim '-'

/// <summary>A function giving each value it receives a unique form: the value first, then <c>value~2</c>, <c>value~3</c> and on.</summary>
let internal uniquifier () : string -> string =
    let counts = Dictionary<string, int>()

    fun value ->
        let count =
            match counts.TryGetValue value with
            | true, count -> count + 1
            | _ -> 1

        counts[value] <- count
        if count = 1 then value else $"{value}~{count}"

/// <summary>The anchor of each declaration in a file, by its index in <c>Model.Declarations</c>. All anchors are unique.</summary>
/// <remarks>A notation's <c>.glob</c> name is an encoded key rather than a name, so a notation gets the same <c>d&lt;index&gt;</c> as an unnamed declaration.</remarks>
let forFile (file: RocqFile) : Map<int, string> =
    let unique = uniquifier ()

    file.Model.Declarations
    |> Array.map (fun declaration ->
        let anchor =
            match declaration.Name with
            | Some name when declaration.Category <> Category.Notation -> name
            | _ -> $"d{declaration.Index}"

        declaration.Index, unique anchor)
    |> Map.ofArray
