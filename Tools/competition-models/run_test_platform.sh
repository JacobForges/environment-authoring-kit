#!/usr/bin/env bash
# Standalone ONNX test platform (no Unity).
set -euo pipefail
HUB="$(cd "$(dirname "$0")/../.." && pwd)"
VENV="$HUB/.venv-competition-onnx"
PY="${VENV}/bin/python"
if [[ ! -x "$PY" ]]; then
  echo "Creating venv + deps…"
  python3 -m venv "$VENV"
  "$VENV/bin/pip" install -q onnx onnxruntime numpy torch
  "$PY" "$HUB/Tools/competition-models/train/setup_trainer.py" 2>/dev/null || true
fi
exec "$PY" "$HUB/Tools/competition-models/test_platform.py" "$@"
