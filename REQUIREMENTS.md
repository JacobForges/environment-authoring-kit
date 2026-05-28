# Hub cave project — requirements

**Status:** Living document  
**Last updated:** 2026-05-27  
**Owners:** Hub / Environment Authoring Kit team  

Package-level detail: [Packages/com.cursor.environment-authoring-kit/docs/REQUIREMENTS.md](Packages/com.cursor.environment-authoring-kit/docs/REQUIREMENTS.md).

When you change scope or acceptance criteria, update this file in the same PR or commit as the code change.

---

## 1. Product vision

Build a **repeatable, graded, partially automated** pipeline that produces **playable underground cave levels** for an **XR (VITURE)** adventure game. Levels should feel like classic dungeon exploration (RO / Zelda lineage): connected rooms and corridors, readable navigation, combat spaces, and interactable mining — not layered “onion” shells or layout-only prototypes shipped as final content.

---

## 2. Functional requirements

### 2.1 Cave generation

| ID | Requirement | Priority |
|----|-------------|----------|
| F-01 | Generate a **complete cave** in the active scene from editor menu (block tunnel + spline/adventure hybrid as configured). | Must |
| F-02 | Support **layout prototype** mode for fast maze/path preview without full visual pipeline. | Must |
| F-03 | Cave must be **walkable end-to-end** from entrance to goal (nav mesh or equivalent floor validation). | Must |
| F-04 | **Player spawn** must align to maze route start; no fall-through at spawn. | Must |
| F-05 | **Minable blocks** present in block-tunnel mode per kit conventions. | Must |
| F-06 | **Mob / prop spawns** along route where adventure features are enabled. | Should |
| F-07 | **Single underground atmosphere** (no duplicate sky/atmosphere stacks). | Must |
| F-08 | Entrance anchored to **`PortalFive`** when present; build fails clearly if portal/ground missing when required. | Must |
| F-09 | **FullWorld** builds surface + 9-tile terrain + vegetation **before** queued cave geometry (terrain-first startup). | Must |
| F-10 | Cave geometry = block tunnel **or** full shell (floor + ceiling) **or** spline tube — not ramp-only partial from terrain fixes. | Must |
| F-11 | Queued pipeline completes **120 paced steps** (validate → geo 1–13 → … → finalize). | Must |

### 2.2 Surface world & vegetation (FullWorld)

| ID | Requirement | Priority |
|----|-------------|----------|
| S-01 | Multi-tile terrain grid (typically **9 tiles**) with unified play center and seam stitch. | Must |
| S-02 | Trails, NavMesh, and `GeneratedSurfaceWorld` manifest before cave meat loop when `FullWorld`. | Must |
| S-03 | **Per-tile vegetation contract** — targets × tile count (e.g. grass **150/tile**, trees **35/tile**); minimum enforced per tile before global fill. | Must |
| S-04 | Every terrain tile has **≥42** vegetation instances in scene; `surface_props` ladder not passed from JSON alone. | Must |
| S-05 | `Build Surface World Only` does not claim full cave completion. | Must |

See package [docs/REQUIREMENTS.md](Packages/com.cursor.environment-authoring-kit/docs/REQUIREMENTS.md) §2.2 for numeric table.

### 2.3 Portals and scene anchors

| ID | Requirement | Notes |
|----|-------------|-------|
| P-01 | **`PortalFive`** = cave **entrance** portal for build placement. | Not the shop portal |
| P-02 | Ground / anchor from **SceneGroundInfo** (user ground mesh or detected surface). | |
| P-03 | **MainScene** is the default rebuild target for menu “Rebuild Complete Cave (MainScene)”. | |

### 2.4 Quality grading

| ID | Requirement | Priority |
|----|-------------|----------|
| Q-01 | Every full build produces **`CaveBuildQualityReport.json`** with stage scores and letter grade. | Must |
| Q-02 | **Dud builds** (onion layers, mode mismatch, critical stage &lt; 70, nav fail on full build, etc.) set `buildAcceptable: false` and cap grade at **D-**. | Must |
| Q-03 | Full builds target **commercial Ship (95+)** via in-editor **meat loop** (bounded passes); **Beta (85+)** = `buildAcceptable` playtest milestone. | Must |
| Q-04 | Pre-build **readiness ladder** (weighted rungs) runs **before** heavy geometry when gate enabled. | Must |
| Q-05 | Pre-build target: **B+ (88+)** overall unless settings relax gate. | Must |
| Q-06 | Exported JSON/Markdown under `Assets/EnvironmentKit/Generated/` for human and Cursor agents. | Must |

### 2.5 Cursor automation (optional but supported)

| ID | Requirement | Priority |
|----|-------------|----------|
| C-01 | **Pre-build workflow:** research → plan → compile_gate → readiness ladder (when API key + settings on). | Should |
| C-02 | **Post-build workflow:** research → compile_gate → scene ladder (when API key + settings on). | Should |
| C-03 | After pre-build success, **geometry continues automatically** (queued pending build), without also scheduling a duplicate full auto-rebuild. | Must |
| C-04 | **`grade-and-fix.ts`** uses `@cursor/sdk`; API key from `Tools/cave-grader/.env` syncable to Unity. | Should |
| C-05 | Chained editor actions use **paced delays** (≈0.3s) and must not run heavy builds synchronously inside nested `EditorApplication.update` (stack safety). | Must |
| C-06 | Play-mode issues can export **`CaveLiveFixRequest.json`** and request Cursor live fix. | Could |

