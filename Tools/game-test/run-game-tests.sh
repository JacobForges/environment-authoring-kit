#!/usr/bin/env bash
# Full gameplay wiring pipeline — test, auto-wire, retest. No manual in-game button.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity}"
LOG="${HUB_GAME_TEST_LOG:-$ROOT/Logs/hub-game-test.log}"
mkdir -p "$(dirname "$LOG")"

echo "=== Hub game test pipeline (automated Play Mode) ==="
echo "Log: $LOG"
echo "Close Unity Editor if open — batchmode needs exclusive project access."
echo ""

"$UNITY" -batchmode -nographics -projectPath "$ROOT" \
  -executeMethod Hub.Editor.HubGameTestBatchRunner.RunHeadless \
  -logFile "$LOG"

echo ""
echo "=== Report ==="
if [[ -f "$ROOT/Assets/Scripts/HubGameTestReport.json" ]]; then
  python3 - <<'PY' "$ROOT/Assets/Scripts/HubGameTestReport.json" 2>/dev/null || true
import json, sys
r = json.load(open(sys.argv[1]))
s = r.get("summary", {})
print(f"pass={s.get('pass',0)} fail={s.get('fail',0)} skip={s.get('skip',0)}")
for t in r.get("tests", []):
    if t.get("status") == "fail":
        print(f"  FAIL {t.get('id')}: {t.get('message')}")
PY
fi

if [[ -f "$ROOT/Assets/Scripts/HubGameTestRemainderReport.json" ]]; then
  echo ""
  echo "Fix decision template — set fixDecision + fixInstruction per failure:"
  echo "  $ROOT/Assets/Scripts/HubGameTestRemainderReport.json"
  echo "Or Play Mode → Hub → Game Tests → Open Fix Decisions From Last Report"
fi

if rg -q "error CS" "$LOG" 2>/dev/null; then
  echo "Compile errors — see $LOG"
  exit 1
fi

if rg -q "\[HubGameTestBatch\] FAIL" "$LOG" 2>/dev/null; then
  exit 1
fi

echo "=== Hub game test pipeline OK ==="
