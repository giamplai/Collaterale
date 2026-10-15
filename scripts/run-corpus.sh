#!/usr/bin/env bash
# Runs every case in tests/corpus/cases through `collaterale --diff --rows` and writes the alignment next to a copy of the inputs.
#   scripts/run-corpus.sh [output-dir]          (default: artifacts/corpus)
# Use the output to compare how another tool aligns the same inputs, or `COLLATERALE_UPDATE_CORPUS=1 dotnet test` to refresh the goldens.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"
out="${1:-artifacts/corpus}"
bin=artifacts/bin/Collaterale.App/release/Collaterale
[ -x "$bin" ] || dotnet build -c Release src/Collaterale.App --nologo -v q
rm -rf "$out"; mkdir -p "$out"
for d in tests/corpus/cases/*/; do
  name=$(basename "$d"); args=""; [ -f "$d/args" ] && args=$(cat "$d/args")
  mkdir -p "$out/$name"; cp "$d"/left.txt "$d"/right.txt "$d"/why.txt "$out/$name/"
  # shellcheck disable=SC2086
  "$bin" --diff --rows $args "$d/left.txt" "$d/right.txt" > "$out/$name/collaterale.rows" || true
done
echo "wrote $(ls "$out" | wc -l) cases to $out"
