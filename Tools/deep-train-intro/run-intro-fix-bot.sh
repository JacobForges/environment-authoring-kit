#!/usr/bin/env bash
# Walk intro.mp4, auto-fix color/tune JSON, verify. One command.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
BOT="$ROOT/Tools/deep-train-intro/fix-intro-playback-bot.py"
VERIFY="$ROOT/Tools/deep-train-intro/verify-intro-color.sh"

echo "=== Intro fix bot ==="
if [[ -f "$ROOT/intro-recording.mov" ]]; then
  echo "=== Recording eval (Unity output) ==="
  python3 "$ROOT/Tools/deep-train-intro/evaluate-intro-recording.py" "$ROOT/intro-recording.mov" || true
  echo ""
fi
python3 "$BOT"
echo ""
echo "=== Post-fix verify ==="
python3 "$ROOT/Tools/deep-train-intro/verify-intro-color-balance.py"
python3 "$ROOT/Tools/deep-train-intro/verify-intro-mp4-color.py"
echo ""
echo "Restart Unity Play Mode to load intro_playback_tune.json + intro_still_colors.json"
