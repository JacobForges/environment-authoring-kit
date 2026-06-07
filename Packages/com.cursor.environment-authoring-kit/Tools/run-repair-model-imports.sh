#!/usr/bin/env bash
# Patches legacy FBX/OBJ meta + force-reimports — no editor menu required.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity}"
if [[ ! -x "$UNITY" ]]; then
  echo "Unity not found at: $UNITY (set UNITY_PATH)" >&2
  exit 1
fi
LOG_DIR="${ROOT}/Logs"
mkdir -p "$LOG_DIR"
"$UNITY" -batchmode -nographics -projectPath "$ROOT" \
  -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RepairLegacyModelImports \
  -quit -logFile "$LOG_DIR/repair-model-imports.log"
echo "Done. Log: $LOG_DIR/repair-model-imports.log"
