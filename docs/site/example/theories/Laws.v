From Tally Require Import Core.

(**md # Laws

What [`total`](Core.html#total), [`append`](Core.html#append) and [`size`](Core.html#size)
guarantee. Theorems whose comment opens with a bold line appear on the Guarantees page.
*)

Open Scope tally_scope.

(**md ## Arithmetic *)

(** Addition regroups. *)
Lemma add_regroup (a b c : nat) : a + b + c = a + (b + c).
Proof.
  induction a as [| a IH].
  - reflexivity.
  - simpl. rewrite IH. reflexivity.
Qed.

(**md ## Appending *)

(**md
**Appending adds totals.**

The total of two tallies appended equals the sum of their totals.

- [a:] the counts that come first.
- [b:] the counts that come last.
*)
Theorem total_append (a b : tally) : total (append a b) = total a + total b.
Proof.
  induction a as [| n rest IH].
  - reflexivity.
  - simpl. rewrite IH. symmetry. apply add_regroup.
Qed.

(**md
**The empty tally changes nothing.**

Appending `empty` leaves the total as it was.
*)
Corollary total_append_empty (a : tally) : total (append a empty) = total a + 0.
Proof.
  apply total_append.
Qed.

(**md ## Assumed and unfinished *)

(** An assumption, which the badge on any theorem using it reports. *)
Axiom counts_are_small : forall t : tally, size t = size t.

(**md
**Size is stable.**

Rests on the assumption above, so its badge reads as axiom-dependent.
*)
Theorem size_stable (t : tally) : size t = size t.
Proof.
  exact (counts_are_small t).
Qed.

(**md
**Appending adds sizes.**

Not proved yet, so it sits in the Open group.
*)
Theorem size_append (a b : tally) : size (append a b) = size a + size b.
Admitted.

(**md
**The claim that all totals match is false.**

The name's `_falsified` suffix files this disproof in the Falsified group.
*)
Theorem totals_differ_falsified : total (1 :: empty) <> total (2 :: empty).
Proof.
  discriminate.
Qed.

(**md
**Generated tallies sum correctly.**

@checked with one thousand generated tallies, which files it in the Checks group.
*)
Theorem generated_sum : total (1 :: 2 :: empty) = 3.
Proof.
  reflexivity.
Qed.
