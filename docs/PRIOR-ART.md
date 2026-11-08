# Prior art

Surveyed on 2026-11-09 from repositories, opam, NuGet and the papers named below. Tools marked "run" were run on Rocq 9.1.0
against a 15-file corpus (296 KB of `.v`), in WSL on one machine. The survey happened after FSharp.Data.Rocq was first built, and
the decision below was open to replacing it.

## Decision: build the base, and use two existing tools as optional layers

No existing tool turns `.v` and `.glob` files into structured data. Each one lacks several of these, which the base needs
together: comments as written, resolved references, declaration and proof structure, and reading without Rocq.

| Tool | Role |
|---|---|
| FSharp.Data.Rocq | The base layer, read from `.v` and `.glob` without executing anything |
| `rocq-lsp` (`fcc`) | Optional layer for goals and parsed ASTs. It has byte offsets, so it can join to sentences |
| `coq-dpdgraph` | Optional layer for kernel-level dependencies, which `.glob` edges lack |

Neither optional layer is built yet.

## Candidates

| Tool | Emits | Rocq 9.1.0 | Maintenance | Licence | Measured |
|---|---|---|---|---|---|
| **rocq-lsp / coq-lsp** `fcc` (run) | JSON or sexp AST per sentence with byte offsets; goals with `goaldump` | Yes, `0.2.5+9.1` | Active, pushed 2026-10 | LGPL-2.1 | All files: 28.4 s, 17.7 MB. Largest file with goals: 6.7 s, 25 MB |
| **coq-dpdgraph** (run) | `.dpd` text: kernel dependency graph with kind, `prop` and body flags | Yes, `1.0+9.1` | Active, pushed 2026-10 | LGPL-2.1 | All files: 0.84 s, 388 KB, 1,034 nodes, 12,444 edges |
| **Alectryon** (run) | JSON: sentence text, messages, goals, and text between sentences | Yes, 2.0.0 | Active, pushed 2026-06 | MIT | `coqc_time` driver: 5.8 s on the largest file, 302 KB, no goals. `vsrocq` driver: 3.9 s on a 7 KB file, 556 KB, with goals. `vsrocq` failed on the largest file |
| **vsrocq** | LSP server for editors | Yes, 2.5.1 | Active | MIT | Only through Alectryon |
| **coqpyt** | Python client for coq-lsp, for interactive proof navigation | Not run. Its README names coq-lsp 0.2.3 | Pushed 2026-05 | MIT | Not run |
| **SerAPI** | Sexp serialisation of ASTs and goals | **No.** Last release is 8.20; the README says development stopped in favour of coq-lsp | Stopped | LGPL-2.1 per opam | Not run |
| **TheoremExtr** | JSON of theorems and definitions: statements, types, dependencies, scope, line numbers | **No.** Targets 8.20 and integrates code into the compiler | Artifact of arXiv 2606.04704, last push 2026-06 | No licence file | Not run |
| **`rocq doc`** (coqdoc) (options read) | HTML, LaTeX, TeXmacs, plain text; no JSON | Yes, ships with Rocq | Part of Rocq | LGPL-2.1 | Not run |
| **coq2html** | HTML; reads `.glob` | Not checked | Pushed 2026-06 | GPL-2.0 | Not run |
| **rocqnavi** | HTML; reads `.glob`. In its `bin` and `lib` sources, JSON appears only in the LSP client and type lookup | Yes, per the earlier trial | Active, pushed 2026-11 | GPL-2.0 | Not run here |
| **IBM/pycoq** | Python API over SerAPI | No | Archived 2022 | MIT | Not run |
| **CoqGym** | JSON proof dataset through SerAPI | Not checked; its tree vendors an old Coq | Not checked | Not checked | Not run |

**.NET:** none found. NuGet has no package for `coq` or `rocq` that concerns the prover, and GitHub searches for F# and C#
`.glob` or Rocq tooling found nothing relevant. `OmniSharp.Extensions.LanguageClient` is a general LSP client, which could
drive `rocq-lsp` from .NET and would not change this comparison. No standalone `.glob` parser library turned up in opam or
GitHub code search; coq2html, rocqnavi and coqdoc each carry an internal one.

