# DTA Training Companion™

**External training laboratory** for [Deep Train Academy™](https://github.com/JacobForges/Hub) — coach agents, manage ONNX brains, deploy checkpoints, and customize cosmetics **outside** the Unity game window.

| Piece | Role |
|--------|------|
| **`dta-companion/`** | This app — React UI + Express API |
| **Unity game** | Play, collect samples, auto-import brains from disk |
| **Shipped build** | `DeepTrainAcademy.app` + **`DTACompanion/`** side by side |

## Does it work?

**Yes.** Production build and TypeScript check pass:

```bash
cd dta-companion
npm install
npm run lint && npm run build
npm run dev    # → http://localhost:3000
```

The UI requires the Node server (`npm run dev` or `Start-DTA-Companion`). Opening static HTML without the server will not work.

**First launch** shows a 6-step onboarding wizard (name, purpose, health check, coach keys, game link, finish).

## Purpose

Train squadmate AI agents smarter between play sessions:

- **AI Coach** — Cursor → Gemini → offline rules (Auto mode)
- **Brain Lab & Training Studio** — inspect adapters, behavior cloning workflow
- **Model Market** — 3 ONNX slots, bundled + GitHub + Hugging Face catalogs
- **Deploy** — write checkpoints; Unity auto-imports on launch
- **Agent Studio** — cosmetic editor + store (6 slots)
- **Classroom** — cohort leaderboards (optional Firebase)

Data syncs via disk:

`~/Library/Application Support/DeepTrainAcademy/checkpoints/{agentId}/`

## Quick start

```bash
cd dta-companion
npm install
cp .env.example .env.local   # optional keys
npm run dev
```

Players (shipped): double-click `DTACompanion/Start-DTA-Companion.command` (needs [Node.js](https://nodejs.org)).

## Documentation

Full doc index: **[docs/README.md](docs/README.md)**

| Guide | Link |
|-------|------|
| Getting started | [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md) |
| Onboarding wizard | [docs/ONBOARDING.md](docs/ONBOARDING.md) |
| Architecture | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) |
| API reference | [docs/API_REFERENCE.md](docs/API_REFERENCE.md) |
| Model market | [docs/MODEL_MARKET.md](docs/MODEL_MARKET.md) |
| Agent studio | [docs/AGENT_STUDIO.md](docs/AGENT_STUDIO.md) |
| AI coach | [docs/COACH.md](docs/COACH.md) |
| Firebase (optional) | [docs/FIREBASE_README.md](docs/FIREBASE_README.md) |
| Legal | [docs/LEGAL.md](docs/LEGAL.md) |

## Ship with game build

```bash
./Tools/dta-companion/package-for-game-build.sh Builds/macOSClient/DTACompanion
```

Unity: **Game → Build** → bundle companion when prompted.

## Legal

© 2026 JacobForges. **Deep Train Academy™** and **DTA Training Companion™** are trademarks.

See [LICENSE](LICENSE), [COPYRIGHT.md](COPYRIGHT.md), [TRADEMARK.md](TRADEMARK.md).

Educational/personal non-commercial use permitted; commercial use requires separate license.
