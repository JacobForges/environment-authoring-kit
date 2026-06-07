#!/usr/bin/env bash
set -euo pipefail
SRC="$(cd "$(dirname "$0")" && pwd)"
DESTS=(
  "/Volumes/Lexar/EnvironmentKit-Hub/DemoRecapApproved"
  "/Users/jacob/Hub/Library/EnvironmentKit/DemoRecapApproved"
  "/Volumes/Lexar/EnvironmentKit-Hub/DemoCapture/20260606-114916"
)
for dir in "${DESTS[@]}"; do
  [[ -d "$dir" ]] || continue
  cp -p "$SRC/ApprovedOutro.png" "$dir/ApprovedOutro.png"
  cp -p "$SRC/_approval_portrait.png" "$dir/_approval_portrait.png"
  cp -p "$SRC/_approval_portrait.png" "$dir/DemoRecapPortrait.png"
  cp -p "$SRC/ApprovedIntro.png" "$dir/ApprovedIntro.png"
  echo "byte-copied -> $dir"
done
