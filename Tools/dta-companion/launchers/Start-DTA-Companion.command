#!/bin/bash
cd "$(dirname "$0")"
export PORT=37123
export NODE_ENV=production
if [[ ! -f "dist/server.cjs" ]]; then
  osascript -e 'display alert "DTA Companion" message "Missing dist/server.cjs. Rebuild the standalone client with companion bundling enabled."'
  exit 1
fi
if ! command -v node >/dev/null 2>&1; then
  osascript -e 'display alert "Node.js required" message "Install Node.js from https://nodejs.org then run Start-DTA-Companion again."'
  exit 1
fi
open "http://127.0.0.1:${PORT}/"
exec node dist/server.cjs
