# rocq-data

Reads a compiled Rocq project's `.v` and `.glob` files and writes structured JSON for renderers and other tools. It keeps
every fact those files give, and consumers choose what to read. See [docs/SCHEMA.md](docs/SCHEMA.md).

## Use

Compile the project first, so each `.v` has a `.glob`. Then:

```bash
dotnet run -c Release --project src/RocqData -- extract --project path/to/_CoqProject --out out
```

The extractor needs the .NET 10 SDK, and no Rocq. A run re-extracts only files whose `.v` or `.glob` changed.

| Option | Effect |
|---|---|
| `--rocq-dep <file>` | Records the output of `rocq dep -f _CoqProject` as `index.fileGraph.rocqDep` |
| `--rocq-version <text>` | Records the Rocq version |
| `--allow-stale` | Keeps lexical data for a file with a stale `.glob`, instead of refusing |
| `--strict` | Exits 3 on an unknown `.glob` kind, as well as on an unparsed line |
| `--force` | Re-extracts every file |

Exit codes: 0 success, 1 usage, 2 refused (stale or missing `.glob`), 3 `.glob` lines not understood.

A sample consumer shares no code with the extractor:

```bash
dotnet run -c Release --project samples/RocqData.Sample -- who-uses out some_lemma
```

`show`, `measure` and `formats` are its other commands. `samples/measure.mjs` is the same idea in plain JavaScript.

## Test

```bash
dotnet run -c Release --project tests/RocqData.Tests
```

This runs against the committed fixture (`tests/RocqData.Tests/fixtures`), compiled with Rocq 9.1.0, and needs no Rocq.
`fixtures/generate.sh` recompiles it. To add a real project:

| Variable | Value |
|---|---|
| `ROCQDATA_CORPUS` | Path of its `_CoqProject`, compiled |
| `ROCQDATA_ORACLE` | Directory of `rocq compile -time-file` outputs, one `<path with / as _>.time` per file |
| `ROCQDATA_DEP` | Output of `rocq dep -f _CoqProject` |

## Design decisions

**F#, no packages.** It builds on the .NET SDK alone and fits F# consumers. The tests add Expecto. The scanner is
hand-written over bytes, following `parsing/cLexer.ml`. Comment and string nesting and the between-commands bullet rule are
context-sensitive, and exact byte offsets are the product, so a combinator library would add a layer without removing
those rules.

**Columnar JSON.** Measured on the largest file's references (4,675 entries), reading them into arrays:

| Format | Bytes | gzip | Read, median |
|---|---|---|---|
| JSON, one object per reference | 590,797 | 55,528 | 6.4 ms |
| **JSON, columnar with a target table** | **135,212** | **42,248** | **1.9 ms** |
| Binary prototype (varints, target table) | 51,376 | 21,448 | 0.2 ms |

Binary would be about 2.6 times smaller and 7 times faster to read, a saving of under 2 ms per file. It would also cost
every consumer a decoder. Browsers parse JSON natively, in about 0.2 ms for this file under Node. Binary can be added
later as a derived layer if the numbers change.

**Three layers per file, plus an index and a graph.** The tokens layer is more than half the output, and most queries never
open it.

**Global tables always rebuild from the outline shards.** Fresh and reused files take the same code path, so an incremental
run writes the same bytes as a full one. A test checks this.

**Incremental key:** MD5 of the `.v`, MD5 of the `.glob`, schema version and generator version. The `.glob` is part of the
key because a change upstream alters it while the `.v` stays the same.

## Performance

Windows 11, .NET 10, 15 files, 295,719 bytes of `.v` and 1,603,561 bytes of `.glob`. Process wall time, the median of 11
runs:

| Run | Time |
|---|---|
| Full extraction | 310 ms |
| Nothing changed | 215 ms |
| One small file changed | 246 ms |
| One large file changed (60 KB) | 301 ms |

Startup and the rebuild of the global tables dominate, so incremental runs save little at this size.

Output: 4,350,921 bytes in 47 files (1,294,099 gzip).

| Part | Bytes |
|---|---|
| `index.json` | 129,998 |
| `xref.json` | 178,772 |
| All outline layers | 940,195 |
| All refs layers | 815,055 |
| All tokens layers | 2,286,901 |

The largest file's layers: 220,112 outline, 135,212 refs, 458,110 tokens. Its `.v` is 60,034 bytes and its `.glob` 303,218.

Read times, the median of 31 calls in a warm process:

| Operation | .NET (`JsonDocument`) | Node (`JSON.parse`) |
|---|---|---|
| Parse `index.json` | 1.1 ms | 0.3 ms |
| Parse `xref.json` | 2.9 ms | 0.4 ms |
| Parse the largest outline | 3.0 ms | 0.7 ms |
| Parse the largest refs | 1.7 ms | 0.2 ms |
| Parse the largest tokens | 5.6 ms | 1.1 ms |
| Who-uses on loaded data (140 mentions) | 0.01 ms | 0.01 ms |
| Who-uses from nothing (load index and xref, answer) | 3.1 ms | |

A whole `who-uses` process, JIT included, took 73 ms in one run.

## Optional type layer: evaluated, not built

`.glob` holds no types. `rocq repl` can print them from the compiled project. Measured in WSL, in one process over all 15
files:

| Step | Time | Output |
|---|---|---|
| `Require` every module | 1.2 s | |
| ...and `Check @<name>` on all 1,018 symbols of a typed kind | 2.2 s | 179 KB, a type for all 1,018 |
| ...or `Print Assumptions` on all 234 theorems | 17.9 s | 13 KB |
| ...or `Print` on all 234 theorems, `Set Printing All` | 13.5 s | 897 MB |

So a type layer costs about 1 ms per symbol after loading, and it could read this project's `index.json` as its work list.
Three things need deciding first. Output depends on what is imported: with `Require` alone, names print qualified and
user notations stay off, while `Import` changes the text. Printed types are unstructured text and hold no spans. And it
needs Rocq and `.vo` files, so it must stay a separate step. The base data does not depend on it.

Raw proof terms are unusable as a layer at that size. `Print Assumptions` is a cheap, valuable extra.
