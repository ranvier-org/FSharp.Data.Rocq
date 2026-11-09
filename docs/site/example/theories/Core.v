(**md # Core

A tally is a list of counts. This module defines the type and the two functions the rest of the
project reasons about: [`total`](Core.html#total) and [`append`](Core.html#append).
*)

(** A list of counts. *)
Inductive tally : Type :=
  | empty : tally
  | cons : nat -> tally -> tally.

Declare Scope tally_scope.
Delimit Scope tally_scope with tally.
Infix "::" := cons (at level 60, right associativity) : tally_scope.
Open Scope tally_scope.

(** The sum of every count in a tally.
    - [t:] the tally summed. *)
Fixpoint total (t : tally) : nat :=
  match t with
  | empty => 0
  | n :: rest => n + total rest
  end.

(**md The counts of `a`, followed by the counts of `b`.

- [a:] the counts that come first.
- [b:] the counts that come last. *)
Fixpoint append (a b : tally) : tally :=
  match a with
  | empty => b
  | n :: rest => n :: append rest b
  end.

(** How many counts a tally holds.
    - [t:] the tally measured. *)
Fixpoint size (t : tally) : nat :=
  match t with
  | empty => 0
  | _ :: rest => S (size rest)
  end.
