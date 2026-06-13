#!/bin/bash
# Force recap + EnvKit heavy data onto Lexar (or first writable external volume).
set -euo pipefail
DIR="$(cd "$(dirname "$0")/../Packages/com.cursor.environment-authoring-kit/Tools/cave-grader" && pwd)"
export HUB_ROOT="${HUB_ROOT:-$HOME/Hub}"
# Always pin heavy recap I/O to Lexar (ignore inherited TMPDIR from Cursor/macOS).
export ENVIRONMENT_KIT_DATA_ROOT="$(python3 -c "import sys; sys.path.insert(0, '$DIR'); from envkit_paths import resolve_envkit_root; print(resolve_envkit_root())")"
export TMPDIR="$ENVIRONMENT_KIT_DATA_ROOT/.recap-tmp"
export TEMP="$TMPDIR"
export TMP="$TMPDIR"
mkdir -p "$TMPDIR" "$ENVIRONMENT_KIT_DATA_ROOT/.recap-unity" "$ENVIRONMENT_KIT_DATA_ROOT/DesktopMirror"
echo "ENVIRONMENT_KIT_DATA_ROOT=$ENVIRONMENT_KIT_DATA_ROOT"
echo "TMPDIR=$TMPDIR"
exec "$@"
