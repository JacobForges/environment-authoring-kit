#!/usr/bin/env bash
# Regenerate Hub.sln / .csproj and compile scripts via Unity (self-hosted CodeQL only).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
UNITY="${UNITY_PATH:-}"

if [[ -z "$UNITY" || ! -x "$UNITY" ]]; then
  echo "ERROR: UNITY_PATH must point to the Unity Editor binary (macOS/Linux)."
  echo "  Example: /Applications/Unity/Hub/Editor/6000.0.46f1/Unity.app/Contents/MacOS/Unity"
  exit 1
fi

LOG_DIR="${ROOT}/Logs"
mkdir -p "$LOG_DIR"
LOG_FILE="${LOG_DIR}/codeql-unity-prep.log"

echo "Project: ${ROOT}"
echo "Unity:   ${UNITY}"
echo "Log:     ${LOG_FILE}"

"$UNITY" -batchmode -nographics -projectPath "$ROOT" \
  -executeMethod EnvironmentAuthoringKit.Editor.CodeQlUnityBootstrap.PrepareForCodeQl \
  -quit -logFile "$LOG_FILE"

if [[ ! -f "${ROOT}/Hub.sln" ]]; then
  echo "ERROR: Hub.sln not found after Unity prep. See ${LOG_FILE}"
  exit 1
fi

echo "OK — Hub.sln present."
