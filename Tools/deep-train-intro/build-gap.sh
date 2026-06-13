#!/usr/bin/env bash
# Build one gap segment package (parallel worker).
# Usage: build-gap.sh 01 02 /path/to/work /path/to/manifest.jsonl

set -euo pipefail

FROM_NUM="${1:?from}"
TO_NUM="${2:?to}"
WORK_DIR="${3:?work}"
MANIFEST="${4:?manifest}"

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
INTRO_DIR="$ROOT/Assets/Resources/DeepTrainAcademy/Intro"
CLIP_DIR="$ROOT/Assets/StreamingAssets/DeepTrainAcademy/intro_clips"

# shellcheck source=lib/common.sh
source "$(dirname "$0")/lib/common.sh"

GAP_WORK="$WORK_DIR/gap_${FROM_NUM}_${TO_NUM}"
mkdir -p "$GAP_WORK"

echo "[gap ${FROM_NUM}→${TO_NUM}] starting..." >&2
build_gap_composite "$FROM_NUM" "$TO_NUM" "$GAP_WORK" "$MANIFEST" > "$GAP_WORK/segments.list"
echo "[gap ${FROM_NUM}→${TO_NUM}] done." >&2
