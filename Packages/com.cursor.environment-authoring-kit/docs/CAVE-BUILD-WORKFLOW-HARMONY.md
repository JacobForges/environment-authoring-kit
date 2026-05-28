# Cave build workflow harmony

One build session is coordinated by `CaveBuildWorkflowCoordinator` so scripts do not undo each other.

## Pipeline order

**FullWorld (Build Complete Cave):**

0. **Surface** — `CaveBuildStartupCoordinator`: world generator → terrain AI phases → terrain ladder (incl. `prop_*` on all tiles)  
1. **Pre-build** (`CaveBuildUnifiedFlow`, readiness ladder, optional Cursor pre-build)  
2. **Queued cave (120 steps)** — validate → **geo 1–13** → playability → validation → ground polish → world → meat → post-meat → research → finalize polish → manifest  

**CaveOnly / prototype:** skip surface when scope or layout prototype requires it.

Coordinator still applies:

3. **Playability** (18 steps — walk floors, colliders, visual pass, nav)  
4. **World** (scatter, materials, lighting, FX, nav, spawns)  
5. **Meat loop** (shell purge → grade batches → enrichment + targeted fix → repeat)  
6. **Post-meat** (light shell tidy, visual, spawns, final grade, optional Cursor post-build)

**Do not** treat terrain-ladder mouth fixes as a substitute for geo 1–13 — partial floor + ramp is not a completed `cave_layout` rung.

## Coordinator rules

| Resource | Policy |
|----------|--------|
| **Walkways** | After playability step 2, purges use `PurgeShellLayersOnly` (no walkway delete) |
| **NavMesh** | At most one automatic bake per phase; step 16 / visual rebuild use `force: true` |
| **World props** | `ScatterExtraProps` once per build |
| **Meat props** | Enrichment scatter capped at 2 passes |
| **Ground XZ** | Locked after mouth grounded; only `TrySnapMouthToSurfaceDepthOnly` in meat/post-meat |
| **Post-grade purge** | Disabled during queue/meat/post-meat |
| **Auto-rebuild** | Waits until pipeline + agent + compile idle |

## Scripts that complement (not fight)

- **Build**: `LavaTubeCaveBuilder` → `LavaTubeCaveBuildPipeline` (sync or queued)
- **Playability**: `CaveAdventurePlayabilityPipeline` — commits walk floors; visual pass strips shells only
- **Ground**: `CaveGroundPlacementUtility` depth snap vs full align (first placement only)
- **Quality**: `CaveBuildQualityMeatLoop` + `CaveBuildMeatLoopEnrichment` + `CaveBuildQualityStageFixer`
- **Cursor**: `CaveBuildCursorAgentBridge` — does not auto-rebuild over an active phased build

## Manual re-grade

`CaveBuildGraderWindow` / `CaveBuildQualityMenu` use `GradingOnly` phase — no post-grade purge, no pipeline mutations.
