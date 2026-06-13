#!/usr/bin/env bash
# Polish intro narration — trueColor + spatialSound helpers.
# Usage: polish-intro-vo.sh raw_vo.wav [output.ogg]

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
RES_DIR="$ROOT/Assets/Resources/DeepTrainAcademy/Intro"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

IN="${1:?input wav}"
OUT="${2:-$RES_DIR/intro_vo.ogg}"

mkdir -p "$(dirname "$OUT")"
python3 "$SCRIPT_DIR/intro-vo-helpers.py" "$IN" -o "$OUT"
echo "Drop into Unity Resources: Assets/Resources/DeepTrainAcademy/Intro/intro_vo.ogg"
