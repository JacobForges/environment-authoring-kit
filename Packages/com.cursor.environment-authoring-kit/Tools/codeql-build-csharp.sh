#!/usr/bin/env bash
# MSBuild/dotnet build for CodeQL manual mode (run after run-codeql-unity-prep.sh).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
SLN=""
if [[ -f "${ROOT}/Logs/codeql-last.sln" ]]; then
  SLN="$(tr -d '\r\n' < "${ROOT}/Logs/codeql-last.sln")"
fi
if [[ -z "$SLN" || ! -f "$SLN" ]]; then
  shopt -s nullglob
  candidates=("${ROOT}"/*.sln)
  if [[ ${#candidates[@]} -gt 0 ]]; then
    SLN="${candidates[0]}"
  fi
fi

build_one() {
  local proj="$1"
  if [[ ! -f "$proj" ]]; then
    return 1
  fi
  echo "Building ${proj}"
  if command -v dotnet >/dev/null 2>&1; then
    dotnet build "$proj" -c Release -v minimal --nologo
    return $?
  fi
  if command -v msbuild >/dev/null 2>&1; then
    msbuild "$proj" /p:Configuration=Release /v:minimal /nologo
    return $?
  fi
  echo "ERROR: Install .NET SDK (dotnet) or MSBuild."
  exit 1
}

cd "$ROOT"

# Prefer kit projects (faster, fewer third-party Unity package compile failures on CI).
EDITOR_PROJ="${ROOT}/EnvironmentAuthoringKit.Editor.csproj"
RUNTIME_PROJ="${ROOT}/EnvironmentAuthoringKit.Runtime.csproj"
if [[ -f "$EDITOR_PROJ" ]]; then
  build_one "$EDITOR_PROJ"
  if [[ -f "$RUNTIME_PROJ" ]]; then
    build_one "$RUNTIME_PROJ"
  fi
  exit 0
fi

if [[ -n "$SLN" && -f "$SLN" ]]; then
  echo "Building solution: ${SLN}"
  if command -v dotnet >/dev/null 2>&1; then
    dotnet build "$SLN" -c Release -v minimal --nologo
    exit $?
  fi
  msbuild "$SLN" /p:Configuration=Release /v:minimal /nologo
  exit $?
fi

echo "ERROR: No .sln or EnvironmentAuthoringKit.*.csproj — run run-codeql-unity-prep.sh first."
exit 1
