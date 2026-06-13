#!/usr/bin/env bash
# Deep Train Academy — multi-pass composited intro from 10 RAW keyframes.
# Parallel gap workers (1→2, 2→3, …). Flatten to MP4 + audit manifest + read-only perms.

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
INTRO_DIR="$ROOT/Assets/Resources/DeepTrainAcademy/Intro"
CLIP_DIR="$ROOT/Assets/StreamingAssets/DeepTrainAcademy/intro_clips"
OUT_DIR="$ROOT/Assets/StreamingAssets/DeepTrainAcademy"
OUT="$OUT_DIR/intro.mp4"
MANIFEST="$OUT_DIR/intro_composite_manifest.json"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

SHOTS=10
MAX_PARALLEL=2

# shellcheck source=lib/common.sh
source "$SCRIPT_DIR/lib/common.sh"

mkdir -p "$OUT_DIR" "$CLIP_DIR" "$TMP/beats" "$TMP/gaps"

target="$(awk "BEGIN { print 10*$BEAT_S + 9*$INTERP_STEPS*$SEG_S }")"
echo "Multi-pass composited build (~${target}s @ ${FPS}fps, ${INTERP_STEPS} interp/gap, zoom ${ZOOM_END}, parallel gaps)..."

MANIFEST_WORK="$TMP/manifest.jsonl"
: > "$MANIFEST_WORK"

# Hash all source keyframes (read-only audit inputs).
echo "Hashing source keyframes..."
for i in $(seq 1 "$SHOTS"); do
  num="$(printf '%02d' "$i")"
  src="$INTRO_DIR/intro_${num}.png"
  if [[ ! -f "$src" ]]; then
    echo "Missing keyframe: $src" >&2
    exit 1
  fi
  echo "  intro_${num}.png $(sha256_file "$src")" >&2
done

# Phase 1 — beats (sequential; fast).
echo "Phase 1: compositing beats..."
for i in $(seq 1 "$SHOTS"); do
  num="$(printf '%02d' "$i")"
  build_beat_composite "$num" "$TMP/beats" "$MANIFEST_WORK" > "$TMP/beats/beat_${num}.path"
done

# Phase 2 — gaps in parallel batches (macOS bash 3.2 — no wait -n).
echo "Phase 2: parallel gap compositing (batches of ${MAX_PARALLEL})..."
batch=0
for i in $(seq 1 $((SHOTS - 1))); do
  num="$(printf '%02d' "$i")"
  next="$(printf '%02d' $((i + 1)))"

  "$SCRIPT_DIR/build-gap.sh" "$num" "$next" "$TMP/gaps" "$MANIFEST_WORK" &
  batch=$((batch + 1))

  if [[ "$batch" -ge "$MAX_PARALLEL" || "$i" -eq $((SHOTS - 1)) ]]; then
    wait
    batch=0
  fi
done

# Phase 3 — concat layer stack → flatten.
list="$TMP/concat.txt"
: > "$list"

for i in $(seq 1 "$SHOTS"); do
  num="$(printf '%02d' "$i")"
  beat_path="$(cat "$TMP/beats/beat_${num}.path")"
  echo "file '$beat_path'" >> "$list"

  if [[ "$i" -lt "$SHOTS" ]]; then
    next="$(printf '%02d' $((i + 1)))"
    gap_list="$TMP/gaps/gap_${num}_${next}/segments.list"
    while IFS= read -r seg; do
      [[ -n "$seg" ]] && echo "file '$seg'" >> "$list"
    done < "$gap_list"
  fi
done

rough="$TMP/intro_rough.mp4"
echo "Phase 3: flattening layers..."
ffmpeg -loglevel error -y -f concat -safe 0 -i "$list" \
  -c:v libx264 -preset fast -crf 15 -pix_fmt yuv420p -r "$FPS" -an "$rough"

TIMELINE="$OUT_DIR/intro_timeline.json"
echo "Phase 3b: measuring segment timeline..."
python3 "$SCRIPT_DIR/write-intro-timeline.py" \
  --concat-list "$list" \
  --fps "$FPS" \
  --transition-speed "$TRANSITION_PLAYBACK_SPEED" \
  --out "$TIMELINE"

# Free intermediate segments before final pass (saves ~400–800 MB on disk).
rm -rf "$TMP/beats" "$TMP/gaps"

staging="$OUT_DIR/intro_staging_$$.mp4"
echo "Phase 4: final denoise + unified grade..."
ffmpeg -loglevel error -y -i "$rough" \
  -vf "${DENOISE_VF},${FINISH_VF}" \
  -c:v libx264 -preset fast -crf 15 -pix_fmt yuv420p -an "$staging"
rm -f "$rough"

# Finalize manifest.
OUT_HASH="$(sha256_file "$staging")"
GENERATED_AT="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
LAYERS_JSON="$(python3 -c "
import json, pathlib
p = pathlib.Path('${MANIFEST_WORK}')
layers = [json.loads(l) for l in p.read_text().splitlines() if l.strip()]
print(json.dumps(layers))
")"

cat > "$MANIFEST" <<EOF
{
  "version": 2,
  "title": "Deep Train Academy Intro Composite",
  "studio": "JacobForges",
  "generatedAt": "${GENERATED_AT}",
  "output": {
    "path": "StreamingAssets/DeepTrainAcademy/intro.mp4",
    "sha256": "${OUT_HASH}",
    "durationTargetS": ${target},
    "fps": ${FPS},
    "perms": "0444"
  },
  "pipeline": {
    "passes": ["base_plate", "depth_parallax", "xfade_transition", "fg_motion_blur", "mci", "flatten", "denoise"],
    "zoomEnd": ${ZOOM_END},
    "scaleMode": "fit_full_frame",
    "vignette": false
  },
  "layers": ${LAYERS_JSON}
}
EOF

MANIFEST_HASH="$(sha256_file "$MANIFEST")"

chmod 644 "$OUT" 2>/dev/null || true
chmod 644 "$MANIFEST" 2>/dev/null || true
mv -f "$staging" "$OUT"

# Embed audit hash in MP4 metadata (re-mux, no re-encode).
meta_tmp="$TMP/intro_meta.mp4"
ffmpeg -loglevel error -y -i "$OUT" -c copy \
  -metadata title="Deep Train Academy Intro" \
  -metadata artist="JacobForges" \
  -metadata comment="composite_manifest_sha256=${MANIFEST_HASH}" \
  -metadata description="Multi-pass composited intro; see intro_composite_manifest.json" \
  "$meta_tmp"
mv -f "$meta_tmp" "$OUT"

# Enforce read-only deliverables.
chmod 444 "$OUT" "$MANIFEST" "$TIMELINE" 2>/dev/null || true

python3 "$SCRIPT_DIR/sample-still-colors.py"
chmod 444 "$OUT_DIR/intro_still_colors.json" 2>/dev/null || true

ls -lh "$OUT" "$MANIFEST" "$OUT_DIR/intro_still_colors.json"
ffprobe -v error -show_entries stream=r_frame_rate -show_entries format=duration -of default=noprint_wrappers=1 "$OUT"
echo "Done — multi-pass composite @ ${FPS}fps | manifest ${MANIFEST_HASH:0:16}… | perms 0444"
