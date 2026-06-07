#!/usr/bin/env bash
# Headless layout workflow using existing kit entry points (close Unity Editor first).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity}"
LOG_DIR="$ROOT/Logs"
mkdir -p "$LOG_DIR"

if [[ ! -x "$UNITY" ]]; then
  echo "Unity not found at: $UNITY"
  exit 1
fi

run() {
  local name="$1"
  local method="$2"
  local log="$LOG_DIR/layout-${name}.log"
  echo "=== $name ==="
  "$UNITY" -batchmode -nographics -projectPath "$ROOT" \
    -executeMethod "$method" -logFile "$log" || {
    echo "FAILED — tail $log"
    tail -30 "$log" 2>/dev/null || true
    exit 1
  }
}

cd "$ROOT/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader"
npm run sync-research-catalog

export CAVE_BUILD_FORCE_FULL=1
run "surface" "EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunSurfaceIterationHeadless"
run "fullworld" "EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunHeadlessCaveLadder"

echo "Done — see Assets/EnvironmentKit/Generated/WorldLayoutAudit.json"
