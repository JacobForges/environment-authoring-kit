#!/usr/bin/env bash
# Build DTA Training Companion SPA into Unity StreamingAssets for standalone .app/.exe packaging.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
COMPANION="$ROOT/dta-companion"
OUT="$ROOT/Assets/StreamingAssets/DTACompanion/web"

cd "$COMPANION"
if [[ ! -d node_modules ]]; then
  npm install
fi

npm exec vite build -- --base=./ --outDir "$OUT" --emptyOutDir

echo "[DTA] Companion UI → $OUT"
echo "[DTA] Unity serves it at http://127.0.0.1:8765/ when the game runs."
