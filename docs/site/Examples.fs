/// <summary>The code the documentation quotes. Each region runs during a build, and its console output appears beside it.</summary>
module Docs.Examples

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open FSharp.Data.Rocq.Project
open FSharp.Data.Rocq.Source
open FSharp.Data.Rocq.Structure
open FSharp.Data.Rocq.Extract
open Nacara.Core
open Nacara.Plugins

let private loaded =
    lazy
        (match load "example/_CoqProject" with
         | Ok project -> project
         | Error failures ->
             failwith (
                 failures
                 |> Array.map describeFailure
                 |> String.concat "\n"
             ))

//#region load
let loadProject () =
    match load "example/_CoqProject" with
    | Ok project ->
        for file in project.Files do
            printfn
                "%-12s %-20s %s %2d declarations %3d sentences %2d comments"
                file.Module
                file.Path
                (file.Digest.Substring (0, 8))
                file.Model.Declarations.Length
                file.Model.Sentences.Length
                file.Model.Comments.Length
    | Error failures ->
        for failure in failures do
            eprintfn "%s" (describeFailure failure)
//#endregion

//#region declarations
let listTheorems (project: RocqProject) =
    for entry in project.Declarations do
        let declaration = entry.Declaration

        match declaration.Category with
        | Category.Theorem ->
            printfn
                "%-24s %-12s %d proof, %d doc comment"
                (defaultArg declaration.Name declaration.Id)
                project.Files[entry.File].Module
                declaration.Proofs.Length
                declaration.Docs.Length
        | _ -> ()
//#endregion

//#region text
let textOf (file: RocqFile) (span: Span) =
    Encoding.UTF8.GetString (file.Model.Bytes, span.Start, span.End - span.Start)

let printStatement (project: RocqProject) (name: string) =
    for entry in project.Declarations do
        if entry.Declaration.Name = Some name then
            let file = project.Files[entry.File]
            printfn "keyword: %A" entry.Declaration.Keyword

            match entry.Declaration.Statement with
            | ValueSome statement -> printfn "statement: %s" (textOf file statement)
            | ValueNone -> ()

            printfn "extent:\n%s" (textOf file entry.Declaration.Extent)
//#endregion

//#region comments
let printComments (file: RocqFile) =
    for comment in file.Model.Comments |> Array.truncate 6 do
        let lines = (textOf file comment.BodySpan).Trim().Split '\n'
        let firstLine = lines[0].Trim()

        let attachedTo =
            comment.AttachedTo
            |> Option.bind (fun declaration -> file.Model.Declarations[declaration].Name)

        printfn "%-5s %-22s %s" (commentFormatName comment.Format) (defaultArg attachedTo "-") firstLine

    for heading in file.Model.Headings do
        printfn "heading %d: %s" heading.Level heading.Text
//#endregion

//#region whoUses
let printUses (project: RocqProject) (name: string) =
    for edge in whoUses project name do
        match edge.Owner with
        | DeclarationOwner index ->
            let owner = project.Declarations[index]
            let ownerName = defaultArg owner.Declaration.Name owner.Declaration.Id
            printfn "%-12s %-24s %d in its %A" project.Files[owner.File].Module ownerName edge.Count edge.Where
        | FileOwner index -> printfn "top level of %s" project.Files[index].Path
//#endregion

//#region usesBy
let printDependencies (project: RocqProject) (name: string) =
    let index =
        project.Declarations
        |> Array.findIndex (fun entry -> entry.Declaration.Name = Some name)

    for edge in usesBy project (DeclarationOwner index) do
        match edge.Target with
        | SymbolNode symbol -> printfn "project: %s" project.Symbols[symbol].Symbol.Name
        | ExternalSymbolNode external ->
            let node: ExternalNode = project.CrossReferences.Externals[external]
            printfn "outside: %s" node.Qualified
//#endregion

