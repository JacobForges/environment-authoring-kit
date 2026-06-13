#!/usr/bin/env bash
# Upscale intro to 4K without racing Unity's StreamingAssets importer.
# Encodes to /tmp, then atomically replaces intro.mp4 when ffmpeg finishes.

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT_DIR="$ROOT/Assets/StreamingAssets/DeepTrainAcademy"
DEST="$OUT_DIR/intro.mp4"
BACKUP="$OUT_DIR/intro_1080p_backup.mp4"
TMP="$(mktemp -t intro_4k.XXXXXX.mp4)"

cleanup() { rm -f "$TMP"; }
trap cleanup EXIT

if [[ -n "${1:-}" ]]; then
  SRC="$1"
elif [[ -f "$BACKUP" ]]; then
  SRC="$BACKUP"
elif [[ -f "$DEST" ]]; then
  SRC="$DEST"
else
  echo "No source found. Pass a path or place intro.mp4 in $OUT_DIR" >&2
  exit 1
fi

echo "Source:  $SRC"
echo "Temp:    $TMP"
echo "Dest:    $DEST"
echo "(Close Unity or wait — encode runs outside StreamingAssets until the final mv)"

ffmpeg -y -i "$SRC" \
  -vf "scale=3840:2160:flags=lanczos" \
  -c:v libx264 -crf 18 -preset medium -pix_fmt yuv420p \
  -movflags +faststart \
  "$TMP"

if [[ -f "$DEST" && ! -f "$BACKUP" ]]; then
  echo "Backing up current intro → intro_1080p_backup.mp4"
  cp -p "$DEST" "$BACKUP"
fi

mv "$TMP" "$DEST"
trap - EXIT
chmod 644 "$DEST"

echo "Done: $(ffprobe -v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 "$DEST") → $DEST"
