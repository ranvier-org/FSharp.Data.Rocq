module RocqData.Program

open System
open RocqData.Project

let private usage =
    """rocq-data extract --project <_CoqProject> --out <dir> [options]

Reads each .v and .glob named by the project and writes structured data to <dir>.
Compile the project first; reading the output needs no Rocq.

Options:
  --rocq-dep <file>      output of `rocq dep -f _CoqProject`, recorded as index.fileGraph.rocqDep
  --rocq-version <text>  recorded as index.rocqVersion
  --allow-stale          keep lexical data for a file with a stale .glob, instead of refusing
  --strict               exit 3 on an unknown .glob kind, as well as on an unparsed line
  --force                re-extract every file

Exit codes: 0 success, 1 usage, 2 refused (stale or missing .glob), 3 .glob lines not understood."""

let rec private parseArgs (args: string list) (o: Options) =
    match args with
    | [] -> Some o
    | "--project" :: v :: rest -> parseArgs rest { o with Project = v }
    | "--out" :: v :: rest -> parseArgs rest { o with OutDir = v }
    | "--rocq-dep" :: v :: rest -> parseArgs rest { o with RocqDep = Some v }
    | "--rocq-version" :: v :: rest -> parseArgs rest { o with RocqVersion = Some v }
    | "--allow-stale" :: rest -> parseArgs rest { o with AllowStale = true }
    | "--strict" :: rest -> parseArgs rest { o with Strict = true }
    | "--force" :: rest -> parseArgs rest { o with Force = true }
    | _ -> None

[<EntryPoint>]
let main argv =
    let empty =
        {
            Project = null
            OutDir = null
            RocqDep = None
            RocqVersion = None
            AllowStale = false
            Strict = false
            Force = false
        }

    match List.ofArray argv with
    | "extract" :: rest ->
        match parseArgs rest empty with
        | Some o when not (isNull o.Project) && not (isNull o.OutDir) ->
            let report = run o

            for r in report.Refused do
                eprintfn "refused: %s" r

            for KeyValue (kind, count) in report.UnknownKinds do
                eprintfn "unknown .glob kind: %s (%d lines), recorded in globLines.unknownKinds" kind count

            if report.UnparsedLines > 0 then
                eprintfn "%d .glob lines matched no known form, recorded in globLines.unparsed" report.UnparsedLines

            printfn
                "extracted %d, reused %d; extract %d ms, index %d ms, total %d ms"
                report.Extracted.Length
                report.Reused.Length
                report.ExtractMs
                report.GlobalMs
                report.TotalMs

            report.ExitCode
        | _ ->
            eprintfn "%s" usage
            1
    | _ ->
        eprintfn "%s" usage
        1
