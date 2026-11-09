---
title: The Tally project
---

Tally is the small Rocq project behind every example here. It lives in `docs/site/example`, compiled with Rocq 9.1.0. Its `.glob` files and `facts.json` are committed, so the site builds without Rocq, and `generate.sh` recompiles it.

| File | Module | On the site |
|---|---|---|
| `theories/Core.v` | `Tally.Core` | [Page]({{base}}example/Tally.Core.html) |
| `theories/Laws.v` | `Tally.Laws` | [Page]({{base}}example/Tally.Laws.html) |
| `theories/Prose.v` | `Tally.Prose` | [Page]({{base}}example/Tally.Prose.html) |

## _CoqProject

```text
-Q theories Tally

theories/Core.v
theories/Laws.v
theories/Prose.v
```

## Core.v

<!-- rocq: theories/Core.v -->

## Laws.v

<!-- rocq: theories/Laws.v -->

## Prose.v

<!-- rocq: theories/Prose.v -->
