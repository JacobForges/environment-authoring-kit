# Changelog

All notable changes to the Hub cave authoring pipeline and documentation are recorded here.

Format: **date** — short title — details.

---

## 2026-05-27

### Environment Authoring Kit v0.2.0 — FullWorld pipeline + 9-tile surface contract

Package release prep: documentation rewrite, `package.json` **0.2.0**.

**Pipeline**

- FullWorld **terrain-first** — surface world, terrain AI phases, terrain ladder (including props), then queued cave **63 steps**.
- **Strict cave geometry** — incremental ladder cannot skip geo 1–13 when scene only has ramp / partial floor; `InvalidateCaveGeometryLadderRungs` on FullWorld without full cave.
- Terrain mouth fixes require **RouteTerrainFloor** from geo — no `BuildFloorOnly` shortcut that replaced full cave generation.
- Research gate does not block validation or geo steps on prompt-export waits.

**Surface vegetation (9 tiles)**

- Per-tile targets: trees 35, grass 150, bushes 95, ground cover 110 (× tile count).
- Grid fill uses **each tile’s footprint**, not center annulus only.
- Scene contract: ≥42 instances per tile, ≥55×N total under `Vegetation`; plan audit ≥88% of targets.

**Docs**

- Package: new [CHANGELOG.md](../Packages/com.cursor.environment-authoring-kit/CHANGELOG.md), [docs/REQUIREMENTS.md](../Packages/com.cursor.environment-authoring-kit/docs/REQUIREMENTS.md), [docs/README.md](../Packages/com.cursor.environment-authoring-kit/docs/README.md), rewritten [README.md](../Packages/com.cursor.environment-authoring-kit/README.md).
- Hub: this entry; [REQUIREMENTS.md](../REQUIREMENTS.md) surface + cave sections updated.

**Fixes**

- CS0104 `Object` ambiguity; `LogCaveWarning` parameter.

---

## 2026-05-21

### Florida research cache — integration, attribution, docs

- **Research utilization:** Agents read `floridaTerrain` from `CaveBuildResearchCache.json` / `CaveBuildResearch.json`; prompts inject local hillshade + aquifer entry paths on `ground_placement` and related rungs (`florida-research-paths.ts`, `research-cache-prompt.ts`).
- **Persistence:** Cache sync writes `floridaTerrain` into `index.json` and pointer JSON; Unity `CaveBuildResearchExporter` merges pointer into `CaveBuildResearch.json`.
- **Attribution:** [RESEARCH_DATA_ATTRIBUTION.md](../Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_DATA_ATTRIBUTION.md) — USGS, NOAA, FGS/FDEP, NWFWMD credits.
- **Requirements:** New §2.5 (R-01–R-06) in `REQUIREMENTS.md`; `THIRD_PARTY_AND_LICENSE_SCOPE.md` and root `LICENSE` clarify geospatial data is not CC0.
- **Commands:** `sync-florida-hillshades`, `sync-florida-terrain`; optional `CAVE_SYNC_FL_HILLSHADES=1` during build research phase.

### Spawn, grading weights, per-rung Cursor prompts

- **Teleport spawn:** `CaveSpawnTeleportAuthority` places `CaveEntrance_SpawnPoint` on maze route start (fixes surface-mouth / 25m fallback); portal relink; no longer resolves `CaveEntrance_Marker` as teleport target.
- **Grading:** `portal` and `spawn_reachability` weight 6→10; `player_floor` 14→16.
- **Cursor:** `export-rung-prompt.ts` + `CaveBuildRungPromptExporter` refresh all Generated JSON and write **per-rung** prompts (`CaveBuildActiveRungPrompt.md`) before each agent invoke.

### Cursor automation — stop when strict AAA+ (99+) is met

- `TryAutoInvokeAfterBuildComplete` — always uses `ShouldInvokeForReport`; **no post-build Cursor workflow** when `MeetsStrictTarget` (fixes infinite post-build / rebuild loop on high scores).
- `TryInvokeCursorDuringMeatLoop` — honors `suppressMeatLoopCursorInvokes` and skips when strict target already met; grade step checks target **before** any meat-pass Cursor invoke.

### Unity editor — system-wide queued build (08:57 crash)

**Symptom:** Crash during **cave quality ladder** at “Purge layered shells” — build had finished world stages 27–37.

**Fix:**

- **Meat loop** split into queued phases (`CaveBuildQualityMeatLoop.Queued.cs`): purge → grade → fix per pass, each on its own heavy editor tick.
- **World build** split from 5 chunks to **11** queue steps (one per stage 27–37).
- **Block tunnel** split: prepare → batched ring cells (2 per tick) → wall details (`CaveAdventureBlockBuilder.BuildRingCells`).
- **Post-meat** adventure cleanup split into **7** queue steps before finalize.
- `CaveBuildActionPacing.ScheduleBuildStep` — standard sleep-wrapped queue API for bridge, meat loop sync, and future tools; max queue depth **256**.

### Unity editor — fine-grained build queue (08:47 crash)

**Symptom:** Unity 6000.4.6f1 crash at 08:47 — `Thread stack size exceeded due to excessive recursion` during phased build step `cave 2/6 — geometry generate` (Editor.log ends at geometry stage 4/40).

**Cause:** Adventure geometry still ran as one synchronous `SplineLavaTubeCaveGenerator.Generate` / `CaveAdventureCaveGenerator.Generate` inside a single heavy queue tick (blocks + spawn alignment are the heaviest sections).

**Fix:**

