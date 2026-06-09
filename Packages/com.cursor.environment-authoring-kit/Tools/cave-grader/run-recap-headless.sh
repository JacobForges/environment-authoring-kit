#!/bin/bash
# Headless Personal Voice narration + compose (no Terminal.app window).
# Dashboard and agents should call this — not run-recap-terminal.sh.
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
eval "$(python3 -c "import sys; sys.path.insert(0, '$DIR'); from envkit_paths import ensure_recap_process_env; ensure_recap_process_env()")"
export ENVIRONMENT_KIT_DATA_ROOT
export TMPDIR TEMP TMP
export PYTHONUNBUFFERED=1
export PYTHONPATH="$DIR${PYTHONPATH:+:$PYTHONPATH}"
# Some macOS builds gate Personal Voice on TTY context; this does not open Terminal.
export TERM_PROGRAM="${TERM_PROGRAM:-Apple_Terminal}"
exec python3 "$DIR/run-producer-recap.py" "$@"
