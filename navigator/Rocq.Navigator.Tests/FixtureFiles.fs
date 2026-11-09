module Rocq.Navigator.Tests.FixtureFiles

open System
open System.IO
open FSharp.Data.Rocq.Project

/// The repository root: `ROCQ_NAVIGATOR_REPO` when set, otherwise a walk up from the assembly to the solution file.
let repoRoot () =
    match Environment.GetEnvironmentVariable "ROCQ_NAVIGATOR_REPO" with
    | null
    | "" ->
        let rec walk (dir: DirectoryInfo) =
            match dir with
            | null -> failwith "Set ROCQ_NAVIGATOR_REPO: no FSharp.Data.Rocq.slnx above the test assembly."
            | d when File.Exists (Path.Combine (d.FullName, "FSharp.Data.Rocq.slnx")) -> d.FullName
            | d -> walk d.Parent

        walk (DirectoryInfo AppContext.BaseDirectory)
    | root -> root

let fixtureDir () =
    Path.Combine (repoRoot (), "navigator", "Rocq.Navigator.Tests", "fixtures")

let private loadProject (path: string) =
    match load path with
    | Ok project -> project
    | Result.Error failures ->
        failures
        |> Array.map describeFailure
        |> String.concat "; "
        |> failwith

let fixtureProject () =
    loadProject (Path.Combine (fixtureDir (), "_CoqProject"))

let libraryProject () =
    loadProject (Path.Combine (repoRoot (), "tests", "FSharp.Data.Rocq.Tests", "fixtures", "_CoqProject"))
