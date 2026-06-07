# Environment Authoring Kit (Unity)

**Clone → open in Unity → Build Complete Cave from the Hub.** First run sets up the project and runs the **full procedural pipeline with no API keys**.

AI grading (Cursor, Gemini, Ollama, etc.) is **optional** — configure providers in **Hub → Settings** when you have keys or local Ollama.

This repository is the **shareable Unity project + UPM package** (`com.cursor.environment-authoring-kit`). It does **not** include third-party Asset Store props, store cave meshes, sample scenes, or the VITURE native SDK — those stay on your machine under separate licenses.

**Accuracy contract:** [docs/PUBLIC_REPO_SCOPE.md](docs/PUBLIC_REPO_SCOPE.md) — what is on GitHub vs local-only (read before trusting other docs).

**Package version:** **0.3.4** · queued cave build: **122** steps (some UI labels still say 120).

**Blank-scene test project:** [THE_NEST_PHASE.md](Packages/com.cursor.environment-authoring-kit/docs/THE_NEST_PHASE.md) — local clone at `~/Projects/the-nest-phase`.

**FullWorld concepts:** Hub dropdown **0–9** (+ random on build) — [FULLWORLD_GENERATION_PRESETS.md](Packages/com.cursor.environment-authoring-kit/docs/FULLWORLD_GENERATION_PRESETS.md). Unity assets under `Assets/EnvironmentKit/Presets/` are atmosphere/terrain profiles — [Presets README](Assets/EnvironmentKit/Presets/README.md).

---

## What is on GitHub (honest scope)

| Included | Not included (bring your own) |
|----------|-------------------------------|
| `Packages/com.cursor.environment-authoring-kit/` — editor pipeline, grading, Hub, optional Cursor/LLM grader | Asset Store / marketplace folders under `Assets/` (city packs, lava-tube meshes, characters, etc.) |
| `Assets/EnvironmentKit/` — presets, recipes, kit docs | Unity scenes (`.unity`) — create or copy locally |
| `ProjectSettings/`, `Packages/manifest.json` — Unity 6 + URP + **OpenXR / XR Interaction Toolkit** deps | `Assets/EnvironmentKit/Generated/` — build output (gitignored) |
| Root docs: `REQUIREMENTS.md`, `docs/`, `.github/workflows/` | `.env` API keys, `node_modules/`, VITURE/GlassesGateway native binaries |

