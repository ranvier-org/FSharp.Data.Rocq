---
title: Navigator Nacara plugin
description: Publishing static interactive Rocq proof sites, hover cards, search, and assumption badges with Nacara.Plugin.Rocq.
---

# Navigator (`Nacara.Plugin.Rocq`)

`Nacara.Plugin.Rocq` turns a compiled `_CoqProject` into a static proof reader inside a Nacara site: one page per Rocq module, a **Guarantees** landing page, syntax-highlighted `rocq`/`coq` Markdown fences, and progressive client enhancements (hover type/doc cards, instant symbol search, reference panels, and persistent reader preferences). Every page reads, folds, and links cleanly even with JavaScript disabled.

## Installation

Add the Cloudsmith feed hosting `Nacara.Plugin.Rocq`'s `Xantham.*` dependency, then install the package:

```bash
dotnet nuget add source https://nuget.cloudsmith.io/shayanhabibi/xantham/v3/index.json --name xantham-cloudsmith
dotnet add package Nacara.Plugin.Rocq --prerelease
```

## Registering a proof collection in Nacara

Navigator can serve either as an entire standalone proof site or as a sub-route collection (for example `/proofs/` or `/example/`) alongside ordinary Nacara documentation collections:

```fsharp
open Nacara.Core
open Nacara.Plugins

let rocqOptions =
    Rocq.defaults
    |> Rocq.project "proofs/_CoqProject"
    |> Rocq.facts (Some "proofs/facts.json")
    |> Rocq.config (Some "proofs/navigator.json")
    |> Rocq.root "proofs"
    |> Rocq.home (Some ("Documentation", baseUrl))

Site.create "My Library"
|> Site.baseUrl baseUrl
|> Rocq.register rocqOptions
|> Site.collection (
    Rocq.collection "proofs" rocqOptions
    |> Collection.layout (Rocq.reader rocqOptions)
)
```

### `Rocq` configuration functions

| Function | Default | Effect |
| :--- | :--- | :--- |
| `Rocq.project` | `"_CoqProject"` | Path to the compiled Rocq project file |
| `Rocq.root` | Site root (`""`) | URL route prefix for all generated module and Guarantees pages |
| `Rocq.facts` | `None` | Optional path to `facts.json` produced by `rocq-capture` |
| `Rocq.config` | `None` | Optional path to `navigator.json` |
| `Rocq.dropStaleFacts` | `false` | Drops facts for changed `.v` files instead of failing with `rocq/facts-stale` |
| `Rocq.home` | `None` | `(label, url)` link back to the parent documentation site |
| `Rocq.extensions` | `None` | Extra Markdig extensions enabled in `(**md *)` comments alongside pipe tables |

## `navigator.json` options

`navigator.json` is optional, as is every key inside it:

```json
{
  "falsifiedSuffix": "_falsified",
  "checkMarker": "@checked",
  "captureImports": ["Tally.Core"],
  "collapseCodeOver": 8
}
```

| Key | Effect |
| :--- | :--- |
| `falsifiedSuffix` | Theorem name suffix categorizing counterexample/disproof theorems under **Falsified** on the Guarantees page |
| `checkMarker` | Substring in an attached comment categorizing property-checked theorems under **Checks** |
| `captureImports` | Modules imported prior to `Check` queries during `rocq-capture` so user notations print naturally |
| `collapseCodeOver` | Maximum line count shown expanded by default for definition bodies; longer bodies fold after line 1 |

## Capturing types and axiom assumptions (`facts.json`)

`.glob` files do not contain inferred Gallina types or transitive axiom dependencies, and a normal Nacara site build never invokes Rocq. Instead, an optional offline command queries `rocq` over compiled `.vo` files and writes `facts.json`:

```bash
dotnet run -- rocq-capture --rocq "rocq" --out facts.json
```

- A site builds without `facts.json` (simply omitting inferred binder types and axiom assumption badges).
- `--rocq` is split on whitespace, so wrapper commands (such as `opam exec -- rocq` or WSL invocations) work directly.
- `Print Assumptions` walks each theorem's full kernel dependency graph; `rocq-capture` parallelizes queries across `--jobs <n>` worker processes (defaulting to CPU count, capped at `8`).

## Rocq syntax highlighting in Markdown

Registering `Rocq.register` enables syntax highlighting for fenced ```` ```rocq ```` and ```` ```coq ```` code blocks across **every** Markdown page in the Nacara site using the library's own nested-comment-aware Rocq lexer:

```coq
(* Comments nest cleanly, and string literals may contain "*)". *)
Theorem add_zero : forall n : nat, n + 0 = n.
Proof.
  induction n; simpl.
  - reflexivity.
  - rewrite IHn. reflexivity.
Qed.
```

## Reader settings

Generated Navigator pages include a responsive **Settings** control (a popover on desktop pointers, a side panel on tablets, and a modal dialog on phones) whose preferences apply immediately and persist in `localStorage`:

| Setting | Choices | Default |
| :--- | :--- | :--- |
| **Look** | `Family`, `Journal` | `Family` |
| **Mode** | `Auto`, `Light`, `Dark` | `Auto` |
| **Definitions** | `Open`, `Closed`, `Close long ones` (line threshold) | Configured `collapseCodeOver` |
| **Proofs** | `Closed`, `Open` | `Closed` |
| **Imports** | `Closed`, `Open` | `Closed` |
| **References** (above 900 px) | `Pinned`, `Badges`, `Under definitions` | `Pinned` |
| **References open** (under definitions) | `Closed`, `Open` | `Closed` |
| **Arguments** | `Names only`, `With types`, `In the panel` | `Names only` |

## HTML minification and build diagnostics

Navigator marks client hydration islands with HTML comments. Default `Nuglify.minifyHtml` strips HTML comments, which would disable hover cards, search, settings, and live reference panels—so Navigator detects stripped markers and fails the build with `rocq/hydration-markers-stripped`. Preserve comments when minifying:

```fsharp
|> Nuglify.minifyHtmlWith (Nuglify.htmlRemoveComments false)
```

| Diagnostic Code | Meaning |
| :--- | :--- |
| `rocq/project-load-failed` | `_CoqProject` could not be loaded (typically a missing or stale `.glob` file) |
| `rocq/facts-stale` | `facts.json` was captured from an older `.v` digest (use `Rocq.dropStaleFacts true` or re-run `rocq-capture`) |
| `rocq/argument-unmatched` | A `- [name:]` doc entry does not match any binder of the declaration (fails `--strict` builds) |
| `rocq/hydration-markers-stripped` | An HTML minifier removed Navigator's hydration comment markers |
