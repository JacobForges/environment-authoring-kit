# Multiplayer setup (free tier only)

Packages are in `Packages/manifest.json`:

| Package | Role |
|---------|------|
| `com.unity.netcode.gameobjects` | Player sync, host/client |
| `com.unity.services.multiplayer` | Relay + Lobby (internet, no port forward) |
| `com.unity.services.authentication` | Anonymous sign-in (free) |
| `com.unity.services.vivox` | Positional voice chat (25 m lobby channel) |

**No credit card required** until you exceed free limits.

## One-time Unity Dashboard (free)

1. [dashboard.unity.com](https://dashboard.unity.com) → **Create project** (or use existing).
2. Link to Hub: **Edit → Project Settings → Services** → link **Project ID**.
3. Enable (all have free tiers):
   - **Authentication** (Anonymous + **Unity Player Accounts** identity provider)
   - **Cloud Save** (player profile + purchase entitlements — lifelong on registered accounts)
   - **Relay**
   - **Lobby**
   - **Vivox**
4. In the Editor: **Game → Setup Unity Player Accounts** (opens Dashboard auth pages).
5. **Do not add a payment method** if you want billing to stop at free limits.
6. **Finance → Budget alerts** → enable 75% / 100% for Relay + Vivox.

## Free tier caps (design targets)

| Service | Free | Use in Hub |
|---------|------|------------|
| Relay | **50 avg monthly CCU** | Sessions capped at **24** players (under free-tier average) |
| Vivox | **5,000 PCU/month** | One **positional** voice channel per lobby (25 m audible range) |
| Lobby | Bandwidth free tier | Public Quick Join |

## What did what (avoid confusion)

| Thing | What it is | Wired into Hub? |
|-------|------------|-----------------|
| **Packages in manifest** (`netcode`, `services.multiplayer`, `authentication`, `vivox`) | Unity networking + cloud Relay/Lobby + voice | Yes — required for Host/Join |
| **Multiplayer Center** (`com.unity.multiplayer.center`) | Package Manager wizard / docs only | No runtime UI — safe to ignore |
| **`Game → Setup Portfolio Menu`** | Our code: `PortfolioMainMenuController` + `PortfolioUiKit` + `PortfolioSessionHud` | Yes — title menu + in-game session strip |
| **`Assets/SlimUI/Modern Menu 1/`** | Asset Store menu pack (sprites/fonts) | **Used for visuals** via `PortfolioUiTheme` (setup copies refs, not SlimUI scenes) |
| **`WorldEconomyHud`** (Wallet top-left) | Env Kit gameplay overlay | Separate from menu; hidden while menu is up |
| **Recap `ApprovedIntro.png`** | Video intro card for `DemoRecapPresentation.mp4` | **Not** menu wallpaper (was a setup bug; fixed) |

## After packages resolve (Unity)

1. Open Hub in Unity — wait for Package Manager import (`com.unity.services.vivox` **16.9.0**).
2. **Game → Setup Vivox** — enable on Dashboard, sync credentials to **Project Settings → Services → Vivox**.
3. **Edit → Project Settings → Vivox** → Environment: **Automatic**.
4. Wire MainScene (one menu command):
   ```text
   Game → Setup Portfolio Menu (MainScene)
   ```
   Creates `PortfolioMultiplayerRoot`, title menu, NetworkManager, player prefab, spawn ring.
   Run **Game → Setup Gameplay Demo (MainScene)** first if `DemoPlayer` is missing.
5. **Voice:** Host Session or Play Online joins a Vivox **positional** channel (25 m). Hold **V** for push-to-talk (toggle in **O** Settings). Agent **voice commands** need `HUB_STT_API_URL` — see [OPERATIONS.md](OPERATIONS.md).
6. **Client builds** (class handout — Mac + Windows):
   - **Game → Build/Windows Standalone Client** → `Builds/WindowsClient/DeepTrainAcademy.exe` (x64, Vivox supported; allow mic permission)
   - **Game → Build/macOS Standalone Client** → `Builds/macOSClient/DeepTrainAcademy.app` (Intel 64-bit for Vivox; Editor on M-series Mac is fine for dev)

**Class multiplayer:** one student **Host Session**; classmates **Play Online**. No 24/7 server or VPS required — teammates take turns hosting during class.

## Runtime skeleton

- `Assets/Scripts/Multiplayer/PortfolioMultiplayerConfig.cs` — caps + lobby names
- `Assets/Scripts/Multiplayer/PortfolioSessionOrchestrator.cs` — host/join + Vivox voice flow
- `Assets/Scripts/Multiplayer/PortfolioVivoxSupport.cs` — positional channel join/leave (25 m)
- `Assets/Scripts/Multiplayer/PortfolioVivoxPositionReporter.cs` — `Set3DPosition` updates
- `Assets/Scripts/Multiplayer/PortfolioVoicePushToTalk.cs` — PTT key (**V** default)
- `Assets/Scripts/Competition/Voice/HubVoiceCommandCapture.cs` — cloud STT agent commands
- `Assets/Scripts/Multiplayer/HubContentUpdater.cs` — menu-boot manifest check; caches config JSON + map bundle under `persistentDataPath/HubContent/`; lobby advertises `contentVersion` + `worldSeed` for Play Online matching

**Content updater:** baseline `Assets/StreamingAssets/Portfolio/hub-content-manifest.json`. Set `remoteManifestUrl` (or env `HUB_CONTENT_MANIFEST_URL`) when hosting updates. Clients show *Checking for updates…* / *Update available* / *Up to date* on the title menu footer. Play Online is blocked until `contentVersion` and `worldSeed` match the host lobby (and any pending map download finishes).

### Publish map updates (JacobForges — sole map author)

1. Finish EnvKit world build (`CaveBuildWorldSessionManifest.json` in Generated).
2. **Game → Publish Content Manifest** — bumps `contentVersion`, stamps `worldSeed`, writes `StreamingAssets/Portfolio/hub-content-manifest.json` + `map-bundle.zip`.
3. Upload to static hosting (GitHub raw, S3, school CDN):
   - `hub-content-manifest.json` (set `bundleUrl`, `configFiles[].url`, `remoteManifestUrl` self-link)
   - `map-bundle.zip`
   - competition config JSONs if changed
4. Point classmates at the manifest:
   - env `HUB_CONTENT_MANIFEST_URL=https://…/hub-content-manifest.json`, or
   - bake `remoteManifestUrl` into the shipped baseline manifest.
5. One student **Host Session** — lobby publishes `contentVersion` + `worldSeed` from `HubContentUpdater.LocalContentVersion` / `LocalWorldSeed`.
6. Classmates launch the app → menu checks remote manifest → downloads map bundle → **Play Online** when footer shows matching content + seed.

## Player & operator docs

| Doc | Audience |
|-----|----------|
| [PLAYER_MANUAL.md](PLAYER_MANUAL.md) | Controls, chat, voice, settings |
| [MULTIPLAYER_GUIDE.md](MULTIPLAYER_GUIDE.md) | Classmates — host, join, updates |
| [OPERATIONS.md](OPERATIONS.md) | JacobForges — Vivox, manifest |
| [PATCH_NOTES.md](PATCH_NOTES.md) | Current feature set |
| [CREDITS.md](CREDITS.md) | Author attribution |
| [LEGAL/README.md](LEGAL/README.md) | Portfolio EULA placeholder |

## ngrok

**Not used for game packets.** Optional later for a status webpage only.

## Verify packages imported

```bash
grep -E 'netcode|multiplayer|vivox|authentication' /Users/jacob/Hub/Packages/packages-lock.json | head -20
```
