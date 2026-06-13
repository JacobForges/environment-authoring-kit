#!/usr/bin/env bash
# Full intro color verify: JSON profiles + MP4 frame sampling.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
python3 "$ROOT/Tools/deep-train-intro/verify-intro-color-balance.py"
python3 "$ROOT/Tools/deep-train-intro/verify-intro-mp4-color.py" "$@"
