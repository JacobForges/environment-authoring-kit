#!/usr/bin/env bash
# Terrain grader — same entry as cave grader with --workflow=terrain
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
if [[ -f "$SCRIPT_DIR/.env" ]]; then
  set -a
  # shellcheck source=/dev/null
  source "$SCRIPT_DIR/.env"
  set +a
fi
export HUB_ROOT="${HUB_ROOT:-/Users/jacob/Hub}"
export CAVE_WORKFLOW=terrain
cd "$SCRIPT_DIR"
exec node --import tsx grade-and-fix.ts --workflow=terrain "$@"
