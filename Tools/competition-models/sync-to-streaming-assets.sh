#!/usr/bin/env bash
# Copy trained ONNX + JSON into Unity Resources (Inference Engine imports .onnx → ModelAsset).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SRC="$ROOT/Tools/competition-models"
RES="$ROOT/Assets/Resources/Competition"
STREAM="$ROOT/Assets/StreamingAssets/Competition"

mkdir -p "$RES/Models" "$STREAM/onboarding" "$STREAM/dialogue" "$STREAM/season"
for onnx in "$SRC/trained/"*.onnx; do
  base="$(basename "$onnx")"
  # Legacy monolithic export uses GRU — Unity cannot import it.
  [[ "$base" == "competition_agent_chat.onnx" ]] && continue
  cp "$onnx" "$RES/Models/"
done
cp "$SRC/trained/"*.json "$STREAM/Models/" 2>/dev/null || cp "$SRC/trained/"*.json "$RES/Models/" 2>/dev/null || true
cp "$SRC/onboarding/questions.json" "$STREAM/onboarding/"
cp "$SRC/dialogue/templates.json" "$STREAM/dialogue/"
cp "$SRC/season/season_manifest.template.json" "$STREAM/season/season_manifest.json"
echo "Synced ONNX → $RES/Models (Unity imports as ModelAsset)"
echo "Synced JSON manifests → $STREAM"
