# Rocq navigator: final report

Branch `navigator`, local only. `navigator` carries the breaking commits `318866e` and `61fbe33` from `tidy/public-api`, so that branch lands or is reviewed first.

## Final gates, on `bbb1e8c`

Each gate ran once on the final code. The last scoped review covered `95c45bb..e33acdc`. The fixes for its findings in `bbb1e8c` were checked by these gates and had no further independent review.

| Gate | Result |
|---|---|
| `dotnet fantomas --check navigator` | exit 0 |
| `build-site.mjs --strict`, fixture | exit 0, 5 pages |
| `dotnet run -c Release --project navigator/Rocq.Navigator.Tests`, with corpus and capture | 138 passed, 0 ignored |
| `dotnet build navigator/Nacara.Plugin.Rocq -c Release --no-incremental`, `--warnon:3390` | 0 warnings |
| Playwright, fixture | 247 passed |
| Strict build, then Playwright with `NAVIGATOR_CORPUS=1`, real-proofs snapshot | 62 passed |
| `review-comments.ps1 -Check` | No FOR-REVIEW comments |

Full-gate run times: client 53 s, fixture site 8 s, Release tests 18 s, Playwright 27 s.

## Not verified

- The current proofs. The Iris worktree fails to load (`theories/Audit.v: stale .glob`), so real-proof gates ran on a scratchpad snapshot.
- The argument list on real pages. It stays empty until the proofs' author adds `- [name:]` entries, and has run only on the fixture.
- Analyzers: this repository has no analyzer configuration. `samples`, `Ranvier.Css.View` and the library's own tests were not rerun for this batch.
- Real touch devices and rotation, Firefox, Safari, screen readers, `navigator.yml` on GitHub or Linux.
- Scroll cost on `Ranvier.Pure.Check` after the lists' owner rule, and first-paint shift on a slow connection.

## Behaviour to know

- About one screen of blank space follows the article with the client running. Jumps land at `calc(5vh - 4px)`.
- A stale `- [x:]` entry raises `rocq/argument-unmatched`, which fails `--strict`.
- A row whose same-named binders have different written types shows Rocq's printed type, or no type when Rocq prints an arrow for that binder, as with `shadowed` in the fixture.
- In the panel, the page holds a second copy of each list, hidden with `display: none` in other modes.
- A partially written `.glob` with a matching digest can load with fewer declarations.

## Left undone from the last review

- `Plugin.fs:372`: `checkPage` rebuilds `Article.blocks` per page. The cost was not measured.
- A comment inside a written type still appears in an argument row. Cards strip it.
- No test pins the listener order behind a touch on a link to the current hash.

## Open owner decisions

- Wire/Thoth (about 31 KB minified) against `JSON.parse`. Removing the server renderer from the client (about 43 KB minified).
- Keeping `collapsibleCode`, and the card section-comment and raw search kinds.
- `data-surface`, `data-name` and `data-collapse-over` stay, awaiting confirmation.
- Pushing, merging, a NuGet release, adding the plugin to the Ranvier docs, and replacing `proofs/docs.sh`.
- Deferred: the pretty printer, identifying differences, the proof states layer.

## Real-proofs measurements

Snapshot build, 18 pages, 11,421,136 bytes in all:

| File | Bytes | Gzipped |
|---|---|---|
| `navigator.js` | 360,909 | 109,496 |
| `navigator.css` | 26,688 | 5,255 |
| `Ranvier.Pure.Runs.html` | 1,451,863 | 119,998 |
| `search.json` | 411,963 | 41,449 |

Earlier measurements, from `58e4af5`:

- `rocq-capture`: 178.4 s for 2,613 symbols and 1,058 assumption entries, through `wsl.exe`. 49 symbols (1.9%) have no type: section names, section-local names, one notation and a few local definitions.
- No change was needed in Nacara, and `--strict` found no broken links in Ranvier's comments.
- Anime is used, for fold height.
- Pinned: Ranvier.View, View.Dom and View.Anime 0.1.44; Ranvier.Css family 0.1.1-alpha.1; Ranvier.Wire 0.1.1-preview.1; Fable.Ranvier.Dom 0.1.0-alpha.54; Nacara.Core 2.0.0-beta.6; Nacara.Plugin.Markdown and LinkValidator 1.0.0-beta.2; Nuglify plugin 1.0.0-beta.1; Markdig 1.3.2; Expecto 11.0.0-alpha8; FSharp.Core 10.1.403; Fable 5.27.0; Fantomas 8.0.5; animejs 4.5.0; Playwright 1.64.1.

## Rulings I made

Each reads: what I decided, why, and what it costs if wrong.

