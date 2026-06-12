#!/bin/bash
# Unstick Unity Hub / Editor when licensing or Cache Server hangs.
set -euo pipefail

echo "=== 1) Quit Unity processes ==="
osascript -e 'quit app "Unity"' 2>/dev/null || true
osascript -e 'quit app "Unity Hub"' 2>/dev/null || true
pkill -9 -f "Unity.Licensing.Client" 2>/dev/null || true
pkill -9 -f "UnityPackageManager" 2>/dev/null || true
pkill -9 -f "Unity Hub" 2>/dev/null || true
pkill -9 -f "Unity.app/Contents/MacOS/Unity" 2>/dev/null || true
sleep 3

echo "=== 2) Start EDITOR licensing client only (Hub client is too old — causes 505 handshake) ==="
LIC="/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/Helpers/UnityLicensingClient.app/Contents/MacOS/Unity.Licensing.Client"
if [[ -x "$LIC" ]]; then
  "$LIC" --namedPipe Unity-LicenseClient-jacob-6000.4.6 &
  sleep 4
else
  echo "Licensing client not found at expected path — install 6000.4.6f1 via Hub."
fi

echo "=== 3) Disable Cache Server wait in Hub project ==="
SETTINGS="${HOME}/Hub/ProjectSettings/EditorSettings.asset"
if [[ -f "$SETTINGS" ]]; then
  # Mode 0 = use preferences; force endpoint empty and download off to avoid hang.
  perl -i -pe 's/m_CacheServerMode: \d+/m_CacheServerMode: 2/' "$SETTINGS" 2>/dev/null || true
  perl -i -pe 's/m_CacheServerEnableDownload: 1/m_CacheServerEnableDownload: 0/' "$SETTINGS" 2>/dev/null || true
  perl -i -pe 's/m_CacheServerEndpoint: .*/m_CacheServerEndpoint: /' "$SETTINGS" 2>/dev/null || true
  echo "Patched EditorSettings.asset (cache server download off)."
fi

echo "=== 4) Clear stuck Hub Library caches (safe; reimports on next open) ==="
HUB="${HOME}/Hub"
rm -rf "${HUB}/Library/PackageCache/.tmp" 2>/dev/null || true
rm -rf "${HUB}/Library/StateCache" 2>/dev/null || true
rm -f "${HUB}/Library/ilpp.pid" 2>/dev/null || true

echo "=== 5) Open Hub project DIRECTLY in Editor (bypasses Hub splash) ==="
UNITY="/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity"
if [[ ! -x "$UNITY" ]]; then
  echo "Unity 6000.4.6f1 not found. Install via Unity Hub, then re-run."
  open -a "Unity Hub"
  exit 1
fi

# Pick first scene if exists
SCENE=""
for s in "${HUB}/Assets/TheStart.unity" "${HUB}/Assets/MainScene.unity" "${HUB}/Assets/NeonCity.unity"; do
  [[ -f "$s" ]] && SCENE="$s" && break
done

ARGS=(-projectPath "$HUB" -acceptSoftwareTermsForThisRunOnly)
[[ -n "$SCENE" ]] && ARGS+=(-openScene "$SCENE")

echo "Launching WITHOUT Hub IPC (avoids LicenseClient-jacob protocol mismatch):"
echo "  $UNITY ${ARGS[*]}"
nohup "$UNITY" "${ARGS[@]}" >/tmp/unity-hub-direct.log 2>&1 &
sleep 2
echo ""
echo "Editor launching in background. Log: /tmp/unity-hub-direct.log"
echo "If licensing dialog appears IN THE EDITOR, sign in there (not only Hub)."
echo "Also update Unity Hub to v3.18.2+ (banner in Hub) — old Hub licensing breaks 6000.4.6."
echo "When loaded, run:"
echo "  Window → Environment Kit → World → Wire Hub Combat"
echo "  Window → Environment Kit → World → Build Cinematic Timelines"
open -a "Console" /tmp/unity-hub-direct.log 2>/dev/null || tail -20 /tmp/unity-hub-direct.log
