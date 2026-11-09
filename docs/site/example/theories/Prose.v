From Tally Require Import Core.

(**md # Prose syntax

Every comment form the navigator reads. The documentation site quotes these comments and renders
them with the plugin's own code.
*)

(** * A doc comment heading

    One to four stars open a heading in a doc comment. *)

(** ** A second-level heading *)

(** A paragraph in a doc comment.
    Lines join into one paragraph, and a blank line starts the next.

    Square brackets mark code: [total] links to its declaration, and [1 + 1] stays plain code. *)
Definition doc_paragraphs := total empty.

(** - This dash is plain text, since doc comments have no lists: only headings, paragraphs and
    [code]. Use a Markdown comment for lists. *)
Definition doc_no_lists := 0.

(**md ## A Markdown heading

The `md` marker after the opening stars selects Markdown, with one to six `#` marks for a heading. *)

(**md **Bold**, *emphasis*, `total` as a linked name and `1 + 1` as plain code.

- A list item
- Another, with a [link to a declaration](Core.html#append)

| Form | Marker | Lists |
|---|---|---|
| Doc | none | no |
| Markdown | `md` | yes |

```text
A fenced block. `total` stays text here.
```

An external [link](https://rocq-prover.org), and raw HTML shown as text: <b>not bold</b>. *)
Definition markdown_forms := total empty.

(** Arguments in a doc comment.
    - [n:] the count added, whose entry
      continues on an indented line.
    - [t:] the tally extended, mentioning [size]. *)
Definition push (n : nat) (t : tally) : tally := cons n t.

(**md Arguments in a Markdown comment.

- [t:] the tally read, mentioning `append`. *)
Definition is_empty (t : tally) : bool :=
  match t with
  | empty => true
  | cons _ _ => false
  end.

(* A plain comment above a declaration documents it, as a doc comment does. *)
Definition with_plain_comment (* a comment inside the statement *) := 0.

(** A comment that has a line of its own, and is attached to no declaration, reads as prose between
    the declarations. *)

(**md
**A first line wholly in bold titles a theorem.**

A `Theorem` or `Corollary` with this form appears on the Guarantees page under that title. A `Lemma`
never does.
*)
Theorem titled_theorem : total empty = 0.
Proof.
  (* A plain comment inside a proof folds with the proof. *)
  reflexivity.
Qed.
