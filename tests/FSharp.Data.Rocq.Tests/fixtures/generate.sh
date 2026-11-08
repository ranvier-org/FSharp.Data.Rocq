#!/usr/bin/env bash
# Recompiles the fixtures with the Rocq on PATH and refreshes the committed .glob, sentence-range and dependency files.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
printf 'From Fixture Require Import Base.\r\n\r\n(** Doc with CRLF ends: \xc2\xb5. *)\r\nDefinition crlf_def := base_id 1.\r\n\r\nLemma crlf_lemma : crlf_def = crlf_def.\r\nProof.\r\n  reflexivity.\r\nQed.\r\n' > theories/Crlf.v
rocq --print-version | cut -d' ' -f1 > rocq-version.txt
mkdir -p oracle
for f in Base Kitchen Crlf; do
  rocq compile -Q theories Fixture -time-file "oracle/$f.time" "theories/$f.v" > /dev/null
done
sed -i -E 's/ [0-9.]+ secs \(.*\)$//' oracle/*.time
rocq dep -f _CoqProject | grep -E '^theories.*\.vo ' | sed -E "s# /[^ ]*##g" > rocqdep.txt
rm -f theories/*.vo theories/*.vok theories/*.vos theories/.*.aux
