#!/usr/bin/env bash
# Wire MainScene gameplay demo (HUD, barks, triggers) — run once or from bot Phase 2.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity}"
LOG="$ROOT/Logs/wire-gameplay.log"
mkdir -p "$ROOT/Logs"

echo "Wiring MainScene gameplay via Unity batchmode…"
echo "Log: $LOG"
"$UNITY" -batchmode -nographics -projectPath "$ROOT" \
  -executeMethod MainSceneGameplaySetup.ApplySilently \
  -logFile "$LOG" -quit

echo "Done. Open MainScene.unity and enter Play Mode to verify."
