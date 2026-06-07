#!/usr/bin/env bash
set -euo pipefail
HUB_ROOT="${HUB_ROOT:-$(cd "$(dirname "$0")/../../../.." && pwd)}"
export HUB_ROOT
VIDEO="$HUB_ROOT/Library/EnvironmentKit/DemoCapture/_smart_example/DemoRecap_SMART_EXAMPLE.mp4"
SCRIPT="$(dirname "$0")/build-smart-recap-example.py"

python3 "$SCRIPT"
open "$VIDEO"
echo "Opened: $VIDEO"
