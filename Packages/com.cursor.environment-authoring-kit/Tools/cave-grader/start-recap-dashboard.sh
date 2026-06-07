#!/usr/bin/env bash
# Boot recap dashboard API + UI. Unity calls this when recording ends.
# Usage:
#   bash start-recap-dashboard.sh                    # latest capture, open browser
#   bash start-recap-dashboard.sh /path/to/capture   # specific run
#   bash start-recap-dashboard.sh --no-open          # latest, no browser
set -euo pipefail
TOOLS="$(cd "$(dirname "$0")" && pwd)"
CAPTURE=""
EXTRA=()
for arg in "$@"; do
  case "$arg" in
    --no-open|--open) EXTRA+=("$arg") ;;
    --*) EXTRA+=("$arg") ;;
    *)
      if [[ -z "$CAPTURE" ]]; then
        CAPTURE="$arg"
      else
        EXTRA+=("$arg")
      fi
      ;;
  esac
done
PYTHON="${PYTHON:-python3}"
for candidate in /opt/homebrew/bin/python3 /usr/local/bin/python3 /usr/bin/python3 python3; do
  if command -v "$candidate" >/dev/null 2>&1; then
    PYTHON="$candidate"
    break
  fi
done
run_ensure() {
  if ((${#EXTRA[@]})); then
    exec "$PYTHON" "$TOOLS/ensure-recap-dashboard.py" "$@" "${EXTRA[@]}"
  fi
  exec "$PYTHON" "$TOOLS/ensure-recap-dashboard.py" "$@"
}

if [[ -n "$CAPTURE" ]]; then
  run_ensure "$CAPTURE"
fi
run_ensure
