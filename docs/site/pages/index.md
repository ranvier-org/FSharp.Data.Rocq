---
title: FSharp.Data.Rocq
description: Typed access to a compiled Rocq project from F#, and a static reader for its proofs.
layout: splash
---

<section class="rocq-hero">
<h1 class="rocq-hero__title">FSharp.Data.Rocq</h1>
<p class="rocq-hero__line">Typed access to a compiled <a href="https://rocq-prover.org">Rocq</a> project from F#, and a static reader for its proofs.</p>
<div class="rocq-hero__actions">
<a class="rocq-button rocq-button--primary" href="{{base}}guide/">Get started</a>
<a class="rocq-button" href="{{base}}example/index.html">Open the example site</a>
<a class="rocq-button" href="{{base}}guide/prose/">Comment syntax</a>
</div>
</section>

<div class="rocq-packages">
<a class="rocq-package" href="{{base}}guide/library/"><code class="rocq-package__name">FSharp.Data.Rocq</code><p>Sentences, declarations, proofs, comments, resolved references and file dependencies, as F# values or JSON. It reads <code>.v</code> and <code>.glob</code> files, and it does not need Rocq installed.</p></a>
<a class="rocq-package" href="{{base}}guide/navigator/"><code class="rocq-package__name">Nacara.Plugin.Rocq</code><p>A Nacara plugin that publishes a project as a site: a page per module, a Guarantees page, hover cards, search and two looks.</p></a>
</div>

## Install

Both packages target .NET 10 and are in preview, so `--prerelease` is required.

```bash
dotnet add package FSharp.Data.Rocq --prerelease
```

The Nacara plugin brings in the library. First add the feed that holds its `Xantham.*` dependency:

```bash
dotnet nuget add source https://nuget.cloudsmith.io/shayanhabibi/xantham/v3/index.json --name xantham-cloudsmith
```

```bash
dotnet add package Nacara.Plugin.Rocq --prerelease
```

## Everything here is real

A small Rocq project, [Tally](guide/example.md), is compiled and committed beside the site. Each build runs the code you see against it and places the output under the code. This is the example site, built by the plugin as part of this site:

<!-- frame: index.html -->
