#!/usr/bin/env bash
# Downloads whisper.cpp CLI + ggml-tiny.en for offline Listen STT in Hub competition.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VOICE_DIR="$ROOT/Tools/competition-voice"
MODEL_DIR="$VOICE_DIR/models"
BIN_DIR="$VOICE_DIR/bin"
MODEL_FILE="$MODEL_DIR/ggml-tiny.en.bin"
MODEL_URL="https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.en.bin"

mkdir -p "$MODEL_DIR" "$BIN_DIR"

if [[ ! -f "$MODEL_FILE" ]]; then
  echo "Downloading ggml-tiny.en.bin (~75 MB)..."
  curl -L --fail -o "$MODEL_FILE" "$MODEL_URL"
else
  echo "Model already present: $MODEL_FILE"
fi

ARCH="$(uname -m)"
OS="$(uname -s | tr '[:upper:]' '[:lower:]')"
CLI="$BIN_DIR/whisper-cli"

if [[ ! -x "$CLI" ]]; then
  echo "whisper-cli not built (cmake/Xcode required)."
  echo "Unity will fall back to Tools/competition-voice/transcribe_wav.py when openai-whisper is installed:"
  echo "  .venv-competition-onnx/bin/pip install openai-whisper"
fi

echo ""
echo "Bundling CLI + model for standalone .app / .exe..."
bash "$ROOT/Tools/competition-voice/bundle-whisper-for-unity.sh"

echo ""
echo "Offline Whisper ready (editor + standalone after player build)."
echo "  Dev CLI:   $CLI"
echo "  Model:     $MODEL_FILE"
echo "  Streaming: Assets/StreamingAssets/Competition/Voice/"
echo "Optional env overrides: HUB_WHISPER_CLI, HUB_WHISPER_MODEL"
echo "Cloud fallback: HUB_STT_API_URL + HUB_STT_API_KEY (Groq/OpenAI-compatible)"
