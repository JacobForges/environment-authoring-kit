#!/bin/bash
# Double-click this file in Finder (or run in Terminal).
# Uses your Safari Mixamo login — one Download click, then batch runs.
set -e
osascript "/Users/jacob/Hub/PlanV4-AssetReview/run-mixamo-safari.scpt" || {
  echo ""
  echo "If Safari blocked automation:"
  echo "  Safari → Settings → Advanced → Show Develop menu"
  echo "  Develop → Allow JavaScript from Apple Events"
  echo "  System Settings → Privacy → Automation → allow Terminal/Cursor → Safari"
  exit 1
}
