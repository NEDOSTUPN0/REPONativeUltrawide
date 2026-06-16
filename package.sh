#!/usr/bin/env bash
# Builds a Thunderstore-ready zip in ./dist (no extra tooling needed).
# Run after `dotnet build -c Release`. Requires icon.png (256x256) at the repo root.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
VERSION="$(grep -m1 versionNumber "$ROOT/thunderstore.toml" | sed -E 's/.*"([^"]+)".*/\1/')"
DLL="$ROOT/artifacts/bin/REPONativeUltrawide/release/com.github.NEDOSTUPN0.REPONativeUltrawide.dll"
OUT="$ROOT/dist"
ZIP="$OUT/REPONativeUltrawide-$VERSION.zip"

[ -f "$DLL" ] || { echo "Build first: dotnet build -c Release"; exit 1; }
[ -f "$ROOT/icon.png" ] || { echo "Missing icon.png (256x256) at the repo root"; exit 1; }

STAGE="$(mktemp -d)"
mkdir -p "$OUT" "$STAGE/plugins"
cp "$ROOT/manifest.json" "$ROOT/README.md" "$ROOT/CHANGELOG.md" "$ROOT/icon.png" "$STAGE/"
cp "$DLL" "$STAGE/plugins/"

rm -f "$ZIP"
( cd "$STAGE" && zip -r -X "$ZIP" . >/dev/null )
rm -rf "$STAGE"
echo "Built $ZIP"
