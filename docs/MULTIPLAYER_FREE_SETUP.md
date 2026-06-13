# Multiplayer setup (free tier only)

Packages are in `Packages/manifest.json`:

| Package | Role |
|---------|------|
| `com.unity.netcode.gameobjects` | Player sync, host/client |
| `com.unity.services.multiplayer` | Relay + Lobby (internet, no port forward) |
| `com.unity.services.authentication` | Anonymous sign-in (free) |
| `com.unity.services.vivox` | Voice + text chat |

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
| Vivox | **5,000 PCU/month** | One voice channel per lobby |
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

1. Open Hub in Unity — wait for Package Manager import.
2. **Edit → Project Settings → Vivox** → Environment: **Automatic**.
3. Wire MainScene (one menu command):
   ```text
   Game → Setup Portfolio Menu (MainScene)
   ```
   Creates `PortfolioMultiplayerRoot`, title menu, NetworkManager, player prefab, spawn ring.
   Run **Game → Setup Gameplay Demo (MainScene)** first if `DemoPlayer` is missing.
4. macOS **standalone build** for Vivox: Project Settings → Player → macOS → **Intel 64-bit** (Vivox does not support Apple Silicon player builds yet). Editor on M4 is fine for dev; ship Intel build or drop in-game voice on Mac .app until Vivox ships arm64.

## Dedicated cloud server (laptop off)

See [MULTIPLAYER_DEDICATED_CLOUD.md](MULTIPLAYER_DEDICATED_CLOUD.md).

1. **Game → Build/Linux Dedicated Cloud Server**
2. Run on a VPS (`./HubDedicatedServer` — auto-starts on server builds)
3. Mac dev: **Game → Play Mode → Start Dedicated Cloud Server (Editor)**, then **Play Online** from a client

## Runtime skeleton

- `Assets/Scripts/Multiplayer/PortfolioMultiplayerConfig.cs` — caps + lobby names
- `Assets/Scripts/Multiplayer/PortfolioDedicatedCloudHost.cs` — headless Relay + lobby heartbeat
- `Assets/Scripts/Multiplayer/PortfolioSessionOrchestrator.cs` — host/join/voice flow
- `Assets/Scripts/Multiplayer/HubContentUpdater.cs` — menu-boot manifest check; caches config JSON under `persistentDataPath/HubContent/`; lobby advertises `contentVersion` + `worldSeed` for Play Online matching

**Content updater MVP:** baseline `Assets/StreamingAssets/Portfolio/hub-content-manifest.json`. Set `remoteManifestUrl` (or env `HUB_CONTENT_MANIFEST_URL`) when hosting updates. Clients show *Checking for updates…* / *Update available* / *Up to date* on the title menu footer.

## ngrok

**Not used for game packets.** Optional later for a status webpage only.

## Verify packages imported

```bash
grep -E 'netcode|multiplayer|vivox|authentication' /Users/jacob/Hub/Packages/packages-lock.json | head -20
```
