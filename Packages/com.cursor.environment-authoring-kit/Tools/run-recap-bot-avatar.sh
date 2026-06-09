#!/usr/bin/env bash
# Render recap bot lip-sync PNG sequence via Unity (Kenney cyborg half-AI avatar).
# Usage: run-recap-bot-avatar.sh <envelope.json> <frame_dir> [log_file]
set -euo pipefail

GRADER="$(cd "$(dirname "$0")/cave-grader" && pwd)"
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity}"
ENVELOPE_JSON="${1:?envelope.json required}"
FRAME_DIR="${2:?frame_dir required}"

_export_paths() {
  python3 -c "
import sys
sys.path.insert(0, '$GRADER')
from envkit_paths import ensure_recap_process_env, recap_temp_dir, unity_recap_scratch_dir
ensure_recap_process_env()
print(recap_temp_dir().parent)
print(recap_temp_dir())
print(unity_recap_scratch_dir())
"
}

_paths="$(_export_paths)"
ENVIRONMENT_KIT_DATA_ROOT="$(echo "$_paths" | sed -n '1p')"
TMPDIR="$(echo "$_paths" | sed -n '2p')"
UNITY_RECAP_SCRATCH="$(echo "$_paths" | sed -n '3p')"
export ENVIRONMENT_KIT_DATA_ROOT TMPDIR UNITY_RECAP_SCRATCH
export TEMP="$TMPDIR"
export TMP="$TMPDIR"

LOG="${3:-$UNITY_RECAP_SCRATCH/unity-recap-bot.log}"
mkdir -p "$FRAME_DIR" "$UNITY_RECAP_SCRATCH" "$(dirname "$LOG")"

if [[ ! -x "$UNITY" ]]; then
  echo "UNITY_PATH not set or not executable — skip Unity recap bot avatar." >&2
  exit 3
fi

export RECAP_BOT_ENVELOPE_JSON="$ENVELOPE_JSON"
export RECAP_BOT_FRAME_DIR="$FRAME_DIR"

# macOS: avoid -nographics — Unity 6 requires com.unity.editor.headless license and aborts before RenderRecapBotAvatar.
UNITY_ARGS=(-batchmode -projectPath "$ROOT")
if [[ "$(uname -s)" != "Darwin" ]]; then
  UNITY_ARGS+=(-nographics)
fi
export RECAP_BOT_FRAME_OFFSET="${RECAP_BOT_FRAME_OFFSET:-0}"
export RECAP_BOT_WIDTH="${RECAP_BOT_WIDTH:-320}"

"$UNITY" "${UNITY_ARGS[@]}" \
  -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RenderRecapBotAvatar \
  -logFile "$LOG" -quit
