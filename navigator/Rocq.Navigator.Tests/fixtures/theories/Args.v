(** * Arguments *)

Definition state := nat.

(** Whether a node dirties itself.
    - [s:] the state read.
    - [n:] the node tested,
      across two lines. *)
Definition self_dirtying (s : state) (n : nat) : Prop := s = n.

(** Three numbers add up the same.
    - [a:] the left number.
    - [b:] the middle number.
    - [c:] the right number, with no written type. *)
Lemma sum_three : forall (a b : nat) c, a + b + c = a + b + c.
Proof. reflexivity. Qed.

(** A number equals itself.
    - [n:] the number compared. *)
Lemma same_named (n : nat) : n = n.
Proof. exact ((fun (n : bool) (m : nat) => eq_refl m) true n). Qed.

(**md Applies a function, with a body that folds.

- [f:] the function, in Markdown.
- [x:] the input of `self_dirtying`'s kind. *)
Definition apply_once (f : nat -> nat) (x : nat) : nat :=
  (fun x : nat => f x) x.

(** - [m:] the only documentation of this definition. *)
Definition entries_only (m : nat) : nat := m.

Definition after_args := entries_only 0.

(** A hypothesis binding the same name as an argument.
    - [n:] the number compared. *)
Lemma shadowed (H : forall n : bool, n = n) (n : nat) : n = n.
Proof. reflexivity. Qed.
