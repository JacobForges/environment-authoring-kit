# Documentation index — Environment Authoring Kit

Package **`com.cursor.environment-authoring-kit`** — start at the [package README](../README.md).

**On GitHub:** read the Hub repo [PUBLIC_REPO_SCOPE.md](../../../../docs/PUBLIC_REPO_SCOPE.md) first.

---

## Start here

| Document | Audience | Content |
|----------|----------|---------|
| [../README.md](../README.md) | Everyone | Install, menus, quick start, pipeline overview |
| [SURFACE_TERRAIN_GRID_AND_OPEN_WORLD.md](SURFACE_TERRAIN_GRID_AND_OPEN_WORLD.md) | Everyone | **Grid snap, fast flat seams, open-world expansion (~289 tiles)** |
| [FULLWORLD_TERRAIN_AND_HUB.md](FULLWORLD_TERRAIN_AND_HUB.md) | Everyone | **Hub monitor, sequential build, LiDAR, research** |
| [FIRST_RUN_AFTER_SESSION.md](FIRST_RUN_AFTER_SESSION.md) | Builders | **Checklist after large surface pipeline changes** |
| [PHASE_CONCEPT_IMAGES.md](PHASE_CONCEPT_IMAGES.md) | Art / pipeline | **Phase concept images, grading, promotion** |
| [FULLWORLD_GENERATION_PRESETS.md](FULLWORLD_GENERATION_PRESETS.md) | Everyone | **Hub presets 1–20** — best-case usage, flags, maze flavors, recipes vs presets |
| [REQUIREMENTS.md](REQUIREMENTS.md) | PM / leads | Functional requirements, prop contract, acceptance |
| [../CHANGELOG.md](../CHANGELOG.md) | Maintainers | Version history |

---

## Pipeline & architecture

| Document | Content |
|----------|---------|
| [WORLD-GENERATION-PIPELINE-LADDER.md](WORLD-GENERATION-PIPELINE-LADDER.md) | Global rung order, invalidation, Far Cry / UE PCG principles |
| [PHASE_CONTRACTS.md](PHASE_CONTRACTS.md) | Rung I/O table + queued step mapping (120 steps) |
| [SURFACE-WORLD-BUILD.md](SURFACE-WORLD-BUILD.md) | Surface menus, scopes, generated hierarchy |
| [AAA-PROCEDURAL-CAVE-PIPELINE.md](AAA-PROCEDURAL-CAVE-PIPELINE.md) | Autonomous Unity + Cursor design |
| [CAVE-BUILD-WORKFLOW-HARMONY.md](CAVE-BUILD-WORKFLOW-HARMONY.md) | Coordinator rules (nav, ground lock, meat loop) |
| [PRODUCT_BOUNDARY.md](PRODUCT_BOUNDARY.md) | In scope / out of scope, Florida policy |

---

## Grading, Cursor, quality

| Document | Content |
|----------|---------|
| [CaveGradingAndCursor.md](CaveGradingAndCursor.md) | JSON outputs, API setup, pre/post workflows |
| [FLOW-AUDIT-2026-05-27.md](FLOW-AUDIT-2026-05-27.md) | Hub/provider flow audit; 63→120 doc drift closed 2026-05-28 |
| [PUBLIC_REPO_SCOPE.md](../../../../docs/PUBLIC_REPO_SCOPE.md) | What GitHub contains vs local-only |
| [COMMERCIAL-PRODUCTION-GRADING.md](COMMERCIAL-PRODUCTION-GRADING.md) | Ship / Beta / Alpha tiers |
| [SURFACE_BUILD_RESPONSIVENESS.md](SURFACE_BUILD_RESPONSIVENESS.md) | Editor freeze avoidance on surface passes |

---

## CI & security

| Document | Content |
|----------|---------|
| [CODEQL_SETUP_AND_USE.md](../../../../docs/CODEQL_SETUP_AND_USE.md) | CodeQL on GitHub — setup, run, results (Hub) |
| [CODEQL_SELFHOSTED_INSTALL.md](CODEQL_SELFHOSTED_INSTALL.md) | Install checklist, legal, troubleshooting depth |

## World layout & placement

