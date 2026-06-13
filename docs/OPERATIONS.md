# Operations — Deep Train Academy (Hub)

**Operator:** [JacobForges](https://github.com/JacobForges) — sole author, map publisher, and content administrator.

This runbook covers what JacobForges runs in Unity Editor and on external services so classmates can play online with voice and content updates.

Player docs: [PLAYER_MANUAL.md](PLAYER_MANUAL.md) · [MULTIPLAYER_GUIDE.md](MULTIPLAYER_GUIDE.md)

---

## Unity Dashboard checklist (one-time)

1. Open [dashboard.unity.com](https://dashboard.unity.com) → select the Hub cloud project.
2. Link project in Editor: **Edit → Project Settings → Services** → Project ID.
3. Enable services (free tier):

| Service | Purpose |
|---------|---------|
| **Authentication** | Anonymous + Unity Player Accounts |
| **Cloud Save** | Profile / entitlements (optional) |
| **Relay** | Internet game traffic |
| **Lobby** | Session listing |
| **Vivox** | Positional voice chat |

4. **Game → Setup Unity Player Accounts** (if using registered accounts).
5. **Finance → Budget alerts** at 75% / 100% for Relay + Vivox.
6. Do **not** add a payment method if you want hard free-tier stops.

---

## Vivox setup (Editor)

Run once per machine / after cloning:

```
Game → Setup Vivox
```

Workflow:

1. If Vivox is not enabled on Dashboard → dialog opens Dashboard → enable **Vivox Voice and Text Chat**.
2. Return → **Game → Setup Vivox** again → credentials sync to **Project Settings → Services → Vivox**.
3. Package `com.unity.services.vivox` is listed in `Packages/manifest.json` (16.9.0). If missing, use **Game → Install Vivox Package**.
4. Set Vivox Environment: **Automatic** (Project Settings → Vivox).

Verify: Host or Join a session — Console logs `[Multiplayer] Vivox positional voice joined (25m).`

Shell fallback: `Tools/multiplayer/sync-vivox-credentials.sh` (signed-in Unity CLI).

---

## MainScene wiring (Editor)

Order:

1. **Game → Setup Gameplay Demo (MainScene)** — if `DemoPlayer` missing.
2. **Game → Setup Portfolio Menu (MainScene)** — NetworkManager, Relay orchestrator, title menu.
3. Play Mode → test **Host Session** + **Play Online**.

---

## Publish content manifest (map updates)

When a new EnvKit world build is ready:

1. Confirm `Assets/EnvironmentKit/Generated/CaveBuildWorldSessionManifest.json` exists locally.
2. **Game → Publish Content Manifest**
   - Bumps `contentVersion`
   - Stamps `worldSeed`
   - Writes `Assets/StreamingAssets/Portfolio/hub-content-manifest.json`
   - Packages `map-bundle.zip`
3. Upload to static hosting (GitHub raw, S3, school CDN):
   - `hub-content-manifest.json` (set `bundleUrl`, `configFiles[].url`, `remoteManifestUrl`)
   - `map-bundle.zip`
   - Updated competition JSON configs if changed
4. Point clients at the manifest:
   - Environment: `HUB_CONTENT_MANIFEST_URL=https://…/hub-content-manifest.json`, or
   - Bake `remoteManifestUrl` into the shipped baseline manifest.

---

## Agent voice commands (cloud STT)

Voice commands are **off** until STT is configured.

Set environment variables before launching Editor or player build:

| Variable | Example | Purpose |
|----------|---------|---------|
| `HUB_STT_API_URL` | `https://api.openai.com` | Base URL (auto-appends `/v1/audio/transcriptions`) |
| `HUB_STT_API_KEY` | `sk-…` | Bearer token |
| `HUB_STT_MODEL` | `whisper-1` | Optional model name |

Players toggle **Voice ON** in the chat panel. Without STT env, the System tab explains configuration is missing.

---

## Standalone client builds (class handout)

Ship **macOS `.app`** and **Windows `.exe`** — classmates install one or the other.

| Menu | Output |
|------|--------|
| **Game → Build/Windows Standalone Client** | `Builds/WindowsClient/DeepTrainAcademy.exe` |
| **Game → Build/macOS Standalone Client** | `Builds/macOSClient/DeepTrainAcademy.app` |

Both menus ensure `Assets/MainScene.unity` is enabled in Build Settings and set product name **Deep Train Academy** / company **JacobForges** for the build.

**Class sessions:** one student clicks **Host Session**; others **Play Online**. No 24/7 server required.

Quick checklist: **[ONLINE_TEST_CHECKLIST.md](ONLINE_TEST_CHECKLIST.md)**

### Windows (Vivox)

- Target: **Windows x64** (menu switches platform automatically).
- Vivox positional voice works on Windows player builds.
- Classmates must allow **microphone** access when Windows prompts.

### macOS (Vivox)

**Build menu sets Intel 64-bit** for Vivox. Manual override: Project Settings → Player → macOS → Architecture → **Intel 64-bit**.

Vivox does not support Apple Silicon-only player binaries yet. Editor on M-series Macs is fine for development.

---

## Session limits (free tier design)

| Cap | Value |
|-----|-------|
| Max players / session | 24 |
| Relay target | Under 50 avg monthly CCU |
| Vivox | One positional channel per lobby; 5,000 PCU/month free tier |

---

## Graceful shutdown

**Logout / Quit** in game or Editor play-mode exit triggers:

1. Vivox `LeaveAllChannels`
2. Hosted lobby delete (if host)
3. NGO safe teardown
4. Profile / cloud flush

Implemented in `HubApplicationQuit` → `PortfolioNetworkShutdown.GracefulTeardownAsync`.

---

## Pre-class checklist

- [ ] Dashboard services enabled (Auth, Relay, Lobby, Vivox)
- [ ] **Game → Setup Vivox** credentials on disk
- [ ] Vivox package imported (manifest 16.9.0)
- [ ] Latest manifest published + uploaded
- [ ] **Game → Build/Windows Standalone Client** and/or **Game → Build/macOS Standalone Client** for class handout
- [ ] Host Session + Play Online smoke test ([ONLINE_TEST_CHECKLIST.md](ONLINE_TEST_CHECKLIST.md))
- [ ] `HUB_STT_API_URL` set for voice commands demo (optional)
- [ ] macOS Intel build for classmates on Mac voice; Windows x64 `.exe` for Windows classmates

---

## Related

- [MULTIPLAYER_FREE_SETUP.md](MULTIPLAYER_FREE_SETUP.md) — package list & caps  
- [MULTIPLAYER_GUIDE.md](MULTIPLAYER_GUIDE.md) — player-facing join guide  
- [PATCH_NOTES.md](PATCH_NOTES.md) — feature summary  
