---
title: Prose syntax
---

Documentation lives in the `.v` file, in comments. This page quotes [`Prose.v`](example.md) and shows, under each quote, the HTML that the plugin's own renderer produced from it during this build.

[The same file as a page]({{base}}example/Tally.Prose.html) shows the comments in place.

## The three comment forms

| Opening | Format | Becomes |
|---|---|---|
| Two stars | Doc | Prose with headings, paragraphs and code spans |
| Two stars and `md` | Markdown | Prose with everything Markdown offers |
| One star | Plain | Prose that reads like a doc comment, or code when it sits inside a sentence |

## Doc comments

### Headings

One to four stars open a heading:

<!-- prose: A doc comment heading -->

<!-- prose: A second-level heading -->

### Paragraphs and code

Lines join into a paragraph. Square brackets mark code, and a name that resolves becomes a link with a hover card:

<!-- prose: A paragraph in a doc comment -->

### No lists

A doc comment has headings, paragraphs and code spans, and nothing else. A leading dash stays text:

<!-- prose: This dash is plain text -->

## Markdown comments

### Headings

One to six `#` marks open a heading:

<!-- prose: A Markdown heading -->

### Everything else

Markdown comments use [CommonMark](https://commonmark.org). Backticks mark code, and a name that resolves becomes a link, as with square brackets in a doc comment:

<!-- prose: Another, with a -->

Pipe tables render too, as in that comment. Other Markdig extensions can be added to the site's options:

```fsharp
|> Rocq.extensions [ Markdig.Extensions.EmphasisExtras.EmphasisExtraExtension Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough ]
```

Three rules hold for every comment:

- Raw HTML appears as text.
- A link with a scheme other than `http`, `https` or `mailto` loses its link, and keeps its text.
- A fenced block holds text only, so names inside it stay unlinked.

Extensions cannot enable raw HTML.

## Links and names

| You write | It links to |
|---|---|
| `[total]` or a backticked `total` | The declaration, when the name matches one symbol, or one in the same file |
| `[text](Core.html#append)` | The declaration on that module's page. `Core` can be the full module name, or a dotted suffix naming one module |
| `[text](https://example.com)` | That address |

A name matching several symbols in other files stays plain code.

## Arguments

One `- [name:] text` line documents an argument of the declaration below. Lines indented past the dash continue the entry. It works in both prose forms:

<!-- prose: Arguments in a doc comment -->

<!-- prose: Arguments in a Markdown comment -->

The entries leave the comment's text, and appear in an Arguments list by the code and on the binder's hover card, with its type. Here is the first of those declarations on its real page:

<!-- frame: Tally.Prose.html#push -->

An entry naming no binder of the statement still appears, and raises the build warning `rocq/argument-unmatched`, which fails a `--strict` build.

Readers choose how arguments appear in [Settings](settings.md).

## Attached and free comments

A comment in any form documents the declaration directly below it. A blank line between them breaks the link, and the comment reads as prose between declarations:

<!-- prose: A comment that has a line of its own -->

A comment inside a sentence stays in the code. A comment on the same line as the end of a sentence documents nothing below it. This declaration has both a plain comment above it, which appears as documentation, and one inside its statement, which stays where it is:

<!-- rocq: theories/Prose.v#with_plain_comment -->

<!-- frame: Tally.Prose.html#with_plain_comment -->

## Titles for the Guarantees page

A Markdown comment on a `Theorem` or `Corollary` whose first line is wholly in bold gives the theorem a title, and puts it on the [Guarantees page](guarantees.md):

<!-- prose: A first line wholly in bold -->

## What the library sees

The same comments are data. `Comment.Format`, `Comment.AttachedTo` and `Model.Headings` come from [the library](library.md#comments-and-headings).
