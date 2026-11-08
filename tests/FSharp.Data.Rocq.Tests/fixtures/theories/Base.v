(** * A heading in coqdoc form *)

(** The doc comment of [base_id]. *)
Definition base_id (n : nat) : nat := n.

Inductive colour : Set := Red | Green.

Lemma base_id_eq n : base_id n = n.
Proof. reflexivity. Qed.
