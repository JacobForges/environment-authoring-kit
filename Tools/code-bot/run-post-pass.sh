#!/usr/bin/env bash
# Code bot post-pass — on macOS uses node fallback (Unity batch Bee IPC often aborts).
# Full ONNX smoke: Unity editor → Game → Code Bot → Run Post Pass
set -uo pipefail

HUB_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
GRADER_DIR="$HUB_ROOT/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity}"
LOG="$HUB_ROOT/Logs/code-bot-post-pass.log"
FALLBACK="$HUB_ROOT/Tools/code-bot/code-bot-post-pass-fallback.ts"

export HUB_ROOT

run_fallback() {
  if [[ ! -f "$GRADER_DIR/node_modules/tsx/dist/cli.mjs" ]]; then
    echo "error: npm install in $GRADER_DIR" >&2
    return 1
  fi
  cd "$GRADER_DIR"
  export NODE_PATH="$GRADER_DIR/node_modules${NODE_PATH:+:$NODE_PATH}"
  node --import tsx "$FALLBACK"
}

print_editor_path() {
  echo ""
  echo "For full post-pass (compile export + Competition ONNX smoke):"
  echo "  1. Open Hub in Unity"
  echo "  2. Game → Code Bot → Run Post Pass"
  echo ""
  echo "Remaining task: demo_g8_smoke — Play Mode once (spawn → portal → goal → combat)."
  echo "  Exiting Play Mode auto-syncs HubGameProgress + HubCodeProgress."
}

if [[ "$(uname -s)" == "Darwin" && "${CODE_BOT_FORCE_UNITY_BATCH:-}" != "1" ]]; then
  echo "[CodeBot] macOS — skipping Unity batch (Bee IPC abort trap is common on this OS)."
  echo "  Set CODE_BOT_FORCE_UNITY_BATCH=1 to force batch anyway."
  run_fallback
  FALLBACK_EXIT=$?
  print_editor_path
  exit "$FALLBACK_EXIT"
fi

if [[ ! -x "$UNITY" ]]; then
  echo "Set UNITY_PATH to your Unity editor." >&2
  exit 1
fi

if pgrep -fl "/Unity.app/Contents/MacOS/Unity" 2>/dev/null | grep -q "$HUB_ROOT"; then
  echo "[CodeBot] Unity is running on Hub — using fallback (use Game → Code Bot → Run Post Pass in editor)."
  run_fallback
  print_editor_path
  exit $?
fi

echo "[CodeBot] Unity batch post-pass…"
set +e
"$UNITY" -batchmode -nographics -quit \
  -projectPath "$HUB_ROOT" \
  -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunCodeBotPostPass \
  -logFile "$LOG"
UNITY_EXIT=$?
set -e

if [[ "$UNITY_EXIT" -eq 0 ]]; then
  echo "[CodeBot] Unity post-pass OK — see $LOG"
  exit 0
fi

echo "[CodeBot] Unity batch failed (exit $UNITY_EXIT). Log: $LOG"
run_fallback
FALLBACK_EXIT=$?
print_editor_path
[[ "$FALLBACK_EXIT" -eq 0 ]] && exit 0
exit "$UNITY_EXIT"