- fonts live at navigator/Nacara.Plugin.Rocq/assets/fonts, not navigator/assets/fonts — the spec says embedded in the plugin and the file map agrees — costs a file move if wrong.
- Task 1 Step 6 may drive the page with the built-in browser or, if a subagent lacks it, a throwaway Playwright run from the scratchpad — either gives the same evidence — costs nothing if wrong.
- batches run as Task 1; then Tasks 2+4 and Task 3 in parallel in separate git worktrees; then Tasks 5+6; then Tasks 7+8; each batch gets one reviewer over its whole diff — the owner asked for batching and speed, and overrides the skill's one-task-at-a-time default — costs a coarser review surface, with the final whole-branch review as the net.
- Fable is 5.27.0, not 5.13.0 — 5.13.0 fails compiling Ranvier.Wire — costs a tool version bump if a later Wire fixes it.
- Task 2's facts key is <Module>.<Qualified> rather than the plan's bare Qualified — Qualified is module-relative and collides across modules — costs a key rewrite in facts.json if wrong.
- Tasks 5+6 start before the 2,3,4 review returns — the owner asked for speed and any fix lands by merge — costs a possible rebase of one fix.
- a guarantee title is a line entirely bold on its own, firstBoldLine stands and the fixture changes — every real proof comment sampled has that shape — costs missed guarantees if some real comment has prose on the bold line; Task 6's independent scan and Task 14's corpus check would show it.
- comment links allow only http, https, mailto, relative and anchor URLs — spec says comment text executes nothing — costs an unlinked label for any other scheme.
- Tasks 7+8 start before the 5,6 review returns — speed — costs a possible merge of one fix.
- root-level modules list first with no heading, then directories in _CoqProject order — inventing a label for the root would name something the project does not — costs a departure from strict _CoqProject order for root files.
- RocqOptions gains Extensions: IMarkdownExtension list, default [] — spec line 115 is binding over the brief's option list — costs one public field and setter.
- Tasks 11+12 start before the 9,10 review returns and run in parallel in worktrees t11 and t12 — speed — costs a possible merge of one fix and a small union in Islands.fs and Main.fs.
- search rows use the View's keyed forEach by Href rather than a direct createProjection call — the View's keyed region is built on core projections, which is what the plan's row asks for — costs one line if the owner wants the direct call.
- Tasks 13+14 start before the 11,12 review returns — speed — costs a possible merge of fixes.
- three brief colours change to pass AA (Journal light Dim #726a5c; Family light Keyword and Link #5e61d3) — spec requires AA — costs a slight hue shift the owner can see in the screenshots.
- the fix batch takes B1, the marker half of B2, I1-I4, wasOpen from I5, and M1, M2, M4-M12 plus the stub island — none needs an owner decision — cost if wrong: rework visible in the diff.
- B2's list placement waits for the owner's references choice — the mockups are already in front of them — cost if wrong: B2 stays open until they answer.
- I6 and M3 go to the owner — I6 is a spec question and M3 is spec:116 as the owner wrote it — cost if wrong: one more question.
- opener, data-closing and data-lists stay and are raised with the owner as additions to the plan's state list — no core primitive holds them — cost if wrong: small rewrite.
- park the re-review's Minor items other than the checkHydrationMarkers summary tag (missing page file skipped silently; prior-selection double click nets one toggle; non-array assumptions read as Closed in hand-edited files) — none reachable from generated output in normal use — a stuck fold toggle or a wrong "No axioms" on a hand-edited facts file if wrong.
- start settings-brief sections 1-4 now and hold section 5 until the owner approves the argument list layout (owner approved the hover, not the list) — sections 1-4 don't depend on it — rework of the shared .doc spacing if the chosen layout conflicts.
- F1 writes folds at the click and in OnOver and deletes the first latch, with no core deferred effect — the signals' only writers are known and this also fixes clicks on the pressed value — a future writer has to remember the fold write if wrong.
- when a heading is the section in view, the lists' owner is the previous declaration if on screen, else the first declaration in the viewport, else none — "No definition in view" was false at page top — differs from the brief's empty line if the owner prefers strictness.
- keep data-surface, data-name and data-collapse-over as DOM state or server HTML, which view-uses-core.md allows, and flag them to the owner — none is a store — removal is small if the owner objects.
- batch the review fixes, the owner's inline references spacing report and section 5 in one dispatch with separate commits — the same files and one writer — a larger re-review diff.
- In the panel renders a second copy between the statement and the proofs — the owner approved placing it above the proof, and the reviewer's `display: contents` route needs about eight rules where this removes rules — costs repeated HTML for documented declarations with entries.
- Keep two presses of Esc in the badge dialog — the owner approved the recommendations, which kept this default — costs one more key press.
- Keep a stale entry as a warning that fails `--strict` — a stale comment is what a strict build exists to catch — costs a failed strict build on a renamed binder until the comment is updated.
- Fix hyphenated operators with a no-wrap span in lines of code — a joiner character would change the bytes — costs about 21 bytes per operator.
