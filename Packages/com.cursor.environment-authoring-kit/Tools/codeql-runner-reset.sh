#!/usr/bin/env bash
# Clear stale Actions work on this Mac after old CodeQL workflow runs (optional).
set -euo pipefail
WORK="${HOME}/actions-runner/_work"
if [[ -d "$WORK" ]]; then
  echo "Removing ${WORK} (fresh checkout on next job)…"
  rm -rf "$WORK"
fi
echo "Done. Start runner: cd ~/actions-runner && ./run.sh"
echo "Then GitHub: Actions → CodeQL → Run workflow (NOT re-run old Unity self-hosted)."
