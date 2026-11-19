---
title: Rocq proofs & Navigator
description: Typed F# access to compiled Rocq theories (.v and .glob) and static interactive proof site generation for Nacara.
---

# `FSharp.Data.Rocq` & `Nacara.Plugin.Rocq`

`FSharp.Data.Rocq` provides typed F# access to compiled Rocq projects—sentences, declarations, proofs, doc/Markdown comments, resolved symbol references, and cross-file dependencies—by reading `.v` source files and `.glob` compiler outputs without requiring a Rocq installation at read time. It also exports deterministic columnar JSON layers for browsers and non-`.NET` consumers, and powers **`Nacara.Plugin.Rocq`** (**Navigator**), which turns compiled Rocq theories into interactive static proof sites with hover cards, symbol search, assumption badges, and a **Guarantees** dashboard.

## Packages

Both packages target `.NET 10` (`net10.0`) and are published in preview (`--prerelease`):

- **`FSharp.Data.Rocq`**: Zero-dependency (`FSharp.Core`-only) `.v` + `.glob` lexer, structural analyzer, cross-reference graph, and incremental columnar JSON extractor (`Extract.run`).
- **`Nacara.Plugin.Rocq`**: Nacara static-site plugin that renders a page per Rocq module, a **Guarantees** index of titled theorems, `rocq`/`coq` syntax-highlighted Markdown code blocks, and progressive client hydration for hover cards, search, and reader settings.

```bash
# Core F# library:
dotnet add package FSharp.Data.Rocq --prerelease

# Nacara static proof site plugin (requires the Xantham dependency feed):
dotnet nuget add source https://nuget.cloudsmith.io/shayanhabibi/xantham/v3/index.json --name xantham-cloudsmith
dotnet add package Nacara.Plugin.Rocq --prerelease
```

## Quick start: reading a compiled `_CoqProject`

Compile your Rocq theories first so every `.v` file has a matching `.glob` file alongside it, then load the project from F#:

```fsharp
open FSharp.Data.Rocq.Project
open FSharp.Data.Rocq.Structure

match load "proofs/_CoqProject" with
| Ok project ->
    for d in project.Declarations do
        if d.Declaration.Category = Category.Theorem then
            printfn "%s: %d proofs" d.Declaration.Id d.Declaration.Proofs.Length

    for edge in whoUses project "some_lemma" do
        match edge.Owner with
        | DeclarationOwner i -> printfn "used by %s" project.Declarations[i].Declaration.Id
        | FileOwner i -> printfn "used at the top level of %s" project.Files[i].Path
| Error failures ->
    for failure in failures do
        eprintfn "%s" (describeFailure failure)
```

`load` verifies each `.glob` file's embedded source MD5 digest against its `.v` file and refuses stale or missing `.glob` outputs with structured reasons; `loadWith { AllowStale = true }` retains lexical data (tokens, sentences, comments) for stale files while omitting stale `.glob` references.

## Quick start: embedding Navigator in a Nacara site

```fsharp
let options =
    Rocq.defaults
    |> Rocq.project "proofs/_CoqProject"
    |> Rocq.facts (Some "facts.json")
    |> Rocq.config (Some "navigator.json")

Site.create "Proofs"
|> Rocq.register options
|> Site.collection (
    Rocq.collection "proofs" options
    |> Collection.layout (Rocq.reader options)
)
```

Explore a live rendered Navigator example on the [Tally sample proof site](https://ranvier-org.github.io/FSharp.Data.Rocq/example/).

## Guides in this section

- **[F# library & cross-references](guide/library.md)**: `RocqProject` and `RocqFile.Model` tables, exact UTF-8 byte spans, comment attachment, cross-reference queries (`whoUses`, `usesOf`, `usesBy`), `FileGraph`, stale-file handling, and CLI JSON extraction.
- **[Navigator Nacara plugin](guide/navigator.md)**: `Nacara.Plugin.Rocq` options, `navigator.json`, capturing types and axiom assumptions (`facts.json`), reader settings, HTML minification rules, and build diagnostics.
- **[Prose syntax & Guarantees page](guide/prose-and-guarantees.md)**: Authoring `(** *)` doc comments and `(**md *)` Markdown comments, `- [arg:]` binder documentation, comment attachment rules, and curating the **Guarantees**, **Checks**, **Falsified**, and **Open** theorem index.
- **[Columnar JSON schema (v1.0)](guide/schema.md)**: Complete reference for `index.json`, `xref.json`, and per-module `outline.json`, `refs.json`, and `tokens.json` shards, invariants, and `.glob` edge semantics.