//#region fileGraph
let printFileGraph (project: RocqProject) =
    let moduleOf (file: int) =
        project.Files[file].Module

    for from, target in project.FileGraph.Requires do
        printfn "%s requires %s" (moduleOf from) (moduleOf target)

    for from, external in project.FileGraph.ExternalRequires do
        printfn "%s requires %s, outside the project" (moduleOf from) external

    for reference in project.FileGraph.References do
        printfn "%s refers to %s %d times" (moduleOf reference.From) (moduleOf reference.To) reference.Count
//#endregion

let private copyDirectory (source: string) (target: string) =
    if Directory.Exists target then
        Directory.Delete (target, true)

    for file in Directory.GetFiles (source, "*", SearchOption.AllDirectories) do
        let destination = Path.Combine (target, Path.GetRelativePath (source, file))

        Directory.CreateDirectory (Path.GetDirectoryName destination)
        |> ignore

        File.Copy (file, destination)

let private staleCopy () =
    copyDirectory "example" ".work/stale"
    File.AppendAllText (".work/stale/theories/Core.v", "\n(* Edited after compiling. *)\n")
    ".work/stale/_CoqProject"

//#region stale
let printStaleLoad (projectFile: string) =
    match load projectFile with
    | Ok _ -> printfn "loaded"
    | Error failures ->
        for failure in failures do
            printfn "refused: %s" (describeFailure failure)

    match loadWith { AllowStale = true } projectFile with
    | Ok project ->
        for file in project.Files do
            printfn "%-12s %A, %d symbols" file.Module file.GlobStatus file.Model.Symbols.Length
    | Error _ -> ()
//#endregion

//#region extract
let extractTwice (outDir: string) =
    let options =
        {
            Project = "example/_CoqProject"
            OutDir = outDir
            RocqDep = None
            RocqVersion = Some "9.1.0"
            AllowStale = false
            Strict = true
            Force = false
        }

    let first = run options
    let second = run options
    printfn "first run:  %d extracted, %d reused, exit %d" first.Extracted.Length first.Reused.Length first.ExitCode
    printfn "second run: %d extracted, %d reused, exit %d" second.Extracted.Length second.Reused.Length second.ExitCode
//#endregion

let private listFiles (directory: string) =
    for file in
        Directory.GetFiles (directory, "*", SearchOption.AllDirectories)
        |> Array.sortWith (fun a b -> String.CompareOrdinal (a, b)) do
        let name = Path.GetRelativePath(directory, file).Replace('\\', '/')
        printfn "%-40s %8d bytes" name (FileInfo file).Length

//#region json
let printIndexShape (outDir: string) =
    use document =
        JsonDocument.Parse (File.ReadAllBytes (Path.Combine (outDir, "index.json")))

    for property in document.RootElement.EnumerateObject () do
        match property.Value.ValueKind with
        | JsonValueKind.Array -> printfn "%-16s array of %d" property.Name (property.Value.GetArrayLength ())
        | JsonValueKind.Object ->
            let keys =
                property.Value.EnumerateObject ()
                |> Seq.map _.Name
                |> Seq.truncate 6

            printfn "%-16s { %s }" property.Name (String.concat ", " keys)
        | _ -> printfn "%-16s %s" property.Name (property.Value.GetRawText ())
//#endregion

let private withoutColour (text: string) =
    Regex.Replace (text, @"\x1b\[[0-9;]*m", "")

let private processOutput (arguments: string list) =
    let start =
        ProcessStartInfo ("dotnet", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false)

    for argument in arguments do
        start.ArgumentList.Add argument

    use running = Process.Start start
    let error = running.StandardError.ReadToEndAsync ()
    let output = running.StandardOutput.ReadToEnd ()
    running.WaitForExit ()
    (withoutColour (output + error.Result)).TrimEnd(), running.ExitCode

let private runCli (arguments: string list) =
    let cli = Path.Combine (AppContext.BaseDirectory, "FSharp.Data.Rocq.Cli.dll")
    let output, code = processOutput (cli :: arguments)
    printfn "$ FSharp.Data.Rocq.Cli %s" (String.concat " " arguments)

    if output <> "" then
        printfn "%s" output

    printfn "exit code %d\n" code

