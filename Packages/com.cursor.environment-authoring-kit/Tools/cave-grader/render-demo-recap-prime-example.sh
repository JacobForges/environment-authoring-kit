#!/usr/bin/env bash
# Renders a prime DemoRecap sample (same 3-line caption bar as Unity DemoRecorder).
# Usage: bash render-demo-recap-prime-example.sh
#    or: cd ~/Hub && bash Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/render-demo-recap-prime-example.sh

set -euo pipefail

HUB_ROOT="${HUB_ROOT:-$(cd "$(dirname "$0")/../../../.." && pwd)}"
OUT_DIR="${HUB_ROOT}/Library/EnvironmentKit/DemoCapture/_prime_example"
SEG_DIR="${OUT_DIR}/segments"
OUTPUT="${OUT_DIR}/DemoRecap_PRIME_EXAMPLE.mp4"
FONT="/System/Library/Fonts/Supplemental/Arial.ttf"
DURATION="2.8"
BAR=168

if ! command -v ffmpeg >/dev/null 2>&1; then
  echo "ffmpeg not found. Install: brew install ffmpeg" >&2
  exit 1
fi

if [[ ! -f "$FONT" ]]; then
  FONT="/Library/Fonts/Arial.ttf"
fi

escape_drawtext() {
  local t="$1"
  t="${t//\\/\\\\}"
  t="${t//:/\\:}"
  t="${t//\'/\\'}"
  t="${t//%/\\%}"
  printf '%s' "$t"
}

render_slide() {
  local idx="$1"
  local bg="$2"      # lavfi color, e.g. 0x2a3528
  local title="$3"   # faint scene hint top-right
  local l1="$4"
  local l2="$5"
  local l3="${6:-}"
  local out="${SEG_DIR}/seg_$(printf '%04d' "$idx").mp4"

  l1="$(escape_drawtext "$l1")"
  l2="$(escape_drawtext "$l2")"
  title="$(escape_drawtext "$title")"

  local filter
  filter="scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2:black,"
  filter+="drawbox=x=0:y=h-${BAR}:w=w:h=${BAR}:color=black@0.58:t=fill,"
  filter+="drawtext=fontfile=${FONT}:text='Scene view (sample)':fontcolor=white@0.45:fontsize=13:x=24:y=22,"
  filter+="drawtext=fontfile=${FONT}:text='${title}':fontcolor=white@0.55:fontsize=14:x=w-tw-28:y=22,"
  filter+="drawtext=fontfile=${FONT}:text='${l1}':fontcolor=white:fontsize=21:x=36:y=h-152,"
  filter+="drawtext=fontfile=${FONT}:text='${l2}':fontcolor=white@0.92:fontsize=17:x=36:y=h-122"

  if [[ -n "$l3" ]]; then
    l3="$(escape_drawtext "$l3")"
    filter+=",drawtext=fontfile=${FONT}:text='${l3}':fontcolor=white@0.85:fontsize=15:x=36:y=h-96"
  fi

  ffmpeg -y -hide_banner -loglevel error \
    -f lavfi -i "color=c=${bg}:s=1280x720:d=${DURATION}:r=30" \
    -vf "$filter" \
    -c:v libx264 -pix_fmt yuv420p \
    "$out"
}

rm -rf "$OUT_DIR"
mkdir -p "$SEG_DIR"

# Prime FullWorld-style story (rule-based captions like CaveBuildDemoNarration)
render_slide 0 "0x1e2420" "Pre-build" \
  "Starting the world build." \
  "I'll explain what each milestone is doing and why it matters — not just read the log." \
  "Keep Scene view visible; slides come from that camera."

render_slide 1 "0x3d5c3a" "Nine-tile play disk" \
  "Locking the nine-tile play disk." \
  "The walkable arena must stay flat, square, and seam-clean — everything else rings around this footprint." \
  ""

render_slide 2 "0x4a4035" "Terrain ladder 4/12" \
  "Terrain quality pass 4 of 12 on the play disk." \
  "We fix height and walkability tile-by-tile so combat areas stay fair before foothills and peaks consume the border." \
  "Full-world build scope."

render_slide 3 "0x5a4a3a" "Mountain pipeline" \
  "Mountain pipeline — massif, mouths, annex." \
  "Height, cave mouths, labyrinth, and trails run after the play disk contract is stable enough to align against." \
  ""

render_slide 4 "0x4f4438" "Labyrinth carve" \
  "Carving the south labyrinth walkways." \
  "Heightmap spines cut walkable benches into the annex so players can loop, retreat, and sight the mountain mouths." \
  ""

render_slide 5 "0x3a4a52" "Plan v4 / content" \
  "Populating the world with characters and props." \
  "CC0 content, NPCs, loot, and landmarks land on stable terrain so the space reads lived-in, not empty heightmap." \
  ""

render_slide 6 "0x2a3238" "Complete" \
  "Build finished." \
  "This is the scene exactly where the pipeline stopped — terrain, content, and whatever steps completed." \
  ""

LIST="${OUT_DIR}/segments.txt"
: >"$LIST"
for f in "$SEG_DIR"/seg_*.mp4; do
  printf "file '%s'\n" "$f" >>"$LIST"
done

ffmpeg -y -hide_banner -loglevel error \
  -f concat -safe 0 -i "$LIST" -c copy "$OUTPUT"

cat >"${OUT_DIR}/README.txt" <<EOF
Prime example for Environment Kit demo recap (3-line captions, 21/17/15px).

Re-render anytime:
  cd ${HUB_ROOT} && bash Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/render-demo-recap-prime-example.sh

Open video:
  open "${OUTPUT}"
EOF

echo ""
echo "Prime example written:"
echo "  ${OUTPUT}"
echo ""
echo "Opening in default video player..."
open "$OUTPUT"
