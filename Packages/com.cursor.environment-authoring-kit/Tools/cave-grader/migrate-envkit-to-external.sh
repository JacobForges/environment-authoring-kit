#!/bin/bash
# Move Environment Kit heavy data to the first writable external volume (e.g. Lexar).
# Falls back to Hub/Library/EnvironmentKit when no external drive is mounted.
set -euo pipefail

HUB="${HUB_ROOT:-$HOME/Hub}"
SRC="$HUB/Library/EnvironmentKit"
EXT=""

for vol in /Volumes/*; do
  name="$(basename "$vol")"
  [[ "$name" == "Macintosh HD" ]] && continue
  [[ "$name" == .* ]] && continue
  candidate="$vol/EnvironmentKit-Hub"
  if mkdir -p "$candidate" 2>/dev/null && touch "$candidate/.write_probe" 2>/dev/null; then
    rm -f "$candidate/.write_probe"
    EXT="$candidate"
    break
  fi
done

if [[ -z "$EXT" ]]; then
  echo "No writable external volume found — keeping data on Mac disk at $SRC"
  exit 0
fi

echo "External data root: $EXT"
mkdir -p "$EXT"

if [[ -d "$SRC" && ! -L "$SRC" ]]; then
  echo "Copying existing data…"
  rsync -a "$SRC/" "$EXT/"
  rm -rf "$SRC"
fi

ln -sfn "$EXT" "$SRC"
echo "Symlink: $SRC -> $(readlink "$SRC")"

echo "Optional: reclaim Unity import cache (safe while Unity is quit)…"
rm -rf "$HUB/Library/Artifacts" 2>/dev/null || true

export ENVIRONMENT_KIT_DATA_ROOT="$EXT"
python3 -c "import sys; sys.path.insert(0, '$(cd "$(dirname "$0")" && pwd)'); from envkit_paths import resolve_envkit_root; print('Python resolves:', resolve_envkit_root())"

df -h /System/Volumes/Data "${EXT%%/EnvironmentKit-Hub*}"
du -sh "$EXT" "$HUB/Library" 2>/dev/null || true
echo "Done."
