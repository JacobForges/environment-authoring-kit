# Planner session — layout-first builds (AI optional)

The **planner** turns a **layout brief** into terrain, trails, props, and optional caves. Unity reads JSON on disk at build time — **no LLM required** once finalized.

**Author:** [JacobForges](https://github.com/JacobForges)

---

## Two ways to plan

| Path | When to use |
|------|-------------|
| **AI Build Wizard** (optional) | Interactive Q&A, research, concept PNG — needs Node + optional `CURSOR_API_KEY` |
| **Non-AI / manual** | Hand-edit or template-copy `CaveBuildPlannerBrief.json` + session config; fastest for bots and repeat demos |

Both paths produce the same Unity contract: **`CaveBuildActiveSessionConfig.json`** + **`CaveBuildPlannerBrief.json`**.

**Content layout (NPCs / enemies / props):** separate tab in the same wizard — see [CONTENT_LAYOUT_WIZARD.md](CONTENT_LAYOUT_WIZARD.md). Writes **`CaveBuildContentLayoutBrief.json`** for MainScene.

---

## Start the wizard (optional AI)

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
npm install
npm run build-wizard
```

Open the local URL, complete the **15-item checklist** (or say **move on** when ready), approve concept/plan, then **Finalize** so Unity JSON is written under `Assets/EnvironmentKit/Generated/`.

---

## Non-AI planner (no keys, no chat at build time)

1. Copy or edit:
   - `Assets/EnvironmentKit/Generated/CaveBuildPlannerBrief.json` — `layoutPlan.markers`, `technicalSpecs`, `trails`, `islands`
   - `Assets/EnvironmentKit/Generated/CaveBuildActiveSessionConfig.json` — scope flags (see below)
2. Set `finalizedUtc` on the session config (wizard does this on approve).
3. **Hub → Build** (or planner-scoped build) — C# authors read the brief directly.

**Concept PNG is optional.** If `concept.png` is missing, terrain uses **marker plan only** (logs: `[PlannerConcept] No concept PNG — marker plan only`). With concept: **75% concept / 25% marker** weight (`CaveBuildPlannerConceptGuide`).

**Fidelity gate** (optional): compares live scene vs concept + markers when concept exists; retries sculpt if similarity &lt; 89%.

---

## Session config flags (scope truth)

Written to `CaveBuildActiveSessionConfig.json`. Bots read this via `CaveBuildActiveSessionConfig.json` / `mission-phase.ts`.

| Flag | Fast walkable demo | Full production |
|------|-------------------|-----------------|
| `tileCount` | `81` (or `13` with `floatingTiles`) | `289` |
| `agentInvokes` | **`false`** — no blocking tsx/agent during build | `true` |
| `use3DCaveSystem` | `true` or **`false`** for surface-only | `true` |
| `sequentialTerrain` | `false` for speed | `true` (default FullWorld) |
| `enhancementPhases` | `false` | `true` |
| `prePlacementResearch` | `false` | `true` |
| `floatingTiles` | `true` for cardinal island demo (13 tiles) | `false` |
| `surfaceTrails` | `true` | `true` |
| `mountainLabyrinth` | per brief `playDisk.labyrinth` | per preset |

Default **non-AI** preset in code: `CaveBuildSessionConfig.CreateDefaults()` — label `"Speed walkable demo"`, `agentInvokes: false`, 81 tiles.

---

## What Unity applies when planner is finalized

| Author | Role |
|--------|------|
| `CaveBuildPlannerTerrainGuide` | Brief-driven heightmaps, LiDAR sculpt, paced queue |
| `CaveBuildPlannerLayoutAuthor` | Platforms, maze topology, layout root |
| `CaveBuildPlannerMeshLandscapeAuthor` | Walk meshes, kit prefabs, bridge ramps |
| `CaveBuildPlannerTrailAuthor` | Trail sculpt + NavMesh |
| `CaveBuildPlannerMarkerPropScatter` | Props from markers |
| `CaveBuildPlannerContentAuthor` | NPCs, enemies, hub dressing |
| `CaveBuildPlannerFidelityGate` | Concept similarity retries (when concept bound) |

Obsolete paths are skipped when `CaveBuildSessionConfig.HasFinalizedActive` (e.g. legacy tsx pre-placement).

---

## Key generated files

| File | Role |
|------|------|
| `CaveBuildPlannerSession.json` | Wizard phase, checklist, chat state |
| `CaveBuildPlannerBrief.json` | **Layout truth** — markers, specs, trails |
| `CaveBuildActiveSessionConfig.json` | **Scope truth** — tiles, caves, agent flags |
| `CaveBuildPlannerFidelityReport.json` | Concept gate score (optional) |
| `ResearchCache/.../concept.png` | Optional concept image |

All under `Generated/` (gitignored; may symlink to external drive).

---

## Cave queue step count

Planner fast demos still use the same queued cave schedule when `use3DCaveSystem: true`: **122** steps (`CaveBuildQueuedPipelineSchedule.Total`). Surface-only (`use3DCaveSystem: false`) skips the cave queue.

---

## Bot playbooks

- [PLANNER_FAST_DEMO.md](../Tools/cursor-bot/playbooks/PLANNER_FAST_DEMO.md)
- [STEP_COUNTER_ETC.md](../Tools/cursor-bot/playbooks/STEP_COUNTER_ETC.md)
- [TERRAIN_SEAM_NINETILE.md](../Tools/cursor-bot/playbooks/TERRAIN_SEAM_NINETILE.md)

---

## Naming reminder

| Name | Meaning |
|------|---------|
| **environment-authoring-kit** | GitHub repo (keep this name) |
| **Environment Authoring Kit** | Product / UPM package |
| **Hub** | This Unity reference project folder (`~/Hub`) |
| **Build Wizard** | Optional planner UI (`npm run build-wizard`) — not the same as the Hub window |
