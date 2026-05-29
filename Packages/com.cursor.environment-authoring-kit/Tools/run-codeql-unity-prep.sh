#!/usr/bin/env bash
# Regenerate Hub.sln / .csproj and compile scripts via Unity (self-hosted CodeQL only).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
UNITY="$(printf '%s' "${UNITY_PATH:-}" | tr -d '\r\n' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"

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

# Do not pass -quit: bootstrap must run EditorApplication.update, sync .sln, then EditorApplication.Exit.
"$UNITY" -batchmode -nographics -projectPath "$ROOT" \
  -executeMethod EnvironmentAuthoringKit.Editor.CodeQlUnityBootstrap.PrepareForCodeQl \
  -logFile "$LOG_FILE"
UNITY_EXIT=$?
if [[ "$UNITY_EXIT" -ne 0 ]]; then
  echo "ERROR: Unity exited ${UNITY_EXIT}. See ${LOG_FILE}"
  exit "$UNITY_EXIT"
fi

shopt -s nullglob
SLNS=("${ROOT}"/*.sln)
if [[ ${#SLNS[@]} -eq 0 ]]; then
  echo "ERROR: No .sln in ${ROOT} after Unity prep. See ${LOG_FILE}"
  exit 1
fi

# Prefer Hub.sln when present; otherwise first solution (repo folder name may differ on CI).
SLN="${ROOT}/Hub.sln"
if [[ ! -f "$SLN" ]]; then
  SLN="${SLNS[0]}"
fi
echo "SLN=${SLN}" >> "${GITHUB_ENV:-/dev/null}" 2>/dev/null || true
echo "${SLN}" > "${ROOT}/Logs/codeql-last.sln"
echo "OK — solution present: ${SLN}"
