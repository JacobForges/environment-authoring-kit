#!/usr/bin/env bash
# Smoke test competition + compile — does NOT run world generation.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

echo "=== ONNX validate (Python) ==="
if [[ -x "$ROOT/.venv-competition-onnx/bin/python" ]]; then
  "$ROOT/.venv-competition-onnx/bin/python" "$ROOT/Tools/competition-models/validate_models.py"
else
  python3 "$ROOT/Tools/competition-models/validate_models.py"
fi

UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity}"
echo "=== Unity compile + CompetitionSmokeTest ==="
"$UNITY" -batchmode -nographics -quit \
  -projectPath "$ROOT" \
  -executeMethod Hub.Editor.CompetitionSmokeTest.RunSilently \
  -logFile /tmp/hub_comp_smoke.log

rg "CompetitionSmoke.*PASS|CompetitionSmoke.*FAIL|error CS" /tmp/hub_comp_smoke.log | tail -20
if rg -q "error CS" /tmp/hub_comp_smoke.log; then
  echo "Compile errors — see /tmp/hub_comp_smoke.log"
  exit 1
fi
if rg -q "CompetitionSmoke.*FAIL" /tmp/hub_comp_smoke.log; then
  exit 1
fi
echo "=== Hub competition smoke OK ==="
