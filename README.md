# Environment Authoring Kit (Unity)

**Clone → open in Unity → Build Complete Cave.** First run sets up the project and runs the **full 120-step procedural pipeline with no API keys**.

AI grading (Cursor, Gemini, Ollama, etc.) is **optional** — turn on via Hub → **FullWorld + AI** when you have keys or local Ollama.

This repository is the **shareable Unity project + UPM package** (`com.cursor.environment-authoring-kit`). It does **not** include third-party Asset Store props, store cave meshes, sample scenes, or the VITURE native SDK — those stay on your machine under separate licenses.

**Accuracy contract:** [docs/PUBLIC_REPO_SCOPE.md](docs/PUBLIC_REPO_SCOPE.md) — what is on GitHub vs local-only (read before trusting other docs).

**Package version:** **0.3.1** · queued cave build: **120/120** steps.

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
2. **Window → Environment Kit → Hub** → **Build Complete Cave (120)** (or menu **Build Complete Cave Level**).

**First click runs clone setup automatically:** starter scene (`Assets/EnvironmentKit/Scenes/StarterWorld.unity`), placeholder floor/wall/ceiling prefabs if none exist, `npm install` in `Tools/cave-grader`, and project paths pointed at **this** clone.

3. You need **Node 18+** installed on the machine (setup runs `npm install` for you).

**Better art (optional):** import licensed 3D cave/dungeon modules into `Assets/` — the kit scans and prefers them over starter cubes. Texture-only or 2D sprite packs are not valid cave geometry.

If build still blocks, open `Assets/EnvironmentKit/Generated/CaveBuildPreflightReport.md` and fix any **BLOCK** row (usually missing Node).

**Not in git (optional, speeds up research):** `Assets/EnvironmentKit/ResearchCache/` — run `npm run sync-research-pull` in `Tools/cave-grader` when you want Florida research data locally.

---

## XR / glasses — what is real vs what is not

### What the kit actually does for XR

- Applies an **`XROptimizationProfile`** during full builds (default preset: `Assets/EnvironmentKit/Presets/VitureXRPro.asset`) — LOD, colliders, URP hints tuned for **mobile / stereoscopic** budgets.
- Project manifest includes **Unity XR Management**, **OpenXR**, **Android XR OpenXR**, and **XR Interaction Toolkit** — you can target head-mounted displays through Unity’s normal XR plug-in setup.
- Optional **`VitureIntegration`** — if a VITURE-named assembly is already in *your* project, the kit logs and skips hard failures; it does **not** ship the VITURE SDK.

### What this repo is **not**

- **Not “plug in glasses and play”** out of the box — no demo scene, no device QA, no VITURE neckband SDK in git.
- **Not a substitute** for VITURE/OpenXR project settings, Android build targets, or your hardware vendor documentation.
- **Not claiming** commercial ship quality on device until **you** grade, profile, and playtest on hardware.

**Short answer:** XR is **supported in the authoring pipeline and dependencies**, not **finished glasses-ready product** in this public repo.

---

## Quick start (after clone)

