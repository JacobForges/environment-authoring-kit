#!/usr/bin/env bash
set -euo pipefail
HUB_ROOT="${HUB_ROOT:-$(cd "$(dirname "$0")/../../../.." && pwd)}"
export HUB_ROOT
VIDEO="$HUB_ROOT/Library/EnvironmentKit/DemoCapture/_prime_example/DemoRecap_PRIME_EXAMPLE.mp4"
SCRIPT="$(dirname "$0")/build-demo-recap-sample.py"

if [[ ! -f "$VIDEO" ]]; then
  echo "Building prime example recap..."
  python3 "$SCRIPT"
fi

if [[ ! -f "$VIDEO" ]]; then
  echo "Failed to build: $VIDEO" >&2
  exit 1
fi

echo "Opening: $VIDEO"
open "$VIDEO"
echo "Done. Your Environment Kit imagery + AI-style dense captions (sharp scene, phase/sub meta line)."
echo "After a real build: python3 $(dirname "$0")/build-demo-recap-from-capture.py --latest [--ai]"
