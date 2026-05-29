#!/usr/bin/env bash
# MSBuild/dotnet build for CodeQL manual mode (run after run-codeql-unity-prep.sh).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
SLN="${ROOT}/Hub.sln"

if [[ ! -f "$SLN" ]]; then
  echo "ERROR: Missing ${SLN} — run run-codeql-unity-prep.sh first."
  exit 1
fi

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
