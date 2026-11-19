---
title: Columnar JSON schema (v1.0)
description: Deterministic columnar JSON shards, byte-span invariants, and cross-reference edge semantics produced by FSharp.Data.Rocq.
---

# Columnar JSON schema (`v1.0`)

`FSharp.Data.Rocq.Extract` (`FSharp.Data.Rocq.Cli extract`) serializes a compiled `_CoqProject` into deterministic, timestamp-free columnar JSON shards so browsers, documentation tools, and non-`.NET` languages can query Rocq theories without installing Rocq or `.NET`.

## Output directory layout

```text
<out>/
  index.json                     files, all declarations & symbols, and the file dependency graph
  xref.json                      bidirectional cross-references between declarations and symbols
  files/<module>.outline.json    sentences, comments, headings, declarations, scopes, imports, notations
  files/<module>.refs.json       resolved .glob references and local binders
  files/<module>.tokens.json     UTF-8 source text and full token partition with UTF-16 offsets
```

Consumers open only the layers they need:
- Table of contents or module graph: `index.json`
- "Who uses this lemma?" across the workspace: `index.json` + `xref.json`
- Theorem list with attached prose: `files/<module>.outline.json`
- Full syntax-highlighted interactive source view: `outline.json` + `refs.json` + `tokens.json`

## Core conventions & invariants

- **UTF-8 half-open byte spans**: Every `start` / `end` pair is `[start, end)` in UTF-8 bytes of the `.v` file (`.glob` inclusive ends are normalized during extraction).
- **Columnar parallel arrays**: `{"start": [...], "end": [...]}` represents row `i` as `(start[i], end[i])`.
- **Sentinel values**: `-1` denotes `None` in integer index columns; `null` denotes absent strings or spans.
- **Deterministic incremental writes**: Each file's cache key hashes `.v` MD5, `.glob` MD5, schema version, and generator version. Incremental runs skip rewriting unchanged per-file shards while recomputing global tables identically to a clean `--force` extraction.

Six structural invariants are verified by the test suite against fixtures and full Rocq corpora:
1. **Exact token partition**: Concatenating all tokens reconstructs the `.v` file byte-for-byte.
2. **Token & character alignment**: Every span lands on both a UTF-8 character boundary and a token boundary, allowing binary search on `tokens.start` -> `tokens.u16` to convert any byte offset to UTF-16.
3. **Complete non-blank coverage**: Every non-whitespace byte belongs to either a sentence or a comment.
4. **Complete `.glob` accounting**: Every line of `.glob` is accounted for exactly once (header, `symbols`, `refs`, `binders`, or `globLines.unparsed`).
5. **Digest safety**: Stale `.glob` files are refused with exit code `2` unless `--allow-stale` is explicitly passed (`globStatus: "stale"`, omitting stale `.glob` facts).
6. **Sentence boundary parity**: Extracted sentence spans match `rocq compile -time-file` spans when oracle timing files are provided.

## Schema versioning

Every JSON file carries `"schema": {"major": 1, "minor": 0}` and `"layer"`:
- `minor` increments on backward-compatible field additions (consumers should ignore unknown properties).
- `major` increments on breaking structural changes (consumers must reject unknown major versions).

## Global shards

### `index.json`

| Field | Description |
| :--- | :--- |
| `generator`, `rocqVersion` | `{name, version}` and optional `--rocq-version` string |
| `files[]` | `path`, `module`, `digest`, `globDigest`, `globStatus` (`ok`, `stale`, `noDigest`), `bytes`, `shards.{outline,refs,tokens}.{path,bytes}`, `counts` |
| `declarations` | Columns: `id` (`<module>#<qualified>` or `<module>#@<sentence>`), `name`, `keyword`, `category`, `file`, `local`, `start`, `end`, `ending`, `docCount` |
| `symbols` | Columns: `qualified`, `kind`, `file`, `local`, `decl` (index into `index.declarations`, or `-1`) |
| `fileGraph` | `requires` (`from`, `to`), `externalRequires` (`from`, `module`), `references` (`from`, `to`, `count`), `externalReferences` (`from`, `module`, `count`), and optional `rocqDep` |
| `unknownKinds`, `unresolved[]` | Unknown `.glob` kind counts and in-project references without a matching symbol |

### `xref.json`

| Field | Description |
| :--- | :--- |
| `symbolCount`, `declarationCount`, `fileCount` | Table dimensions from `index.json` |
| `externals` | Columns: `module`, `qualified`, `kind` for targets outside `index.symbols` (node indices `>= symbolCount`) |
| `edges` | Columns sorted by `(to, from, where)`: `to`, `from` (`>= 0` declaration index, or `~f` bitwise complement for top-level directives in file `f`), `where` (`0` statement, `1` body, `2` proof, `3` directive), `count` |
| `toOffsets` | CSR row pointers: incoming edges for node `n` are `edges[toOffsets[n] .. toOffsets[n+1]-1]` |
| `fromOffsets`, `byFrom` | Outgoing CSR index: edges leaving owner slot `s` (`declIdx`, or `declarationCount + f`) are `edges[byFrom[k]]` for `k` in `fromOffsets[s] .. fromOffsets[s+1]-1` |

> **Source mentions vs. kernel proof terms:** An `xref.json` edge records that the `.v` source text explicitly mentions a symbol according to `.glob` (excluding local binders and `Require` commands). Terms inserted silently by elaboration, implicit arguments, coercions, typeclass resolution, or `auto` hints without being named in source text do not appear in `.glob`—use `rocq-capture` (`Print Assumptions`) when auditing kernel-level axiom dependencies.

## Per-module shards (`files/<module>.*.json`)

- **`<module>.outline.json`**:
  - `lineStarts` (byte offset per line), `sentences` (`start`, `end`, `category`, `keyword`, `decl`, `firstToken`, `endToken`, `modifiers[]`)
  - `declarations[]` (`id`, `name`, `nameSource`, `nameSpan`, `keyword`, `modifiers`, `category`, `scope`, `sentenceSpan`, `extent`, `statement`, `binders`, `type`, `body`, `proofs[]`, `symbols`, `docs`)
  - `comments[]` (`format`, `span`, `bodySpan`, `text`, `sentence`, `nextSentence`, `newlinesToNext`, `trailingSentence`, `decl`, `attachedTo`)
  - `headings[]`, `scopes[]` (`Section`, `Module`, `Module Type`), `imports[]`, `notations[]`, `symbols`, `uses`, `globLines`, and `diagnostics[]`
- **`<module>.refs.json`**:
  - `refs` (`start`, `end`, `target`, `line`), `targets` (`module`, `path`, `name`, `kind`, `binder`, `symbol`), and `binders` (`start`, `end`, `name`, `uid`, `line`).
- **`<module>.tokens.json`**:
  - `source` (UTF-8 file string), `bytes`, `utf16Length`, `kinds` (`Whitespace`, `Comment`, `Ident`, `Number`, `String`, `Symbol`, `Terminator`, `Bullet`, `Focus`, `Quotation`, `Keyword`), parallel `kind` / `start` / `u16` arrays, and `links` (`token`, `type` in `0=refs | 1=symbols | 2=binders`, `index`).
