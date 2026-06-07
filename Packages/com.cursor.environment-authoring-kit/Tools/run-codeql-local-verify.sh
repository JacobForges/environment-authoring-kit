#!/usr/bin/env bash
# Local full C# prep for CodeQL (same steps as GitHub self-hosted job). Run before pushing.
set -euo pipefail

KIT_TOOLS="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$KIT_TOOLS/../../.." && pwd)"
ENV_FILE="${ROOT}/.env.codeql"

if [[ -f "$ENV_FILE" ]]; then
  # shellcheck disable=SC1090
  source "$ENV_FILE"
fi

UNITY="${UNITY_PATH:-}"
if [[ -z "$UNITY" || ! -x "$UNITY" ]]; then
  echo "ERROR: Set UNITY_PATH to your Unity Editor binary."
  echo "  cp Packages/com.cursor.environment-authoring-kit/Tools/env.codeql.example ${ROOT}/.env.codeql"
  echo "  Edit .env.codeql, then re-run this script."
  exit 1
fi

export UNITY_PATH="$UNITY"

echo "=== CodeQL local verify (Hub: ${ROOT}) ==="
"$KIT_TOOLS/run-codeql-unity-prep.sh"
"$KIT_TOOLS/codeql-build-csharp.sh"

echo ""
echo "OK — Unity prep + C# build succeeded."
echo "Next: push to GitHub and run Actions → CodeQL (Unity self-hosted) on a machine with a self-hosted runner."
echo "See Packages/com.cursor.environment-authoring-kit/docs/CODEQL_SELFHOSTED_INSTALL.md"
