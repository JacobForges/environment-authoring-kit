#!/bin/bash
# Pull Vivox credentials from Unity Dashboard into ProjectSettings (same path the editor uses).
set -euo pipefail

HUB="/Users/jacob/Hub"
PROJECT_ID="56a5eef0-fdad-4c12-ab8a-6023c8a0936d"
SETTINGS="$HUB/ProjectSettings/Packages/com.unity.services.vivox/Settings.json"

TOKEN="${UNITY_ACCESS_TOKEN:-}"
if [[ -z "$TOKEN" ]]; then
  # Prefer the live editor token (Hub launch arg); ignore stale shell env tokens.
  TOKEN=$(pgrep -lf "Unity.app/Contents/MacOS/Unity " 2>/dev/null | sed -n 's/.*-accessToken \([^ ]*\).*/\1/p' | head -1)
fi

if [[ -z "$TOKEN" ]]; then
  echo "No UNITY_ACCESS_TOKEN and no running Hub-launched Unity editor." >&2
  echo "Open Hub from Unity Hub (signed in) or export UNITY_ACCESS_TOKEN." >&2
  exit 1
fi

GW_JSON=$(/usr/bin/curl -sS -X POST "https://services.unity.com/api/auth/v1/genesis-token-exchange/unity" \
  -H "Content-Type: application/json" \
  -H "AUTHORIZATION: Bearer $TOKEN" \
  -d "{\"token\":\"$TOKEN\"}")

GW_TOKEN=$(/usr/bin/python3 -c "import json,sys; print(json.loads(sys.argv[1]).get('token',''))" "$GW_JSON")
if [[ -z "$GW_TOKEN" ]]; then
  echo "Gateway token exchange failed: $GW_JSON" >&2
  exit 1
fi

CREDS_JSON=$(/usr/bin/curl -sS "https://services.unity.com/api/vivox/v1/projects/$PROJECT_ID/credentials" \
  -H "AUTHORIZATION: Bearer $GW_TOKEN")

/usr/bin/python3 - "$SETTINGS" "$CREDS_JSON" <<'PY'
import json, sys, pathlib

settings_path = pathlib.Path(sys.argv[1])
payload = json.loads(sys.argv[2])
creds = (payload or {}).get("credentials")
if not creds:
    print("[Vivox] credentials:null — cloud project Hub has no Vivox credentials yet (Unity dashboard provisioning pending).", file=sys.stderr)
    sys.exit(2)

env = creds.get("environment") or {}
issuer = creds.get("issuer") or ""
server = env.get("serverUri") or ""
domain = env.get("domain") or ""
key = creds.get("key") or ""

if not issuer or not server or not domain:
    print(f"[Vivox] Incomplete credential payload: {payload}", file=sys.stderr)
    sys.exit(3)

def entry(key_name, type_name, value):
    return {
        "type": type_name,
        "key": key_name,
        "value": json.dumps({"m_Value": value}),
    }

values = [
    entry("server", "System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", server),
    entry("domain", "System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", domain),
    entry("tokenIssuer", "System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", issuer),
    entry("tokenKey", "System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", key),
    entry("isServiceEnabled", "System.Boolean, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", True),
    entry("isTestMode", "System.Boolean, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", False),
    entry("isEnvironmentCustom", "System.Boolean, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", False),
]

settings_path.parent.mkdir(parents=True, exist_ok=True)
settings_path.write_text(json.dumps({"m_Dictionary": {"m_DictionaryValues": values}}, indent=4) + "\n", encoding="utf-8")
print(f"[Vivox] Wrote credentials → {settings_path}")
print(f"  server={server}")
print(f"  domain={domain}")
print(f"  issuer={issuer}")
PY
