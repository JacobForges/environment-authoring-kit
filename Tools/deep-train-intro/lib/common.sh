# Shared compositing helpers for Deep Train Academy intro.

FPS=72
HOLD_S=1.2
PUSH_S=2.0
BEAT_S="$(awk "BEGIN { print $HOLD_S + $PUSH_S }")"
SEG_S=1.4
INTERP_STEPS=7
GAP_S="$(awk "BEGIN { print $INTERP_STEPS * $SEG_S }")"
ZOOM_END=1.005
TRANSITION_PLAYBACK_SPEED="${TRANSITION_PLAYBACK_SPEED:-2.25}"

probe_duration_s() {
  ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 "$1" 2>/dev/null \
    | awk '{printf "%.6f", $1}'
}

# Full frame visible — fit entire image with letterbox (no center-crop tunnel).
SCALE_FIT="scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=0x080808"
# Single unified grade — applied once on final concat (not per beat/gap pass).
FINISH_VF="eq=brightness=0.02:contrast=1.05:saturation=1.02:gamma=1.02"
DENOISE_VF="hqdn3d=0.8:0.8:2:2"

sha256_file() {
  shasum -a 256 "$1" | awk '{print $1}'
}

log_layer() {
  local manifest="$1"
  local id="$2"
  local z="$3"
  local passes="$4"
  local src_hash="$5"
  local out_hash="$6"
  local out_path="$7"

  printf '%s\n' "{\"id\":\"${id}\",\"zOrder\":${z},\"passes\":[${passes}],\"sourceSha256\":\"${src_hash}\",\"outputSha256\":\"${out_hash}\",\"path\":\"${out_path}\"}" >> "$manifest"
}

build_beat_composite() {
  local num="$1"
  local work="$2"
  local manifest="$3"
  local src="$INTRO_DIR/intro_${num}.png"
  local out="$work/beat_${num}.mp4"
  local hold_f push_f total_f zoom_delta

  hold_f="$(awk "BEGIN { printf \"%d\", $HOLD_S * $FPS }")"
  push_f="$(awk "BEGIN { printf \"%d\", $PUSH_S * $FPS }")"
  total_f="$(awk "BEGIN { printf \"%d\", $BEAT_S * $FPS }")"
  zoom_delta="$(awk "BEGIN { printf \"%.5f\", $ZOOM_END - 1.0 }")"

  local src_hash
  src_hash="$(sha256_file "$src")"

  # Pass stack: base plate → depth BG blur → parallax FG → half-strength dolly → grade.
  ffmpeg -loglevel error -y -loop 1 -i "$src" \
    -filter_complex "
      [0:v]${SCALE_FIT},split=3[base][depth_src][para_src];
      [depth_src]gblur=sigma=3[depth_blur];
      [base][depth_blur]blend=all_expr='A*0.88+B*0.12'[depth_stack];
      [para_src]scale=iw*1.014:ih*1.014,format=rgba,colorchannelmixer=aa=0.14[para_fg];
      [depth_stack][para_fg]overlay=x='(W-w)/2+1*sin(2*PI*t/7)':y='(H-h)/2':format=auto[parallax];
      [parallax]zoompan=z='if(lte(on,${hold_f}),1,1+${zoom_delta}*(1-cos(PI*(on-${hold_f})/${push_f}))/2)':x='(iw-iw/zoom)/2':y='(ih-ih/zoom)/2':d=${total_f}:s=1920x1080:fps=${FPS}[dolly];
      [dolly]format=yuv420p[vout]
    " -map "[vout]" -t "$BEAT_S" -an -c:v libx264 -preset fast -crf 16 -pix_fmt yuv420p "$out"

  local out_hash
  out_hash="$(sha256_file "$out")"
  log_layer "$manifest" "beat_${num}" 0 "\"base\",\"depth_blur\",\"parallax_fg\",\"dolly\",\"grade\"" "$src_hash" "$out_hash" "$out"
  echo "$out"
}

build_blend_png() {
  local src_a="$1"
  local src_b="$2"
  local frac="$3"
  local tag="$4"
  local work="$5"
  local out="$work/blend_${tag}.png"
  local aw bw

  aw="$(awk "BEGIN { printf \"%.4f\", 1.0 - $frac }")"
  bw="$(awk "BEGIN { printf \"%.4f\", $frac }")"

  ffmpeg -loglevel error -y -i "$src_a" -i "$src_b" \
    -filter_complex "[0:v]${SCALE_FIT}[a];[1:v]${SCALE_FIT}[b];[a][b]blend=all_expr='${aw}*A+${bw}*B',format=rgb24" \
    -frames:v 1 "$out"
  echo "$out"
}

