#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
exec python3 "$ROOT/Tools/deep-train-intro/normalize-intro-color.py" "$@"
