#!/usr/bin/env bash
# Loop code-bot until HubCodeProgress.json → codeReady: true
set -euo pipefail

HUB_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
GRADER_DIR="$HUB_ROOT/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader"

if [[ ! -f "$GRADER_DIR/node_modules/tsx/dist/cli.mjs" ]]; then
  echo "error: run 'npm install' in $GRADER_DIR first" >&2
  exit 1
fi

export HUB_ROOT
export NODE_PATH="$GRADER_DIR/node_modules${NODE_PATH:+:$NODE_PATH}"

# Recommended for compile verify between sessions:
# export UNITY_PATH="/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity"

cd "$GRADER_DIR"
exec node --import tsx "$HUB_ROOT/Tools/code-bot/until-code-complete.ts" "$@"
