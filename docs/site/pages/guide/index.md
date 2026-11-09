---
title: Getting started
---

## Install

Both packages target .NET 10 and are in preview, so `--prerelease` is required.

For the library:

```bash
dotnet add package FSharp.Data.Rocq --prerelease
```

For the Nacara plugin, which brings in the library, first add the feed that holds its `Xantham.*` dependency:

```bash
dotnet nuget add source https://nuget.cloudsmith.io/shayanhabibi/xantham/v3/index.json --name xantham-cloudsmith
```

```bash
dotnet add package Nacara.Plugin.Rocq --prerelease
```

## Compile the project

Both packages read what Rocq leaves behind, so compile first. Each `.v` needs a `.glob` beside it.

```bash
rocq compile -Q theories Tally theories/Core.v
```

## Load it

`load` takes the path of a `_CoqProject`. This is the program, and what it printed when this page was built:

<!-- source: load -->

<!-- output: load -->

`load` refuses a project with a stale or missing `.glob`, and returns every reason. See [Library](library.md#stale-files).

## Publish it

To turn the same project into a site, add the plugin to a Nacara site. [Navigator](navigator.md) has the details.

<!-- source: site -->

## Where to go next

| You want | Read |
|---|---|
| Declarations, proofs and source text in F# | [Library](library.md) |
| What uses what | [References and dependencies](references.md) |
| The data in another language | [JSON and the command line](json.md) |
| A reader for your proofs | [Navigator](navigator.md) |
| To write documentation in your `.v` files | [Prose syntax](prose.md) |
| Every type and function | <a href="{{base}}reference/">API reference</a> |
