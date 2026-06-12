#!/bin/bash
# AI Director — studio UI for recap compose (server + Vite dev or static dist).
#   bash start-ai-director.sh [capture_path] [--no-open]
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
PYTHON="${PYTHON:-python3}"
for c in /opt/homebrew/bin/python3 /usr/local/bin/python3 /usr/bin/python3; do
  [[ -x "$c" ]] && PYTHON="$c" && break
done

CAPTURE=""
NO_OPEN=0
for arg in "$@"; do
  case "$arg" in
    --no-open) NO_OPEN=1 ;;
    --open) NO_OPEN=0 ;;
    -*) ;;
    *) CAPTURE="$arg" ;;
  esac
done

EXTRA=()
[[ $NO_OPEN -eq 1 ]] && EXTRA+=(--no-open)

if [[ -n "$CAPTURE" ]]; then
  exec "$PYTHON" "$DIR/ensure-ai-director.py" "$CAPTURE" "${EXTRA[@]}"
fi
exec "$PYTHON" "$DIR/ensure-ai-director.py" "${EXTRA[@]}"
