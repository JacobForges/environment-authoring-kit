#!/usr/bin/env bash
# Emit intro_timeline.json from an existing intro.mp4 (scaled to measured duration).

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT_DIR="$ROOT/Assets/StreamingAssets/DeepTrainAcademy"
INTRO="$OUT_DIR/intro.mp4"
TIMELINE="$OUT_DIR/intro_timeline.json"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# shellcheck source=lib/common.sh
source "$SCRIPT_DIR/lib/common.sh"

if [[ ! -f "$INTRO" ]]; then
  echo "Missing intro: $INTRO" >&2
  exit 1
fi

content="$(probe_duration_s "$INTRO")"
chmod 644 "$TIMELINE" 2>/dev/null || true
python3 "$SCRIPT_DIR/write-intro-timeline.py" \
  --scaled \
  --content-duration "$content" \
  --beat-s "$BEAT_S" \
  --gap-s "$GAP_S" \
  --shots 10 \
  --fps "$FPS" \
  --transition-speed "$TRANSITION_PLAYBACK_SPEED" \
  --out "$TIMELINE"

chmod 444 "$TIMELINE" 2>/dev/null || true
python3 "$SCRIPT_DIR/sample-still-colors.py"
chmod 444 "$OUT_DIR/intro_still_colors.json" 2>/dev/null || true
echo "Wrote $TIMELINE and intro_still_colors.json"
