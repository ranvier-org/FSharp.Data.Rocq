# Rocq navigator: design

A Nacara plugin that turns a compiled Rocq project into public, static documentation pages, built from
`FSharp.Data.Rocq` on `Ranvier.View`, `Ranvier.Css` and, where it earns its place, `Ranvier.View.Anime`. It replaces
rocqnavi for the Ranvier core proofs, and a Nacara docs site can add it as one more collection.

## Core and view state

**Core primitives used:**

| Primitive | Job |
|---|---|
| `Signal` | Current look, light or dark mode, each reading setting, the settings surface and its open state, hovered or focused reference, the chip whose dialog is wanted and shown, search text, search open, section in view |
| `createMemo` / `createMemoWithComparer` | Search results from the text; the hover card's symbol from the hovered reference, skipping unchanged symbols |
| `createProjection` | Keyed rows of the search results |
| `createEffect` / `createEffectOn` | Writes look, mode and the reference placement to the root element, a fold setting to its folds, and opens and closes the settings dialog when its signal changes |
| `createRoot`, `Owner`, `onCleanup` | Lifetime of each hydrated island and its listeners |

**View-owned state:**

| State | Why the core cannot hold it |
|---|---|
| DOM nodes of the server-rendered article | DOM state; the article is static HTML |
| The one delegated `pointerover`/`focusin` listener on the article | Event delegation |
| The stateless `click` listener on the document for settings triggers and a click outside the popover | Event delegation |
| `data-lists` on the root: `pane`, `badges`, or absent under definitions | DOM state; the stylesheet reads it (owner's agreement, 2026-11-09) |
| `open` on `details`, and `data-closing` during a fold's closing motion | DOM state; the stylesheet and the browser read them (owner's agreement, 2026-11-09) |
| `.current-decl` on the section in view. `aria-current` stays on the outline links | DOM state; the stylesheet reads it (owner's agreement, 2026-11-09) |
| `<dialog>` open state, and the opener that focus returns to | Neither has a core equivalent (owner's agreement, 2026-11-09) |
| `data-surface` on the settings dialog, bound to a signal | The last value written to a DOM target; the stylesheet reads it |
| Hover card position, measured from the reference's rectangle | The last value written to a DOM target |
| Server HTML | Written by the build |

Saved look, mode and reading settings live in `localStorage`, which is browser storage, one key per setting. Signals hold the current values and start from the saved ones.

The view adds no ownership tree, reconciler, selection cache or commit queue.

## Intent

The owner wants a better-looking system with better navigation, where comments are documentation tied to the values
they describe, so that hovering any name shows that documentation. Rocqnavi output is hard to move around in, and its
comments are not attached to anything. The `proofs/docs.sh` pipeline already patches behaviour in with `ranvier.js` and
`guarantees.py`.

## Decided by the owner

- **Layout: Reader.** Modules on the left. A doc-first article in the middle, with each statement under its doc and proofs
  folded. Page outline and cross-references on the right.
- **Two looks, both built:** Ranvier family (violet accent `#6669d7` light / `#9b9ef0` dark, sans-serif) and Journal
  (warm paper, serif prose, restrained colour). See [Visual reference](#visual-reference). A switch on the page and a
  light and dark mode for each look are the author's call.
- **Rocq layers in the first version:** inferred types and an axiom badge from `Print Assumptions`.
- **Public static page.** Readers download files; nothing runs Rocq for them.
- **MIT licence.**
- **Packages:** `Ranvier.View` and `Ranvier.Css`, plus `Ranvier.View.Anime` if suitable.
- **Nacara:** the navigator is a Nacara plugin, so it can come into the docs natively. The front end stays entirely ours:
  the plugin's layout replaces the theme's chrome on these pages.
- **Demo data:** the Ranvier core proofs (see [Data](#data)).

## Goals

1. Every module reads top to bottom as headings, doc prose, statements and folded proofs.
2. Every reference in statements, proofs and doc code has a card on hover. A reference that is a link also opens its card on
   keyboard focus, and activating it goes to the definition. The card shows the type or source header, the attached doc, the
   module and the mention count.
3. A reader finds any declaration in under a few keystrokes with `Ctrl+K` or `/`.
4. The site opens on the Guarantees page: what the proofs promise, in plain language, grouped by module.
5. Each declaration shows what it uses and what uses it.
6. Pages are readable, foldable and linked with JavaScript off. Cards, search and the look switch need JavaScript.

## Non-goals

- Proof states, tactic stepping, `rocq-lsp` or `fcc` data. A later version may add them.
- Links or hover inside printed types, which are plain text with no positions.
- Running Rocq in the browser, editing, or comments from readers.
- Publishing the plugin to NuGet, deploying the site, or changing the Ranvier repository. Adding the plugin to the
  Ranvier docs is a later step. A small demo site in this repository builds a directory.
- Copying rocqnavi code or assets. It is GPL-2.0.

## Anti-goals

- A page that looks good but loses source fidelity: statement and proof text is byte-exact from the `.v`.
- Motion a reader notices for its own sake.
- Claims the data cannot support: a missing fact shows as absent, never as a green badge.

## Page structure

**URLs.** One page per module, at `<root>/<Module>.html` through `Route.file`, with anchor `#<declaration name>`. `root` is
the plugin's route prefix, empty by default. Existing comments link in this form
(`Ranvier.Pure.Witnesses.html#walk_visits_added_observer`), so their source needs no edits. The plugin resolves such a
link to its site URL when it renders the comment, which is also what `LinkValidator` can check. A heading gets anchor
`#h-<slug>`.

**Guarantees (`<root>/index.html`).** Each `Theorem` and `Corollary` whose attached `(**md` comment opens with a bold line
becomes an entry titled by that line, under its module. Entries sort into groups, first match winning (the author's call):

| Group | Rule | Input |
|---|---|---|
| Open | The proof ends `Admitted` | `proofs[].ending`; always on |
| Falsified | The name ends with the configured suffix | `navigator.json`: `"falsifiedSuffix": "_falsified"` |
| Checks | The comment holds the configured marker | `navigator.json`: `"checkMarker": "*Check, not a guarantee.*"` |
| Guarantees | Every other entry | |

`navigator.json` sits beside `_CoqProject`, is named in the plugin's options, and is optional: without it only Open and Guarantees
exist. The tool stays generic, and it does not read `BUGS.md`. Open and Falsified entries are marked in the structure
and never show a passing badge. An Admitted theorem's `Print Assumptions` result is therefore not presented as a pass.

**Article order.** Source order. Each sentence renders as one of:

| Source | Rendering |
|---|---|
| Heading comment `(** * x *)` | A heading at that level |
| Declaration with an attached doc | Doc prose, then the statement |
| Declaration with no attached doc | The statement in the same pane as a documented item, with no prose |
| `Proof. ... Qed.` | `<details>` in the footer of the statement's pane, closed, labelled with its ending (`Qed`, `Defined`, `Admitted`) |
| Other commands (`Import`, `Section`, `Instance`, ...) | Compact code; the file stays complete |
| Comment attached to nothing | Prose in place |

Two optional `navigator.json` settings fold a statement's code body, and both are off without them. `"collapsibleCode": true`
makes the rendered first line of every multi-line body the fold for the rest, starting open. `"collapseCodeOver": <lines>`
does the same for a body longer than that, starting closed. A folded body shows its first line and its length.

A page's imports collect in one closed block after its opening heading and prose: imports and `Open Scope` forms at file top
level, before the first scope or declaration. Any other, including a `Local` or `Global` form inside a `Section` or `Module`, stays in
place as compact code with its indent bytes. Source lines are blocks whose wrapped text hangs four columns past their own indent, and
copied text stays exact.

`Admitted` shows a visible warning. Every byte of the `.v` appears on the page, apart from comments rendered as prose.

**Docs.** `Comment.AttachedTo` and `Declaration.Docs` decide attachment. `(**md` bodies are Markdown, rendered by Markdig at the
version `Nacara.Plugin.Markdown` uses, with any `IMarkdownExtension` the site registered and raw HTML disabled. `(** ... *)`
bodies are coqdoc, of which headings, `[code]` and paragraphs are handled. Code in docs that names a symbol in scope becomes
a reference.

**Right pane.** Outline of the page, with the section in view marked. For the declaration in view, "Uses" and "Used by" from
`CrossReferences`.

**Reading position.** The section in view follows a line that moves through the viewport as the page scrolls:
`progress = scrollTop / (scrollHeight - clientHeight)`, 0 when nothing scrolls, and `line = clientHeight * progress`. The line
is at the view's top at the page's start, its middle halfway and its bottom at the end. The section in view is the last to
start at or above the line, else the first to start inside the viewport, else none. A jump lands its target at `landingMargin`
while the line may be far below, so a `location.hash` target whose top lies within 6 px of the landing position is the section
in view. Scrolling away releases it, with no flag. Updates run on a passive `scroll` listener, `hashchange`, `resize`, and a
`ResizeObserver` on the article for folds that change the page's height. Measured on `Ranvier.Pure.Check` (197 sections, about
1,400 cards): 0.3 ms median and 1.1 ms at most per update with layout clean, and 2.1 ms median and 3.2 ms at most for a jump that
changes the section and pays for the layout.

**Cross-references without JavaScript (the author's call).** Each declaration's lists are static HTML, in a `<details>`
under its statement. Lists show the first 8 entries, with the rest in a nested `<details>`, so there is no separate list page.

**Reference placements.** The `References` setting picks one above 900 px. Below 900 px the lists stay inline folds and the
setting's row is hidden. The head script applies the saved value before first paint through `data-lists`. A client that fails
to start removes `data-lists`, so the inline lists return for every value.

| Placement | `data-lists` | Rendering |
|---|---|---|
| Pinned (default) | `pane` | The bottom region of the right pane, always in view, at most 46vh, with its own scroll. A header names the definition beside a 3 px `--link` bar, and the section in view carries the same bar inside its panel through `.current-decl`. A definition with no lists shows one quiet line. A heading in view leaves the declaration before it while any of it is on screen, and otherwise shows the quiet line, so a jump never shows a stale name. |
| Badges | `badges` | A server-rendered `uses N · used by M` chip per definition with any entries, a real `<button>` outside the `pre`, at 10 px, in the panel's background and border colours, clear of the caret and line-count column. `.decl` spacing grows in this mode only, so a chip never touches the previous panel. The chip opens a dialog with the definition's name and lists on hover, keyboard focus and tap. It reuses the card's hover delay and placement, and it ends at the chip's right edge, so it never covers the definition's caret. It stays open while the pointer is on it or on a card opened from it. Enter moves focus into it, Tab past its end returns to the chip, and Esc closes the top layer and returns focus. |
| Under definitions | absent | The inline `details.refs`, visible. A `References open` setting (Closed by default) shows only in this placement and below 900 px. |

**Settings.** One `<dialog>` in one island, `settings`, hosted outside the panes. Plain `<button data-settings-open>` triggers sit in
a footer pinned to the bottom of the left pane, and at the end of the Modules drawer below 900 px. They stay hidden with `visibility`
until `data-client=ready`, which keeps their space. Each opening picks the surface with `matchMedia`:

| Condition | Surface |
|---|---|
| Width at most 600 px | Dialog: `showModal`, centred, dimmed backdrop |
| `(pointer: coarse)`, wider than 600 px | Side panel: `showModal`, full height at the inline end, light backdrop; a tap outside closes |
| Fine pointer, wider than 900 px | Popover: non-modal `show`, rising from the footer; a click outside and Esc close |
| Fine pointer, 601 to 900 px | Dialog, since the footer is inside a drawer |

A resize that changes the row re-picks the surface. Focus moves into the surface on opening and returns to the trigger on closing.

Controls apply live and save per key in `localStorage`: Look (Family, Journal) and Mode (Auto, Light, Dark), on their existing keys;
Definitions (Open, Closed, Close long ones with a line count; unset leaves the site's `collapseCodeOver` output standing); Proofs and
Imports (Closed, Open); References and References open, as above. Reset clears every saved key and restores the server's state with no
reload, using the site's limit from `data-collapse-over` on the body. Invalid values and blocked storage fall back to defaults.

Look, mode and `data-lists` apply in the head script. Fold settings need the elements, so a small inline script directly after the
article sets `open` from the saved values, and only when a value is saved. A jump to a target inside a closed fold, or to a definition
whose own body is closed, opens that fold.

**Left pane.** Modules in `_CoqProject` order, grouped by directory, with declaration counts.

**Narrow screens.** Below about 900 px the side panes collapse into drawers. Cards become tap-to-open. No horizontal page
scroll; long code wraps.

## Hover card

| Part | Source | When absent |
|---|---|---|
| Type | `Check` capture | Source header of the declaration |
| Doc | Attached comment | "No documentation" in quiet text |
| Module, mention count | `RocqProject` | Always present |
| Axiom badge, on theorems | `Print Assumptions` capture | No badge |

Local binders show their inferred type where the capture has it. A reference outside the project (`stdpp`, `Corelib`) shows
its qualified name and "external"; those libraries are not documented or linked.

Cards open after about 120 ms, stay open while the pointer is on them, and close on `Esc`. Keyboard focus opens a card for a
reference that is a link. A reference rendered as a span has no link and stays unfocusable.

## Data

**Project.** `proofs/_CoqProject` on Ranvier branch `claude/iris-rocq-ranvier-project-f3d6ae`: 16 files, 455 KB, compiled
with Rocq 9.1.0. No file currently imports Iris, so the demo shows Rocq model proofs. That worktree belongs to another
session and is read only.

**Facts step.** A separate command the plugin adds to the site's command line, `rocq-capture`, runs `rocq repl` over the
compiled project and writes one `facts.json`: per symbol the `Check` text, per theorem the `Print Assumptions` result, plus
the Rocq version and each file's digest. It uses `Require Import` of every project module in `_CoqProject` order, so names
print in their shortest unambiguous form, after any `captureImports` lines from `navigator.json` (the author's call: probes
showed names like `is_Some` need `From stdpp Require Import prelude gmap`). Measured at about 2.2 s for 1,018 symbols and 18 s for 234 theorems. Local-binder types come from the
`Check` of the enclosing theorem, by matching binder names.

**Without Rocq.** The plugin takes `facts.json` as an option. Absent, the site still builds, with source headers in cards and
no badges. If a file's digest in `facts.json` differs from the project, the build reports the error `rocq/facts-stale` unless
told to drop facts for that file. A build never runs Rocq, and `FSharp.Data.Rocq` itself gains no Rocq dependency.

**Client data.** A `symbols.json` per module and one `search.json`, fetched on first hover or first search. Pages carry the
symbol's identifier on each reference in a data attribute.

## Architecture

Four projects under `navigator/` in this repository. The library project stays free of packages, and the navigator
projects are `IsPackable=false` for now.

| Project | Compiled by | Holds |
|---|---|---|
| `Rocq.Navigator.Shared` | .NET and Fable | Island components, typed styles and tokens, data types and codecs |
| `Nacara.Plugin.Rocq` | .NET | The plugin: collection, front matter format, content transform, Reader layout, assets, `rocq-capture`. Reads `RocqProject` directly |
| `Rocq.Navigator.Client` | Fable, then esbuild | Hydrates the islands. The bundle is embedded in the plugin, so a consuming site needs neither Fable nor Node |
| `Rocq.Navigator.Site` | .NET | The demo site: a few lines of `Site.create` over the plugin |

**The plugin (the shape of `Nacara.Plugin.FSharpApi`).** `Rocq.collection` is a producer that generates one document per
module plus Guarantees, each depending on its `.v`, `.glob` and `facts.json`. Generated documents use the extension
`.rocqpage`, with the plugin's own front matter format and content transform. The Markdown transform therefore never sees
them, which is what keeps source text byte-exact. `Rocq.reader` is the layout. It renders the whole document with
`Server.renderDocument` and hands it to Nacara as raw text. The site can use it, or supply another layout over the same
semantic article. Problems are build diagnostics named `rocq/...`, and `--strict` is Nacara's own.

**Islands.** The article is server-only HTML with no hydration, because a 60 KB module is large and static. Hydrated
islands: search dialog, settings, hover card layer, section-in-view marker. A failure in the client leaves a
fully readable page.

`Server.renderDocument` and page declarations assume one host, and hydration removes marked head entries it did not adopt.
The first implementation step proves several islands on one static page, inside a Nacara build. If they do not work, the fallback is one host for
the islands' shell with the article in a plain element outside it, or mounting the islands fresh on the client; the
article stays server-only either way.

**Build output.** The site's output directory, with URLs from `Site.baseUrl`, so a GitHub Pages subpath is a setting: HTML,
one extracted CSS file, one JS bundle, data files, fonts. Link and anchor checks come from `Nacara.Plugin.LinkValidator`;
the plugin adds its own check that every `data-ref` has a card. Output must also survive `Nuglify.minifyHtml`, which the
Ranvier docs turn on: the byte check runs on the written file with it enabled.

**Styles.** Typed `Ranvier.Css`, with static CSS from the published `Ranvier.Css.Build` (`Compiler.compile` over explicit
`CssModule` records), shipped as a plugin asset, so CI needs no sibling checkout (the author's call). `Ranvier.Css.Compile` is an unpublished tool and is
not used. Looks and modes are the same token set redefined on `:root[data-look]` and `[data-mode]`. Mode follows the system unless overridden. Both looks ship in one
stylesheet. An inline script sets the attributes before first paint, to avoid a flash.

**Fonts.** Self-hosted, free-licensed: Inter, JetBrains Mono, and a serif for Journal. Unicode in proofs (`∀ ∧ → ∅ ∘`) must
render in the mono face.

**Motion.** Only where it keeps a reader's place: card appearance, folds opening and closing, and the target settling after a jump. With
`prefers-reduced-motion` all are instant. Use `Ranvier.View.Anime` when the bundle cost is acceptable and the versions
resolve; otherwise CSS transitions, and say so. Fold height is the case CSS handles worst.

**Packages.** `Nacara.Core` (2.0.0-beta.6), `Nacara.Plugin.LinkValidator`, Markdig, `Ranvier.View`, `Ranvier.View.Dom`,
`Ranvier.Css`, `Ranvier.Css.Bindings`, `Ranvier.Css.Build`, `Ranvier.Css.View`, `Ranvier.View.Anime`. Published versions differ (`Ranvier.View` 0.1.48, `Ranvier.View.Anime` 0.1.44), and
the View family depends on itself at exact versions. The first implementation step finds the newest set that restores and compiles, in both
.NET and Fable, and pins it. Prereleases are acceptable. If no set includes Anime, Anime is dropped, not forced.

## Constraints

- `Ranvier.View` rules: the view uses core primitives, and this document's first section is binding. New view-owned state
  needs the owner's agreement.
- Doc comment rules from Ranvier's `comments.md` apply to code here.
- Do not edit Ranvier's `proofs/`, `src/Ranvier` or docs site, or the Nacara repository.
- Follow Nacara's plugin conventions: `create` / `createWith`, `register` / `registerWith`, an options record with `defaults`,
  one setter per option, and diagnostics that name the situation and give a hint.
- Tests must not hardcode real proof names. Tests run on the committed fixture, with the proofs as an opt-in corpus.

## Acceptance

Run on the fixture in CI and on the proofs as a corpus:

1. All modules render, and every declaration has its anchor.
2. `LinkValidator` passes on every internal link and anchor, and every `data-ref` has a card.
3. The bytes of each statement and proof on the page equal the `.v` span.
4. Every Guarantees entry matches an independent scan of the comments.
5. Browser checks under Chromium, for both looks and both modes: card opens by hover and by keyboard, search finds a
   declaration and navigates, fold opens, look and mode persist across pages, no hydration mismatch reported, no console errors.
6. With JavaScript off, a module page reads, folds and links.
7. At 375 px there is no horizontal page scroll.
8. Text meets WCAG AA contrast in all four look and mode combinations.
9. `prefers-reduced-motion` removes motion.
10. Report JS, CSS and largest-page sizes and build time. There is no fixed budget in the first version.

The owner judges "looks better" against rocqnavi output and the mockups. One real module page, rendered in both looks, goes
to the owner for review before the styles spread to the other components.

## Left to the builder

Search ranking; exact spacing and type scale within the mockups; the serif face; test tooling; how Fable and esbuild are
invoked to produce the embedded bundle.

## Visual reference

The mockups are in [`2026-11-09-rocq-navigator-mockups/`](2026-11-09-rocq-navigator-mockups/): `layout.html` (the chosen layout
is Reader, option A), `visual-style.html` (the chosen looks are A and B) and `proof-layers.html` (the chosen layers are 1 and
2). They are HTML fragments, unstyled outside their own `<style>` block. The tokens below cover the two looks shown there.

| Token | Ranvier family, dark | Journal, light |
|---|---|---|
| Background | `#0f1419` | `#fbf9f4` |
| Side pane, code | `#161c23` | `#f3efe6` |
| Line | `#2a3340` | `#ddd6c8` |
| Ink | `#e8ebf0` | `#23201b` |
| Soft text | `#c3c9d3` | `#3d3931` |
| Dim text | `#8d97a5` | `#7a7264` |
| Selected | `#21224a` | `#e8e1d2` |
| Keyword | `#9b9ef0` | `#7c2d3f` |
| Binder | `#e5c07b` | `#4f6a3a` |
| Link | `#9b9ef0` | `#27508f` |
| Passing badge | `#7fd8a6` | `#2f6b47` |
| Corner radius | 7 px | 3 px |
| Body | Inter | Georgia-like serif |
| Title | Inter 19 px, 650 | Serif 22 px, 500 |

Ranvier family light takes its base from the docs theme (`--nacara-bg #ffffff`, `--nacara-bg-subtle #f6f7f9`, primary
`#6669d7`). Journal dark is warm dark paper, in the same restrained hue families. Both are the builder's to tune for
contrast.
