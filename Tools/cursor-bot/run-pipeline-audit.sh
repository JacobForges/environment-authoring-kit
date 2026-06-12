#!/usr/bin/env bash
# Read-only end-to-end pipeline audit — no agent, no world build, no scene saves.
# Tier 0: JSON + disk (always). Tier 2: Unity probes when UNITY_PATH set and editor closed.
#
#   ./Tools/cursor-bot/run-pipeline-audit.sh
#   ./Tools/cursor-bot/run-pipeline-audit.sh --unity
set -euo pipefail

HUB_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
GRADER_DIR="$HUB_ROOT/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader"

if [[ ! -f "$GRADER_DIR/node_modules/tsx/dist/cli.mjs" ]]; then
  echo "error: run 'npm install' in $GRADER_DIR first" >&2
  exit 1
fi

export HUB_ROOT
UNITY_FLAG=()
if [[ "${1:-}" == "--unity" ]] || [[ -n "${UNITY_PATH:-}" ]]; then
  UNITY_FLAG=(--unity)
fi

cd "$GRADER_DIR"
exec node --import tsx bot-pipeline-audit.ts "${UNITY_FLAG[@]}"
