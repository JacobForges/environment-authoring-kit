# Cursor bot backlog — Hub clone (2026-06-07)

Prioritized work streams derived from repo docs, generated reports, and `REQUIREMENTS.md`. Re-read `Assets/EnvironmentKit/Generated/CaveBuildNextStepsPrompt.md` after each Unity build — it supersedes this list when fresh.

**Mission model:** [CURSOR_BOT_PHASED_MISSION.md](./CURSOR_BOT_PHASED_MISSION.md) — bot auto-routes Phase 1 → 2 → 3 via `./Tools/cursor-bot/run-session.sh`.

**Current snapshot:** Grade **Blocked (0/100)** on seed `1835808069`. Preflight **PASS**. **Phase 1 active** (world gate not passed). Gameplay milestones **G1–G8 pending** in `Assets/Scripts/HubGameProgress.json`.

---

## Phase 1 — World pipeline (Streams A–B) — **active now**

Exit when `CaveBuildQualityReport.json` → `buildAcceptable: true` and `overallScore` ≥ **85**.

### Stream A — Cave geometry & placement (blocking)

**Owner:** `Packages/com.cursor.environment-authoring-kit/Editor/Blockout/`

| # | Issue | Evidence | Agent action |
|---|-------|----------|--------------|
| A1 | Cave root 50m above surface | `CaveBuildCompletionReadout.md` dud | Fix `cave_mouth_seal` / depth snap in entrance builders |
| A2 | No walkable floors along route | `CaveBuildNextStepsPrompt.md` player_floor + visual_shell | Fix spline/maze shell per active route cells |
| A3 | Path / block_tunnel / geometry_integrity critical | Quality readout stage scores | One rung: `route_mesh_nav` or `geometry_integrity` |
| A4 | Missing mob spawns, water, atmosphere | Completion readout | Wire spawners and `UndergroundRiver_Pool` per ladder |

### Stream B — Surface & terrain integration

**Owner:** `SurfaceTerrainTileExpansion.cs`, `CaveBuildSurfacePipeline`

| # | Issue | Evidence |
|---|-------|----------|
| B1 | NavMesh PathPartial 0→1 | `CaveBuildNextStepsPrompt.md` terrain_integration |
| B2 | Water_649 floating | water rung |
| B3 | No professional entrance descent mesh | ground_placement |
| B4 | FullWorld seam / sculpt | `SurfaceTerrainGridManifest.json`, terrain ladder |

---

## Phase 2 — Playable demo (Stream F) — after world gate

**Owner:** `Assets/Scripts/` + `MainScene.unity` wiring  
**Skill:** `.cursor/skills/hub-gameplay-completion/SKILL.md`  
**Progress:** `Assets/Scripts/HubGameProgress.json`  
**Acceptance:** [GAMEPLAY_DEMO_ACCEPTANCE.md](./GAMEPLAY_DEMO_ACCEPTANCE.md)

Bot runs `--workflow=gameplay` automatically when Phase 1 gate passes. **One milestone per session.**

| ID | Milestone | Agent action |
|----|-----------|--------------|
| G1 | Assembly bootstrap | Add `Hub.asmdef`; reference EnvKit runtime assemblies |
| G2 | Player scene wiring | Player prefab/scene: `PlayerController`, `CharacterController`, `PlayerCameraRig`, spawn |
| G3 | CombatStats | `Assets/Scripts/CombatStats.cs` — HP/damage/death (EnvKit reflects this type) |
| G4 | Surface → portal | Walkable path spawn → `PortalFive` without noclip |
| G5 | Cave goal marker | Trigger at route end; log/UI on reach |
| G6 | Combat encounter | One spawner mob; player can take/deal damage |
| G7 | Minimal HUD | HP + objective text |
| G8 | Demo smoke pass | Human Play Mode verify; set `demoReady: true` |

**Exit criteria:** `HubGameProgress.json` → `demoReady: true`.

---

## Phase 3 — Polish (Stream E) — after demoReady

| # | Task | Notes |
|---|------|-------|
| E1 | Licensed art in `Assets/EnvironmentKit/CC0Imports/` | 398 floor / 647 wall prefabs discovered |
| E2 | Demo recap pipeline | `Packages/.../cave-grader/demo-recap-pipeline.ts` |
| E3 | Performance budget | 1.1M+ tris flagged in last readout |

---

## Stream C — Pipeline reliability & auto-heal

**Owner:** `CaveBuildHealOrchestrator`, `watch-grade.ts`

| # | Task | Notes |
|---|------|-------|
| C1 | Run `npm run doctor` in cave-grader | Confirms API key + model |
| C2 | Enable heal in Hub → Settings | Default on per `HEAL_AND_SYSTEM_MATRIX.md` |
| C3 | `npm run watch-grade` during long builds | Invokes SDK when score drops |

---

## Suggested bot schedule

| When | What |
|------|------|
| **Per session** | `./Tools/cursor-bot/run-session.sh --stream` (auto phase) |
| **After Unity build** | Re-grade (Phase 1) or Play Mode smoke (Phase 2) |
| **Weekday cron** | Cursor Automation: phased prompt (see `automation-draft.json`) |
| **Nightly** | GitHub showcase on self-hosted Unity (optional) |

---

## Explicit non-goals

- Unattended commercial game ship without Unity editor
- VITURE / OpenXR device certification
- Committed Asset Store art or `ResearchCache/` blobs to git
