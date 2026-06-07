#!/bin/bash
# One-time: grant this terminal Personal Voice access (macOS 14+).
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
echo "Requesting Personal Voice authorization — approve the system dialog if it appears."
swift "$DIR/personal-voice-speak.swift" --authorize 2>&1
echo ""
swift "$DIR/personal-voice-speak.swift" --list --personal-only 2>&1
echo ""
echo "If you see your voice above with [PERSONAL], test mysay (most reliable):"
echo "  cd \"$DIR\""
echo "  DYLD_INSERT_LIBRARIES=./mysay.dylib say -v \"Jacob Adkins\" -o ~/Desktop/PersonalVoice-Test.caf \"Hello\""
echo "  afplay ~/Desktop/PersonalVoice-Test.caf"
echo ""
echo "Or:"
echo "  python3 \"$DIR/prepare-narrator-voice.py\" --test"
echo ""
echo "If the list is empty:"
echo "  System Settings → Accessibility → Speech → Personal Voice"
echo "  (on some Macs: Accessibility → Personal Voice directly)"
echo "  • Your voice must show Ready"
echo "  • Under \"Allow applications to use your Personal Voice\" — enable Terminal"
echo "    (Apps only appear AFTER you approve the dialog from THIS script in that app.)"
echo ""
echo "There is NO \"Personal Voices\" item under Privacy & Security on current macOS."
