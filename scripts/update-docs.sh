#!/usr/bin/env bash
# Regenerate every screenshot used by docs/HOWTO.md (headless Skia render of the real UI).
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH" COLLATERALE_SCREENSHOTS="$PWD/docs/images"
mkdir -p docs/images
dotnet test tests/Collaterale.App.UiTests --nologo -v q
echo "Updated screenshots in docs/images:"; ls docs/images
