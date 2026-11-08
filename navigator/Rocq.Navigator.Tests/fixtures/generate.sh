#!/usr/bin/env bash
# Recompiles the fixtures with the Rocq on PATH and refreshes the committed .glob files.
# The .vo files stay on disk, ignored by git, since the gated capture test requires them.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
printf '(** Doc with CRLF ends: \xc2\xb5.\r\n    Second line. *)\r\nDefinition other_def (n : nat) : nat := n.\r\n\r\nLemma other_lemma :\r\n  (* \xc2\xb5 inside *) other_def 1 = 1.\r\nProof.\r\n  reflexivity.\r\nQed.\r\n' > theories/Other.v
for f in Other Guide; do
  rocq compile -Q theories Fixture "theories/$f.v" > /dev/null
done
rm -f theories/.*.aux
