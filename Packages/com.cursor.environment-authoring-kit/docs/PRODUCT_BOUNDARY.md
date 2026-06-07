# Product boundary — Environment Authoring Kit

**Public repo scope:** [PUBLIC_REPO_SCOPE.md](../../../../docs/PUBLIC_REPO_SCOPE.md)

## What this is

**Environment Authoring Kit** (`com.cursor.environment-authoring-kit`) authors **Florida karst surface + lava-tube cave worlds** for **Unity 6 + URP** projects that *may* target **XR** — not a general-purpose game engine or a shipped glasses product.

| In scope | Out of scope |
|----------|----------------|
| Radial surface terrain (panhandle LiDAR / hillshade refs) | Generic open-world MMO tooling |
| Trail / NavMesh walk bands to cave mouth | Multiplayer netcode |
| Procedural lava-tube cave (layout, shell, route floor) | Custom renderers or physics engines |
| Editor-queue builds, graders, route bots | Replacing Unity Terrain / NavMesh |
| Cursor agent workflows + ResearchCache | Runtime game logic (lives in Hub game code) |

## Ownership

| Path | Owner |
|------|--------|
| `Packages/com.cursor.environment-authoring-kit/` | Package — tools, editors, cave-grader CLI |
| `Assets/EnvironmentKit/Generated/` | Build artifacts (JSON, prompts, logs) — safe to regenerate |
| `Assets/EnvironmentKit/ResearchCache/` | Curated R&D URLs + summaries — **local** (gitignored on public Hub) |
| `Assets/EnvironmentKit/Recipes/` | Versioned world recipes (JSON) — **committed** on public Hub |
| `Assets/EnvironmentKit/Presets/` | Unity ScriptableObject presets — **committed** |
| `.unity` scenes, store art, gameplay code | **Your** project — not in public GitHub kit repo |

## Florida data policy

Aquifer, LiDAR, and karst references are **cave-structure only** (voids, mouths, gradients). Do not drive water simulation, spring discharge, or bathymetry from those sources. See `docs/RESEARCH_DATA_ATTRIBUTION.md`.

## Showcase vertical (demo reel)

**One county · one seed · one path** — requires **your** scene, art, and optional XR rig locally.

- Production recipe (default **Build Complete Cave**): `Assets/EnvironmentKit/Recipes/aaa-full-cave-production.json`
- Menu: **Window → Environment Kit → Hub** → **Build Complete Cave Level (Active Scene)** (or showcase menu when present in your branch)
- Headless (when batch API exists in your tree): `Unity -batchmode -projectPath <project> -executeMethod … -quit`
- **Not implied:** VITURE hardware validated or committed on GitHub

## Success metrics

1. **Iteration** — change one mask / trail → downstream rebake &lt; 60s in editor (incremental ladder on).
2. **Determinism** — pinned seed reproduces same artifacts while debugging.
3. **Cheap validation** — route probes read NavMesh; no nested 60-phase polish inside validation.
4. **Contracts** — each ladder rung documents inputs, outputs, invalidation (`docs/PHASE_CONTRACTS.md`).
5. **FullWorld surface** — all **9 terrain tiles** show vegetation and walkable trails before cave geo; cave has blocks or full shell, not ramp-only partial.
