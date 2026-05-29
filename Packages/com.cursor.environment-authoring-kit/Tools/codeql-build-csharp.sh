#!/usr/bin/env bash
# MSBuild/dotnet build for CodeQL manual mode (run after run-codeql-unity-prep.sh).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
SLN="${CODEQL_SLN:-}"
if [[ -z "$SLN" && -f "${ROOT}/Logs/codeql-last.sln" ]]; then
  SLN="$(tr -d '\r\n' < "${ROOT}/Logs/codeql-last.sln")"
fi
if [[ -z "$SLN" || ! -f "$SLN" ]]; then
  SLN="${ROOT}/Hub.sln"
fi
if [[ ! -f "$SLN" ]]; then
  shopt -s nullglob
  candidates=("${ROOT}"/*.sln)
  if [[ ${#candidates[@]} -gt 0 ]]; then
    SLN="${candidates[0]}"
  fi
fi
if [[ ! -f "$SLN" ]]; then
  echo "ERROR: No .sln — run run-codeql-unity-prep.sh first."
  exit 1
fi
echo "Building solution: ${SLN}"

cd "$ROOT"

if command -v dotnet >/dev/null 2>&1; then
  echo "Building with dotnet: ${SLN}"
  dotnet build "$SLN" -c Release -v minimal --nologo
  exit $?
fi

if command -v msbuild >/dev/null 2>&1; then
  echo "Building with msbuild: ${SLN}"
  msbuild "$SLN" /p:Configuration=Release /v:minimal /nologo
  exit $?
fi

echo "ERROR: Install .NET SDK (dotnet) or MSBuild for CodeQL C# tracing."
exit 1
