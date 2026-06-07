#!/usr/bin/env bash
# Headless Personal Voice narration for latest or given capture folder.
set -euo pipefail
HUB="$(cd "$(dirname "$0")/.." && pwd)"
GRADER="$HUB/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader"
CAPTURE="${1:-}"
if [[ -z "$CAPTURE" ]]; then
  CAPTURE="$(ls -td "$HOME/Library/EnvironmentKit/DemoCapture"/*/ 2>/dev/null | head -1 || true)"
  CAPTURE="${CAPTURE:-$(ls -td /Volumes/Lexar/EnvironmentKit-Hub/DemoCapture/*/ 2>/dev/null | head -1 || true)}"
fi
if [[ -z "$CAPTURE" || ! -d "$CAPTURE" ]]; then
  echo "Usage: bash ~/Hub/Tools/run-recap-narration.sh [capture_folder]" >&2
  exit 1
fi
shift || true
exec bash "$GRADER/run-recap-headless.sh" "$CAPTURE" --narration-only "$@"
