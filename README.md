# FSharp.Data.Rocq

Typed access to a compiled Rocq project from F#: sentences, declarations, proofs, comments, resolved references and
cross-file dependencies. It reads `.v` and `.glob` files, and it needs the compiled output of Rocq but not Rocq itself.
It can also write the same data as JSON, for browsers and other languages.

## Use

Compile the project first, so each `.v` has a `.glob`. Then:

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

`load` refuses a project when a `.glob` is stale or missing, and returns every reason. `loadWith { AllowStale = true }`
keeps lexical data for a stale file instead.

| Value | Holds |
|---|---|
| `RocqProject.Files` | Each file's digests, parsed `.glob` and `Model` |
| `RocqFile.Model` | Tokens, sentences, comments, headings, declarations, scopes, imports, notations, symbols, binders, references and diagnostics |
| `RocqProject.Declarations`, `Symbols` | Those tables numbered across the project |
| `RocqProject.CrossReferences` | Mentions between declarations and symbols; read with `usesOf`, `usesBy` or `whoUses` |
| `RocqProject.FileGraph` | Dependencies between files |

Spans are half-open byte offsets into `Model.Bytes`. An index or string field that can be absent is an `option`. The JSON
writes `None` as `-1` on index fields and `null` on string fields.

## Write JSON

For consumers outside .NET, `Extract.run` writes the project as JSON, and the command line calls it:

```bash
dotnet run -c Release --project src/FSharp.Data.Rocq.Cli -- extract --project path/to/_CoqProject --out out
```

[docs/SCHEMA.md](docs/SCHEMA.md) documents every field. A run rewrites only the layers of files whose `.v` or `.glob` changed.

| Option | Effect |
|---|---|
| `--rocq-dep <file>` | Records the output of `rocq dep -f _CoqProject` as `index.fileGraph.rocqDep` |
| `--rocq-version <text>` | Records the Rocq version |
| `--allow-stale` | Keeps lexical data for a file with a stale `.glob`, instead of refusing |
| `--strict` | Exits 3 on an unknown `.glob` kind, as well as on an unparsed line |
| `--force` | Rewrites every file's layers |

Exit codes: 0 success, 1 usage, 2 refused (stale or missing `.glob`), 3 `.glob` lines not understood.

A sample JSON consumer shares no code with the library:

```bash
dotnet run -c Release --project samples/FSharp.Data.Rocq.Sample -- who-uses out some_lemma
```

`show`, `measure` and `formats` are its other commands. `samples/measure.mjs` is the same idea in plain JavaScript.

## Navigator

