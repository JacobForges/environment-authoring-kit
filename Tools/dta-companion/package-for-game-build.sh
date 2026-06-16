#!/usr/bin/env bash
# Package the standalone DTA Training Companion app beside a Unity standalone build.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
COMPANION="$ROOT/dta-companion"
OUT="${1:?usage: package-for-game-build.sh <output/DTACompanion>}"

cd "$COMPANION"
if [[ ! -d node_modules ]]; then
  npm install
fi

npm run build

rm -rf "$OUT"
mkdir -p "$OUT"
cp -R dist "$OUT/dist"
cp package.json "$OUT/package.json"
cp -R catalog "$OUT/catalog" 2>/dev/null || true
cp firebase-applet-config.example.json "$OUT/" 2>/dev/null || true
cp README-COMPANION.txt "$OUT/README.txt" 2>/dev/null || true

LAUNCHERS="$ROOT/Tools/dta-companion/launchers"
cp "$LAUNCHERS/Start-DTA-Companion.command" "$OUT/"
cp "$LAUNCHERS/Start-DTA-Companion.bat" "$OUT/"
chmod +x "$OUT/Start-DTA-Companion.command"

echo "[DTA] Companion app packaged → $OUT"
echo "[DTA] Players: run the game OR double-click Start-DTA-Companion (needs Node.js on PATH)."
