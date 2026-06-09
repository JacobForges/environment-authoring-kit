#!/bin/bash
# Move Environment Kit heavy data to the first writable external volume (e.g. Lexar).
# Symlinks back into the Hub project so Unity + grader paths stay unchanged.
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

migrate_dir() {
  local hub_sub="$1"
  local ext_sub="$2"
  local label="$3"
  if [[ ! -d "$hub_sub" ]]; then
    mkdir -p "$ext_sub"
    ln -sfn "$ext_sub" "$hub_sub"
    echo "  $label: created symlink $hub_sub -> $ext_sub"
    return
  fi
  if [[ -L "$hub_sub" ]]; then
    echo "  $label: already symlinked -> $(readlink "$hub_sub")"
    return
  fi
  echo "  $label: rsync $hub_sub -> $ext_sub"
  mkdir -p "$ext_sub"
  rsync -a "$hub_sub/" "$ext_sub/"
  rm -rf "$hub_sub" 2>/dev/null || true
  if [[ -e "$hub_sub" ]]; then
    find "$hub_sub" -mindepth 1 -delete 2>/dev/null || true
    rmdir "$hub_sub" 2>/dev/null || rm -rf "$hub_sub" 2>/dev/null || true
  fi
  ln -sfn "$ext_sub" "$hub_sub"
  echo "  $label: symlink $hub_sub -> $ext_sub"
}

echo "Library/EnvironmentKit…"
if [[ -d "$SRC" && ! -L "$SRC" ]]; then
  echo "Copying existing Library/EnvironmentKit data…"
  rsync -a "$SRC/" "$EXT/"
  rm -rf "$SRC"
fi
ln -sfn "$EXT" "$SRC"
echo "Symlink: $SRC -> $(readlink "$SRC")"

echo "Assets/EnvironmentKit heavy folders…"
migrate_dir "$HUB/Assets/EnvironmentKit/Generated" "$EXT/Generated" "Generated"
migrate_dir "$HUB/Assets/EnvironmentKit/ResearchCache" "$EXT/ResearchCache" "ResearchCache"

echo "Unity Library import cache (quit Unity first for cleanest result)…"
UNITY_CACHE="$EXT/UnityLibraryCache"
mkdir -p "$UNITY_CACHE"
for sub in Artifacts Bee ShaderCache Temp PackageCache BurstCache; do
  hub_sub="$HUB/Library/$sub"
  ext_sub="$UNITY_CACHE/$sub"
  if [[ -d "$hub_sub" && ! -L "$hub_sub" ]]; then
    echo "  $sub → $ext_sub (move, not delete)"
    mkdir -p "$ext_sub"
    rsync -a "$hub_sub/" "$ext_sub/"
    rm -rf "$hub_sub"
    ln -sfn "$ext_sub" "$hub_sub"
  elif [[ ! -e "$hub_sub" ]]; then
    mkdir -p "$ext_sub"
    ln -sfn "$ext_sub" "$hub_sub"
  else
    echo "  $sub: already external ($(readlink "$hub_sub" 2>/dev/null || echo ok))"
  fi
done
hub_db="$HUB/Library/ArtifactDB"
ext_db="$UNITY_CACHE/ArtifactDB"
if [[ -e "$hub_db" && ! -L "$hub_db" ]]; then
  echo "  ArtifactDB → $ext_db (move)"
  mkdir -p "$UNITY_CACHE"
  rsync -a "$hub_db" "$ext_db"
  rm -rf "$hub_db"
  ln -sfn "$ext_db" "$hub_db"
elif [[ -L "$hub_db" ]]; then
  echo "  ArtifactDB: already symlinked"
fi

export ENVIRONMENT_KIT_DATA_ROOT="$EXT"
python3 -c "import sys; sys.path.insert(0, '$(cd "$(dirname "$0")" && pwd)'); from envkit_paths import resolve_envkit_root; print('Python resolves:', resolve_envkit_root())"

df -h /System/Volumes/Data "${EXT%%/EnvironmentKit-Hub*}"
du -sh "$EXT" "$HUB/Assets/EnvironmentKit/Generated" "$HUB/Assets/EnvironmentKit/ResearchCache" 2>/dev/null || true
echo "Done. Reopen Unity from $HUB when finished."
