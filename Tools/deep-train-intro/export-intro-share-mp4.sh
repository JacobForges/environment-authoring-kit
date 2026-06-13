#!/usr/bin/env bash
# Build shareable intro MP4 (video synced to narration + full audio mix).
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
exec python3 "$SCRIPT_DIR/export-intro-share-mp4.py" "$@"
