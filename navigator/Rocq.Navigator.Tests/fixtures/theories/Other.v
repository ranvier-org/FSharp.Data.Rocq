(** Doc with CRLF ends: µ.
    Second line. *)
Definition other_def (n : nat) : nat := n.

Lemma other_lemma :
  (* µ inside *) other_def 1 = 1.
Proof.
  reflexivity.
Qed.
