---
title: FSharp.Data.Rocq
---

Typed access to a compiled [Rocq](https://rocq-prover.org) project from F#, and a static reader for its proofs.

The repository holds two packages:

| Package | What you get |
|---|---|
| `FSharp.Data.Rocq` | Sentences, declarations, proofs, comments, resolved references and file dependencies, as F# values or JSON. It reads `.v` and `.glob` files, and it does not need Rocq installed. |
| `Nacara.Plugin.Rocq` | A [Nacara](https://github.com/shayanhabibi/Nacara) plugin that publishes a project as a site: a page per module, a Guarantees page, hover cards, search and two looks. |

Everything in this documentation is real. A small Rocq project, [Tally](guide/example.md), is compiled and committed beside the site. Each build runs the code you see against it and places the output under the code.

- [Start with the guide](guide/index.md)
- [Open the example site]({{base}}example/index.html), built by the plugin as part of this site
- [Read the comment syntax](guide/prose.md)

<!-- frame: index.html -->
