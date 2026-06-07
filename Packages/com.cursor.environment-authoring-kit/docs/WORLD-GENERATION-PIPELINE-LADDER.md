# World generation pipeline ladder

**Product scope:** [PRODUCT_BOUNDARY.md](PRODUCT_BOUNDARY.md) · **Contracts:** [PHASE_CONTRACTS.md](PHASE_CONTRACTS.md) · **Recipes:** `Assets/EnvironmentKit/Recipes/`

Curated from AAA production talks (Ubisoft Far Cry 5, Guerrilla Horizon), engine R&D (UE5 PCG, SideFX Houdini), indie scale (Hello Games No Man's Sky), and ML repair loops (NVIDIA Fly, Fail, Fix). Serialized for the Cursor bot under `Assets/EnvironmentKit/ResearchCache/entries/world-gen-pipeline-ladder-best-practices/`.

## Core rule

**One ordered queue. Each phase reads upstream artifacts and writes downstream artifacts. A phase never redoes completed upstream work unless upstream inputs change.**

| Principle | Source |
|-----------|--------|
| Tools pass **masks and heightfields** to the next tool; order is fixed | Far Cry 5 (GDC 2018) |
| **Topological execution** — dependents run after producers | UE5 PCG compiler |
| **Separate streams** for terrain vs scatter points | SideFX HeightField Scatter |
| **Deterministic seeds** per cell/coordinate; store rules not worlds | No Man's Sky |
| **Placement after terrain rules** | Horizon GPU procedural placement |
| **Repair only failed subsystems** after validation | NVIDIA Fly, Fail, Fix |

## Recommended global order

| Rung | Phase | Outputs (artifacts) | Must not rerun when |
|------|--------|---------------------|---------------------|
| 0 | Research + seed lock | `ResearchCache` pull, compile gate, seed in `CaveLayoutRoll` | Same seed + same policy version |
| 1 | Macro terrain | Heightmap, county hillshade refs, terrain grade report | Terrain hash unchanged |
| 2 | Hydrology / karst masks | Structure masks (cave only — no water sim) | Masks unchanged |
| 3 | Trails + walk band | Splines, NavMesh surface band, trail repair report | Trail graph unchanged |
| 4 | Surface props | `GeneratedSurfaceWorld/Vegetation` — per-tile density contract (9-tile FullWorld: e.g. 150 grass/tile target) | Prop seed + masks unchanged; scene instances must exist |
| 5 | Pre-build Cursor gate | Readiness report, pre-placement gate | Gate passed for seed |
| 6 | Cave layout + spline | `CaveMazeLayout`, route spline | Layout seed unchanged |
| 7 | Route floor / ceiling + NavMesh | Walk mesh, cave NavMesh | Route geometry unchanged |
| 8 | Shell rings + materials | Block rings, PBR materials | Shell pass complete |
| 9 | Gameplay props + mobs | Spawners, portals, interactables | Gameplay layer unchanged |
| 10 | Validation bots | Route probe, surface probe (read-only) | Inputs unchanged — **no nested full playtest ladder** |
| 11 | Polish / post | Lighting, fog, XR profile tweaks | Only if grade &lt; ship target |

**Continue after pre-build** (`LavaTubeCaveBuilder.ContinueCaveGeometryAfterPreBuild`) must start at **rung 6**, not 0.

**Build Complete Cave** applies `aaa-full-cave-production` via `CaveBuildAaaProductionBootstrap` (default editor path — not a separate toy pipeline).

## Environment Kit mapping

| Kit system | Ladder rungs |
|------------|----------------|
| `CaveBuildSurfacePipeline` / `SurfaceWorldGenerator` | 1–4 |
| `CaveBuildPreBuildLadder` / Cursor bridge | 5 |
| `LavaTubeCaveBuildPipeline.QueueRun` (120 steps) | 6–9 |
| `CaveBuildAutomatedValidation` | 10 |
| `CavePlaytestPreBuildPipeline` | 11 only (not inside route probe) |
| `CaveBuildActionPacing` | One queue action per editor frame; no `Thread.Sleep` |

## Invalidate downstream only

When an upstream artifact changes, mark **downstream rungs dirty** and queue only those rungs — never replay the full build from rung 0 unless the user changes seed or scope.

Examples:

- Trail spline edited → rerun 3–4, 6–10 (skip 1–2 if terrain untouched).
- Cave layout seed changed → rerun 6–10 (skip surface if `SurfaceBuildScope.CaveOnly` and surface artifacts valid).
- Research policy bump → rerun 0 only; do not rebake terrain if masks unchanged.

## Source URLs (also in ResearchCache)

### Ubisoft — Far Cry 5

- https://tools.engineer/gdc2018-procedural-world-generation-of-far-cry-5
- https://christianjmills.com/posts/procedural-tools-far-cry-5-notes/
- https://blog.playstation.com/2018/03/22/the-procedural-world-generation-of-far-cry-5/
- https://www.gamedeveloper.com/design/video-the-world-generation-tech-behind-i-far-cry-5-i-

### Guerrilla — Horizon Zero Dawn

- https://www.guerrilla-games.com/read/gpu-based-procedural-placement-in-horizon-zero-dawn
- https://www.gdcvault.com/play/1024120/GPU-Based-Run-Time-Procedural
- https://www.gdcvault.com/play/1024124/Creating-a-Tools-Pipeline-for
- https://www.gdcvault.com/play/1025066/Between-Tech-and-Art-The

### Epic — UE5 PCG

- https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/PCG/UPCGGraph
- https://www.strayspark.studio/blog/procedural-content-generation-pcg-framework-production-ue5-7
- https://dev.epicgames.com/documentation/en-us/unreal-engine/procedural-content-generation-overview

### SideFX — Houdini heightfields

- https://www.sidefx.com/docs/houdini/heightfields/creation.html
- https://www.sidefx.com/docs/houdini/heightfields/scattersop.html
- https://www.sidefx.com/docs/houdini/heightfields/scatterattribs.html
- https://www.sidefx.com/community-main-menu/complete-a-z-terrain-handbook/

### Hello Games — No Man's Sky

- https://www.polygon.com/2017/3/2/14790028/no-mans-sky-was-flat-procedural-world-generation-maths/
- https://en.wikipedia.org/wiki/No_Man%27s_Sky

### NVIDIA — iterative repair

- https://arxiv.org/abs/2507.12666

### Bot instructions

1. Read `ResearchCache/index.json` and entry `world-gen-pipeline-ladder-best-practices` before changing queue order.
2. Prefer extending **phase contracts** (artifact paths + dirty flags) over adding synchronous work inside validation.
3. Never run a full menu build from `CaveBuildPendingGeometryBuild` — use `ContinueCaveGeometryAfterPreBuild` only (cave-only continuation after pre-build).

Refresh cache: `cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader && HUB_ROOT=/path/to/Hub npm run sync-research-cache`
