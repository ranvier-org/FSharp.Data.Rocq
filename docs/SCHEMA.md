# rocq-data schema, version 1.0

For someone building a renderer or another consumer. The output is plain JSON, and reading it needs no Rocq.

## Layout

```
<out>/
  index.json                     files, every declaration and symbol, the file graph
  xref.json                      uses between declarations and symbols, both directions
  files/<module>.outline.json    structure: sentences, comments, declarations, scopes, notations
  files/<module>.refs.json       every .glob reference and binder
  files/<module>.tokens.json     the source text and a token partition of it
```

Read only what you need. A table of contents needs `index.json`. A theorem list with docs needs `outline`. A rendered
page needs all three layers of its file. "Who uses this" needs `index.json` and `xref.json`.

## Conventions

- **Offsets are UTF-8 byte offsets into the `.v` file.**
- **A span is half-open:** `[start, end)`. `.glob` writes inclusive ends, and the extractor converts them.
- **Tables are columnar.** `{"start": [...], "end": [...]}` means row `i` is `start[i], end[i]`. A column named after
  another table holds indexes into it.
- **`-1` means none** in an index column. `null` means absent in a string or span field.
- **Every index is local to its file** unless this page says otherwise.
- **Output is deterministic.** The same inputs give the same bytes, and it holds no timestamps.

## Guarantees

Tests check each of these on the fixture and, when `ROCQDATA_CORPUS` is set, on a real project.

1. **Tokens partition the file.** Joining them gives the source back byte for byte.
2. **Every span falls on a token boundary and a character boundary**, so `start`/`u16` converts any span to UTF-16.
3. **Every byte is covered:** each non-blank byte lies in a sentence or a comment.
4. **Every `.glob` line is accounted for:** the header, `symbols`, `refs`, `binders` or `globLines.unparsed`, once each.
5. **A file is never paired with a `.glob` from different source.** The first `.glob` line holds the source's MD5. A
   mismatch refuses the run, unless `--allow-stale` is passed, which gives `globStatus: "stale"` and no `.glob` facts.
6. **Sentence spans equal Rocq's own**, compared with `rocq compile -time-file` output when it is provided.

## Versioning

Each file holds `"schema": {"major": 1, "minor": 0}` and `"layer"`.

- `minor` grows when a field is added. Ignore fields you do not know.
- `major` grows on any other change. Refuse a major you do not know.

`index.generator.version` and both schema numbers form part of the incremental key, so a new version re-extracts
everything.

## The `.glob` format and unknown input

Written against Rocq 9.1.0's `interp/dumpglob.ml`. Rocq documents the format as carrying no stability guarantee, and
`index.rocqVersion` records the version when passed with `--rocq-version`.

| Input | Result | Exit code |
|---|---|---|
| Stale, missing or `DIGEST NO` `.glob` | The run is refused and nothing is written | 2 |
| A line of a known form with an unknown kind | Kept in the tables, listed in `globLines.unknownKinds` and `index.unknownKinds`, warned | 0, or 3 with `--strict` |
| A line of no known form | Listed with its text in `globLines.unparsed`, warned | 3 |
| A span outside the file or inside a character | Dropped from the tables, listed in `diagnostics` as `glob-span-invalid` | 0 |

Known kinds: `ax def coe subclass canonstruc ex scheme proj inst meth defax prfax thm prf prim symb class var indrec rec
corec ind variant coind constr not binder lib mod modtype sec abbrev`, and `var` followed by any of them. The `.mli` list
omits `prf`, `sec`, `symb`, `abbrev` and the `var` forms. A definition is `prf` and a reference to it is `thm`.

## `index.json`

