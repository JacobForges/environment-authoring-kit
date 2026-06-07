## Scope (every rung)

- **Research before placement:** Sync ResearchCache + read `CaveBuildResearchExecutionBrief.md` **before** any terrain/prop/geometry spawn. Pre-placement gate runs at pipeline step 0.
- **Additive build:** Extend existing Ground/Terrain — do **not** radial-overwrite the center ~48% land disk. FullWorld reuses `GeneratedSurfaceWorld` when present.
- **Stale compile diagnostics:** `CaveBuildCompileDiagnostics.json` lists only **verifiedOnDisk** errors. If `staleErrorCount > 0`, Bee/Editor.log is behind — do not re-fix lines already correct on disk (e.g. removed `AnchorWorld`).
- **Workflow order:** `research` (plan only) → `compile_gate` (zero verified CS errors, cite plan) → scene ladder rung. Check off `CaveBuildWorkflowContext.json` checklist; obey `CaveBuildAgentMemory.json`.
- Edit **kit C# only** under `Packages/com.cursor.environment-authoring-kit/`.
- Do not change gameplay scripts unless the active rung explicitly includes live/play-mode fixes.
- After code changes the user runs in Unity:
  1. **Window → Environment Kit → Remove Cave Layered Shells**
  2. **Window → Environment Kit → Build Complete Cave Level**
- Read on disk when needed: `CaveBuildQualityReport.json`, `CaveBuildResearchExecutionBrief.json`, `ResearchCache/index.json`, `ResearchCache/entries/{id}/content.md`, `CaveBuildVisualShellAudit.json`, `CaveBuildFailingStages.json`, `CaveBuildLadderContext.json`.
- **Every plan row and C# fix** must cite which ResearchCache entry or local image (`images/fl-*-hillshade/`, `images/{id}/ref-*.png`) informed the change.
- **Commercial production grades:** Ship (95+) = release-ready slice; Beta (85+) = playtest milestone; not marketing “AAA game” quality.

## Hard fail (full adventure build)

- **AdventureShell** present or rebuilt.
- Stacked **PathCeiling** / horizontal slab bands under CaveGeometry.
- **RouteTerrainFloor** exists but **PathPlatforms** renderers still enabled (must stay hidden via `HideRoutePlatformSlabs`).
- Block rings: count > `pathSteps + 2` with average > **36 blocks/ring** (onion walls).
- Cave root world **Y** must match `SceneGroundInfo.SurfaceY - CaveGeometryPaths.UndergroundDepthMeters` (not grid Y=0 floating in void).
- **No invisible solid colliders** on adventure caves (`geometry_integrity` / playability gate). Shell blocks and decorative meshes are visible-only unless minable gameplay rocks.
- **Pit falls** respawn the player on the **surface main area** (`PlayerSpawn` tag), not on hidden colliders under the gap.
- **Build Complete Cave** runs a **paced validation bot** (route probe + visual shell + geometry + surface entrance) — use `issues[].suggestedStageId` for targeted fixes only.
- **Surface walk-in:** `CaveSurfaceEntranceBuilder` — mouth pad on terrain + stepped descent; spawn at surface mouth (`keepAtSurfaceMouth: true`).