### 2.6 Real-world terrain & aquifer research (Florida panhandle)

| ID | Requirement | Priority |
|----|-------------|----------|
| R-01 | Maintain categorized **`Assets/EnvironmentKit/ResearchCache/`** (entries, categories, optional images) synced from proven public sources. | Must |
| R-02 | Include **NW Florida panhandle** references: Bay, Washington, Jackson, Calhoun — LiDAR bare-earth + Floridan aquifer **structural** data. | Must |
| R-03 | Agents and builders read **local cache first** (`CaveBuildResearchCache.json`, `entries/*/content.md`, county hillshades) before web search. | Must |
| R-04 | **Cave structure only** for underground layout: use thickness/structural surfaces and ground LiDAR; **exclude** water table, TDS, bathymetry, inundation DEMs, spring discharge for void sculpting. | Must |
| R-05 | **`docs/RESEARCH_DATA_ATTRIBUTION.md`** documents USGS, NOAA, FGS/FDEP, NWFWMD credits; shipped docs link to it. | Must |
| R-06 | **Mandatory** research pull on every full build: cache metadata + reuse on-disk images + fetch missing previews + FL county hillshades (`SyncFullResearchPull`). | Must |

---

## 3. Non-functional requirements

### 3.1 Performance (XR / VITURE)

| ID | Requirement |
|----|-------------|
| N-01 | Apply **XROptimizationProfile** (`VitureXRPro` preset) on full world/cave builds where configured. |
| N-02 | Grading includes **performance** stage (collider/renderer budget). |
| N-03 | Block culling menu available for runtime visibility in dense block caves. |

### 3.2 Editor stability

| ID | Requirement |
|----|-------------|
| N-04 | Bulk builds must not call **per-frame AssetDatabase.Refresh** or unbounded **Undo** on thousands of objects. |
| N-05 | Material upgrade / pack ensure runs **once per session** during bulk build, not per sub-step. |
| N-06 | No **stack overflow** from scheduling duplicate full builds + draining action queue in one editor tick. |

### 3.3 Tooling

| ID | Requirement |
|----|-------------|
| N-07 | Unity **6000.4+** (project validated on 6000.4.6f1). |
| N-08 | **URP** rendering pipeline. |
| N-09 | Node **18+** for `Tools/cave-grader`. |
| N-10 | macOS editor supported (primary dev platform). |

---

## 4. Acceptance criteria (release-quality cave)

A cave build is **acceptable for playtest** when all are true:

1. `CaveBuildQualityReport.json` → `buildAcceptable: true`
2. Letter grade ≥ **B+** on pre-build (if gate ran) and no blocking dud reasons on post-build
3. Diagnose menu shows coherent hierarchy (cave root, route, spawn, colliders)
4. Enter Play Mode: player on solid ground at spawn, can traverse to goal without cheats
5. No pink materials on primary cave surfaces (or repair menu fixes them)
6. Visual shell stage ≥ 80 (no dominant horizontal onion layering)

**AAA+ strict** remains the target for automated meat loop completion, not only manual playtest.

---

## 5. Out of scope (current)

- Fully unattended “one button” shipping without Unity editor (partial automation only).
- Shop portal logic tied to cave entrance (explicitly separate from `PortalFive`).
- Linux/Windows editor QA unless explicitly tested.
- Multiplayer / networking cave sync.

---

## 6. Traceability

| Area | Primary code / doc |
|------|-------------------|
| Build entry | `LavaTubeCaveBuilder.cs`, `CaveBuildUnifiedFlow.cs` |
| 120-step queued pipeline | `LavaTubeCaveBuildPipeline.Queued.cs` |
| Surface + props | `SurfaceWorldGenerator.cs`, `SurfaceIntelligentPropPlacer.cs` |
| Pre/post Cursor | `CaveBuildCursorAgentBridge.cs`, `docs/CaveGradingAndCursor.md` |
| Pacing | `CaveBuildActionPacing.cs` |
| Grading | `CaveBuildQualitySystem.cs`, `Tools/cave-grader/` |
| Research cache | `Tools/cave-grader/research-cache-sync.ts`, `ResearchCache/`, `docs/RESEARCH_DATA_ATTRIBUTION.md` |
| Requirements | **This file** |
| Change history | `docs/CHANGELOG.md` |

---

## 7. Revision history

| Date | Summary |
|------|---------|
| 2026-05-27 | FullWorld terrain-first; 9-tile vegetation contract; strict cave geometry; kit v0.2.0 docs. |
| 2026-05-21 | Florida panhandle LiDAR + Floridan aquifer research cache; attribution doc; ground_placement integration. |
| 2026-05-21 | Initial requirements doc; documents portal rules, grading, Cursor flows, stability NFRs. |
