#!/bin/bash
# AI Director Studio — local server + UI (standalone, no Unity).
#   bash start.sh [project_path] [--no-open]
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
PYTHON="${PYTHON:-python3}"
for c in /opt/homebrew/bin/python3 /usr/local/bin/python3 /usr/bin/python3; do
  [[ -x "$c" ]] && PYTHON="$c" && break
done

VENV="$DIR/venv"
if [[ ! -x "$VENV/bin/python" ]]; then
  echo "Creating Python venv at $VENV ..."
  "$PYTHON" -m venv "$VENV"
  "$VENV/bin/python" -m pip install -r "$DIR/requirements.txt"
fi
PYTHON="$VENV/bin/python"

export STANDALONE=1

# Load ai-director-studio/.env before Python starts (bash 3.2-safe; no `source` needed).
if [[ -f "$DIR/.env" ]]; then
  while IFS= read -r _line || [[ -n "$_line" ]]; do
    _line="${_line%%#*}"
    _line="${_line#"${_line%%[![:space:]]*}"}"
    [[ -z "$_line" || "$_line" != *=* ]] && continue
    _key="${_line%%=*}"
    _val="${_line#*=}"
    _key="${_key%"${_key##*[![:space:]]}"}"
    _val="${_val#"${_val%%[![:space:]]*}"}"
    _val="${_val%\"}"; _val="${_val#\"}"
    _val="${_val%\'}"; _val="${_val#\'}"
    export "$_key=$_val"
  done < "$DIR/.env"
fi

PROJECT=""
NO_OPEN=0
for arg in "$@"; do
  case "$arg" in
    --no-open) NO_OPEN=1 ;;
    --open) NO_OPEN=0 ;;
    -*) ;;
    *) PROJECT="$arg" ;;
  esac
done

# bash 3.2 (macOS): "${array[@]}" on an empty array fails under set -u — avoid arrays.
if [[ -n "$PROJECT" ]]; then
  if [[ $NO_OPEN -eq 1 ]]; then
    exec "$PYTHON" "$DIR/server/ensure-ai-director.py" "$PROJECT" --no-open
  else
    exec "$PYTHON" "$DIR/server/ensure-ai-director.py" "$PROJECT"
  fi
fi
if [[ $NO_OPEN -eq 1 ]]; then
  exec "$PYTHON" "$DIR/server/ensure-ai-director.py" --no-open
else
  exec "$PYTHON" "$DIR/server/ensure-ai-director.py"
fi
