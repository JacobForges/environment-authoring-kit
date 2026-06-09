#!/bin/bash
# Legacy fallback: opens Terminal.app when headless Personal Voice is blocked.
# Prefer: bash run-recap-headless.sh <capture> --narration-only
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
eval "$(python3 -c "import sys; sys.path.insert(0, '$DIR'); from envkit_paths import ensure_recap_process_env; ensure_recap_process_env()")"
export ENVIRONMENT_KIT_DATA_ROOT
export TMPDIR TEMP TMP
CAPTURE="${1:-}"
if [[ -z "$CAPTURE" ]]; then
  CAPTURE="$(ls -td "$ENVIRONMENT_KIT_DATA_ROOT/DemoCapture"/*/ 2>/dev/null | head -1 || true)"
fi
if [[ -z "$CAPTURE" || ! -d "$CAPTURE" ]]; then
  echo "Usage: bash run-recap-terminal.sh <capture_folder> [--preview|--narration-only] …" >&2
  echo "Data root: $ENVIRONMENT_KIT_DATA_ROOT" >&2
  exit 1
fi

RECAP_ARGS=("$CAPTURE")
shift || true
# Remaining flags (--preview, --narration-only, …). Default: full presentation (not preview).
if [[ $# -gt 0 ]]; then
  RECAP_ARGS+=("$@")
fi

open_recap_if_ready() {
  local recap_path="$1"
  if [[ -f "$recap_path" ]]; then
    echo "Opening recap video..."
    open "$recap_path"
  fi
}

if [[ "${TERM_PROGRAM:-}" == "Apple_Terminal" ]] || [[ -z "${CURSOR_AGENT:-}" ]]; then
  bash "$DIR/run-recap-headless.sh" "${RECAP_ARGS[@]}"
  status=$?
  if [[ $status -eq 0 ]]; then
    if [[ " ${RECAP_ARGS[*]} " == *" --preview "* ]]; then
      open_recap_if_ready "$HOME/Desktop/DemoRecap-Card-Preview/DirectorPreview.mp4"
    elif [[ -f "$CAPTURE/HybridRecapPresentation.mp4" ]]; then
      open_recap_if_ready "$CAPTURE/HybridRecapPresentation.mp4"
    else
      open_recap_if_ready "$CAPTURE/DemoRecapPresentation.mp4"
    fi
  fi
  exit $status
fi

echo "Opening Terminal.app to run recap with Personal Voice…"
echo "Capture: $CAPTURE"

LAUNCH_DIR="$HOME/Library/EnvironmentKit"
mkdir -p "$LAUNCH_DIR"
WRAPPER="$LAUNCH_DIR/recap-terminal-launch.sh"
{
  echo '#!/bin/bash'
  echo 'set -euo pipefail'
  printf 'cd %q\n' "$DIR"
  printf 'export ENVIRONMENT_KIT_DATA_ROOT=%q\n' "$ENVIRONMENT_KIT_DATA_ROOT"
  printf 'export PYTHONUNBUFFERED=1\n'
  printf 'python3 %q' "$DIR/run-producer-recap.py"
  for arg in "${RECAP_ARGS[@]}"; do
    printf ' %q' "$arg"
  done
  echo
  if [[ " ${RECAP_ARGS[*]} " == *" --preview "* ]]; then
    echo 'PREVIEW="$HOME/Desktop/DemoRecap-Card-Preview/DirectorPreview.mp4"'
    echo 'if [[ -f "$PREVIEW" ]]; then echo "Opening recap video..."; open "$PREVIEW"; fi'
  else
    printf 'RECAP=%q\n' "$CAPTURE/HybridRecapPresentation.mp4"
    echo 'if [[ ! -f "$RECAP" ]]; then RECAP='"$(printf '%q' "$CAPTURE/DemoRecapPresentation.mp4")"'; fi'
    echo 'if [[ -f "$RECAP" ]]; then echo "Opening recap video..."; open "$RECAP"; fi'
  fi
  echo 'echo "Done — recap finished (this tab will close)."'
  echo 'exit "$?"'
} > "$WRAPPER"
chmod +x "$WRAPPER"

osascript -e "tell application \"Terminal\" to do script \"bash \" & quoted form of \"$WRAPPER\""
