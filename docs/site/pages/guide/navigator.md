---
title: Navigator
---

`Nacara.Plugin.Rocq` publishes a compiled project as a static site. Pages read, fold and link with JavaScript off, and the client adds hover cards, search and settings.

## Install

The package targets .NET 10 and is in preview. Add the feed that holds its `Xantham.*` dependency, then the package:

```bash
dotnet nuget add source https://nuget.cloudsmith.io/shayanhabibi/xantham/v3/index.json --name xantham-cloudsmith
```

```bash
dotnet add package Nacara.Plugin.Rocq --prerelease
```

## Add it to a site

This is the complete program that built the site on the [Site output](output.md) page:

<!-- source: site -->

The plugin also works beside other content. This documentation site is one Nacara site: the guide uses `Partas.Nacara.Theme`, and [the example]({{base}}example/index.html) is a collection with `Rocq.root "example"`, so its pages live under `/example/`:

```fsharp
let rocqOptions =
    Rocq.defaults
    |> Rocq.project "example/_CoqProject"
    |> Rocq.facts (Some "example/facts.json")
    |> Rocq.config (Some "example/navigator.json")
    |> Rocq.root "example"
    |> Rocq.home (Some ("Documentation", baseUrl))
```

## Options

| Function | Default | Effect |
|---|---|---|
| `Rocq.project` | `_CoqProject` | The project to read |
| `Rocq.root` | The site root | The route prefix of every page |
| `Rocq.facts` | None | The file written by `rocq-capture` |
| `Rocq.config` | None | The path of `navigator.json` |
| `Rocq.dropStaleFacts` | `false` | Drops the facts of a changed file, instead of failing with `rocq/facts-stale` |
| `Rocq.home` | None | A label and URL linking back to the host site |
| `Rocq.extensions` | None | Markdig extensions applied to comments, alongside pipe tables |

## navigator.json

The file is optional, and so is every key. The example project uses this one:

```json
{
  "falsifiedSuffix": "_falsified",
  "checkMarker": "@checked",
  "captureImports": ["Tally.Core"],
  "collapseCodeOver": 8
}
```

| Key | Effect |
|---|---|
| `falsifiedSuffix` | The name suffix marking a theorem that disproves a claim |
| `checkMarker` | The text marking a comment as a record of property checking |
| `captureImports` | The modules imported before capture, which select the notations Rocq prints |
| `collapseCodeOver` | The most lines a code body shows on load. A longer body starts closed, showing its first line |

The first line of every multi-line code body is a fold for the rest, so readers can open or close any of them. Bodies start open unless `collapseCodeOver` closes them.

## Types and assumption badges

A build never runs Rocq. Types and assumptions come from `facts.json`, which a separate command writes. It needs Rocq and the `.vo` files. A site builds without it, and shows no badges.

```bash
dotnet run -- rocq-capture --rocq "rocq" --out facts.json
```

The value of `--rocq` is split into words, so a wrapper works too. The example's facts were captured with Rocq 9.1.0 running in WSL, through `wsl.exe` and `opam exec`.

## Rocq code blocks

The plugin colours Markdown fences marked `rocq` or `coq` on any page of the site, with the lexer and keywords the library uses to read a source file:

```rocq
(* Comments nest, and a string can hold a closing mark: "*)". *)
Theorem add_zero : forall n : nat, n + 0 = n.
Proof.
  induction n; simpl.
  - reflexivity.
  - rewrite IHn. reflexivity.
Qed.
```

## Building the client

The plugin embeds its client bundle. A build from this repository needs the bundle first, and the script compiles it with [Fickl](https://github.com/shayanhabibi/fickl):

```bash
bash navigator/build-client.sh
```

## Minifying HTML

The default `Nuglify.minifyHtml` removes the comments that mark hydration roots. Cards, search, the settings and the right pane's lists would stop working, so the build fails with `rocq/hydration-markers-stripped`. Keep the comments:

```fsharp
|> Nuglify.minifyHtmlWith (Nuglify.htmlRemoveComments false)
```

## Diagnostics

| Code | Meaning |
|---|---|
| `rocq/project-load-failed` | The project was refused, usually for a stale `.glob` |
| `rocq/facts-stale` | `facts.json` describes a file that changed |
| `rocq/argument-unmatched` | A `- [name:]` entry names no binder of the statement. It fails a `--strict` build |
| `rocq/hydration-markers-stripped` | A minifier removed the comments the client needs |
