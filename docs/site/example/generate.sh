#!/usr/bin/env bash
# Recompiles the example with the Rocq on PATH and refreshes the committed .glob files.
# The .vo files stay on disk, ignored by git, since rocq-capture requires them.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
for f in Core Laws Prose; do
  rocq compile -Q theories Tally "theories/$f.v" > /dev/null
done
rm -f theories/.*.aux
