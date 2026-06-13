#!/usr/bin/env bash
# When Lexar (or external EnvironmentKit-Hub) is unplugged, Unity Library symlinks break.
# This restores local folders so ShaderCache, PackageCache, Generated, etc. work again.
#
# When Lexar is back: quit Unity, run migrate-envkit-to-external.sh to move heavy data off Mac disk.
set -euo pipefail

HUB="${HUB_ROOT:-$(cd "$(dirname "$0")/.." && pwd)}"
LEXAR="/Volumes/Lexar/EnvironmentKit-Hub"

if [[ -d "$LEXAR" ]]; then
  echo "Lexar is mounted at $LEXAR — symlinks should work. Nothing to repair."
  echo "If Unity still errors, quit Unity and re-run: Packages/.../migrate-envkit-to-external.sh"
  exit 0
fi

echo "Lexar not mounted — repairing broken symlinks under $HUB"

repair_path() {
  local rel="$1"
  local abs="$HUB/$rel"
  if [[ ! -L "$abs" ]]; then
    return
  fi
  if [[ -e "$abs" ]]; then
    echo "  ok: $rel"
    return
  fi
  local target
  target="$(readlink "$abs")"
  echo "  fix: $rel (was -> $target)"
  rm -f "$abs"
  mkdir -p "$abs"
}

repair_path "Library/ShaderCache"
# EditorEncounteredVariants is a FILE Unity creates — never mkdir it.
repair_path "Library/Artifacts"
repair_path "Library/Bee"
repair_path "Library/PackageCache"
repair_path "Library/BurstCache"
repair_path "Library/EnvironmentKit"
repair_path "Assets/EnvironmentKit/Generated"
repair_path "Assets/EnvironmentKit/ResearchCache"

echo ""
echo "Done. Local fallbacks created."
echo "  - Shader cache and import caches will rebuild on next Unity open (first launch may be slower)."
echo "  - Generated/ on Mac disk is empty until you plug Lexar back or run a new build."
echo "  - Plug Lexar and run migrate-envkit-to-external.sh when you want external storage again."