`navigator/Nacara.Plugin.Rocq` is a [Nacara](https://github.com/shayanhabibi/Nacara) plugin that turns a compiled project into a
static reader: a page per module, a Guarantees page, hover cards, search and two looks. Pages read, fold and link with
JavaScript off. A site adds these lines:

```fsharp
let options =
    Rocq.defaults
    |> Rocq.project "proofs/_CoqProject"
    |> Rocq.facts (Some "facts.json")
    |> Rocq.config (Some "navigator.json")

Site.create "Proofs"
|> Rocq.register options
|> Site.collection (Rocq.collection "proofs" options |> Collection.layout (Rocq.reader options))
```

`navigator.json` is optional. Its keys are `falsifiedSuffix`, `checkMarker` and `captureImports`.

Types and assumption badges come from `facts.json`, written by a separate command that needs Rocq and the `.vo` files. A
site builds without it, and shows no badges:

```bash
dotnet run -- rocq-capture --rocq "rocq" --out facts.json
```

Before building the plugin itself, run `navigator/build-client.sh`, which compiles the client bundle the plugin embeds.

**Minifying HTML:** default `Nuglify.minifyHtml` removes the comments that mark hydration roots. The page stays readable and
foldable, but cards, search and the look switch stop working. Keep the comments:

```fsharp
|> Nuglify.minifyHtmlWith (Nuglify.htmlRemoveComments false)
```

The acceptance suite runs on the committed fixture:

```bash
cd navigator/browser
npm ci
npm run site -- --strict
npx playwright test
```

For a real project, build its site with `NAVIGATOR_BASE_URL=/sub/path/`. Then set `NAVIGATOR_SITE` to its output and
`NAVIGATOR_CORPUS=1` for Playwright, and `FSHARP_DATA_ROCQ_CORPUS` to its `_CoqProject` for `Rocq.Navigator.Tests`.

## Test

```bash
dotnet run -c Release --project tests/FSharp.Data.Rocq.Tests
```

This runs against the committed fixture (`tests/FSharp.Data.Rocq.Tests/fixtures`), compiled with Rocq 9.1.0, and needs no Rocq.
`fixtures/generate.sh` recompiles it. To add a real project:

| Variable | Value |
|---|---|
| `FSHARP_DATA_ROCQ_CORPUS` | Path of its `_CoqProject`, compiled |
| `FSHARP_DATA_ROCQ_ORACLE` | Directory of `rocq compile -time-file` outputs, one `<path with / as _>.time` per file |
| `FSHARP_DATA_ROCQ_DEP` | Output of `rocq dep -f _CoqProject` |

## Format

Fantomas is pinned in `.config/dotnet-tools.json`, and its output differs between versions. Use the pinned one:

```bash
dotnet tool restore
```

```bash
dotnet fantomas --check src tests samples
```

## Publish

[`publish.yml`](.github/workflows/publish.yml) checks formatting, tests and packs on every push and pull request. A `v*` tag
also publishes to nuget.org through trusted publishing, so the repository holds no API key. The tag must equal `v` plus
`Version` in the library's project file:

```bash
git tag v0.1.0-preview.1
```

```bash
git push origin v0.1.0-preview.1
```

## Design decisions

**Built rather than adopted.** No existing tool provides this data; see [docs/PRIOR-ART.md](docs/PRIOR-ART.md).

**The typed model is the source, and JSON is written from it.** A .NET consumer calls the library directly and never
touches JSON. The JSON writers read the same `RocqProject`, and a test checks that the two agree.

**F#, no packages.** It builds on the .NET SDK alone. The tests add Expecto. The scanner is hand-written over bytes,
following `parsing/cLexer.ml`. Comment and string nesting and the between-commands bullet rule are context-sensitive, and
exact byte offsets are the product, so a combinator library would add a layer without removing those rules.

**Unions for closed sets, strings for open ones.** Categories, proof endings and similar fields are unions. `.glob` symbol
kinds stay strings, because a new Rocq can add kinds, and unknown ones are recorded instead of rejected.

**Columnar JSON.** Measured on the largest file's references (4,675 entries), reading them into arrays:

| Format | Bytes | gzip | Read, median |
|---|---|---|---|
| JSON, one object per reference | 590,797 | 55,528 | 6.4 ms |
| **JSON, columnar with a target table** | **135,212** | **42,248** | **1.9 ms** |
| Binary prototype (varints, target table) | 51,376 | 21,448 | 0.2 ms |

Binary would be about 2.6 times smaller and 7 times faster to read, a saving of under 2 ms per file. It would also cost
every non-.NET consumer a decoder. Browsers parse JSON natively, in about 0.2 ms for this file under Node.

**Three JSON layers per file, plus an index and a graph.** The tokens layer is more than half the output, and most queries
never open it.

**Every run analyses every file.** The per-file key (MD5 of the `.v`, MD5 of the `.glob`, schema version and generator
version) only decides which layers to rewrite. The global tables always come from fresh models, so an incremental run
writes the same bytes as a full one. A test checks this. The `.glob` is part of the key because a change upstream alters it
while the `.v` stays the same.

## Performance

Windows 11, .NET 10, 15 files, 295,719 bytes of `.v` and 1,603,561 bytes of `.glob`.

Library, in a process under the `tr-TR` culture, the median of 31 calls:

| Operation | Time |
|---|---|
| `load`, first call | 114 ms |
| `load`, warm | 89 ms |

Command line, process wall time, the median of 11 runs:

| Run | Time |
|---|---|
| Full extraction | 312 ms |
| Nothing changed | 250 ms |
| One small file changed | 249 ms |
| One large file changed (60 KB) | 268 ms |

Startup and analysing every file dominate, so incremental runs save little at this size.

JSON output: 4,350,928 bytes in 47 files.

| Part | Bytes |
|---|---|
| `index.json` | 130,005 |
| `xref.json` | 178,772 |
| All outline layers | 940,195 |
| All refs layers | 815,055 |
| All tokens layers | 2,286,901 |

The largest file's layers: 220,112 outline, 135,212 refs, 458,110 tokens. Its `.v` is 60,034 bytes and its `.glob` 303,218.

JSON read times, the median of 31 calls in a warm process:

| Operation | .NET (`JsonDocument`) | Node (`JSON.parse`) |
|---|---|---|
| Parse `index.json` | 1.1 ms | 0.3 ms |
| Parse `xref.json` | 2.9 ms | 0.4 ms |
| Parse the largest outline | 3.0 ms | 0.7 ms |
| Parse the largest refs | 1.7 ms | 0.2 ms |
| Parse the largest tokens | 5.6 ms | 1.1 ms |
| Who-uses on loaded data (140 mentions) | 0.01 ms | 0.01 ms |
| Who-uses from nothing (load index and xref, answer) | 3.1 ms | |

## Optional type layer: evaluated, not built

`.glob` holds no types. `rocq repl` can print them from the compiled project. Measured in WSL, in one process over all 15
files:

| Step | Time | Output |
|---|---|---|
| `Require` every module | 1.2 s | |
| ...and `Check @<name>` on all 1,018 symbols of a typed kind | 2.2 s | 179 KB, a type for all 1,018 |
| ...or `Print Assumptions` on all 234 theorems | 17.9 s | 13 KB |
| ...or `Print` on all 234 theorems, `Set Printing All` | 13.5 s | 897 MB |

So a type layer costs about 1 ms per symbol after loading. Three things need deciding first. Output depends on what is
imported: with `Require` alone, names print qualified and user notations stay off, while `Import` changes the text. Printed
types are unstructured text and hold no spans. And it needs Rocq and `.vo` files, so it must stay a separate step. The base
data does not depend on it.

Raw proof terms are unusable as a layer at that size. `Print Assumptions` is a cheap, valuable extra.
