#!/bin/bash
# Run batch download if mixamo.local.env has a real Bearer (not placeholder).
set -euo pipefail
cd "$(dirname "$0")"
ENV_FILE="mixamo.local.env"
if [[ ! -f "$ENV_FILE" ]]; then
  echo "Create $ENV_FILE with one line:"
  echo "  MIXAMO_BEARER=Bearer eyJ..."
  echo "(from Network → export POST → Request → Authorization)"
  exit 1
fi
# shellcheck disable=SC1090
source "$ENV_FILE"
export MIXAMO_BEARER
node mixamo-batch-download.mjs
