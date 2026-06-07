#!/usr/bin/env bash
# Register this Mac as a GitHub Actions self-hosted runner for environment-authoring-kit.
# Usage:
#   1. Open GitHub (steps printed below) and copy the registration token.
#   2. ./setup-github-selfhosted-runner.sh PASTE_TOKEN_HERE
set -euo pipefail

REPO_URL="https://github.com/JacobForges/environment-authoring-kit"
RUNNER_VERSION="2.334.0"
RUNNER_DIR="${HOME}/actions-runner"
ARCHIVE="actions-runner-osx-arm64-${RUNNER_VERSION}.tar.gz"
DOWNLOAD_URL="https://github.com/actions/runner/releases/download/v2.334.0/${ARCHIVE}"

if [[ "${1:-}" == "" || "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  cat <<EOF
GitHub self-hosted runner setup (macOS arm64)

WHERE to get the token (you must do this in the browser):
  1. Open: ${REPO_URL}/settings/actions/runners/new?arch=arm64&os=osx
  2. Sign in as JacobForges if asked.
  3. Under "Runners" you will see a token like AXXXXXXXX — copy it.
     (Tokens expire in ~1 hour; generate a fresh one if configure fails.)

THEN run:
  $0 YOUR_TOKEN_HERE

To start the runner after configure:
  cd ${RUNNER_DIR} && ./run.sh

To run in the background (until logout):
  cd ${RUNNER_DIR} && nohup ./run.sh >> runner.log 2>&1 &

EOF
  exit 0
fi

TOKEN="$1"

if [[ ! -d "$RUNNER_DIR" ]]; then
  echo "Downloading Actions runner ${RUNNER_VERSION} to ${RUNNER_DIR} …"
  mkdir -p "$RUNNER_DIR"
  tmp="$(mktemp -d)"
  curl -fsSL "$DOWNLOAD_URL" -o "${tmp}/${ARCHIVE}"
  tar xzf "${tmp}/${ARCHIVE}" -C "$RUNNER_DIR"
  rm -rf "$tmp"
else
  echo "Using existing ${RUNNER_DIR}"
fi

cd "$RUNNER_DIR"

if [[ -f .runner ]]; then
  echo "Runner already configured (.runner exists). To re-register, remove ${RUNNER_DIR} and run again."
  echo "Start with: cd ${RUNNER_DIR} && ./run.sh"
  exit 0
fi

./config.sh --url "$REPO_URL" --token "$TOKEN" --unattended --replace

echo ""
echo "Configured. Start the runner:"
echo "  cd ${RUNNER_DIR} && ./run.sh"
echo ""
echo "Leave that Terminal open while CodeQL runs, or use nohup (see --help)."
