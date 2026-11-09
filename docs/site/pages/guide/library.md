---
title: Library
---

`FSharp.Data.Rocq` has no dependencies beyond `FSharp.Core`. The examples on this page run against the [Tally](example.md) project.

```bash
dotnet add package FSharp.Data.Rocq --prerelease
```

```fsharp
open FSharp.Data.Rocq.Project
open FSharp.Data.Rocq.Structure
```

## What a project holds

| Value | Holds |
|---|---|
| `RocqProject.Files` | Each file's digests, parsed `.glob` and `Model` |
| `RocqFile.Model` | Tokens, sentences, comments, headings, declarations, scopes, imports, notations, symbols, binders, references and diagnostics |
| `RocqProject.Declarations`, `Symbols` | Those tables numbered across the project |
| `RocqProject.CrossReferences` | Mentions between declarations and symbols |
| `RocqProject.FileGraph` | Dependencies between files |

An index or string field that can be absent is an `option`.

## Declarations and proofs

`RocqProject.Declarations` lists every declaration in project order. `Category` is a closed union, so a match covers the kinds you care about.

<!-- source: declarations -->

<!-- output: declarations -->

## Source text

Spans are half-open byte offsets into `Model.Bytes`. A span gives back the exact bytes of the `.v` file, so a statement read this way is the statement as written.

<!-- source: text -->

The result for `total_append`:

<!-- output: text -->

## Comments and headings

A comment records its format and the declaration it documents. These are the first comments of `Laws.v`, with its headings after them:

<!-- source: comments -->

<!-- output: comments -->

`AttachedTo` is the link the navigator uses to place documentation beside a declaration. [Prose syntax](prose.md) shows how each format renders.

## Stale files

A `.glob` records the digest of the source it came from. Editing a `.v` without compiling makes the two disagree, and references would then point at the wrong bytes. So `load` refuses, and `loadWith { AllowStale = true }` keeps the lexical data instead.

This example copies the project and adds one comment to `Core.v`:

<!-- source: stale -->

<!-- output: stale -->

The stale file keeps its tokens, sentences and comments, and has no symbols or references.