**Jasmin:** a GitHub code search of its repository for `coqdoc` found nothing, and it offers no reusable documentation tool
in that search. This check was shallow.

## Against the fields FSharp.Data.Rocq needs

| Field | FSharp.Data.Rocq | `fcc` astdump | dpdgraph | Alectryon JSON |
|---|---|---|---|---|
| Sentence boundaries | Yes | Yes | No | Text only, no offsets |
| Keyword as written | Yes | Parsed as a variant, such as `Lemma` | Kind only | In the text |
| Statement, binders, type, body | Token scan | **Exact, a full AST** | No | No |
| Proof grouping and ending | Yes | Separate sentences for a consumer to pair | Flag only | No |
| Comments as written | Yes | **No.** Absent from the dump, also with `--record_comments` | No | As text between sentences |
| Doc attachment, headings | Yes | No | No | No |
| Sections and modules as a tree | Yes | Separate sentences | Short module name only | No |
| Resolved references with spans | Yes | **No.** The AST holds names as written | No spans | No |
| Binders | Yes | In the AST, unresolved | No | No |
| Notations resolved at use | Yes | Keys in the AST | No | No |
| Dependencies | Source mentions | Names as written | **Kernel level** | No |
| Goals at each step | No | **Yes** | No | **Yes** |
| Works from `.v` and `.glob` without Rocq | **Yes** | No: re-executes | No | No |

## Speed, for scale

A sequential `rocq compile` of the corpus took 21.8 s in WSL, and a project compiles anyway to produce its `.vo` and
`.glob`. Reading those results with FSharp.Data.Rocq added about 0.3 s, measured on Windows. `fcc` executes the proofs again, 28.4 s
in all, unless it replaces the compile. Timings from the two systems are not directly comparable.

## Why not wrap `rocq-lsp`

It is the strongest candidate, and its sentence count on the largest file matched Rocq's `-time-file` and FSharp.Data.Rocq (1,138).
Wrapping it would still leave comments and resolved references to get from the `.v` and `.glob`, so the scanner and the
`.glob` reader would both remain. It would add a Rocq requirement to generation, and about 90 times the run time, in
exchange for exact statement structure and goals. Those are worth having as a layer, joined on byte offsets, and the
limit on `binders`, `type` and `body` in `SCHEMA.md` is the first thing such a layer would lift.

## Not verified

- **`vsrocq` failure:** Alectryon raised `BrokenPipeError` at client shutdown after about 18 s, on the largest file. The
  cause was not diagnosed, so this may not be a defect in either tool.
- **`rocq-lsp`'s comment recording:** `--record_comments` left comments out of the `astdump` output. Other `rocq-lsp` APIs
  may expose them, and were not explored.
- **TheoremExtr's fields:** from the paper and README, whose schema is not published in detail. It was not run.
- **Coverage of "not run" rows:** from their documentation only.

## Sources

- [rocq-lsp](https://github.com/rocq-community/rocq-lsp), including `etc/doc/PROTOCOL.md` and `plugins/`
- [coq-dpdgraph](https://github.com/rocq-community/coq-dpdgraph)
- [Alectryon](https://github.com/cpitclaudel/alectryon)
- [vsrocq](https://github.com/rocq-prover/vsrocq)
- [coqpyt](https://github.com/sr-lab/coqpyt)
- [SerAPI](https://github.com/rocq-archive/coq-serapi)
- [TheoremExtr](https://github.com/Rw1nd/TheoremExtr) and [its paper](https://arxiv.org/abs/2606.04704)
- [coq2html](https://github.com/xavierleroy/coq2html)
- [rocqnavi](https://github.com/affeldt-aist/rocqnavi)
- [IBM/pycoq](https://github.com/IBM/pycoq)
- [CoqGym](https://github.com/princeton-vl/CoqGym)
- [OmniSharp.Extensions.LanguageClient](https://www.nuget.org/packages/OmniSharp.Extensions.LanguageClient)
