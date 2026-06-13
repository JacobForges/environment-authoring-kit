#!/usr/bin/env bash
# Play preview without loading huge WAVs into Unity/Cursor.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
M4A="$SCRIPT_DIR/listen/intro_vo_preview.m4a"
OGG="$ROOT/Assets/Resources/DeepTrainAcademy/Intro/intro_vo_preview.ogg"

if [[ -f "$M4A" ]]; then
  exec afplay "$M4A"
fi
if [[ -f "$OGG" ]]; then
  exec afplay "$OGG"
fi
echo "Run ./Tools/deep-train-intro/render-intro-vo-preview.sh first." >&2
exit 1
