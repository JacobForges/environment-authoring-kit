#!/usr/bin/env bash
# Bundle whisper.cpp CLI + ggml-tiny.en for standalone Mac .app / Windows .exe builds.
# Output: Assets/StreamingAssets/Competition/Voice/
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VOICE_DIR="$ROOT/Tools/competition-voice"
STREAM="$ROOT/Assets/StreamingAssets/Competition/Voice"
BIN_OUT="$STREAM/bin"
MODEL_FILE="$VOICE_DIR/models/ggml-tiny.en.bin"
MODEL_URL="https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.en.bin"
WHISPER_TAG="${WHISPER_TAG:-v1.8.6}"
WIN_ZIP_URL="https://github.com/ggml-org/whisper.cpp/releases/download/${WHISPER_TAG}/whisper-bin-x64.zip"
BUILD_ROOT="$VOICE_DIR/.build-whisper"

resolve_cmake() {
  for candidate in cmake /opt/homebrew/bin/cmake /usr/local/bin/cmake; do
    if [[ "$candidate" == "cmake" ]]; then
      if command -v cmake >/dev/null 2>&1; then
        command -v cmake
        return 0
      fi
    elif [[ -x "$candidate" ]]; then
      echo "$candidate"
      return 0
    fi
  done
  if command -v brew >/dev/null 2>&1 && [[ "${SKIP_BREW:-}" != "1" ]]; then
    echo "→ Installing cmake via Homebrew (needed once for Mac whisper-cli)..." >&2
    brew install cmake >&2
    if [[ -x /opt/homebrew/bin/cmake ]]; then
      echo /opt/homebrew/bin/cmake
      return 0
    fi
    if command -v cmake >/dev/null 2>&1; then
      command -v cmake
      return 0
    fi
  fi
  return 1
}

try_copy_whisper_from_path() {
  local folder="$1"
  local dest="$BIN_OUT/$folder/whisper-cli"
  [[ -x "$dest" ]] && return 0
  local found
  found="$(command -v whisper-cli 2>/dev/null || true)"
  if [[ -n "$found" && -x "$found" ]]; then
    cp -f "$found" "$dest"
    chmod +x "$dest"
    echo "→ macOS $folder CLI copied from PATH → $dest"
    return 0
  fi
  return 1
}

mkdir -p "$VOICE_DIR/models" "$BIN_OUT/osx_arm64" "$BIN_OUT/osx_x64" "$BIN_OUT/win_x64"

if [[ ! -f "$MODEL_FILE" ]]; then
  echo "→ Downloading ggml-tiny.en.bin (~75 MB)..."
  curl -L --fail -o "$MODEL_FILE" "$MODEL_URL"
fi

cp -f "$MODEL_FILE" "$STREAM/ggml-tiny.en.bin"
echo "→ Model → $STREAM/ggml-tiny.en.bin"

build_mac_cli() {
  local cmake_arch="$1"
  local folder="$2"
  local dest="$BIN_OUT/$folder/whisper-cli"
  if [[ -x "$dest" ]]; then
    echo "→ macOS $folder CLI already present"
    return 0
  fi

  local cmake_bin
  if ! cmake_bin="$(resolve_cmake)"; then
    echo "WARN: cmake not found — skip build for macOS $folder (install cmake or brew install whisper-cpp)." >&2
    try_copy_whisper_from_path "$folder" && return 0
    return 1
  fi

  local build_dir="$BUILD_ROOT/$folder"
  echo "→ Building whisper-cli for macOS $folder ($cmake_arch)..."
  rm -rf "$build_dir"
  git clone --depth 1 --branch "$WHISPER_TAG" https://github.com/ggml-org/whisper.cpp.git "$build_dir/src"
  "$cmake_bin" -S "$build_dir/src" -B "$build_dir/build" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_OSX_ARCHITECTURES="$cmake_arch" \
    -DBUILD_SHARED_LIBS=OFF \
    -DWHISPER_BUILD_TESTS=OFF \
    -DWHISPER_BUILD_EXAMPLES=ON
  "$cmake_bin" --build "$build_dir/build" --config Release -j"$(sysctl -n hw.ncpu 2>/dev/null || echo 4)" --target whisper-cli
  cp -f "$build_dir/build/bin/whisper-cli" "$dest"
  chmod +x "$dest"
  echo "→ macOS $folder CLI → $dest"
}

