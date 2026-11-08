# Rocq navigator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

## Core and view state

**Core primitives used:**

| Primitive | Job | Task |
|---|---|---|
| `Signal` | Look, mode, hovered or focused reference, search text, search open, section in view | 10, 11, 12 |
| `createMemo` / `createMemoWithComparer` | Search results from the text; the card's symbol from the hovered reference, skipping unchanged symbols | 11, 12 |
| `createProjection` | Keyed rows of the search results | 12 |
| `createEffect` | Writes look and mode to the root element | 10 |
| `createRoot`, `Owner`, `onCleanup` | Lifetime of each hydrated island and its listeners | 10, 11, 12 |

**View-owned state:**

| State | Why the core cannot hold it |
|---|---|
| DOM nodes of the server-rendered article | DOM state; the article is static HTML |
| The one delegated `pointerover`/`focusin` listener on the article | Event delegation |
| Hover card position, measured from the reference's rectangle | The last value written to a DOM target |
| Server HTML | Written by the build |

Saved look and mode live in `localStorage`, which is browser storage. Anything beyond this list needs the owner's agreement first: raise it as a question naming the core primitive and the behaviour it lacks.

---

**Goal:** A .NET build that turns a compiled Rocq project into a static, public documentation site in two looks, with hover cards, search and doc-first reading.

**Architecture:** `Rocq.Navigator` reads `RocqProject` from `FSharp.Data.Rocq`, renders each page with `Server.renderDocument`, and writes a self-contained directory. The article is server-only markup placed with `t.Unsafe.html`. Four small islands (switch, section marker, card layer, search) hydrate from a Fable bundle, and a client failure leaves a readable page.

**Tech Stack:** F# on .NET 10, Fable 5.13 with Vite, `Ranvier.View`, `Ranvier.View.Dom`, `Ranvier.Wire`, `Ranvier.Css` with `.Bindings`, `.View` and `.Build`, optionally `Ranvier.View.Anime`, Markdig, Expecto, Playwright.

**Spec:** [`docs/superpowers/specs/2026-11-09-rocq-navigator-design.md`](../specs/2026-11-09-rocq-navigator-design.md). Read it, with its mockups, before starting any task.

## Global Constraints

- `src/FSharp.Data.Rocq` gains no packages. Its JSON output stays byte-identical.
- All navigator projects are `IsPackable=false` and live under `navigator/`.
- Do not edit Ranvier's `proofs/`, `src/Ranvier` or docs site. The Iris worktree is read only. `Ranvier.View` and `Ranvier.Css` sibling clones are reference reading only: use NuGet.
- Do not copy rocqnavi code or assets (GPL-2.0).
- Statement and proof text on the page is byte-exact from the `.v`.
- A missing fact shows as absent, never as a green badge. Open and Falsified entries never show a passing badge.
- Pages are readable, foldable and linked with JavaScript off.
- Page URLs are `<Module>.html`, anchors `#<declaration name>`, heading anchors `#h-<slug>`. All URLs in output are relative.
- Tests do not hardcode real proof names. They run on committed fixtures, with the proofs as an opt-in corpus through `FSHARP_DATA_ROCQ_CORPUS`.
- A filtered test run is not an acceptance check.
- F# work uses the `sagefs` loop: red and green in the REPL, persist, `hard_reset_fsi_session rebuild=true`, re-verify, then `dotnet` as the final gate. Report REPL friction. Do not restart the daemon. Stop sessions you create.
- Doc comments follow Ranvier's `.claude/rules/comments.md` and the `fsharp-xml-docs` skill. Format with `dotnet fantomas` (pinned 8.0.5).
- Shell commands use the `rtk` prefix. Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Nothing is pushed without the owner's request.
- `publish.yml` keeps its name and job shape: nuget.org's trusted publishing policy names it.

## Review Focus

1. **Awkward declaration names.** A name with a prime, a Unicode name, the same name in two sections, or no name at all. Each gets a unique working anchor, and a link to it lands on it. Pinned in Task 5.
2. **HTML-significant text in prose and code.** `s < fuel`, `<script>` and `&` in comments, and a printed type containing `<`. They display literally and execute nothing. Pinned in Tasks 3 and 4.
3. **Non-ASCII and CRLF source.** Spans over `µ`, `∀` and CRLF line ends. The rendered text equals the `.v` bytes. Pinned in Task 4.
4. **A browser that refuses things.** `localStorage` throwing, a data file failing to load, or opening a page in a subpath. The page stays readable, the switch still works for that page, and the card shows the reference's name instead of spinning. Pinned in Tasks 10, 11 and 13.
5. **Facts that do not match.** A `facts.json` naming a symbol the project no longer has, or a `Check` that errored for one symbol. That symbol falls back to its source header, and the rest of the facts still apply. Pinned in Task 2.

