---
title: Library & cross-references
description: Querying declarations, proofs, byte spans, comments, cross-references, file graphs, and JSON extraction with FSharp.Data.Rocq.
---

# Library & cross-references

`FSharp.Data.Rocq` has no runtime dependencies beyond `FSharp.Core`. Its hand-written byte-level lexer (following Rocq's `parsing/cLexer.ml` rules for nested comments, strings, quotations, and context-sensitive bullets) pairs exact UTF-8 byte spans from `.v` files with symbol, binder, and reference tables from `.glob` files.

```bash
dotnet add package FSharp.Data.Rocq --prerelease
```

## Project model overview

| Value | Holds |
| :--- | :--- |
| `RocqProject.Files` | Each file's `.v` and `.glob` MD5 digests, `GlobStatus`, parsed `.glob` records, and `Model` |
| `RocqFile.Model` | `Bytes`, `Tokens`, `Sentences`, `Comments`, `Headings`, `Declarations`, `Scopes`, `Imports`, `Notations`, `Symbols`, `Binders`, `References`, and `Diagnostics` |
| `RocqProject.Declarations`, `Symbols` | Project-wide declaration and symbol tables indexed across all files |
| `RocqProject.CrossReferences` | Directed mentions between declarations/directives and project or external symbols |
| `RocqProject.FileGraph` | In-project `Requires`, `ExternalRequires`, and cross-file symbol `References` counts |

All spans (`Span`) are half-open UTF-8 byte ranges `[Start, End)` into `file.Model.Bytes`. Optional indices or strings are represented as F# `option` / `voption` values.

## Inspecting declarations, proofs, and source text

`RocqProject.Declarations` lists every declaration in project order, with a closed `Category` union (`Theorem`, `Goal`, `Definition`, `Inductive`, `Assumption`, `Notation`, `Ltac`, `Scheme`, `Other`) and each proof's `Ending` (`Qed`, `Defined`, `Admitted`, `Abort`, `Save`, `ProofTerm`, `Unterminated`):

```fsharp
open System.Text
open FSharp.Data.Rocq.Project
open FSharp.Data.Rocq.Source
open FSharp.Data.Rocq.Structure

let textOf (file: RocqFile) (span: Span) =
    Encoding.UTF8.GetString(file.Model.Bytes, span.Start, span.End - span.Start)

let inspectTheorems (project: RocqProject) =
    for entry in project.Declarations do
        let decl = entry.Declaration
        match decl.Category with
        | Category.Theorem ->
            let file = project.Files[entry.File]
            printfn
                "%-24s %-12s %d proof(s), %d doc comment(s)"
                (defaultArg decl.Name decl.Id)
                file.Module
                decl.Proofs.Length
                decl.Docs.Length

            match decl.Statement with
            | ValueSome statement -> printfn "  statement: %s" (textOf file statement)
            | ValueNone -> ()
        | _ -> ()
```

## Comments and headings

Every comment records its `Format` (`Doc` for `(** *)`, `Md` for `(**md *)`, or `Plain` for `(* *)`), its `Span` and inner `BodySpan`, and `AttachedTo: int option` indexing `file.Model.Declarations` when attached to a declaration directly below it:

```fsharp
let printCommentsAndHeadings (file: RocqFile) =
    for comment in file.Model.Comments do
        let attachedTo =
            comment.AttachedTo
            |> Option.bind (fun declIdx -> file.Model.Declarations[declIdx].Name)
        printfn "%-5s -> %s" (commentFormatName comment.Format) (defaultArg attachedTo "-")

    for heading in file.Model.Headings do
        printfn "H%d: %s" heading.Level heading.Text
```

## Cross-references and file dependencies

`.glob` files record what every resolved identifier and notation refers to. `FSharp.Data.Rocq.Project` joins those references across all modules in the `_CoqProject`:

- **`whoUses project name`**: Returns every `CrossReferenceEdge` mentioning `name` (`Owner` is `DeclarationOwner declIdx` or `FileOwner fileIdx`, `Where` indicates statement, body, proof, or top-level directive, and `Count` gives the mention count). When a short name is ambiguous, resolve candidates with `symbolsNamed` and query **`usesOf`** for a specific `Node`.
- **`usesBy project owner`**: Lists every symbol referenced by `DeclarationOwner declIdx` or `FileOwner fileIdx`, distinguishing in-project `SymbolNode` targets from external `ExternalSymbolNode` targets (`Module`, `Qualified`, `Kind`).
- **`project.FileGraph`**: Exposes `(fromFile, toFile)` in `Requires`, `(fromFile, moduleName)` in `ExternalRequires`, and `{ From; To; Count }` in `References`.

```fsharp
let printWhoUses (project: RocqProject) (name: string) =
    for edge in whoUses project name do
        match edge.Owner with
        | DeclarationOwner idx ->
            let owner = project.Declarations[idx]
            let ownerName = defaultArg owner.Declaration.Name owner.Declaration.Id
            printfn "%-12s %-24s %d in %A" project.Files[owner.File].Module ownerName edge.Count edge.Where
        | FileOwner idx ->
            printfn "top level of %s" project.Files[idx].Path

let printDependencies (project: RocqProject) (declName: string) =
    let idx =
        project.Declarations
        |> Array.findIndex (fun entry -> entry.Declaration.Name = Some declName)

    for edge in usesBy project (DeclarationOwner idx) do
        match edge.Target with
        | SymbolNode symbol ->
            printfn "project: %s" project.Symbols[symbol].Symbol.Name
        | ExternalSymbolNode ext ->
            let node = project.CrossReferences.Externals[ext]
            printfn "external: %s" node.Qualified
```

## Stale `.glob` handling

The first line of every `.glob` file stores the MD5 digest of the `.v` source that produced it. If a `.v` file is edited without recompiling, `.glob` byte offsets would no longer align with source tokens:

- `load "_CoqProject"` refuses the load and returns every `LoadFailure` (printable via `describeFailure`).
- `loadWith { AllowStale = true } "_CoqProject"` succeeds, marking the edited file `GlobStatus.Stale`—preserving its tokens, sentences, declarations, and comments while omitting stale `.glob` symbols and references.

## Extracting columnar JSON from F# or the CLI

Non-`.NET` tools and browser UIs can consume the exact same model as deterministic JSON via `FSharp.Data.Rocq.Extract.run` or `FSharp.Data.Rocq.Cli`:

```fsharp
open FSharp.Data.Rocq.Extract

let summary =
    run {
        Project = "proofs/_CoqProject"
        OutDir = "out"
        RocqDep = None
        RocqVersion = Some "9.1.0"
        AllowStale = false
        Strict = true
        Force = false
    }
```

Or from the command line:

```bash
dotnet run -c Release --project src/FSharp.Data.Rocq.Cli -- extract --project proofs/_CoqProject --out out
```

| CLI Flag | Effect |
| :--- | :--- |
| `--rocq-dep <file>` | Embeds `rocq dep -f _CoqProject` output as `index.fileGraph.rocqDep` |
| `--rocq-version <text>` | Records the compiler version string in `index.json` |
| `--allow-stale` | Writes lexical data with `globStatus: "stale"` instead of refusing on stale `.glob` |
| `--strict` | Exits `3` on unknown `.glob` symbol kinds as well as unparsed `.glob` lines |
| `--force` | Rewrites all per-file shards even when `.v` and `.glob` digests are unchanged |

Exit codes: `0` success, `1` usage error, `2` refused (stale or missing `.glob`), `3` unparsed or strict-unknown `.glob` lines. See [Columnar JSON schema](schema.md) for the complete shard specification.
