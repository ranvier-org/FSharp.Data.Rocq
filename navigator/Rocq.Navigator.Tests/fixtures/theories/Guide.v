From Fixture Require Import Other.

(**md # Guide

An introduction with a link to [the definition](Other.html#other_def), and the same text in
code: `[the definition](Other.html#other_def)`.

Comment text holds <, & and <script>alert(1)</script>.
*)

Notation "∀ x .. y , P" := (forall x, .. (forall y, P) ..)
  (at level 200, x binder, y binder, right associativity).
Notation "∅" := (False).

(**md ## Theorems *)

(**md **Reflexivity of sums.** A theorem with a bold first line. *)
Theorem sum_refl (a b : nat) : a + b = a + b.
Proof. reflexivity. Qed.

(**md **Sum is zero on zero.** A corollary with a bold first line. *)
Corollary sum_zero : 0 + 0 = 0.
Proof. reflexivity. Qed.

(**md A doc with no bold line. *)
Lemma plain_doc : other_def 2 = 2.
Proof.
  (* a comment inside the proof *)
  reflexivity.
Qed.

(**md An open statement. *)
Theorem open_goal (n : nat) : n = n + 0.
Admitted.

Theorem zero_absorbs_falsified : ~ (forall n : nat, n = 0).
Proof.
  intro H.
  discriminate (H 1).
Qed.

(* @checked 1000 cases *)
Theorem checked_law : 1 = 1.
Proof. reflexivity. Qed.

Axiom fixture_axiom : 2 = 2.

Theorem rests_on_axiom : 2 = 2 /\ 1 = 1.
Proof. split. exact fixture_axiom. reflexivity. Qed.

Lemma sum_refl' (a : nat) : a = a.
Proof. reflexivity. Qed.

Lemma αβ_law : ∀ (P : Prop) (a : nat), (∅ -> P) -> a = a.
Proof. reflexivity. Qed.

(* An unattached comment. *)

Section First.
  Variable shared : nat.
  Definition first_use := shared.
End First.

Section Second.
  Variable shared : nat.
  Definition second_use := shared.
End Second.

Module Nest.
  (**md A nested definition. *)
  Definition inner := other_def 3.

  Module Deep.
    Lemma deep_law : inner = 3.
    Proof. reflexivity. Qed.
  End Deep.
End Nest.

Class Stable (P : Prop) := stable_step : P -> P.

(**md **Stable twice.** A generated binder. *)
Theorem stable_twice (P : Prop) `{!Stable P} : P -> P.
Proof. intro H. exact (stable_step H). Qed.
