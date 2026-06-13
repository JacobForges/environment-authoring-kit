#!/usr/bin/env bash
# Print HubCodeProgress.json summary
set -euo pipefail
HUB_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROGRESS="$HUB_ROOT/Assets/Scripts/HubCodeProgress.json"

if [[ ! -f "$PROGRESS" ]]; then
  echo "Missing $PROGRESS"
  exit 1
fi

python3 - <<PY
import json
from pathlib import Path
p = json.loads(Path("$PROGRESS").read_text())
print(f"codeReady: {p.get('codeReady', False)}")
print(f"notes: {p.get('notes', '')}")
print()
for tid, row in (p.get("tasks") or {}).items():
    st = row.get("status", "?")
    mark = "✓" if st == "done" else "○"
    print(f"  {mark} {tid} [{st}] — {row.get('title', '')}")
pending = [k for k,v in (p.get("tasks") or {}).items() if v.get("status") not in ("done", "cancelled")]
if pending:
    print()
    print(f"Next: {pending[0]}")
PY
