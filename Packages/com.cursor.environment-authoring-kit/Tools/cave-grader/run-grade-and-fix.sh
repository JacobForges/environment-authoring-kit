#!/usr/bin/env bash
# Cave post-build grader (default workflow). For above-ground terrain use run-terrain-grade-and-fix.sh
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
if [[ -f "$SCRIPT_DIR/.env" ]]; then
  set -a
  # shellcheck source=/dev/null
  source "$SCRIPT_DIR/.env"
  set +a
fi
export HUB_ROOT="${HUB_ROOT:-/Users/jacob/Hub}"
cd "$SCRIPT_DIR"
exec node --import tsx grade-and-fix.ts "$@"
