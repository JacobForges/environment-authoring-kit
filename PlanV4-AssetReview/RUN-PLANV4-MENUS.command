#!/bin/bash
set -euo pipefail
HUB="${HOME}/Hub"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity}"
LOG="${HUB}/Logs/planv4-menus.log"
mkdir -p "${HUB}/Logs"
touch "${HUB}/Assets/EnvironmentKit/.run-planv4-menus"
echo "==> Wire Hub Combat"
"${UNITY}" -batchmode -nographics -projectPath "${HUB}" \
  -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.WireHubCombat \
  -logFile "${LOG}" -quit || true
echo "==> Build Cinematic Timelines"
"${UNITY}" -batchmode -nographics -projectPath "${HUB}" \
  -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.BuildCinematicTimelines \
  -logFile "${LOG}" -quit || true
echo "Done. Log: ${LOG}"
echo "If batchmode failed, open Hub in Unity — the flag file will run both menus on compile."
