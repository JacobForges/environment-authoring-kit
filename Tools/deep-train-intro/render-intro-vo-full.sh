#!/usr/bin/env bash
# Render full Deep Train Academy intro VO mix (ChristopherNeural + music + SFX).
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
if [[ ! -x "$SCRIPT_DIR/.venv-intro-vo/bin/python" ]]; then
  python3 -m venv "$SCRIPT_DIR/.venv-intro-vo"
  "$SCRIPT_DIR/.venv-intro-vo/bin/pip" install -q edge-tts
fi
exec "$SCRIPT_DIR/.venv-intro-vo/bin/python" "$SCRIPT_DIR/compose-intro-full.py" "$@"
