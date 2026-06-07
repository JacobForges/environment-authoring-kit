#!/usr/bin/env bash
# Short MP4: approved intro → one timelapse frame → approved outro
set -euo pipefail
RUN="${1:?capture folder}"
DESKTOP="${HOME}/Desktop/DemoRecap-Card-Preview"
OUT="${DESKTOP}/CardsPreview.mp4"
WORK="${RUN}/_card_preview"
FPS=30
INTRO_SEC=5
OUTRO_SEC=6
MID_SEC=3

mkdir -p "$DESKTOP" "$WORK"
MID_FRAME="$(ls "${RUN}/timelapse"/tl_*.png 2>/dev/null | sed -n '2000p' || true)"
[[ -z "$MID_FRAME" ]] && MID_FRAME="$(ls "${RUN}/timelapse"/tl_*.png | head -1)"

ffmpeg -y -loop 1 -t "$INTRO_SEC" -i "${RUN}/ApprovedIntro.png" \
  -vf "scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2:color=black,fps=${FPS}" \
  -c:v libx264 -pix_fmt yuv420p -crf 18 "${WORK}/intro.mp4"

ffmpeg -y -loop 1 -t "$MID_SEC" -i "$MID_FRAME" \
  -vf "scale=1280:720:force_original_aspect_ratio=increase,crop=1280:720,fps=${FPS}" \
  -c:v libx264 -pix_fmt yuv420p -crf 18 "${WORK}/mid.mp4"

ffmpeg -y -loop 1 -t "$OUTRO_SEC" -i "${RUN}/ApprovedOutro.png" \
  -vf "scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2:color=black,fps=${FPS}" \
  -c:v libx264 -pix_fmt yuv420p -crf 18 "${WORK}/outro.mp4"

printf "file '%s/intro.mp4'\nfile '%s/mid.mp4'\nfile '%s/outro.mp4'\n" "$WORK" "$WORK" "$WORK" > "${WORK}/list.txt"
ffmpeg -y -f concat -safe 0 -i "${WORK}/list.txt" -c copy "$OUT"

open "$OUT" 2>/dev/null || true
echo "$OUT (~14s: ${INTRO_SEC}s intro + ${MID_SEC}s scene + ${OUTRO_SEC}s outro)"