| Field | Meaning |
|---|---|
| `generator` | `{name, version}` |
| `rocqVersion` | From `--rocq-version`, or null |
| `files[]` | `path`, `module`, `digest` (MD5 of the `.v`), `globDigest` (MD5 of the `.glob`), `globStatus` (`ok`, `stale` or `noDigest`), `bytes`, `shards.{outline,refs,tokens}.{path,bytes}`, `counts` |
| `declarations` | Columns: `id`, `name`, `keyword`, `category`, `file`, `local` (index in the file's `declarations`), `start`, `end` (the extent), `ending` (the last proof's ending, or null), `docCount` |
| `symbols` | Columns: `qualified`, `kind`, `file`, `local` (row in the file's `symbols`), `decl` (index into `index.declarations`, or -1) |
| `fileGraph.requires` | `from`, `to`: file indexes. In-project `Require`s, from `.glob` `lib` references |
| `fileGraph.externalRequires` | `from`, `module`: `Require`s outside the project |
| `fileGraph.references` | `from`, `to`, `count`: references between project files, excluding `lib` |
| `fileGraph.externalReferences` | `from`, `module`, `count`: references to modules outside the project |
| `fileGraph.rocqDep` | `from`, `to`: from `--rocq-dep`, or null. A test checks that it equals `requires` |
| `unknownKinds` | Kind to line count |
| `unresolved[]` | `file`, `target`, `kind`: references into the project with no matching symbol |

**`id`** is `<module>#<qualified name>`. Anonymous declarations use `<module>#@<sentence index>`, which changes when the
file above them changes. A name used twice gets `@<sentence index>` appended.

## `xref.json`

| Field | Meaning |
|---|---|
| `symbolCount`, `declarationCount`, `fileCount` | Sizes of the index tables |
| `externals` | Columns: `module`, `qualified`, `kind`. Targets outside `index.symbols` |
| `edges` | Columns, sorted by `to`, `from`, `where`: `to`, `from`, `where`, `count` |
| `toOffsets` | Edges reaching node `n` are rows `toOffsets[n] .. toOffsets[n+1]-1` |
| `fromOffsets`, `byFrom` | Edges leaving owner `s` are `edges[byFrom[k]]` for `k` in `fromOffsets[s] .. fromOffsets[s+1]-1` |

- **A node** `n` is `index.symbols[n]` when `n < symbolCount`, otherwise `externals[n - symbolCount]`.
- **`from`** is an index into `index.declarations`, or `~f` (negative) for a top-level directive in file `f`, such as
  `Print Assumptions` or `Check`.
- **An owner slot** `s` is the declaration index, or `declarationCount + f` for file `f`'s directives.
- **`where`:** 0 statement, 1 body (after a top-level `:=`), 2 proof, 3 top-level directive.
- **`count`** is the number of mentions.

### What an edge means

An edge says **the source text mentions the target within this declaration**, as `.glob` records it. References to local
binders and `Require`s are excluded. A recursive call and a mutual sibling appear as edges.

It is not the dependency set of the kernel term. Measured on all 234 theorems of the corpus at commit `ab4ee9fa`, against
`Print` under `Set Printing All`, excluding symbols of kind `not`, `abbrev`, `mod`, `modtype`, `sec` and a theorem's own name:

| | Project constants |
|---|---|
| In the kernel term | 3,271 |
| Mentioned in source | 1,383 |
| In both | 1,305 |
| **In the term, not the source** | **1,966 (60%), in 224 theorems.** All are `def`, `rec`, `constr`, `proj`, `ind`, `inst` or `scheme`. **None are theorems.** |
| In the source, not the term | 78 (5.6%), of which 64 are theorems |

- **Missed:** anything elaboration or a tactic inserts without a name in the source, such as types, implicit arguments,
  unfolded definitions and record projections. `Hint` databases, `auto`, typeclass search and coercions can reach a
  theorem without naming it. The corpus has no `Hint` sentence, so the zero above says nothing of a project using them.
- **Extra:** a name a tactic mentioned that left no trace in the term, for example a `try rewrite` that had no effect.
  The comparison did not establish the cause of each of the 78.
- **Absent from `.glob`:** calls to `Ltac` definitions, names inside `Ltac` strings, and record constructors'
  definitions. A reference to such a constructor appears in `unresolved`.
- **Not compared:** the bodies of definitions.

## `files/<module>.outline.json`

Header on all layers: `schema`, `layer`, `module`, `path`, `digest`, `globDigest`, `globStatus`. This layer adds `bytes`
and `lineStarts`, the byte offset of each line.

### `sentences`

Columns: `start`, `end`, `category` (into `categories`), `keyword` (into `keywords`, or -1), `decl` (owning declaration,
or -1), `firstToken`, `endToken` (exclusive). `modifiers[]`: `{sentence, values}`, listed only when present.

A sentence runs from its first token through its terminator, and includes any comments inside it. A bullet, `{`, `}` and
`1:{` are sentences.

`keyword` is the leading word as written: a command, or the tactic's name inside a proof. Some commands span two words,
such as `Module Type`. It is -1 when the sentence starts with a symbol.

`category`: `theorem goal definition inductive assumption notation ltac scheme other` (declarations), `section module end
import`, `proofStart proofEnd obligation tactic bullet focus`, `directive`, `unterminated`.

### `declarations[]`

| Field | Meaning |
|---|---|
| `id` | See `index.json` |
| `name` | Primary name: the first `.glob` symbol in the sentence that is no constructor, projection, scheme or notation. Null when anonymous. A notation's name is its key |
| `nameSource` | `glob`, `source` (`Ltac`, read from the text) or `none` |
| `nameSpan` | Null for `Goal`, whose `.glob` span covers its statement, and for anonymous declarations |
| `keyword`, `keywordSpan` | As written, without modifiers. **Comes from the `.v`**, since `.glob` writes `prf` for all of `Theorem`, `Lemma` and the rest |
| `modifiers` | `Local`, `Global`, `Program`, `#[...]` and so on, as written |
| `category`, `scope`, `sentence` | `scope` indexes `scopes`, or -1 |
| `sentenceSpan` | The declaring sentence |
| `extent` | From the sentence's start to its last proof's end |
| `statement` | After the name, up to `:=` or the terminator. An assumption's starts after the keyword |
| `binders`, `type` | `statement` split at its first top-level `:`. Without one, `binders` is the whole statement and `type` is null |
| `body` | After the first top-level `:=` that belongs to no top-level `let`, up to the terminator |
| `proofs[]` | See below. Usually zero or one; a `Program` definition has one per obligation |
| `symbols` | Rows of `symbols` in the sentence: mutual siblings, constructors, projections, schemes, where-notations |
| `docs` | Comments attached by the rule below, in source order |

A sentence is a declaration when its keyword is in a declaration category, or when it sits outside a proof and `.glob`
gives it a term-level symbol (`other`, such as `Canonical Structure`). A `Fail` sentence is a `directive`.

**A proof opens** after a theorem keyword or `Goal`, after a definition-like keyword with no top-level `:=`, and at an
obligation command.

`proofs[]`: `ending` (`Qed`, `Defined`, `Admitted`, `Abort`, `Save`, `ProofTerm` for `Proof <term>.`, `Unterminated`),
`span` (first sentence through terminator), `bodySpan` (after `Proof ...`, up to the terminator), `opener` (the `Proof`
sentence or -1), `firstSentence`, `lastSentence`, `tacticSentences`.

### `comments[]`

`format` (`md` for `(**md`, `doc` for `(** `, otherwise `plain`), `span`, `bodySpan` (between the delimiters), `text`
(exactly as written, delimiters included), `sentence` (the containing sentence, or -1), `nextSentence`, `newlinesToNext`,
`trailingSentence` (the sentence ending on the comment's line, or -1), `decl` (the declaration whose proof or sentence
holds it, or -1), `attachedTo`.

**Attachment rule.** Walking back from a declaration's or scope's first token, a top-level comment attaches when at most
one newline separates it from what follows it, and it does not trail an earlier sentence. The walk stops at the first
comment that fails. Comments of every format attach. For another rule, use `nextSentence` and `newlinesToNext`.

### `headings[]`

`level`, `text`, `span` (the title's bytes), `format`, `comment`. Markdown: 1 to 6 `#` and a space, outside code fences.
Coqdoc: 1 to 4 `*` and a space, outside `<<` `>>`. Both are read per line in `doc` and `md` comments.

### `scopes[]`

`kind` (`Section`, `Module`, `Module Type`), `name`, `parent`, `sentence`, `endSentence`, `span`, `docs`. For `Module A :=
B.` and `Declare Module`, `endSentence` equals `sentence`.

### `imports[]`

`sentence`, `keyword` (`From`, `Require`, `Import`, `Export`), `from`, `mode` (`Import`, `Export` or null), and
`modules[]`: `{text, span, resolved}`. `resolved` is the full module path from `.glob`, or null.

### `notations[]`

One row per `not` or `abbrev` symbol, and one per notation sentence without a symbol (`Reserved Notation`, `Tactic
Notation`). `sentence`, `decl`, `keyword`, `literal` (the string, unquoted, or null for an abbreviation), `literalSpan`,
`symbol`, `entry`, `scope`, `key`.

`key` is Rocq's encoding: non-terminals are `x`, terminals are single-quoted, and `_` separates tokens, as in
`x_'+++'_x`. A use of the notation is a reference of kind `not` whose name is `:<entry>:<scope>:<key>`.

### `symbols`

Every `.glob` definition line except binders. Columns: `name`, `path` (`<>` when none), `qualified`, `kind`, `start`,
`end`, `line` (0-based `.glob` line), `sentence`, `decl`.

The span is the name's bytes, with these exceptions. A `not` covers its opening quote. A `scheme` covers the inductive's
name. A `Goal`'s covers its statement.

`qualified` joins `path` and `name`. Sections stay out of `path`, except for section variables and sections themselves.

### Other fields

- `uses`: `owner`, `target`, `where`, `count`, and `targets` (`module`, `path`, `name`, `kind`), the input to `xref.json`.
- `requires`, `referencedModules` (`module`, `count`): the input to `fileGraph`.
- `globLines`: `total`, `refs`, `symbols`, `binders`, `unknownKinds[]` (`line`, `kind`), `unparsed[]` (`line`, `text`).
- `diagnostics[]`: `severity`, `code`, `message`, `offset`. A compiled file yields none.

## `files/<module>.refs.json`

- `refs`: columns `start`, `end`, `target`, `line`. Sorted by `start`, `end`, `line`. `line` recovers `.glob` order.
- `targets`: columns `module`, `path`, `name`, `kind`, `binder`, `symbol`.
- `binders`: columns `start`, `end`, `name`, `uid`, `line`.

**`targets.binder`** is the row in `binders` when the target is a local binder, whose reference `.glob` writes as kind
`var` with name `x:12`. **`targets.symbol`** is the row in outline `symbols` when the target is a name under definition: a
recursive call, a mutual sibling or a `Context` variable, written as `name:uid` with no binder line.

A pattern binder has a generated name, and its span covers the whole pattern. A span may be shared: several references
can have the same bytes.

## `files/<module>.tokens.json`

- `source`: the file's text.
- `bytes`, `utf16Length`.
- `kinds`: names for `kind`: `Whitespace Comment Ident Number String Symbol Terminator Bullet Focus Quotation Keyword`.
- `kind`, `start` (bytes), `u16` (UTF-16 offset into `source`). A token ends where the next starts.
- `links`: columns `token`, `type` (0 refs, 1 symbols, 2 binders), `index`. Sorted by `token`. A token can have several.

To render: walk the tokens, slice `source` with `u16`, and look up `links` to decide what each token points at.

To convert a byte offset from another layer, binary-search `start` and read `u16`. A span at the file's end converts to
`utf16Length`.

Token kinds are a rendering hint and no parse. A token splits at every `.glob` span boundary, and a notation's symbol can
be one token or several. A comment is one token. `Keyword` covers a sentence's modifiers and leading words, and a fixed set of
reserved words wherever they occur.

## Limits

- **Sentence splitting knows no notations.** A notation symbol ending in `.` and followed by a blank reads as a sentence
  end. None occurs in the corpus, and the `-time-file` comparison detects it.
- **Elpi and Ltac2 quotations** other than `ident:{{ ... }}` have no special handling.
- **No types and no goals.** `.glob` holds neither. See the README for the optional type evaluation.
- **`binders`, `type` and `body`** come from a token scan and no Gallina parser. The fields are reliable for ordinary
  declarations. An unusual form can split elsewhere, and `sentenceSpan` stays exact.
- **`.glob` records a name once it is resolved**, so a sentence that never ran has no references. A failed `Fail` still
  appears as a sentence.
