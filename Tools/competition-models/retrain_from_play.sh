#!/usr/bin/env bash
# Retrain shared competition_gameplay.onnx from Unity episode JSONL, then sync to Resources.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT/Tools/competition-models"

VENV=".venv-competition-onnx/bin/python3"
if [[ ! -x "$VENV" ]]; then
  echo "Missing $VENV — create venv and install torch first."
  exit 1
fi

export HUB_EPISODES_DIR="${HUB_EPISODES_DIR:-$HOME/Library/Application Support/DefaultCompany/Hub/Competition/Episodes}"
echo "Episodes: $HUB_EPISODES_DIR"
if [[ -n "${HUB_ACTIVITY_FILTER:-}" ]]; then
  echo "Activity filter: $HUB_ACTIVITY_FILTER"
fi

"$VENV" train/episode_bc_trainer.py
bash sync-to-streaming-assets.sh
echo "Done — restart Play Mode or respawn agent to load new gameplay ONNX."
