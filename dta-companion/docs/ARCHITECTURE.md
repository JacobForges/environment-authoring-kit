# Architecture — Companion ↔ Unity

## Components

```
┌─────────────────────────────────────────────────────────────┐
│  dta-companion/                                              │
│  ├── server.ts          Express API + static/Vite (dev)    │
│  ├── server/lib/        coach, models, cosmetics, settings   │
│  ├── src/               React 19 UI                          │
│  └── catalog/           cosmetics.json, github-models.json   │
└─────────────────────────────────────────────────────────────┘
         │ HTTP (localhost:3000 dev / 37123 shipped)
         ▼
┌─────────────────────────────────────────────────────────────┐
│  Browser UI — coach, market, studio, deploy pages          │
└─────────────────────────────────────────────────────────────┘
         │ read/write files
         ▼
┌─────────────────────────────────────────────────────────────┐
│  ~/Library/Application Support/DeepTrainAcademy/             │
│  ├── companion_settings.json                                 │
│  ├── cosmetics.json                                          │
│  ├── model_slots/slot_{1,2,3}/gameplay_adapter.onnx          │
│  └── checkpoints/{agentId}/                                  │
│        ├── gameplay_adapter.onnx                             │
│        ├── pending_import.json                               │
│        └── agent_appearance.json                             │
└─────────────────────────────────────────────────────────────┘
         │ auto-import on launch
         ▼
┌─────────────────────────────────────────────────────────────┐
│  Deep Train Academy (Unity 6)                                │
│  CompetitionCheckpointAutoImport.cs                          │
└─────────────────────────────────────────────────────────────┘
```

## Ports

| Mode | Port | Start command |
|------|------|---------------|
| Development | 3000 | `npm run dev` |
| Shipped launcher | 37123 | `Start-DTA-Companion.command` |

Override with `PORT` or `DTA_COMPANION_PORT`.

## Unity proxy (optional)

If Unity embedded host runs on `127.0.0.1:8765`, the companion server **proxies** some routes (`syncSnapshot`, `deploy`, `install`) to Unity. Standalone mode works without Unity — filesystem fallbacks apply.

## Data ownership

| Data | Location | Owner |
|------|----------|-------|
| Settings & keys | `companion_settings.json` | Companion |
| Model slots | `model_slots/` | Companion |
| Checkpoints | `checkpoints/{agentId}/` | Shared contract |
| Unity adapter | `JacobForges/Deep Train Academy/Competition/Agents/...` | Copied on install |
| Cosmetics | `cosmetics.json` + per-agent appearance | Companion |

## Build & ship

```bash
# From Hub repo
./Tools/dta-companion/package-for-game-build.sh Builds/macOSClient/DTACompanion
```

Copies `dist/`, `catalog/`, launchers, and `package.json`. Players need Node.js to run `node dist/server.cjs`.

## Tech stack

- **Frontend:** React 19, Vite 6, Tailwind 4, Lucide icons
- **Backend:** Express, TypeScript via tsx (dev) / esbuild bundle (prod)
- **Coach:** `@cursor/sdk`, `@google/genai`, local OpenAI-compatible endpoint, rules fallback
- **Auth (optional):** Firebase client SDK
