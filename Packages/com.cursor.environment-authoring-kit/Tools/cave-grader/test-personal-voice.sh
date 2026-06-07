#!/bin/bash
# One-command Personal Voice test (run in Terminal.app from cave-grader folder).
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$DIR"
OUT="$HOME/Desktop/PersonalVoice-Test.wav"
CAF="$HOME/Desktop/PersonalVoice-Test.caf"

echo "=== 1) Authorize (approve dialog if shown) ==="
swift "$DIR/personal-voice-speak.swift" --authorize || true
echo ""

echo "=== 2) say + mysay (recap uses this first) ==="
export DYLD_INSERT_LIBRARIES="$DIR/mysay.dylib"
say -v "Jacob Adkins" -r 156 -o "$CAF" "Hello. [[slnc 300]] This is my Personal Voice, at a calmer documentary pace."
unset DYLD_INSERT_LIBRARIES
afplay "$CAF"
echo ""

echo "=== 3) Python pipeline test ==="
python3 "$DIR/prepare-narrator-voice.py" --test
afplay "$OUT"
echo ""
echo "Done. If both played your voice, run: bash $DIR/run-recap-terminal.sh"