## File map

```text
navigator/
  Packages.props                     pinned versions, imported by each project
  Rocq.Navigator.Shared/             .NET and Fable
    Model.fs       Look, Mode, CardData, SearchEntry, wire codecs
    Tokens.fs      token names and values for 4 look/mode combinations
    Styles.fs      typed component styles and CssModule records
    Islands.fs     switch, marker, card layer and search components
  Rocq.Navigator/                    .NET CLI
    Config.fs      navigator.json
    Facts.fs       capture, facts.json, digest check
    Anchors.fs     page names, unique anchors, slugs
    Docs.fs        comment body to HTML
    Source.fs      source span to HTML with references
    Article.fs     sentences to article blocks and HTML
    Guarantees.fs  entries and groups
    Cards.fs       CardData and SearchEntry from the project and facts
    Page.fs        document shell, panes, island hosts
    Site.fs        writes the directory, checks links
    Program.fs     capture and build commands
  Rocq.Navigator.Client/             Fable then Vite
    Main.fs, main.js, vite.config.js, package.json
  Rocq.Navigator.Tests/              Expecto; fixtures/
  browser/                           Playwright acceptance
.github/workflows/navigator.yml
```

---

### Task 1: Pin the packages and prove islands on a static page

Everything later depends on this result. Record the outcome at the top of `navigator/Packages.props` in a comment, and report the pinned set and whether Anime made it in.

**Files:**
- Create: `navigator/Packages.props`, the four project files, `navigator/Rocq.Navigator/Program.fs`, `navigator/Rocq.Navigator.Shared/Islands.fs`, `navigator/Rocq.Navigator.Client/{Main.fs,main.js,vite.config.js,package.json}`, `navigator/Rocq.Navigator.Tests/{Main.fs,IslandTests.fs}`
- Modify: `FSharp.Data.Rocq.slnx`, `.config/dotnet-tools.json` (add `fable` 5.13.0)

**Interfaces:**
- Produces: the pinned set in `Packages.props`; `Page.hostAttribute = "data-island"`; the island mounting shape every later island uses.

- [ ] **Step 1: Try the Anime set first.** In `Packages.props`, pin `Ranvier.View`, `Ranvier.View.Dom` and `Ranvier.View.Anime` at 0.1.44 with `Ranvier.Css.View`, `Ranvier.Css.Build` and the matching `Ranvier.Css` and `.Bindings` from 0.1.1-alpha.1. Add `Ranvier.Wire`.

  Run: `rtk dotnet restore navigator/Rocq.Navigator.Tests` then `dotnet fable navigator/Rocq.Navigator.Client --outDir navigator/Rocq.Navigator.Client/generated`.
  Expected: both succeed, with a trivial `Motion` use compiling in the client. If not, walk `Ranvier.View` newest first from 0.1.49-rc.2 without Anime, and take the first set where both succeed. Note that `Ranvier.Css.View` needs a compatible `Ranvier.View`.

- [ ] **Step 2: Write the failing island test.** In `IslandTests.fs`, render a page with `Server.renderDocument` whose body is a static `<article>` and two island hosts.

```fsharp
testCase "each island host holds its own root marker, outside the article" <| fun () ->
    let html = Spike.renderPage ()
    Expect.stringContains html "<div data-island=\"a\"><!--[r" "first island"
    Expect.stringContains html "<div data-island=\"b\"><!--[r" "second island"
    Expect.isFalse (articleOf html |> fun a -> a.Contains "<!--[r") "the article has no markers"
```

- [ ] **Step 3: Run it to verify it fails.** Run: `rtk dotnet run --project navigator/Rocq.Navigator.Tests`. Expected: FAIL, as `Spike.renderPage` is undefined.

- [ ] **Step 4: Implement `Spike.renderPage () : string`.** The first shape to try: render each island with `Server.render` using distinct `IdPrefix` values. Place those strings and the article through `t.Unsafe.html` in a server-only shell component passed to `Server.renderDocument` with `Policy = Omit`.

- [ ] **Step 5: Run the test.** Expected: PASS.

- [ ] **Step 6: Hydrate both islands in a browser.** `Main.fs` calls `Hydration.hydrate` per host with `Strict = true` and its matching `IdPrefix`. Each island is a button counting clicks in a signal. Serve the output and drive it in the built-in browser.
  Expected: the server's button stays the same DOM object, both count independently, and the console has no errors. Check that hydration removes no head entries: give the page a title and a stylesheet link, and confirm they survive.

- [ ] **Step 7: If Step 6 fails, use the design's fallback.** Mount the islands fresh into empty hosts with `Mounting.mount`. The article stays server-only. Note the choice in `Packages.props`. Later tasks use whichever mounting shape passed, through one helper.