After clone, open the folder in **Unity Hub** (see [Requirements](#requirements)).

---

## First build (after clone)

1. **Unity Hub** → open this repo folder (Unity **6** + URP).
2. **Window → Environment Kit → Hub** → leave **Sequential FullWorld terrain** checked (default); pick a **FullWorld preset** (default **02 — Ideal layout**) — [preset guide](Packages/com.cursor.environment-authoring-kit/docs/FULLWORLD_GENERATION_PRESETS.md).
3. **Build Complete Cave (122)** (same as menu **Build Complete Cave Level**).

**First click runs clone setup automatically:** tagged **Ground** plane, **PortalFive**, grid anchors (`SurfaceTerrainFlatHost`, `SurfaceFullWorldGridAnchor`), placeholder prefabs if needed, `npm install` in `Tools/cave-grader`, and paths pointed at **this** clone. **Empty scene is OK** — no manual setup.

**Isolated test clone:** [THE_NEST_PHASE.md](docs/THE_NEST_PHASE.md) + `~/Projects/the-nest-phase`.

4. You need **Node 18+** on the machine (setup runs `npm install` for you).

**Watch the build in the Hub** — activity feed, sub-action, and live status. The floating **Environment Kit** progress bar stays hidden while Hub is open. You do **not** need **Diagnostics → Pipeline Console** unless you want a second log window.

**Better art (optional):** import licensed 3D cave/dungeon modules into `Assets/` — the kit scans and prefers them over starter cubes.

If build blocks, open `Assets/EnvironmentKit/Generated/CaveBuildPreflightReport.md` and fix any **BLOCK** row (usually missing Node).

**Layout / placement:** [PROJECT_LAYOUT_PLAN_20_STEPS.md](Packages/com.cursor.environment-authoring-kit/docs/PROJECT_LAYOUT_PLAN_20_STEPS.md) — **Build Surface World Only**, then **Build Complete Cave**; read `WorldLayoutAudit.json` between steps.

**Research cache (optional, speeds gates):** `Assets/EnvironmentKit/ResearchCache/` — `HUB_ROOT=/path/to/Hub npm run sync-research-cache` in `Tools/cave-grader`. Hub **Enrich** / **Grade research** buttons are maintenance only, not required every build.

---

## XR / glasses — what is real vs what is not

### What the kit actually does for XR

- Applies an **`XROptimizationProfile`** during full builds (default: `Assets/EnvironmentKit/Presets/VitureXRPro.asset`).
- Manifest includes **Unity XR Management**, **OpenXR**, **Android XR OpenXR**, and **XR Interaction Toolkit**.
- Optional **`VitureIntegration`** — logs if a VITURE assembly exists in *your* project; does **not** ship the VITURE SDK.

### What this repo is **not**

- **Not “plug in glasses and play”** out of the box — no demo scene, no device QA, no VITURE SDK in git.
- **Not claiming** commercial ship quality on device until **you** grade and playtest on hardware.

---

## Quick start (after clone)

| Step | Action |
|------|--------|
| 1 | Unity Hub → **Unity 6000.4.6f1** (or 6000.x per `ProjectSettings/ProjectVersion.txt`) → open repo root |
| 2 | Let Package Manager resolve URP/XR packages |
| 3 | Complete scene setup: **Ground**, **`PortalFive`**, licensed prefabs, `npm install` in `Tools/cave-grader` |
| 4 | **Window → Environment Kit → Hub** — preflight / `CaveBuildPreflightReport.md` |
| 5 | **Build Complete Cave (122)** — watch Hub through **122/122** queued steps |
| 6 | Optional: **Cave Build Grader** + `.env` keys ([CaveGradingAndCursor.md](Packages/com.cursor.environment-authoring-kit/docs/CaveGradingAndCursor.md)) |

Recovery: **Hub → Stuck or half-built? → Full AAA Rebuild** or **Cave Build → Advanced → Full AAA Rebuild**.

**Secrets:** `Tools/cave-grader/.env.example` → `.env` locally; never commit keys.

---

## Repository layout

```
environment-authoring-kit/
├── README.md
├── REQUIREMENTS.md
├── docs/
├── Assets/EnvironmentKit/       ← presets, recipes (committed)
│   └── Generated/               ← local only (gitignored)
├── Packages/com.cursor.environment-authoring-kit/
│   ├── README.md
│   ├── Editor/
│   └── Tools/cave-grader/
└── ProjectSettings/
```

---

## Main editor entry points

All under **Window → Environment Kit**:

| Menu | Purpose |
|------|---------|
| **Hub** | **Recommended** — builds, settings, live monitor, 20 FullWorld styles |
| **Build Complete Cave Level** | Same FullWorld build as Hub (opens Hub) |
| **Build Surface World Only** | Surface terrain grid (81 tiles) + mountains / props |
| **Cave Build → Advanced → Snap Surface Terrain Grid** | Recovery — snap all slots to grid anchor |
| **Cave Build → Advanced → Expand Open World — Add Next Terrain Ring** | Incremental open world (+1 ring) |
| **Build Cave Only — Align to Surface** | Underground only |
| **Cave Build Grader** | Quality report, optional agent fixes |
| **Diagnostics → Pipeline Console** | Optional pop-out log (not auto-opened during Hub builds) |

Full menu table: [Package README](Packages/com.cursor.environment-authoring-kit/README.md).

---

## Requirements

| | |
|--|--|
| **Unity** | 6000.0+ |
| **Rendering** | Universal RP 17+ |
| **XR (optional)** | OpenXR + your device SDK |
| **Node** | 18+ + `npm install` in `Tools/cave-grader` |
| **Disk** | Your own art packs and scenes |

---

## Code scanning (CodeQL) — optional CI

[docs/CODEQL_SETUP_AND_USE.md](docs/CODEQL_SETUP_AND_USE.md) — self-hosted Mac + `UNITY_PATH` for C#.

---

## AI-assisted workflow (optional)

- **Environment Kit Hub** + in-editor grading ladder
- **cave-grader** (`grade-and-fix.ts`) with Cursor SDK and/or other LLM providers
- External file edits **opt-in** (Hub → Allow external provider edits)

Details: [CaveGradingAndCursor.md](Packages/com.cursor.environment-authoring-kit/docs/CaveGradingAndCursor.md).

---

## Documentation

| Doc | Content |
|-----|---------|
| [Package README](Packages/com.cursor.environment-authoring-kit/README.md) | Install, pipeline, menus |
| [SURFACE_TERRAIN_GRID_AND_OPEN_WORLD.md](Packages/com.cursor.environment-authoring-kit/docs/SURFACE_TERRAIN_GRID_AND_OPEN_WORLD.md) | **Grid snap, fast seams, open-world expansion** |
| [FULLWORLD_TERRAIN_AND_HUB.md](Packages/com.cursor.environment-authoring-kit/docs/FULLWORLD_TERRAIN_AND_HUB.md) | **Hub monitor, sequential build, LiDAR, presets** |
| [REQUIREMENTS.md](REQUIREMENTS.md) | Product requirements |
| [docs/PUBLIC_REPO_SCOPE.md](docs/PUBLIC_REPO_SCOPE.md) | GitHub vs local |
| [Package docs index](Packages/com.cursor.environment-authoring-kit/docs/README.md) | All package docs |
| [docs/CHANGELOG.md](docs/CHANGELOG.md) | Hub changelog |

---

## License

**Educational and personal non-commercial use — free.**  
**Commercial use** requires a separate license from the copyright holder.

[LICENSE.md](Packages/com.cursor.environment-authoring-kit/LICENSE.md) · [THIRD_PARTY_AND_LICENSE_SCOPE.md](docs/THIRD_PARTY_AND_LICENSE_SCOPE.md)

---

## Tagline (for GitHub About)

*AI-assisted Unity framework for procedural terrain, caves, and world scaffolding — a strong base you refine, not a one-click finished world.*
