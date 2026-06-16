#!/usr/bin/env bash
# Build macOS standalone demo client (DeepTrainAcademy.app) + optional zip handout.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity}"
LOG="${HUB_MACOS_BUILD_LOG:-$ROOT/Logs/macos-client-build.log}"
APP="$ROOT/Builds/macOSClient/DeepTrainAcademy.app"
ZIP="$ROOT/Builds/macOSClient/DeepTrainAcademy-macOS-demo.zip"
mkdir -p "$ROOT/Logs" "$ROOT/Builds/macOSClient"

if pgrep -f "Unity.app/Contents/MacOS/Unity -projectpath $ROOT" >/dev/null 2>&1; then
  echo "Unity Editor has Hub open — using in-editor build flag (no batchmode)."
  rm -f "$ROOT/Logs/hub-build-macos-client.done" "$ROOT/Logs/hub-build-macos-client.failed"
  touch "$ROOT/Logs/hub-build-macos-client.request"
  for i in $(seq 1 3600); do
    if [[ -f "$ROOT/Logs/hub-build-macos-client.done" ]]; then break; fi
    if [[ -f "$ROOT/Logs/hub-build-macos-client.failed" ]]; then echo "Build failed — check Unity Console"; exit 1; fi
    sleep 2
  done
  if [[ ! -f "$ROOT/Logs/hub-build-macos-client.done" ]]; then
    echo "Timed out waiting for in-editor build (2h)."
    exit 1
  fi
else
  echo "Batchmode build…"
  "$UNITY" -batchmode -nographics -quit -projectPath "$ROOT" \
    -executeMethod Hub.Editor.PortfolioStandaloneClientBuild.BuildMacOsStandaloneClientBatch \
    -logFile "$LOG"
fi

if [[ ! -d "$APP" ]]; then
  echo "Missing $APP"
  exit 1
fi

ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"
echo "Built: $APP"
echo "Zip:   $ZIP"