//#region site
let buildSite (baseUrl: string) (arguments: string[]) =
    let options =
        Rocq.defaults
        |> Rocq.project "example/_CoqProject"
        |> Rocq.facts (Some "example/facts.json")
        |> Rocq.config (Some "example/navigator.json")

    Site.create "Tally"
    |> Site.baseUrl baseUrl
    |> Site.output ".work/minimal"
    |> Site.noStaticFiles
    |> Rocq.register options
    |> Site.collection (
        Rocq.collection "proofs" options
        |> Collection.layout (Rocq.reader options)
    )
    |> fun site -> Nacara.run site arguments
//#endregion

let private minimalBuild =
    lazy
        (let self = Path.Combine (AppContext.BaseDirectory, "Rocq.Docs.dll")
         processOutput [ self; "minimal-site" ])

/// The element holding the id, with a line per tag that opens a block.
let private elementWithId (html: string) (id: string) =
    let at = html.IndexOf ($"id=\"{id}\"", StringComparison.Ordinal)
    let start = html.LastIndexOf ('<', at)
    let tag = Regex.Match(html.Substring start, @"^<([a-z]+)").Groups[1].Value
    let marks = Regex $"<(/?){tag}\\b"
    let mutable depth = 0
    let mutable stop = -1

    for mark in marks.Matches (html, start) do
        if stop < 0 then
            depth <-
                depth
                + (if mark.Groups[1].Value = "" then 1 else -1)

            if depth = 0 then
                stop <- html.IndexOf ('>', mark.Index) + 1

    let blocks =
        @"(?=<(div|section|article|details|summary|pre|p|h[1-6]|ul|ol|li|nav|aside|dl|dt|dd)\b)"

    (Regex.Replace (html.Substring (start, stop - start), blocks, "\n")).Trim()

/// <summary>Every example, by the name its marker uses on a page. Running one writes its output to the console.</summary>
let all: Map<string, unit -> unit> =
    Map.ofList
        [
            "load", loadProject
            "declarations", (fun () -> listTheorems loaded.Value)
            "text", (fun () -> printStatement loaded.Value "total_append")
            "comments", (fun () -> printComments loaded.Value.Files[1])
            "whoUses", (fun () -> printUses loaded.Value "total")
            "usesBy", (fun () -> printDependencies loaded.Value "total_append")
            "fileGraph", (fun () -> printFileGraph loaded.Value)
            "stale", (fun () -> printStaleLoad (staleCopy ()))
            "extract",
            (fun () ->
                if Directory.Exists ".work/out" then
                    Directory.Delete (".work/out", true)

                extractTwice ".work/out")
            "extract-files", (fun () -> listFiles ".work/out")
            "json", (fun () -> printIndexShape ".work/out")
            "cli",
            (fun () ->
                runCli
                    [
                        "extract"
                        "--project"
                        "example/_CoqProject"
                        "--out"
                        ".work/cli"
                        "--force"
                    ]

                runCli [ "extract"; "--project"; "example/_CoqProject"; "--out"; ".work/cli" ])
            "cli-stale",
            (fun () ->
                let projectFile = staleCopy ()
                runCli [ "extract"; "--project"; projectFile; "--out"; ".work/cli-stale" ]

                runCli
                    [
                        "extract"
                        "--project"
                        projectFile
                        "--out"
                        ".work/cli-stale"
                        "--allow-stale"
                    ])
            "site-build",
            (fun () ->
                let output, code = minimalBuild.Value
                printfn "%s\nexit code %d" output code)
            "site-files",
            (fun () ->
                minimalBuild.Value |> ignore
                listFiles ".work/minimal")
            "site-html",
            (fun () ->
                minimalBuild.Value |> ignore
                printfn "%s" (elementWithId (File.ReadAllText ".work/minimal/Tally.Core.html") "size"))
        ]
