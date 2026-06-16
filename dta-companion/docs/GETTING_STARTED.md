# Getting Started — DTA Training Companion™

## What you need

| Requirement | Notes |
|-------------|--------|
| **Node.js 18+** | [nodejs.org](https://nodejs.org) — required for shipped `DTACompanion/` launcher |
| **Deep Train Academy** (Unity) | Optional at first; needed for live sync & gameplay samples |
| **API keys** | Optional — Cursor and/or Gemini for cloud coach; works offline without them |

## Purpose (30 seconds)

The **DTA Training Companion** is a **separate browser lab** that ships beside the game. You use it to:

1. **Coach** — ask the AI lab scientist how to improve behavior cloning
2. **Inspect** — view brain metrics, training studio, lineage
3. **Market** — download ONNX brains into **3 model slots**
4. **Deploy** — push checkpoints to disk; Unity **auto-imports** on launch
5. **Customize** — Agent Studio cosmetics (helmet, visor, chassis, trail, emblem, voice)
6. **Classroom** — cohort leaderboards (Firebase optional)

The game and companion share data through a **folder on disk**, not an in-game webview.

```
Deep Train Academy (Unity)          DTA Companion (browser + Node)
        │                                      │
        │  gameplay samples, agent id          │  coach, market, deploy
        └──────────────┬───────────────────────┘
                       ▼
     ~/Library/Application Support/DeepTrainAcademy/checkpoints/{agentId}/
```

## Developer setup (Hub repo)

```bash
cd dta-companion
npm install
cp .env.example .env.local    # optional GEMINI_API_KEY, CURSOR_API_KEY
npm run dev                     # http://localhost:3000
```

Unity menu: **Hub → Competition → Launch Training Companion** (opens dev URL).

## Player setup (shipped build)

After building the game with companion bundled:

```
Builds/macOSClient/
  DeepTrainAcademy.app
  DTACompanion/
    Start-DTA-Companion.command   # macOS
    Start-DTA-Companion.bat       # Windows
```

1. Install Node.js once
2. Double-click **Start-DTA-Companion**
3. Browser opens (default port **37123** for production launcher)
4. Complete the **onboarding wizard** (first launch)

## Onboarding wizard

On first launch you will see 6 steps:

1. **Welcome** — name + what the lab is
2. **Purpose** — game vs companion roles
3. **Lab check** — server health + sync folder path
4. **Coach** — optional Cursor / Gemini keys (Auto recommended)
5. **Link game** — how deploy sync works
6. **Ready** — enter the laboratory

Progress is saved in `companion_settings.json` under your DeepTrainAcademy app data folder.

To replay setup: delete `onboardingComplete` from that file or set it to `false`, then refresh.

## Optional: Firebase auth

Copy `firebase-applet-config.example.json` → `firebase-applet-config.json` with your Firebase web app config. See [FIREBASE_README.md](FIREBASE_README.md). Sandbox sign-in works without Firebase.

## Optional: API keys

| Key | Where | Used for |
|-----|--------|----------|
| Cursor API | Settings or `CURSOR_API_KEY` | Primary coach when **Auto** or **Cursor** |
| Gemini | Settings or `GEMINI_API_KEY` | Coach when Cursor unavailable |
| Hugging Face | Settings | Private model downloads in Model Market |

Keys are stored locally in `companion_settings.json` — never committed to git.

## Next steps

- [ONBOARDING.md](ONBOARDING.md) — detailed wizard guide
- [PLAYER_FLOW.md](PLAYER_FLOW.md) — training loop
- [MODEL_MARKET.md](MODEL_MARKET.md) — brain slots
- [AGENT_STUDIO.md](AGENT_STUDIO.md) — cosmetics
