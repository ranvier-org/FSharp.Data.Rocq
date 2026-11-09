/// Acceptance over a real project. `FSHARP_DATA_ROCQ_CORPUS` names its `_CoqProject`, and `FSHARP_DATA_ROCQ_CORPUS_CONFIG`
/// its `navigator.json`. Without the first, every test here is skipped.
module Rocq.Navigator.Tests.CorpusTests

open System
open System.IO
open System.Text
open System.Text.RegularExpressions
open Expecto
open FSharp.Data.Rocq.Project
open Nacara.Core
open Nacara.Plugins
open Rocq.Navigator
open Rocq.Navigator.Article
open Rocq.Navigator.Tests

type private SourceSpan = FSharp.Data.Rocq.Source.Span

let private environment (name: string) =
    match Environment.GetEnvironmentVariable name with
    | null
    | "" -> None
    | value -> Some value

let private corpusPath () =
    environment "FSHARP_DATA_ROCQ_CORPUS"

let private config () =
    match environment "FSHARP_DATA_ROCQ_CORPUS_CONFIG" with
    | None -> Config.none
    | Some path ->
        match Config.read path with
        | Ok config -> config
        | Result.Error message -> failwith message

let private project =
    lazy
        (match load (corpusPath ()).Value with
         | Ok project -> project
         | Result.Error failures ->
             failures
             |> Array.map describeFailure
             |> String.concat "; "
             |> failwith)

/// The site built once, with `LinkValidator`, into a temporary directory the process removes on exit.
let private built =
    lazy
        (let path =
            Path.Combine (
                Path.GetTempPath (),
                "rocq-navigator-corpus-"
                + Guid.NewGuid().ToString "N"
            )

         Directory.CreateDirectory path |> ignore
         AppDomain.CurrentDomain.ProcessExit.Add (fun _ -> Directory.Delete (path, true))

         let options =
             Rocq.defaults
             |> Rocq.project (corpusPath ()).Value
             |> Rocq.config (environment "FSHARP_DATA_ROCQ_CORPUS_CONFIG")

         let result =
             Site.create "Corpus"
             |> Site.baseUrl "/"
             |> Site.output "output"
             |> Site.noStaticFiles
             |> Rocq.register options
             |> LinkValidator.register
             |> Site.collection (Rocq.collection "proofs" options)
             |> Build.run (AbsolutePath.create path)

         path, result)

let private page (file: RocqFile) =
    File.ReadAllText (Path.Combine (fst built.Value, "output", $"{file.Module}.html"))

let private sourceText (file: RocqFile) (span: SourceSpan) =
    Encoding.UTF8.GetString (file.Model.Bytes, span.Start, span.End - span.Start)

let private writtenBlocks (html: string) (pattern: string) =
    Regex.Matches (html, pattern, RegexOptions.Singleline)
    |> Seq.map (fun m -> Source.stripTags m.Groups[m.Groups.Count - 1].Value)
    |> String.concat "\u0000"

/// Theorems titled by a comment's bold first line, read from the source text alone.
let private scanTitles (file: RocqFile) =
    let pattern =
        Regex (
            @"\(\*\*md[ \t]*\r?\n[ \t]*\*\*([^\r\n]+)\*\*[ \t]*\r?\n((?:(?!\*\)).)*)\*\)\s*(Theorem|Corollary)\s+([^\s(:]+)",
            RegexOptions.Singleline
        )

    [
        for m in pattern.Matches (Encoding.UTF8.GetString file.Model.Bytes) do
            m.Groups[1].Value, m.Groups[4].Value
    ]

let private corpusTest (name: string) (run: unit -> unit) =
    testCase name
    <| fun () ->
        if (corpusPath ()).IsNone then
            skiptest "FSHARP_DATA_ROCQ_CORPUS is not set"

        run ()

let tests =
    testList
        "corpus"
        [
            corpusTest "the bytes of each statement and proof on the page equal the .v span"
            <| fun () ->
                for file in project.Value.Files do
                    let html = page file

                    let statements =
                        writtenBlocks html "<pre class=\"?(statement|command)\"?><code>(.*?)</code></pre>"

                    let proofs = writtenBlocks html "</summary><pre><code>(.*?)</code></pre>"

                    let normal (text: string) =
                        text.Replace ("\r\n", "\n")

                    for declaration in file.Model.Declarations do
                        match declaration.Statement with
                        | ValueSome span ->
                            Expect.isTrue
                                ((normal statements).Contains(normal (sourceText file span)))
                                $"{file.Module} {declaration.Name} statement at {span.Start}"
                        | ValueNone -> ()

                        for proof in declaration.Proofs do
                            Expect.isTrue
                                ((normal proofs).Contains(normal (sourceText file proof.Span)))
                                $"{file.Module} {declaration.Name} proof at {proof.Span.Start}"

            corpusTest "all modules render, and every declaration has its anchor"
            <| fun () ->
                for file in project.Value.Files do
                    let html = page file
                    let anchors = Anchors.forFile file |> Map.toList |> List.map snd
                    Expect.equal (List.distinct anchors).Length anchors.Length $"{file.Module} anchors are unique"

                    for anchor in anchors do
                        Expect.stringContains html ("id=\"" + Source.encode anchor + "\"") $"{file.Module}#{anchor}"

            corpusTest "LinkValidator passes, and every data-ref has a card"
            <| fun () ->
                let root, result = built.Value

                Expect.equal
                    (result.Diagnostics
                     |> List.filter (fun diagnostic -> diagnostic.Code <> "rocq/client-missing")
                     |> List.map (fun diagnostic -> diagnostic.Code + ": " + diagnostic.Message))
                    []
                    "no diagnostics"

                for file in project.Value.Files do
                    let cards =
                        File.ReadAllText (Path.Combine (root, "output", "data", $"{file.Module}.symbols.json"))

                    for key in Cards.keysIn (page file) do
                        Expect.stringContains cards $"\"{key}\"" $"{file.Module}: {key}"

            corpusTest "every Guarantees entry matches an independent scan of the comments"
            <| fun () ->
                let links =
                    {
                        Module = fun name -> $"/{name}.html"
                        Pipeline = Docs.pipeline []
                    }

                let found =
                    Guarantees.entries project.Value links (config ()) None
                    |> Array.map (fun entry -> entry.Title, Regex.Replace (entry.Href.Substring (entry.Href.IndexOf '#' + 1), "~\d+$", ""))
                    |> Array.toList
                    |> List.sort

                let scanned =
                    project.Value.Files
                    |> Array.toList
                    |> List.collect scanTitles
                    |> List.sort

                Expect.isNonEmpty scanned "the scan finds entries"
                Expect.equal found scanned "the same titles and names"
        ]
