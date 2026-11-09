---
title: JSON and the command line
---

For a consumer outside .NET, `Extract.run` writes the project as JSON. Reading the result needs neither Rocq nor this library. [JSON schema](schema.md) documents every field.

## From F#

<!-- source: extract -->

A run rewrites only the layers of files whose `.v` or `.glob` changed:

<!-- output: extract -->

The output for the example project:

<!-- output: extract-files -->

Read only what you need. A table of contents needs `index.json`, and a rendered page needs the three layers of its file.

## The shape of the index

<!-- source: json -->

<!-- output: json -->

## From the command line

`FSharp.Data.Rocq.Cli` calls the same function. Both of these ran during this build:

<!-- output: cli -->

A stale file stops the run with exit code 2, unless you allow it:

<!-- output: cli-stale -->

| Option | Effect |
|---|---|
| `--rocq-dep <file>` | Records the output of `rocq dep -f _CoqProject` as `index.fileGraph.rocqDep` |
| `--rocq-version <text>` | Records the Rocq version |
| `--allow-stale` | Keeps lexical data for a file with a stale `.glob`, instead of refusing |
| `--strict` | Exits 3 on an unknown `.glob` kind, as well as on an unparsed line |
| `--force` | Rewrites every file's layers |

Exit codes: 0 success, 1 usage, 2 refused (stale or missing `.glob`), 3 `.glob` lines not understood.