fetch_win_cli() {
  local dest="$BIN_OUT/win_x64/whisper-cli.exe"
  if [[ -f "$dest" ]]; then
    echo "→ Windows x64 CLI already present"
    return 0
  fi

  echo "→ Downloading Windows whisper-bin-x64.zip..."
  local zip="$BUILD_ROOT/whisper-bin-x64.zip"
  mkdir -p "$BUILD_ROOT"
  curl -L --fail -o "$zip" "$WIN_ZIP_URL"
  rm -rf "$BUILD_ROOT/win"
  unzip -q -o "$zip" -d "$BUILD_ROOT/win"
  local found
  found="$(find "$BUILD_ROOT/win" -name 'whisper-cli.exe' -print -quit)"
  if [[ -z "$found" ]]; then
    found="$(find "$BUILD_ROOT/win" -name 'main.exe' -print -quit)"
  fi
  if [[ -z "$found" || ! -f "$found" ]]; then
    echo "ERROR: whisper-cli.exe not found in Windows zip" >&2
    return 1
  fi
  cp -f "$found" "$dest"
  echo "→ Windows x64 CLI → $dest"
}

if [[ "$(uname -s)" == "Darwin" ]]; then
  case "$(uname -m)" in
    arm64)
      build_mac_cli arm64 osx_arm64 || try_copy_whisper_from_path osx_arm64 || true
      if arch -x86_64 /usr/bin/true 2>/dev/null; then
        echo "→ Building Intel macOS CLI via Rosetta (optional)..."
        arch -x86_64 env WHISPER_TAG="$WHISPER_TAG" BUILD_ROOT="$BUILD_ROOT" BIN_OUT="$BIN_OUT" bash -c '
          set -euo pipefail
          build_dir="$BUILD_ROOT/osx_x64"
          dest="$BIN_OUT/osx_x64/whisper-cli"
          [[ -x "$dest" ]] && exit 0
          rm -rf "$build_dir"
          git clone --depth 1 --branch "$WHISPER_TAG" https://github.com/ggml-org/whisper.cpp.git "$build_dir/src"
          cmake -S "$build_dir/src" -B "$build_dir/build" \
            -DCMAKE_BUILD_TYPE=Release \
            -DCMAKE_OSX_ARCHITECTURES=x86_64 \
            -DBUILD_SHARED_LIBS=OFF \
            -DWHISPER_BUILD_TESTS=OFF \
            -DWHISPER_BUILD_EXAMPLES=ON
          cmake --build "$build_dir/build" --config Release -j4 --target whisper-cli
          cp -f "$build_dir/build/bin/whisper-cli" "$dest"
          chmod +x "$dest"
        ' || echo "WARN: Intel macOS CLI skipped — install Rosetta for x86_64 builds."
      fi
      ;;
    x86_64)
      build_mac_cli x86_64 osx_x64 || try_copy_whisper_from_path osx_x64 || true
      ;;
  esac
fi

fetch_win_cli || echo "WARN: Windows whisper-cli download failed." >&2

cat > "$STREAM/voice_bundle_manifest.json" <<EOF
{
  "schemaVersion": 1,
  "whisperTag": "${WHISPER_TAG}",
  "modelFile": "ggml-tiny.en.bin",
  "platforms": {
    "osx_arm64": "bin/osx_arm64/whisper-cli",
    "osx_x64": "bin/osx_x64/whisper-cli",
    "win_x64": "bin/win_x64/whisper-cli.exe"
  }
}
EOF

echo ""
echo "Whisper standalone bundle ready:"
echo "  $STREAM"
echo "Before player build: Hub → Competition → Bundle Whisper for Standalone"