| Document | Content |
|----------|---------|
| [RESEARCH_WORLD_LAYOUT_PLACEMENT.md](RESEARCH_WORLD_LAYOUT_PLACEMENT.md) | 30 URLs — seams, scatter, cave mouth, validation, POI; generic 20-step plan |
| [PROJECT_LAYOUT_PLAN_20_STEPS.md](PROJECT_LAYOUT_PLAN_20_STEPS.md) | **Hub audit** — personalized 20×3 substeps vs generic plan |
| [LAYOUT_PLAN_EXECUTION_STATUS.md](LAYOUT_PLAN_EXECUTION_STATUS.md) | Last run status + how to finish batch/menu execution |
| [RESEARCH_SURFACE_PROPS_UNDERGROUND.md](RESEARCH_SURFACE_PROPS_UNDERGROUND.md) | Prop spread + underground entrance synthesis |
| [RESEARCH_OPEN_WORLD_STREAMING.md](RESEARCH_OPEN_WORLD_STREAMING.md) | Option A streaming research (after 9-tile quality) |
| [RESEARCH_3D_GAUSSIAN_SPLAT.md](RESEARCH_3D_GAUSSIAN_SPLAT.md) | 3DGS fundamentals, labs, Mac local use (~52 URLs) |
| [RESEARCH_3D_GAUSSIAN_SPLAT_UNITY.md](RESEARCH_3D_GAUSSIAN_SPLAT_UNITY.md) | 3DGS Unity Editor integration (~51 URLs) |
| [GAUSSIAN_SPLAT_INTEGRATION.md](GAUSSIAN_SPLAT_INTEGRATION.md) | Optional hero splat at cave mouth + hardware safeguards |
| [PLAN_PIPELINE_RESPONSIVENESS.md](PLAN_PIPELINE_RESPONSIVENESS.md) | FullWorld stall audit + seam/connectivity pacing plan |
| [RESEARCH_PIPELINE_RESPONSIVENESS.md](RESEARCH_PIPELINE_RESPONSIVENESS.md) | Editor/terrain pacing research (25 URLs) |

---

## Data & legal

| Document | Content |
|----------|---------|
| [RESEARCH_DATA_ATTRIBUTION.md](RESEARCH_DATA_ATTRIBUTION.md) | USGS, NOAA, FGS/FDEP credits |
| [PUBLISHING.md](PUBLISHING.md) | Release checklist for GitHub / UPM |

---

## Session notes (archive)

| Document | Content |
|----------|---------|
| [SESSION-SUMMARY-2026-05-21.md](SESSION-SUMMARY-2026-05-21.md) | Point-in-time notes — prefer [CHANGELOG.md](../CHANGELOG.md) for current history |

---

## When to update

| You changed… | Update |
|--------------|--------|
| Menu path, Hub workflow, or FullWorld grid | [../README.md](../README.md) + [FULLWORLD_TERRAIN_AND_HUB.md](FULLWORLD_TERRAIN_AND_HUB.md) + [CHANGELOG.md](../CHANGELOG.md) |
| Hub FullWorld preset list or `FullWorldGenerationStyleCatalog` | [FULLWORLD_GENERATION_PRESETS.md](FULLWORLD_GENERATION_PRESETS.md) + `fullworld-generation-style-papers.ts` |
| Acceptance bar or prop/cave contract | [REQUIREMENTS.md](REQUIREMENTS.md) |
| Ladder rung I/O | [PHASE_CONTRACTS.md](PHASE_CONTRACTS.md) + `CaveBuildPhaseContractRegistry` |
| Grade JSON or Cursor env | [CaveGradingAndCursor.md](CaveGradingAndCursor.md) |
| Research / attribution | [RESEARCH_DATA_ATTRIBUTION.md](RESEARCH_DATA_ATTRIBUTION.md) |
| CodeQL workflows or Unity prep scripts | [CODEQL_SETUP_AND_USE.md](../../../../docs/CODEQL_SETUP_AND_USE.md) + [CHANGELOG.md](../CHANGELOG.md) |
| World layout / placement / scatter | [RESEARCH_WORLD_LAYOUT_PLACEMENT.md](RESEARCH_WORLD_LAYOUT_PLACEMENT.md) + [REQUIREMENTS.md](REQUIREMENTS.md) |
