(* Exercises what a sentence splitter and a declaration classifier must handle. *)
From Fixture Require Import Base.
From Corelib Require Import Program.Tactics.
Require Corelib.Lists.ListDef.

(**md
# A markdown heading

Body text.

```
# not a heading, in a fence
```

## A second heading
*)

(* A nested (* comment *) and a string holding "*) still comment" and a period. here *)

(** α doc comment with non-ASCII: ∀ β. *)
Definition αβγ (δ : nat) : nat := δ. (* trailing αβγ *)

Definition after_unicode := αβγ 1.

Section Outer.
  Variable (v : nat).
  Hypothesis h : v = v.
  Context {T : Type} (t : T).

  (** Attached across no blank line. *)
  Lemma uses_section_var : v = v.
  Proof using h. exact h. Qed.

  Section Inner.
    Let local_def := v.
    Definition inner_def := local_def.
  End Inner.

  (** Detached: a blank line follows. *)

  Theorem uses_inner : inner_def = inner_def.
  Proof.
    reflexivity.
  Defined.
End Outer.

Module M.
  Definition in_module := 1.
  Module Nested.
    Definition in_nested := in_module.
  End Nested.
  Module Type Sig.
    Parameter p : nat.
  End Sig.
End M.

Module Alias := M.Nested.
Import M.

Inductive tree : Set :=
  | Leaf
  | Node (f : forest)
with forest : Set :=
  | FNil
  | FCons (t : tree) (rest : forest).

Record point := mkPoint { px : nat; py : nat }.

Class Shape (A : Type) := { area : A -> nat }.

#[global] Instance nat_shape : Shape nat := { area := fun n => n }.

Instance point_shape : Shape point.
Proof.
  constructor. intros p. exact (px p).
Defined.

Notation "a +++ b" := (Nat.add a b) (at level 50, left associativity).
Reserved Notation "a *** b" (at level 40).
Fixpoint mul2 (a b : nat) : nat :=
  match a with
  | O => O
  | S a' => b + (a' *** b)
  end
where "a *** b" := (mul2 a b).

Infix "=?=" := Nat.eqb (at level 70).
Notation double x := (x +++ x).
Local Notation "'νotation' x" := (S x) (at level 10).
Declare Scope kitchen_scope.
Notation "%% x" := (S x) (at level 10) : kitchen_scope.

Tactic Notation "fixture_tac" constr(x) := exact x.
Ltac solve_it := reflexivity.
Local Ltac solve_other := solve_it.

Goal 1 = 1.
Proof.
  solve_it.
Qed.

Lemma brackets_and_bullets (a b : bool) : (a = a /\ b = b) /\ True.
Proof.
  split.
  - split.
    + reflexivity.
    + { reflexivity. }
  - exact I.
Qed.

Lemma selectors : True /\ True.
Proof.
  split.
  1:{ exact I. }
  { (* a comment inside *) exact I. }
Qed.

Lemma named_goal : True.
Proof.
  refine ?[g].
  [g]: exact I.
Abort.

Lemma ellipsis : True.
Proof with auto.
  idtac...
Qed.

Lemma left_open : 1 = 2.
Proof.
Admitted.

Lemma let_statement : let x := 1 in x = 1.
Proof. reflexivity. Qed.

Lemma proof_term : True.
Proof I.

Lemma no_proof_keyword : True.
  exact I.
Qed.

Definition interactive_def : nat.
  exact 1.
Defined.

Definition uses_dots (l : nat * nat) := (fst l, snd l).
Comments "ends with a dot. and ""quotes"" (* no comment".
Local Open Scope nat_scope.
Definition with_inner_comment (* inner . comment *) := 1 + 2.

Axiom ax1 : nat.
Parameters p1 p2 : nat.
Conjecture c1 : p1 = p1.
Scheme tree_forest_ind := Induction for tree Sort Prop
with forest_tree_ind := Induction for forest Sort Prop.

Local Obligation Tactic := idtac.
Program Definition obligated : { n : nat | n = 1 } := exist _ 1 _.
Next Obligation. reflexivity. Qed.

Coercion base_id : nat >-> nat.
Canonical Structure my_canonical := mkPoint 1 2.
Example an_example : 1 = 1 := eq_refl.
Arguments mkPoint _ _ : assert.
Print Assumptions base_id_eq.
Fail Check undefined_name.
Check base_id 1.
Definition last_one := base_id 1.
