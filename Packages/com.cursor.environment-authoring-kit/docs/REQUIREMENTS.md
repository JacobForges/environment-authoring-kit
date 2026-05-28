# Environment Authoring Kit — requirements

**Status:** Living document (package scope)  
**Last updated:** 2026-05-28  
**Package:** `com.cursor.environment-authoring-kit` v0.3.0+

**Public GitHub accuracy:** [docs/PUBLIC_REPO_SCOPE.md](../../../../docs/PUBLIC_REPO_SCOPE.md) (consuming Hub repo).

Update this file when behavior, contracts, or acceptance criteria change.

---

## 1. Product vision

Deliver a **repeatable, graded, editor-safe** pipeline that authors:

1. **Florida karst surface** — multi-tile terrain, trails, NavMesh, dense vegetation.  
2. **Playable lava-tube caves** — maze layout, block tunnel or shell route mesh, walk-in mouth, XR budgets.  
3. **Optional Cursor automation** — research cache, phase prompts, grade-and-fix scripts.

Target: **Unity 6 + URP** adventure levels that *may* use **XR** (OpenXR / vendor SDK in **your** project) — enclosed exploration, mining, combat spaces — not open-world MMO tooling. Kit applies **XR optimization profiles** in-editor; it does **not** ship device SDKs or certify glasses-ready builds.

---

## 2. Functional requirements

### 2.1 Surface world (FullWorld)

| ID | Requirement | Priority |
|----|-------------|----------|
| S-01 | Build **surface world** from Ground anchor before underground geo on `FullWorld` scope. | Must |
| S-02 | Support **9-tile** (or more) terrain grids via `SurfaceTerrainTileExpansion` with unified play center. | Must |
| S-03 | Generate trails, optional roads/water/mountains per `WorldGenerationRequest`. | Must |
| S-04 | Bake **surface NavMesh** after terrain phases complete. | Must |
| S-05 | **Vegetation scatter** meets per-tile contract on every locked terrain (see §2.2). | Must |
| S-06 | `Build Surface World Only` must not require or destroy full cave geometry unless user chooses full rebuild. | Must |
| S-07 | Florida LiDAR / hillshade stamping uses georeferenced county bbox (`SurfaceDemGeorefStatus.json`). | Should |

### 2.2 Surface vegetation contract (per tile)

Targets are **per terrain tile**; multiply by `terrainTileCount` (typically **9** on FullWorld).

| Category | Target / tile | Minimum enforced / tile |
|----------|---------------|-------------------------|
| Trees | 35 | 28 |
| Grass | 150 | 110 |
| Bushes | 95 | 75 |
| Ground cover | 110 | 85 |

**Placement rules:**

- Slots generated inside **each tile’s footprint** (not only a radial annulus around play center).
- Pass 1 fills **minimum per tile** before global fill.
- `surface_props` ladder rung is complete only when scene has **≥42 vegetation instances per tile** and **≥55 × tileCount** total instances under `GeneratedSurfaceWorld/Vegetation`.
- Plan JSON coverage audit: **≥88%** of category targets placed.

Implementation: `SurfaceTerrainPropPlacementRegion`, `SurfaceIntelligentPropPlacer`.

### 2.3 Cave generation

| ID | Requirement | Priority |
|----|-------------|----------|
| F-01 | Generate **complete cave** from editor menu (block tunnel + adventure shell as configured). | Must |
| F-02 | **Layout prototype** mode for fast maze preview without full visual pipeline. | Must |
| F-03 | Cave **walkable** entrance → goal (`CaveBuildRouteProbe`, NavMesh). | Must |
| F-04 | **Spawn** on maze route start; no fall-through at spawn. | Must |
| F-05 | **Minable blocks** in block-tunnel mode per kit conventions. | Must |
| F-06 | Entrance anchored to **`PortalFive`** when present; clear errors if ground/portal missing. | Must |
| F-07 | **Full cave geometry** before mouth-only fixes: blocks, or floor+ceiling shell, or spline tube — not ramp-only partial. | Must |
| F-08 | Queued pipeline **120 steps** with paced editor queue (no monolithic geo in one tick). | Must |

### 2.4 Portals and anchors

| ID | Requirement | Notes |
|----|-------------|-------|
| P-01 | **`PortalFive`** = cave entrance portal for placement. | |
| P-02 | Ground from **`SceneGroundResolver`** / Ground tag. | |
| P-03 | **Rebuild Complete Cave (MainScene)** opens `MainScene` only if present in the consumer project (not shipped on public GitHub). | |

### 2.5 Quality grading