- [ ] **Step 8: Measure Anime.** Build the bundle with and without the `Motion` use, and record both minified and gzipped sizes.
  Keep Anime if the gzipped difference is at most 40 KB (the author's call: more than the rest of the client is likely to weigh). Otherwise, drop it and move to the newest set without it.

- [ ] **Step 9: Commit.** Replace the spike's counters with empty island stubs so nothing throwaway stays.

```bash
rtk git add navigator FSharp.Data.Rocq.slnx .config/dotnet-tools.json
rtk git commit -m "feat(navigator): pin the packages and prove islands on a static page"
```

### Task 2: Fixture, config and facts

**Files:**
- Create: `navigator/Rocq.Navigator.Tests/fixtures/{_CoqProject,generate.sh,navigator.json,theories/Guide.v,theories/Guide.glob,theories/Other.v,theories/Other.glob,transcript.txt}`, `navigator/Rocq.Navigator/{Config.fs,Facts.fs}`, `navigator/Rocq.Navigator.Tests/FactsTests.fs`

`Guide.v` must contain: `(**md` docs with a bold first line on a `Theorem` and a `Corollary`; a doc with no bold line; an `Admitted` theorem; a `_falsified` theorem; a comment with the check marker; names with a prime and with Unicode; one name in two `Section`s; a `Module` with a nested declaration; `<`, `&` and `<script>` in comment text; a markdown link in `Other.html#name` form and one in code; an unattached comment; a CRLF-free non-ASCII statement (`∀ ∅`); headings at two levels; a `{!Stable P}`-style generated binder. `Other.v` is imported by `Guide.v` and holds a referenced definition plus a CRLF line-end doc.

**Interfaces:**
- Produces, in `Config.fs`:
  `type Config = { FalsifiedSuffix: string option; CheckMarker: string option; CaptureImports: string[] }`, `Config.none`, `Config.read : string -> Result<Config, string>`.
- Produces, in `Facts.fs`:

```fsharp
type Assumptions = Closed | RestsOn of string[]
type SymbolFact = { Type: string option; Binders: Map<string, string>; Assumptions: Assumptions option }
type Facts = { RocqVersion: string; Digests: Map<string, string>; Symbols: Map<string, SymbolFact> } // key: Qualified
val captureScript : RocqProject -> Config -> string
val parseTranscript : project: RocqProject -> transcript: string -> Map<string, SymbolFact>
val parseBinders : checkText: string -> Map<string, string>
val capture : rocq: string[] -> projectDir: string -> RocqProject -> Config -> Result<Facts, string>
val read : string -> Result<Facts, string>
val write : string -> Facts -> unit
val reconcile : dropStale: bool -> RocqProject -> Facts -> Result<Facts, string[]>
```

`CaptureImports` is new to the design: lines such as `From stdpp Require Import prelude gmap.` written before the project imports. The probes found names like `is_Some` need them, and this keeps the tool generic.

- [ ] **Step 1: Write the fixture and compile it.** `generate.sh` follows the library fixture's script. Run in WSL, from the fixture directory:
  `MSYS_NO_PATHCONV=1 wsl.exe -e bash -lc 'export PATH=$HOME/.local/bin:$PATH; opam exec --switch=ranvier-proofs -- ./generate.sh'`.
  Expected: `.glob` files are written, and `Project.load` returns `Ok` for the fixture.

- [ ] **Step 2: Fix the separator.** Use `Fail Check __nav_<n>.` between commands: its message echoes the unique name, and it needs no imports. Run a two-symbol script through `rocq repl -Q theories Fixture` (no `-batch`) to confirm, and save the real output as `fixtures/transcript.txt`. `Loading ML` and `Fetching opaque proofs` lines are filtered.

- [ ] **Step 3: Write the failing tests** in `FactsTests.fs`.

```fsharp
testCase "a symbol whose Check errored has no type, and its neighbours keep theirs"
testCase "Closed under the global context reads as Closed"
testCase "named axioms read as RestsOn with each axiom"
testCase "binders come from the leading forall, and a generated binder name matches only itself"
testCase "a digest mismatch refuses, naming the file"
testCase "dropStale removes that file's facts and keeps the others"
testCase "a fact for an unknown symbol is ignored"
```

- [ ] **Step 4: Run them to verify they fail.** Run: `rtk dotnet run --project navigator/Rocq.Navigator.Tests`. Expected: compile failure on `Facts`.

- [ ] **Step 5: Implement `Config.fs` and `Facts.fs`.** `parseBinders` reads the leading `forall (a b : T) (c : U),` groups, tracking bracket depth, and stops at the first comma at depth 0. `facts.json` uses `System.Text.Json`. Each theorem gets `Check` and `Print Assumptions`, and other symbols get `Check`. Kinds with no `Check` (notations, sections) are skipped.

- [ ] **Step 6: Add the `capture` command** to `Program.fs`: `navigator capture <_CoqProject> --out facts.json --rocq "<command>" [--config navigator.json]`. Run it on the fixture and confirm a theorem's type is present.

- [ ] **Step 7: Run the tests.** Expected: PASS.

- [ ] **Step 8: Commit** with message `feat(navigator): capture types and assumptions into facts.json`.

### Task 3: Docs to HTML

**Files:**
- Create: `navigator/Rocq.Navigator/Docs.fs`, `navigator/Rocq.Navigator.Tests/DocsTests.fs`

**Interfaces:**
- Consumes: `Comment`, `CommentFormat` from `FSharp.Data.Rocq.Structure`.
- Produces:

```fsharp
type Resolve = string -> (string * string) option // code text -> (ref key, href)
val render : resolve: Resolve -> format: CommentFormat -> body: string -> string
val firstBoldLine : format: CommentFormat -> body: string -> string option
```

- [ ] **Step 1: Write the failing tests.**

```fsharp
testCase "a markdown comment renders headings, paragraphs, links and code"
testCase "a coqdoc comment renders headings, [code] and paragraphs"
testCase "angle brackets and ampersands in prose display literally" <| fun () ->
    let html = Docs.render noResolve CommentFormat.Markdown "s < fuel & <script>alert(1)</script>"
    Expect.isFalse (html.Contains "<script") "no live script"
    Expect.stringContains html "s &lt; fuel &amp;" "literal text"
testCase "code naming a symbol in scope becomes a reference, with data-ref and href"
testCase "code naming nothing stays plain code"
testCase "firstBoldLine reads the opening bold line and returns None without one"
```

- [ ] **Step 2: Run them to verify they fail.** Expected: compile failure on `Docs`.

- [ ] **Step 3: Implement `Docs.fs`.** Use Markdig with `DisableHtml()`, walking the AST to turn `CodeInline` into a reference when `resolve` hits. Coqdoc handles `*` headings, `[code]` and blank-line paragraphs, with everything else escaped.

- [ ] **Step 4: Run the tests.** Expected: PASS.

- [ ] **Step 5: Commit** with message `feat(navigator): render comment bodies to safe HTML`.

### Task 4: Source spans to HTML

**Files:**
- Create: `navigator/Rocq.Navigator/Source.fs`, `navigator/Rocq.Navigator.Tests/SourceTests.fs`

**Interfaces:**
- Consumes: `RocqFile`, `Span`, `Reference` data in `FileModel`, `RocqProject.Symbols`.
- Produces:

```fsharp
type Ref = { Key: string; Href: string option } // Key: "s<symbol index>", "x<external index>" or "b<decl>.<binder>"
val render : file: RocqFile -> refAt: (Span -> Ref option) -> span: Span -> string
val text : file: RocqFile -> span: Span -> string // the exact source text
val stripTags : html: string -> string // decodes entities too; tests and Task 14 use it
```

- [ ] **Step 1: Write the failing tests.**

```fsharp
testCase "every declaration's rendered statement and proof decode to the exact source bytes" // all fixture files
testCase "a span over non-ASCII text and CRLF line ends round-trips" // Other.v and the library's Crlf.v
testCase "a reference is an anchor with data-ref, and an external is a span with data-ref"
testCase "a binder in the statement carries its b key"
testCase "keywords are marked, and comments inside a span stay in place"
```

The first test computes `Encoding.UTF8.GetBytes` of the decoded text and compares it with the file's bytes at the span, so a char-offset and byte-offset mix-up fails it.

- [ ] **Step 2: Run them to verify they fail.** Expected: compile failure on `Source`.

- [ ] **Step 3: Implement `Source.fs`.** Check first in the REPL whether `Span` counts bytes or chars. Walk references in span order, escaping the text between them, and do not nest anchors.

- [ ] **Step 4: Run the tests.** Expected: PASS.

- [ ] **Step 5: Commit** with message `feat(navigator): render source spans with references, byte exact`.

### Task 5: Anchors and article blocks

**Files:**
- Create: `navigator/Rocq.Navigator/{Anchors.fs,Article.fs}`, `navigator/Rocq.Navigator.Tests/ArticleTests.fs`

**Interfaces:**
- Consumes: `Source.render`, `Docs.render`, `Project.usesBy`, `Project.usesOf`.
- Produces:

```fsharp
// Anchors.fs
val pageOf : moduleName: string -> string                  // "<Module>.html"
val slug : heading: string -> string                       // "h-<slug>"
val forFile : RocqFile -> Map<int, string>                 // declaration index -> anchor
// Article.fs
type Block =
    | Heading of level: int * text: string * anchor: string
    | Documented of decl: int * anchor: string * docHtml: string * statementHtml: string * proofs: ProofBlock[] * refs: RefLists
    | Quiet of decl: int * anchor: string * statementHtml: string * proofs: ProofBlock[] * refs: RefLists
    | Command of html: string
    | Prose of html: string
and ProofBlock = { Ending: ProofEnding; Html: string }
and RefItem = { Name: string; Key: string; Href: string option }
and RefLists = { Uses: RefItem[]; UsedBy: RefItem[] }
val blocks : RocqProject -> fileIndex: int -> Facts option -> Block[]
val toHtml : Block[] -> string
```

- [ ] **Step 1: Write the failing tests.**

```fsharp
testCase "anchors: a primed name keeps its prime and resolves through getElementById-style matching"
testCase "anchors: a repeated name keeps #name first, then #name~2"
testCase "anchors: an unnamed declaration gets #d<index>, and a Unicode name stays readable"
testCase "blocks: source order, and every sentence appears exactly once"
testCase "blocks: an attached doc sits on its declaration, an unattached comment becomes Prose"
testCase "toHtml: a proof is a closed details labelled with its ending"
testCase "toHtml: Admitted carries the warning class and text"
testCase "toHtml: reference lists show the first 8, with the rest in a nested details"
testCase "every byte of the file appears on the page, apart from comments rendered as prose"
```

- [ ] **Step 2: Run them to verify they fail.** Expected: compile failure on `Anchors`.

- [ ] **Step 3: Implement both files.** Attachment comes from `Comment.AttachedTo` and `Declaration.Docs`. A `Proof` block groups its sentences, so tactic sentences are not separate `Command`s. Heading comments come from `FileModel` headings.

- [ ] **Step 4: Run the tests.** Expected: PASS.

- [ ] **Step 5: Commit** with message `feat(navigator): build article blocks in source order`.

### Task 6: Guarantees

**Files:**
- Create: `navigator/Rocq.Navigator/Guarantees.fs`, `navigator/Rocq.Navigator.Tests/GuaranteesTests.fs`

**Interfaces:**
- Consumes: `Config`, `Docs.firstBoldLine`, `Facts`.
- Produces:

```fsharp
type Group = Open | Falsified | Check | Guarantee
type Entry = { Title: string; Group: Group; Module: string; Href: string; DocHtml: string; Axioms: Assumptions option }
val entries : RocqProject -> Config -> Facts option -> Entry[]
val toHtml : Entry[] -> string
```

- [ ] **Step 1: Write the failing tests.**

```fsharp
testCase "only Theorem and Corollary with an opening bold line become entries"
testCase "first match wins: an Admitted _falsified theorem is Open"
testCase "without a config only Open and Guarantee exist"
testCase "an Open or Falsified entry never renders the passing badge, even with Closed facts"
testCase "a Guarantee without facts renders no badge"
testCase "entries match an independent regex scan of the fixture's comments" // acceptance 4
```

The independent scan is a regex over the `.v` text in the test. It does not use `Comment` or `Declaration`.

- [ ] **Step 2: Run them to verify they fail.** Expected: compile failure on `Guarantees`.

- [ ] **Step 3: Implement `Guarantees.fs`.** Rules are from the spec's Guarantees table.

- [ ] **Step 4: Run the tests.** Expected: PASS.

- [ ] **Step 5: Commit** with message `feat(navigator): sort theorems into guarantee groups`.

### Task 7: Client data and site writer

**Files:**
- Create: `navigator/Rocq.Navigator.Shared/Model.fs`, `navigator/Rocq.Navigator/{Cards.fs,Page.fs,Site.fs}`, `navigator/Rocq.Navigator.Tests/SiteTests.fs`
- Modify: `navigator/Rocq.Navigator/Program.fs`

**Interfaces:**
- Produces, in `Model.fs`, compiled by both .NET and Fable with `Ranvier.Wire` codecs:

```fsharp
type Look = Family | Journal
type Mode = System | Light | Dark
type Badge = NoAxioms | Rests of string[]
type CardData =
    { Key: string; Name: string; Qualified: string; Module: string; Href: string option
      Signature: string; SignatureIsType: bool; DocHtml: string option; Mentions: int
      Badge: Badge option; External: bool }
type SearchEntry = { Name: string; Qualified: string; Module: string; Kind: string; Href: string; Summary: string }
```

- Produces, in .NET:

```fsharp
val Cards.forModule : RocqProject -> Facts option -> fileIndex: int -> CardData[]   // written to data/<Module>.symbols.json
val Cards.search : RocqProject -> Facts option -> SearchEntry[]                     // written to data/search.json
val Page.modulePage : SiteContext -> fileIndex: int -> string
val Page.guaranteesPage : SiteContext -> string
type BuildOptions = { Project: string; Out: string; Facts: string option; Config: string option; DropStaleFacts: bool; Strict: bool; ClientAssets: string option }
val Site.build : BuildOptions -> Result<BuildReport, string[]>
val Site.checkLinks : outDir: string -> string[]
```

`BuildReport` holds JS, CSS and largest-page sizes and the build time (acceptance 10).

`Page` lays out the left pane (modules in `_CoqProject` order, grouped by directory, with declaration counts), the article and the right pane (outline, plus empty Uses and Used by slots for Task 10), with the island hosts from Task 1. It is unstyled until Task 8.

- [ ] **Step 1: Write the failing tests.**

```fsharp
testCase "codecs round-trip CardData and SearchEntry, including text with quotes and Unicode"
testCase "a card prefers the captured type, and falls back to the source header"
testCase "an external card has no Href and External = true"
testCase "build: all modules render, and every declaration has its anchor"     // acceptance 1
testCase "build: every internal link and data-ref resolves"                    // acceptance 2
testCase "build: a broken comment link is reported, and fails only under Strict"
testCase "build: every URL in the output is relative"
testCase "build: without facts the site builds, with no badges"
testCase "build: a stale facts file refuses, and DropStaleFacts proceeds"
testCase "build: output for the same input is byte-identical across two runs"
```

- [ ] **Step 2: Run them to verify they fail.** Expected: compile failure on `Model`.

- [ ] **Step 3: Implement the files, and the `build` command:** `navigator build <_CoqProject> --out <dir> [--facts f] [--config c] [--drop-stale-facts] [--strict]`. `checkLinks` parses the written files, so it covers links in comments.

- [ ] **Step 4: Run the tests.** Expected: PASS.

- [ ] **Step 5: Commit** with message `feat(navigator): write the site directory and check its links`.

### Task 8: Tokens, styles and the owner's checkpoint

**Files:**
- Create: `navigator/Rocq.Navigator.Shared/{Tokens.fs,Styles.fs}`, `navigator/Rocq.Navigator.Tests/TokenTests.fs`, `navigator/assets/fonts/*` with licences
- Modify: `navigator/Rocq.Navigator/{Page.fs,Site.fs,Article.fs,Guarantees.fs}`

**Interfaces:**
- Produces:

```fsharp
type Tokens = { Bg: string; Side: string; Line: string; Ink: string; Soft: string; Dim: string; Selected: string
                Keyword: string; Binder: string; Link: string; Pass: string; Warn: string; Radius: string
                Body: string; Title: string; TitleSize: string; TitleWeight: int }
val Tokens.of : Look -> resolved: Mode -> Tokens     // Light or Dark
val Styles.modules : CssModule list                  // passed to Compiler.compile with SingleFile "navigator.css"
```

Values for Family dark and Journal light come from the spec's table. Family light uses `#ffffff`, `#f6f7f9` and `#6669d7`. Journal dark is warm dark paper. Tokens are CSS custom properties redefined on `:root[data-look][data-mode]`, with `prefers-color-scheme` handling when no mode is saved.

- [ ] **Step 1: Write the failing tests.**

```fsharp
testCase "all four combinations meet WCAG AA" // Ink, Soft, Dim, Keyword, Binder, Link, Pass and Warn on Bg and Side: 4.5:1
testCase "the two looks' specified values match the design table"
testCase "the compiled stylesheet holds both looks and is one file"
```

- [ ] **Step 2: Run them to verify they fail.** Expected: compile failure on `Tokens`.

- [ ] **Step 3: Implement tokens and the styles for shell, panes, article, statement, proof fold, badge and Guarantees.** Self-host Inter, JetBrains Mono and a free serif (for example Source Serif 4), each under the SIL Open Font License, with their licence files. Confirm `∀ ∧ → ∅ ∘` render in the mono face. The layout collapses side panes below about 900 px into drawers using `<details>`, so they work without JavaScript. Long code wraps.

- [ ] **Step 4: Run the tests.** Expected: PASS.

- [ ] **Step 5: Render one real module.** Build from the demo project (`--strict` off) and take screenshots of one module page and Guarantees in all four combinations, setting `data-look` and `data-mode` by hand. Compare them with `visual-style.html`.

- [ ] **Step 6: Commit** with message `feat(navigator): add both looks and the reader layout`.

- [ ] **Step 7: Stop for the owner.** Send the screenshots of the module page in both looks, and wait for their response before starting Task 9. Apply what they ask for, then continue.

### Task 9: Motion

**Files:**
- Modify: `navigator/Rocq.Navigator.Shared/Styles.fs`, and `Islands.fs` if Anime stayed
- Test: `navigator/browser/motion.spec.ts`, set up with Playwright here (`navigator/browser/{package.json,playwright.config.ts,serve.mjs}`)

**Interfaces:**
- Produces: `navigator/browser` runs against a site directory named by `NAVIGATOR_SITE`. `serve.mjs` serves it under `/sub/path/`, so every browser test is also a subpath test.

Motion only for card appearance, fold opening and the target settling after a jump. No other motion is added. Use Anime for fold height if it stayed in Task 1, and CSS transitions (`interpolate-size` with a plain fallback) if it did not.

- [ ] **Step 1: Write the failing test.**

```ts
test("reduced motion makes every animated property instant", async ({ page }) => {
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.goto(MODULE_PAGE);
  await page.locator("details.proof summary").first().click();
  expect(await runningAnimations(page)).toBe(0); // document.getAnimations() and computed transition-duration
});
```

- [ ] **Step 2: Run it to verify it fails.** Run: `npx playwright test motion` from `navigator/browser`. Installing Playwright's Chromium is part of this step. Expected: FAIL, as a transition is running.

- [ ] **Step 3: Implement motion, with the `prefers-reduced-motion` override.**

- [ ] **Step 4: Run it.** Expected: PASS.

- [ ] **Step 5: Commit** with message `feat(navigator): add motion that keeps the reader's place`.

### Task 10: Look switch and section marker islands

**Files:**
- Modify: `navigator/Rocq.Navigator.Shared/Islands.fs`, `navigator/Rocq.Navigator.Client/Main.fs`, `navigator/Rocq.Navigator/Page.fs`
- Test: `navigator/browser/switch.spec.ts`, `navigator/browser/marker.spec.ts`

**Interfaces:**
- Produces: `Islands.lookSwitch`, `Islands.sectionMarker`, and `Page.preloadScript : string`, the inline script setting `data-look` and `data-mode` before first paint. Written with `try/catch` around storage, using `Server.ModuleScript`'s nonce-free inline form or a plain head script, whichever avoids a flash (a module script is deferred, so check this).

- [ ] **Step 1: Write the failing tests.**

```ts
test("the switch persists across pages in both looks and modes")
test("the look is set before first paint") // the attribute is present when the first stylesheet applies
test("with localStorage throwing, the switch still changes this page and no error is logged")
test("the marker follows the section in view in the outline")
test("wide screens move the current declaration's lists to the right pane and hide the inline ones")
test("no hydration mismatch is reported and the console has no errors")
```

The `localStorage` case uses `addInitScript` to replace the getter with one that throws.

- [ ] **Step 2: Run them to verify they fail.** Expected: FAIL, as the islands are stubs.

- [ ] **Step 3: Implement both islands** using only the primitives in **Core and view state**: signals, `createEffect` for root attributes, and `onCleanup` for the observer. `IntersectionObserver` is created inside the island's owner.

- [ ] **Step 4: Run them.** Expected: PASS.

- [ ] **Step 5: Commit** with message `feat(navigator): add the look switch and section marker`.

### Task 11: Hover card island

**Files:**
- Modify: `navigator/Rocq.Navigator.Shared/Islands.fs`, `navigator/Rocq.Navigator.Client/Main.fs`
- Test: `navigator/browser/card.spec.ts`

**Interfaces:**
- Consumes: `CardData` codec, `data-ref` keys from Task 4, `data/<Module>.symbols.json`.
- Produces: `Islands.cardLayer`, one delegated `pointerover`/`focusin` listener on the article.

- [ ] **Step 1: Write the failing tests.**

```ts
test("hover opens after about 120 ms with type, doc, module and mention count")
test("the card stays open while the pointer is on it")
test("keyboard focus opens it, Esc closes it, and Enter follows the link")
test("an external reference shows its qualified name and 'external', with no link")
test("a binder shows its inferred type")
test("a doc-less symbol shows 'No documentation' quietly")
test("an Admitted theorem's card shows no passing badge")
test("when the data file fails to load, the card shows the name and the link still works")
test("at 375 px a tap opens the card")
test("the card stays inside the viewport near every edge")
```

The load failure uses `page.route` to abort `*.symbols.json`.

- [ ] **Step 2: Run them to verify they fail.** Expected: FAIL.

- [ ] **Step 3: Implement the card.** The hovered key is a signal, and the card's symbol is a `createMemoWithComparer`. Position is measured from the reference's rectangle, as listed in **Core and view state**. Each data file is fetched once, on first hover, and cached in a signal holding the loaded map: raise it as a question if that needs more than this.

- [ ] **Step 4: Run them.** Expected: PASS.

- [ ] **Step 5: Commit** with message `feat(navigator): add hover cards for every reference`.

### Task 12: Search island

**Files:**
- Create: `navigator/Rocq.Navigator.Shared/Ranking.fs`, `navigator/Rocq.Navigator.Tests/RankingTests.fs`
- Modify: `navigator/Rocq.Navigator.Shared/Islands.fs`, `navigator/Rocq.Navigator.Client/Main.fs`
- Test: `navigator/browser/search.spec.ts`

**Interfaces:**
- Produces: `Ranking.rank : SearchEntry[] -> query: string -> SearchEntry[]`, `Islands.search`.

- [ ] **Step 1: Write the failing tests.**

```fsharp
testCase "exact name beats prefix, prefix beats substring, substring beats subsequence"
testCase "the query is case-insensitive, and an empty query returns nothing"
```

```ts
test("Ctrl+K and / open search, but / typed in an input does not")
test("typing finds a declaration, and Enter navigates to its anchor")
test("arrow keys move the selection, and Esc closes")
test("when search.json fails to load, the dialog says so")
```

- [ ] **Step 2: Run them to verify they fail.** Expected: FAIL.

- [ ] **Step 3: Implement ranking and the dialog.** Results come from a `createMemo` over the text, and rows from `createProjection`, keyed by `Href`. `search.json` is fetched on first open.

- [ ] **Step 4: Run them.** Expected: PASS.

- [ ] **Step 5: Commit** with message `feat(navigator): add search over every declaration`.

### Task 13: Full browser acceptance and CI

**Files:**
- Create: `navigator/browser/acceptance.spec.ts`, `.github/workflows/navigator.yml`
- Modify: `README.md` (a short section on the navigator, with its two commands)

**Interfaces:**
- Consumes: the whole site, built from the Task 2 fixture with `--strict`.

`navigator.yml` is a separate workflow, so `publish.yml` is untouched. It installs Node, restores tools, checks `dotnet fantomas --check navigator`, runs the Fable and Vite builds, builds the fixture site, and runs the .NET tests and Playwright.

- [ ] **Step 1: Write the acceptance spec,** parameterised over both looks and all modes, covering what earlier tasks did not:

```ts
test("JavaScript off: a module page reads, folds and links")          // acceptance 6
test("375 px: no horizontal page scroll on every page")               // acceptance 7
test("a subpath serve finds CSS, JS, fonts and data")
test("a failing bundle leaves a fully readable page")                 // route the bundle to abort
test("a link to a primed or repeated name lands on its declaration")  // from Task 5's anchors
```

- [ ] **Step 2: Run it.** Run: `npx playwright test` from `navigator/browser`. Expected: all pass, or fix what fails. The Task 7 byte check runs in the same suite as acceptance 3.

- [ ] **Step 3: Validate the workflow.** Run the steps of `navigator.yml` locally, in order, on a clean `git worktree` of this branch. Expected: each exits 0 and no step needs a sibling checkout.

- [ ] **Step 4: Commit** with message `ci(navigator): run the full acceptance on the fixture`.

### Task 14: Generate from the proofs and final gate

**Files:**
- Create: `navigator/Rocq.Navigator.Tests/CorpusTests.fs` (skipped without `FSHARP_DATA_ROCQ_CORPUS`)
- Output, not committed: `navigator/out/` (add to `.gitignore`)

**Interfaces:**
- Consumes: the demo project at `C:\Users\shaya\RiderProjects\Ranvier\.claude\worktrees\iris-rocq-ranvier-project-f3d6ae\proofs\_CoqProject`, read only, with a `navigator.json` kept in the scratchpad (`falsifiedSuffix`, `checkMarker` and `captureImports` from `From stdpp Require Import prelude gmap.`).

- [ ] **Step 1: Write the corpus tests:** the byte check, anchors, links and Guarantees scan from Tasks 4, 5, 6 and 7, over whichever project the variable names.

- [ ] **Step 2: Capture facts.** Run `navigator capture` on the demo project in the background. Expected: facts for most symbols. Report how many symbols have no type, and compare times with the measured 2.2 s and 18 s.

- [ ] **Step 3: Build with `--strict`, and run the corpus tests.** If `--strict` fails only on a broken link written in a Ranvier comment, list it for the owner and build without `--strict`. Do not edit Ranvier.

- [ ] **Step 4: Run Playwright against the demo site** with `NAVIGATOR_SITE=navigator/out`. Expected: all pass. Then check a real page by eye in the built-in browser.

- [ ] **Step 5: Run the final gates, unfiltered.**

```bash
rtk dotnet run -c Release --project tests/FSharp.Data.Rocq.Tests
rtk dotnet run -c Release --project navigator/Rocq.Navigator.Tests
dotnet fantomas --check src tests samples navigator
```

Expected: all pass, and the library project has no new `PackageReference`. Also run the comment hygiene script over `navigator/` and `review-comments.ps1 -Check`.

- [ ] **Step 6: Report.** JS, CSS and largest-page sizes and build time from `BuildReport`, the pinned set, whether Anime made it in, symbols without facts, and every acceptance item with its result.

- [ ] **Step 7: Commit** with message `test(navigator): run acceptance over a real project`. Do not push.
