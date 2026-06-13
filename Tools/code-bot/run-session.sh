#!/usr/bin/env bash
# Hub Code Bot — one wiring task per session (Assets/Scripts only).
# NOT the world bot — use Tools/cursor-bot for terrain/cave pipeline.
#
#   ./Tools/code-bot/run-session.sh --stream
#   ./Tools/code-bot/run-session.sh --stream --task=comp_chat_commands
set -euo pipefail

HUB_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
GRADER_DIR="$HUB_ROOT/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader"
CODE_SESSION="$HUB_ROOT/Tools/code-bot/code-session.ts"

if [[ ! -f "$GRADER_DIR/node_modules/tsx/dist/cli.mjs" ]]; then
  echo "error: run 'npm install' in $GRADER_DIR first" >&2
  exit 1
fi

export HUB_ROOT
export NODE_PATH="$GRADER_DIR/node_modules${NODE_PATH:+:$NODE_PATH}"
cd "$GRADER_DIR"
exec node --import tsx "$CODE_SESSION" "$@"
