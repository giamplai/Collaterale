#!/usr/bin/env bash
# Full local gate: build (warnings are errors) + tests.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"
dotnet build --nologo -v q
dotnet test --nologo --no-build -v q