| Step | Action |
|------|--------|
| 1 | Unity Hub → **Unity 6000.4.6f1** (or 6000.x matching `ProjectSettings/ProjectVersion.txt`) → open this repo root |
| 2 | Let Package Manager resolve URP/XR packages (first open may take a few minutes) |
| 3 | Complete the **[Required before your first build](#required-before-your-first-build-read-this-after-clone)** checklist (scene, Ground, PortalFive, prefabs, `npm install`) |
| 4 | **Window → Environment Kit → Hub** → confirm preflight passes (or read `CaveBuildPreflightReport.md`) |
| 5 | Run **Build Complete Cave Level (Active Scene)** |
| 6 | Watch **Cave Build → Diagnostics → Pipeline Console** through **120/120** queued steps |
| 7 | Optional: **Cave Build Grader** + Cursor/LLM automation ([package docs](Packages/com.cursor.environment-authoring-kit/docs/CaveGradingAndCursor.md)) — needs `.env` API keys |

If a prior run left only a ramp/mouth with no tunnels: **Cave Build → Advanced → Build Complete Cave — Full AAA Rebuild (invalidate ladder)**.

**Secrets:** copy `Tools/cave-grader/.env.example` → `.env` locally; never commit API keys.

---

## Repository layout

```
environment-authoring-kit/
├── README.md                 ← this file
├── REQUIREMENTS.md           ← product & acceptance criteria
├── docs/                     ← repo-level changelog & license scope
├── Assets/
│   └── EnvironmentKit/       ← presets, recipes, documentation (committed)
│       ├── Presets/          ← incl. VitureXRPro XR optimization profile
│       ├── Recipes/
│       └── Generated/        ← local only (gitignored)
├── Packages/
│   ├── manifest.json
│   └── com.cursor.environment-authoring-kit/
│       ├── README.md         ← package API & menus
│       ├── Editor/           ← build pipeline, Hub, XR optimizer
│       ├── Runtime/
│       └── Tools/cave-grader/
└── ProjectSettings/
```

---

## Main editor entry points

All under **Window → Environment Kit**:

| Menu | Purpose |
|------|---------|
| **Hub** | Central status, settings, build controls (recommended) |
| **Build Complete Cave Level** | FullWorld: surface (9-tile terrain + vegetation) then **120-step** cave pipeline |
| **Build Surface World Only** | Surface / terrain / props only |
| **Build Cave Only — Align to Surface** | Underground only |
| **Cave Build Grader** | Quality report, prompts, optional agent fixes |

Full menu table: [Package README](Packages/com.cursor.environment-authoring-kit/README.md).

---

## Requirements

| | |
|--|--|
| **Unity** | 6000.0+ (see `ProjectSettings/ProjectVersion.txt`) |
| **Rendering** | Universal RP 17+ |
| **XR (optional)** | Configure XR Plug-in Management + OpenXR for your device; VITURE SDK separate |
| **Node** | 18+ + `npm install` in `Tools/cave-grader` — **required** for automated FullWorld preflight (optional only if you skip grader integration) |
| **Disk** | Your own art packs and scenes — not in this repository |

---

## Code scanning (CodeQL) — optional CI

Security static analysis on GitHub (**Security → Code scanning**). Separate from cave-grader quality checks.

| Piece | Requirement |
|-------|----------------|
| **Workflow** | [`.github/workflows/codeql.yml`](.github/workflows/codeql.yml) on `main` |
| **C# (Unity)** | Self-hosted Mac runner + repo variable **`UNITY_PATH`** |
| **TypeScript / Actions** | GitHub-hosted (no Unity) |

**Setup and daily use:** [docs/CODEQL_SETUP_AND_USE.md](docs/CODEQL_SETUP_AND_USE.md)  
**Do not** enable GitHub’s “Code scanning default setup” alongside this workflow (SARIF conflict).

Verified **2026-05-29** on self-hosted Mac (~16 min first full C# job including CodeQL bundle download).

---

## AI-assisted workflow (optional)

- In-editor grading ladder and **Environment Kit Hub**
- External **cave-grader** (`grade-and-fix.ts`) with Cursor SDK and/or other LLM providers
- External file edits are **opt-in** (`allowExternalProviderEdits` in Hub settings; dry-run default)

Details: [CaveGradingAndCursor.md](Packages/com.cursor.environment-authoring-kit/docs/CaveGradingAndCursor.md).

---

## Documentation

| Doc | Content |
|-----|---------|
| [Package README](Packages/com.cursor.environment-authoring-kit/README.md) | Install, pipeline, grader |
| [REQUIREMENTS.md](REQUIREMENTS.md) | Hub project requirements |
| [docs/PUBLIC_REPO_SCOPE.md](docs/PUBLIC_REPO_SCOPE.md) | What GitHub contains vs what you add locally |
| [docs/THIRD_PARTY_AND_LICENSE_SCOPE.md](docs/THIRD_PARTY_AND_LICENSE_SCOPE.md) | Kit license vs Unity, store assets, APIs |
| [Package docs index](Packages/com.cursor.environment-authoring-kit/docs/README.md) | Pipeline, grading, attribution |
| [docs/CODEQL_SETUP_AND_USE.md](docs/CODEQL_SETUP_AND_USE.md) | CodeQL self-hosted Mac + GitHub Actions |

---

## License

**Educational and personal non-commercial use — free.**  
**Commercial use** (selling access, paid products/services built on the kit, paid training, etc.) **requires a separate license or purchase from the copyright holder.**

Full text: [Packages/com.cursor.environment-authoring-kit/LICENSE.md](Packages/com.cursor.environment-authoring-kit/LICENSE.md) · [THIRD_PARTY_AND_LICENSE_SCOPE.md](docs/THIRD_PARTY_AND_LICENSE_SCOPE.md)

**Not CC0 / not public domain.** Unity, UPM packages, your `Assets/` art, LLM APIs, and geodata remain under their own terms.

---

## Tagline (for GitHub About)

*AI-assisted Unity framework for procedural terrain, caves, and world scaffolding — a strong base you refine, not a one-click finished world.*
