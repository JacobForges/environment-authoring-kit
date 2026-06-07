# Mixamo FBX — quick batch (one-time login)

The **FullWorld build does not wait** for these files. Build anytime; swap FBX in later.

## Option A — Automatic (fastest after setup)

1. Sign in at https://www.mixamo.com
2. Open DevTools → **Network** tab
3. Download **any** character once (Download button)
4. Click the `export` request → copy the **Authorization** header value
5. In Terminal:

```bash
export MIXAMO_BEARER='paste Bearer token here'
cd /Users/jacob/Hub/PlanV4-AssetReview
node mixamo-batch-download.mjs
```

Files land in `Assets/EnvironmentKit/AdobeImports/Mixamo/Characters/*.fbx`

## Option B — Manual

Use `MIXAMO_DOWNLOAD_CHECKLIST.md` — one character at a time, **FBX for Unity**, **With Skin**.