| ID | Requirement | Priority |
|----|-------------|----------|
| Q-01 | Full build writes **`CaveBuildQualityReport.json`**. | Must |
| Q-02 | Dud builds set `buildAcceptable: false` (onion layers, nav fail, mode mismatch, etc.). | Must |
| Q-03 | Pre-build readiness ladder before heavy geo when gate enabled. | Must |
| Q-04 | Artifacts under `Assets/EnvironmentKit/Generated/` for humans and agents. | Must |
| Q-05 | Surface terrain ladder grades heightfield, trails, **prop_*** rungs against §2.2. | Must |

### 2.6 Cursor automation (optional)

| ID | Requirement | Priority |
|----|-------------|----------|
| C-01 | Pre-build workflow: research → plan → gate → ladder (when API key on). | Should |
| C-02 | Post-build / meat-loop invoke when settings allow and grade below target. | Should |
| C-03 | Geometry continues after pre-build without duplicate full rebuild scheduling. | Must |
| C-04 | `grade-and-fix.ts` via `@cursor/sdk`; API key from `.env` syncable to Unity. | Should |
| C-05 | Paced delays; no blocking `WaitForExit` on Unity main thread during active build. | Must |

### 2.7 Research cache (Florida)

| ID | Requirement | Priority |
|----|-------------|----------|
| R-01 | **`Assets/EnvironmentKit/ResearchCache/`** categorized entries + images. | Must |
| R-02 | NW Florida panhandle LiDAR + Floridan **structural** aquifer refs. | Must |
| R-03 | Agents read local cache before web search. | Must |
| R-04 | **Cave structure only** — exclude water table / bathymetry for void sculpt. | Must |
| R-05 | [RESEARCH_DATA_ATTRIBUTION.md](RESEARCH_DATA_ATTRIBUTION.md) credits. | Must |
| R-06 | Full research pull on full builds (`SyncFullResearchPull`). | Must |

---

## 3. Non-functional requirements

| ID | Requirement |
|----|-------------|
| N-01 | **XR optimization profile** (`VitureXRPro` or project preset) on full builds where configured — LOD/colliders/URP hints, not device QA. |
| N-02 | No per-frame `AssetDatabase.Refresh` or unbounded Undo on thousands of instances. |
| N-03 | Unity **6000.0+**, **URP 17+**. |
| N-04 | Node **18+** for `Tools/cave-grader`. |
| N-05 | Editor queue depth limits; step watchdog for stuck pipeline. |

---

## 4. Acceptance criteria

### 4.1 FullWorld surface

1. Nine (or configured) terrains present and seam-stitched.  
2. `GeneratedSurfaceWorld/Vegetation` has dense scatter on **every** tile (visual spot-check + `SurfacePropPlacementPlan_*.json`).  
3. `SurfaceTerrainBuildLadderReport.json` — prop rungs show counts ≥ minimum per §2.2.  
4. Surface NavMesh bakes without blocking cave queue indefinitely.

### 4.2 Full cave build

1. `CaveBuildQualityReport.json` → `buildAcceptable: true` for playtest milestone.  
2. `UndergroundCaveSystem/CaveGeometry` contains **BlockTunnel** and/or **RouteTerrainFloor** + **RouteTerrainCeiling**.  
3. Walk-in descent connects to route floor (not void / wall).  
4. Pipeline Console shows **cave geo 1/13 … 13/13** on fresh or invalidated ladder builds.  
5. Play Mode: spawn on solid floor; traverse to goal without cheats.

---

## 5. Out of scope

- Unattended CI shipping without Unity editor.  
- Shop portal = cave entrance (explicitly separate).  
- Runtime multiplayer sync.  
- Replacing Unity Terrain / NavMesh systems.

---

## 6. Traceability

| Area | Code / doc |
|------|------------|
| Surface pipeline | `CaveBuildSurfacePipeline`, `SurfaceTerrainAiPhases` |
| Props contract | `SurfaceTerrainPropPlacementRegion` |
| Cave queue | `LavaTubeCaveBuildPipeline.Queued.cs` |
| Ladder contracts | `CaveBuildPhaseContractRegistry`, [PHASE_CONTRACTS.md](PHASE_CONTRACTS.md) |
| Grading | `CaveBuildQualitySystem`, `Tools/cave-grader/` |
| This document | **docs/REQUIREMENTS.md** |
| Changes | [CHANGELOG.md](../CHANGELOG.md) |

---

## 7. Revision history

| Date | Summary |
|------|---------|
| 2026-05-28 | PUBLIC_REPO_SCOPE alignment; XR honesty; ResearchCache gitignored on GitHub. |
| 2026-05-27 | 9-tile vegetation; terrain-first FullWorld; 120-step queue (v0.3.0); Hub. |
| 2026-05-21 | Initial package requirements excerpt (Florida research, grading, Cursor). |
