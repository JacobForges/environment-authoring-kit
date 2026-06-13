#!/usr/bin/env bash
# Push baked intro mix WAVs → Unity Resources (MP3). No TTS re-run.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
exec python3 "$SCRIPT_DIR/export-intro-audio-to-unity.py"
