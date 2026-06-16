#!/usr/bin/env bash
# Trigger Polyvania map apply inside the open Unity Editor (or batchmode if editor closed).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_PATH="${UNITY_PATH:-/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity}"
FLAG="$ROOT/Logs/hub-apply-polyvania.request"
IMPORT_DIR="$ROOT/Assets/_Import/Polyvania"

mkdir -p "$ROOT/Logs" "$IMPORT_DIR"

if ! compgen -G "$IMPORT_DIR"/*.unitypackage >/dev/null; then
  echo "Missing Polyvania .unitypackage in $IMPORT_DIR"
  echo "Download from https://emaceart.itch.io/polyvania (CC0, free)."
  exit 1
fi

if pgrep -f "Unity.*-projectpath $ROOT" >/dev/null 2>&1; then
  touch "$FLAG"
  echo "Apply requested in open Unity editor — watch Console for [Polyvania]."
  exit 0
fi

echo "Unity not open — running batchmode import + apply..."
"$UNITY_PATH" -batchmode -nographics -projectPath "$ROOT" \
  -executeMethod Hub.Editor.PolyvaniaMapApplier.ApplyBatch \
  -logFile "$ROOT/Logs/polyvania-apply.log" -quit
echo "Done. Log: $ROOT/Logs/polyvania-apply.log"
