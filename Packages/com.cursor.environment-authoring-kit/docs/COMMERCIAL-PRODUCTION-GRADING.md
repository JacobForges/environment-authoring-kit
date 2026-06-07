# Commercial production grading

The kit grades cave builds using **commercial production tiers** (ship gates), not marketing “AAA game” scores or academic letter ladders (AAA+, AA, etc.).

## Tiers

| Grade | Score | Meaning |
|-------|-------|---------|
| **Ship** | 95–100 | Release-ready for target platform: playable first minute, exports, compile clean, critical stages pass |
| **Beta** | 85–94 | Feature-complete enough for structured playtest; polish and perf before ship |
| **Alpha** | 70–84 | Playable vertical slice; internal QA only |
| **Prototype** | 50–69 | R&D / demo; not a ship candidate |
| **Blocked** | &lt;50 or dud | Do not treat as player-facing |

## JSON fields (`CaveBuildQualityReport.json`)

| Field | Meaning |
|-------|---------|
| `gradingStandard` | `"commercial_production"` |
| `letterGrade` | Ship / Beta / Alpha / Prototype / Blocked |
| `gradeDescription` | Human-readable tier explanation |
| `buildAcceptable` | **Beta+** milestone (≥85, not dud, stage floors) |
| `meetsBetaTarget` | Same as acceptable for full builds |
| `meetsShipTarget` | **Ship** tier (95+, critical stages ≥90, letter Ship) |
| `meetsStrictTarget` | Legacy alias for `meetsShipTarget` |
| `targetGrade` / `targetScore` | Ship / 95 |
| `betaGrade` / `betaScore` | Beta / 85 |

## Stage rubric

- **Critical pass:** 90+ on critical stages (spawn, shell, walkability, packaging, etc.)
- **Floor:** 80+ on all non-waived stages (terrain_integration relaxed on adventure builds but still needs floor)
- **New stage:** `packaging_readiness` — spawn pad, reachability, surface PlayerSpawnPoint, compile, NavMesh

## 100-point checklist

Exported to `CaveBuildCommercialProductionManifest.json` (class `CaveBuildCommercialProductionGrader`).

Categories: asset validation, params, geometry, spline, FX, colliders/LOD, breakables, nav/lighting, stats export, connectivity, packaging.

**Ship on checklist:** 95+ overall. **Pass iteration:** 85+ (Beta).

## Cursor prompts

Agent prompts under `Tools/cave-grader/prompt-ladder/` reference **Ship (95+)** and **Beta (85+)** playtest milestones — not AAA+ / 99+.

## Pre-build ladder

Unchanged numerically (88+ tooling gate). Separate from full-build commercial tiers.

## Migration from v2 rubric

| Old | New |
|-----|-----|
| AAA+ @ 99+ | **Ship** @ 95+ |
| AAA @ 97 | **Ship** (same band) |
| buildAcceptable @ strict AAA+ | **Beta+** @ 85+ (Ship also OK) |
| `CaveBuildAaaFeatureManifest.json` | `CaveBuildCommercialProductionManifest.json` (alias class kept) |

Re-run **Build Complete Cave** to regenerate reports with `gradingVersion: 3.0.0-commercial`.
