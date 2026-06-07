#!/usr/bin/env bash
# Loop agent sessions until HubGameProgress.json → demoReady: true
# Phase 1 world ladder → Phase 2 gameplay G1–G8 → stop at playable demo.
set -euo pipefail

HUB_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
GRADER_DIR="$HUB_ROOT/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader"

if [[ ! -f "$GRADER_DIR/node_modules/tsx/dist/cli.mjs" ]]; then
  echo "error: run 'npm install' in $GRADER_DIR first" >&2
  exit 1
fi

export HUB_ROOT
export CAVE_BOT_SUPERVISOR=1

# Recommended for unattended loop (refresh compile + apply scene fix + re-grade):
# export UNITY_PATH="/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity"

cd "$GRADER_DIR"
exec node --import tsx until-demo.ts "$@"
