#!/usr/bin/env bash
# Download Piper narration voices (local ONNX, no cloud TTS).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
VOICES="$ROOT/voices"
BASE="https://huggingface.co/rhasspy/piper-voices/resolve/main"

mkdir -p "$VOICES"

download_voice() {
  local rel="$1"
  local name
  name="$(basename "$rel")"
  echo "→ $name"
  curl -fsSL -o "$VOICES/${name}.onnx" "${BASE}/${rel}.onnx"
  curl -fsSL -o "$VOICES/${name}.onnx.json" "${BASE}/${rel}.onnx.json"
}

# Primary: Lessac — US male audiobook narrator (trained on Lessac dataset).
download_voice "en/en_US/lessac/high/en_US-lessac-high"

# Alternate: John — deeper US male (older / prospector feel).
download_voice "en/en_US/john/medium/en_US-john-medium"

# Alternate: LibriTTS-R — strong storytelling prosody.
download_voice "en/en_US/libritts_r/medium/en_US-libritts_r-medium"

cat > "$VOICES/voices.json" <<'EOF'
{
  "version": 1,
  "engine": "piper-tts",
  "default": "en_US-lessac-high",
  "prospector": "en_US-john-medium",
  "storyteller": "en_US-libritts_r-medium",
  "notes": "Lessac = clean US male narration (audiobook). John = deeper male. LibriTTS-R = paragraph storytelling."
}
EOF

echo ""
echo "Downloaded to $VOICES"
ls -lh "$VOICES"/*.onnx
