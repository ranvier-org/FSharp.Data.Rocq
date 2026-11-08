From Fixture Require Import Base.

(** Doc with CRLF ends: µ. *)
Definition crlf_def := base_id 1.

Lemma crlf_lemma : crlf_def = crlf_def.
Proof.
  reflexivity.
Qed.