build_interp_composite() {
  local src_a="$1"
  local src_b="$2"
  local tag="$3"
  local work="$4"
  local manifest="$5"
  local raw="$work/seg_raw_${tag}.mp4"
  local out="$work/seg_${tag}.mp4"
  local seg_f

  seg_f="$(awk "BEGIN { printf \"%d\", $SEG_S * $FPS }")"

  local hash_a hash_b
  if [[ -f "$src_a" ]]; then hash_a="$(sha256_file "$src_a")"; else hash_a="blend"; fi
  if [[ -f "$src_b" ]]; then hash_b="$(sha256_file "$src_b")"; else hash_b="blend"; fi

  # Pass stack: depth BG → xfade transition → FG parallax + motion blur → MCI → grade.
  ffmpeg -loglevel error -y -loop 1 -t "$SEG_S" -i "$src_a" -loop 1 -t "$SEG_S" -i "$src_b" \
    -filter_complex "
      [0:v]${SCALE_FIT},fps=${FPS}[va];
      [1:v]${SCALE_FIT},fps=${FPS}[vb];
      [va]split=2[va_sharp][va_depth];
      [va_depth]gblur=sigma=2.5[va_bg];
      [va_sharp][va_bg]blend=all_expr='A*0.9+B*0.1'[va_stack];
      [va_stack][vb]xfade=transition=fade:duration=${SEG_S}:offset=0[xfade];
      [vb]scale=iw*1.012:ih*1.012,gblur=sigma=1.1,format=rgba,colorchannelmixer=aa=0.18[fg_blur];
      [xfade][fg_blur]overlay=x='(W-w)/2+2*sin(2*PI*t/5)':y='(H-h)/2':format=auto[stack];
      [stack]format=yuv420p[vout]
    " -map "[vout]" -frames:v "$seg_f" -an -c:v libx264 -preset ultrafast -crf 16 -pix_fmt yuv420p "$raw"

  ffmpeg -loglevel error -y -i "$raw" \
    -vf "minterpolate=fps=${FPS}:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:vsbmc=1:scd=none" \
    -an -c:v libx264 -preset ultrafast -crf 16 -pix_fmt yuv420p "$out"
  rm -f "$raw"

  local out_hash
  out_hash="$(sha256_file "$out")"
  log_layer "$manifest" "interp_${tag}" 1 "\"depth_bg\",\"xfade\",\"fg_motion_blur\",\"parallax\",\"mci\",\"grade\"" "${hash_a}:${hash_b}" "$out_hash" "$out"
  echo "$out"
}

normalize_clip() {
  local src="$1"
  local tag="$2"
  local work="$3"
  local out="$work/clip_${tag}.mp4"

  ffmpeg -loglevel error -y -i "$src" \
    -vf "${SCALE_FIT},fps=${FPS},format=yuv420p" \
    -an -c:v libx264 -preset fast -crf 16 -pix_fmt yuv420p "$out"
  echo "$out"
}

build_gap_composite() {
  local from_num="$1"
  local to_num="$2"
  local work="$3"
  local manifest="$4"
  local clip_file="$CLIP_DIR/gap_${from_num}_${to_num}.mp4"
  local src_a="$INTRO_DIR/intro_${from_num}.png"
  local src_b="$INTRO_DIR/intro_${to_num}.png"
  local i frac prev next_path blend_path seg

  mkdir -p "$work"

  if [[ -f "$clip_file" ]]; then
    echo "Using real clip: $clip_file" >&2
    normalize_clip "$clip_file" "gap_${from_num}_${to_num}" "$work"
    return
  fi

  prev="$src_a"
  for i in $(seq 1 "$INTERP_STEPS"); do
    if [[ "$i" -eq "$INTERP_STEPS" ]]; then
      next_path="$src_b"
    else
      frac="$(awk "BEGIN { printf \"%.6f\", $i / $INTERP_STEPS }")"
      blend_path="$(build_blend_png "$src_a" "$src_b" "$frac" "${from_num}_${to_num}_${i}" "$work")"
      next_path="$blend_path"
    fi
    seg="$(build_interp_composite "$prev" "$next_path" "${from_num}_${to_num}_${i}" "$work" "$manifest")"
    echo "$seg"
    prev="$next_path"
  done
}