- `CaveAdventureCaveGenerator.QueuedSteps.cs` — eight incremental geometry steps shared by sync `Generate` and the editor queue.
- `LavaTubeCaveBuildPipeline.Queued.cs` — **34 heavy queue steps**: validate → 9 geometry → 18 playability → 5 world chunks → quality (replaces 6 coarse phases).
- `CaveBuildActionPacing.cs` — max queue depth **64** for long builds.

**Research (approved sites in `CaveBuildResearch.json`):**

- [NVIDIA Fly, Fail, Fix](https://arxiv.org/abs/2507.12666) — iterative repair from play traces; supports splitting build into small steps with validation between them.
- [EA SEED AAA game testing](https://www.ea.com/seed/news/seed-ml-research-aaa-game-testing) — scale testing via chunked automated runs rather than one monolithic pass.
- Unity `InstantiateAsync` / per-frame integration budgets ([docs](https://docs.unity3d.com/ScriptReference/AsyncInstantiateOperation.SetIntegrationTimeMS.html)) — same principle: cap main-thread work per editor tick.

### Documentation

- Added project **README**, **REQUIREMENTS.md**, **docs/** index, and package **README**.
- Established policy: update requirements, changelog, and READMEs alongside code changes.

### License

- Added root **CC0 1.0** (`LICENSE`) — public domain; use original kit code and docs however you like.
- Added **docs/THIRD_PARTY_AND_LICENSE_SCOPE.md** (Unity, Assets, npm, Cursor SDK remain under their own terms).

### Unity editor — stack overflow crash fix

**Symptom:** Unity 6000.4.6f1 main-thread crash, ~32k levels of recursion during `EditorApplication.update` after pre-build Cursor workflow completed; logs showed both “continue geometry after pre-build” and “auto-rebuild after agent”.

**Cause:**

1. `FinishPreBuildReadinessLadderPhase` returned `false` after scheduling geometry, so `PollBackgroundAgent` still scheduled **auto-rebuild**.
2. `CaveBuildActionPacing` drained the **entire** action queue in one `update` tick, allowing nested updates during heavy `LavaTubeCaveBuilder` work.

**Fix:**

- `CaveBuildCursorAgentBridge.cs`: ladder finish returns `true` when pre-build is consumed; skip auto-rebuild when geometry continuation is queued (`_skipAutoRebuildAfterPreBuildGeometry` / pending build).
- `CaveBuildActionPacing.cs`: one action per tick, `EditorApplication.delayCall`, `_actionRunning` reentrancy guard.

### Unity editor — paced queue + phased cave build (overload / crash mitigation)

**Symptom:** Repeated Unity crashes during post-pre-build geometry and full cave builds.

**Fix:**

- `CaveBuildActionPacing.cs` — serial queue with **1.5s normal / 3.5s heavy** lead-in, **1.5–2.5s cooldown** after each job, max depth 48, no `delayCall`.
- `LavaTubeCaveBuildPipeline.QueueRun` — splits the 40-stage build into **5 heavy queue phases** with pacing between each.
- `LavaTubeCaveBuilder` — default **phased queue** (`usePhasedCaveBuild` in Cave Build Cursor Settings).
- `CaveBuildCursorAgentBridge` — geometry continuation and auto-rebuild wait when `IsBusy`; heavy scheduling for rebuild / workflow advances.
- Progress bar updates throttled to ~350ms during sync phases.

Tune delays in **Cave Build Cursor Settings** → Editor queue pacing (seconds).

### Unity editor — stack overflow crash (post-pre-build geometry, 08:09)

**Symptom:** Unity 6000.4.6f1 crash with ~32k stack depth; crash thread in `EditorApplication.Internal_CallDelayFunctions` right after pre-build ladder and `[SplineCave] Spawn aligned to maze route start`.

**Cause:** `CaveBuildActionPacing` ran the full 40-stage cave build inside `EditorApplication.delayCall`, which nests Unity’s delay-call dispatcher. `_skipAutoRebuildAfterPreBuildGeometry` was cleared before the build finished.

**Fix:**

- `CaveBuildActionPacing.cs` — one paced action per editor `update` tick (no `delayCall`).
- `CaveBuildCursorAgentBridge.cs` — keep auto-rebuild suppressed until pending geometry build completes.
- `CaveBuildPendingGeometryBuild.cs` — reentrancy guard.
- `LavaTubeCaveBuildPipeline.cs` — stop repainting Scene views on every spline progress callback during sync build.

### Unity editor — Package Manager ScriptableSingleton warnings

**Symptom:** Console spam on domain reload: `ScriptableSingleton already exists` from `ServicesContainer` / `PackageManagerProjectSettings` constructors.

**Cause:** `HandsSampleProjectValidation` queried Package Manager during static field initialization and registered validation rules synchronously on `[InitializeOnLoad]`, racing Unity’s UPM UI singleton setup.

**Fix:** `Assets/Samples/XR Interaction Toolkit/.../HandsSampleProjectValidation.cs` — defer rule creation and registration via `EditorApplication.delayCall` (same pattern as Starter Assets sample).

### Unity editor — compile fix

- `LavaTubeCaveBuilder.cs`: `TryRunPreBuildGate` → `TryRunPreBuildPhase` (API rename).

### Unity editor — material / refresh stability (earlier same day)

- Session-cached material upgrade; deferred `SaveAssets`/`Refresh` during bulk builds; `AssetDatabase.StartAssetEditing` around build; skip per-object Undo during bulk (`CaveEditorUndo.IsBulkBuild`).

### Automation — action pacing

- Central **0.3s** pacing queue for Cursor bridge, grader CLI sleeps, and chained ladder rungs (`CaveBuildActionPacing`).

---

## Template (copy for new entries)

```markdown
## YYYY-MM-DD

### Short title

- What changed.
- Why (if not obvious).
- Files or menus touched.
```
