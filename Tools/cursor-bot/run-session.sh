#!/usr/bin/env bash
# Hub Cursor bot — phased mission: world ladder → gameplay demo → polish.
# Auto-picks workflow unless you pass --workflow=post_build|gameplay|terrain|pre_build
#
# One session:  ./run-session.sh --stream
# Until demo:   ./run-until-demo.sh --stream   (loops Phase 1 → 2 until demoReady)
set -euo pipefail

HUB_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
GRADER_DIR="$HUB_ROOT/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader"

if [[ ! -d "$GRADER_DIR" ]]; then
  echo "error: cave-grader not found at $GRADER_DIR" >&2
  exit 1
fi

if [[ ! -f "$GRADER_DIR/node_modules/tsx/dist/cli.mjs" ]]; then
  echo "error: run 'npm install' in $GRADER_DIR first" >&2
  exit 1
fi

export HUB_ROOT

WORKFLOW=""
PASSTHRU=()
for arg in "$@"; do
  if [[ "$arg" == --workflow=* ]]; then
    WORKFLOW="${arg#--workflow=}"
  else
    PASSTHRU+=("$arg")
  fi
done

if [[ -z "$WORKFLOW" ]]; then
  PHASE="$(cd "$GRADER_DIR" && node --import tsx mission-phase.ts 2>/dev/null | tail -1 || echo world)"
  case "$PHASE" in
    gameplay) WORKFLOW=gameplay ;;
    polish)   WORKFLOW=post_build ;;
    *)        WORKFLOW=post_build ;;
  esac
  echo "[cursor-bot] mission phase=$PHASE → workflow=$WORKFLOW (G1–G7 done + no CaveBuildQualityReport.json → gameplay/G8)"
fi

exec "$GRADER_DIR/run-grade-and-fix.sh" --auto --workflow="$WORKFLOW" "${PASSTHRU[@]}"
