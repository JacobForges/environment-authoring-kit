#!/usr/bin/env bash
# Nightly / farm: full showcase build. Does NOT run during normal editor work.
# Requires UNITY_PATH (e.g. /Applications/Unity/Hub/Editor/6000.x/Unity.app/Contents/MacOS/Unity)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
UNITY="${UNITY_PATH:-}"
if [[ -z "$UNITY" || ! -x "$UNITY" ]]; then
  echo "UNITY_PATH not set or not executable — skip Unity headless (zero local editor impact)."
  exit 0
fi
export CAVE_BUILD_FORCE_FULL=1
LOG_DIR="${ROOT}/Logs"
mkdir -p "$LOG_DIR"
"$UNITY" -batchmode -nographics -projectPath "$ROOT" \
  -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunShowcaseHeadless \
  -quit -logFile "$LOG_DIR/showcase-headless.log"
echo "Done. Log: $LOG_DIR/showcase-headless.log"
