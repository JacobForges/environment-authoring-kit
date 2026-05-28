# Environment Authoring Kit (Unity)

**Not a one-click finished world — a strong procedural base framework you can build on.**

Environment Authoring Kit gives you a fast, **AI-assisted foundation** for Unity: procedural land layout, cave structure, and core world scaffolding. You still shape, refine, and detail the final world.

This repository is the **shareable Unity project + UPM package** (`com.cursor.environment-authoring-kit`). It does **not** include third-party Asset Store props, store cave meshes, sample scenes, or the VITURE native SDK — those stay on your machine under separate licenses.

**Accuracy contract:** [docs/PUBLIC_REPO_SCOPE.md](docs/PUBLIC_REPO_SCOPE.md) — what is on GitHub vs local-only (read before trusting other docs).

**Package version:** **0.3.0** · queued cave build: **120/120** steps.

---

## What is on GitHub (honest scope)

| Included | Not included (bring your own) |
|----------|-------------------------------|
| `Packages/com.cursor.environment-authoring-kit/` — editor pipeline, grading, Hub, optional Cursor/LLM grader | Asset Store / marketplace folders under `Assets/` (city packs, lava-tube meshes, characters, etc.) |
| `Assets/EnvironmentKit/` — presets, recipes, kit docs | Unity scenes (`.unity`) — create or copy locally |
| `ProjectSettings/`, `Packages/manifest.json` — Unity 6 + URP + **OpenXR / XR Interaction Toolkit** deps | `Assets/EnvironmentKit/Generated/` — build output (gitignored) |
| Root docs: `REQUIREMENTS.md`, `docs/`, `.github/workflows/` | `.env` API keys, `node_modules/`, VITURE/GlassesGateway native binaries |

After clone, open the folder in **Unity Hub** (see [Requirements](#requirements)). Assign your own ground mesh, portal anchor, and art prefabs, then run the kit from the editor.

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
| 1 | Unity Hub → **Unity 6000.x** → open this repo root |
| 2 | Let Package Manager resolve URP/XR packages (first open may take a few minutes) |
| 3 | Create or open a scene with a **Ground** walkable surface and **`PortalFive`** (cave entrance anchor) |
| 4 | Add your licensed prefabs under `Assets/` (or use kit blockout / primitives) |
| 5 | **Window → Environment Kit → Hub** → run **Build Complete Cave Level (Active Scene)** |
| 6 | Watch **Cave Build → Diagnostics → Pipeline Console** through **120/120** queued steps |
| 7 | Optional: **Cave Build Grader** + Node grader ([package docs](Packages/com.cursor.environment-authoring-kit/docs/CaveGradingAndCursor.md)) |

If a prior run left only a ramp/mouth with no tunnels: **Cave Build → Advanced → Build Complete Cave — Full AAA Rebuild (invalidate ladder)**.

**Secrets:** copy `Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/.env.example` → `.env` locally; never commit API keys.

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
| **Node (optional)** | 18+ for `Tools/cave-grader` |
| **Disk** | Your own art packs and scenes — not in this repository |

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
| [docs/THIRD_PARTY_AND_LICENSE_SCOPE.md](docs/THIRD_PARTY_AND_LICENSE_SCOPE.md) | What CC0 covers vs store assets |
| [Package docs index](Packages/com.cursor.environment-authoring-kit/docs/README.md) | Pipeline, grading, attribution |

---

## License

**Kit code and repo docs:** [CC0 1.0](LICENSE) (see [third-party scope](docs/THIRD_PARTY_AND_LICENSE_SCOPE.md)).

**Unity, UPM packages, your `Assets/` art, government geodata, and Cursor/npm deps** — each under its own terms.

Package also ships [LICENSE.md](Packages/com.cursor.environment-authoring-kit/LICENSE.md) (educational free / commercial license) for the UPM package when published separately.

---

## Tagline (for GitHub About)

*AI-assisted Unity framework for procedural terrain, caves, and world scaffolding — a strong base you refine, not a one-click finished world.*
