---
name: hub-game-completion
description: >-
  Phase 1 Hub bot — finish the Environment Authoring Kit world pipeline (one grading
  ladder rung per session). Use when mission phase is world or CaveBuildQualityReport
  is below Beta gate. Switches to hub-gameplay-completion after buildAcceptable + grade ≥ 85.
---

# Hub game completion (Phase 1)

## When to use this skill

Use when **mission phase = world** (default until world gate passes):

- `CaveBuildQualityReport.json` missing, `buildAcceptable: false`, or `overallScore` < **85**

When gate passes, `./Tools/cursor-bot/run-session.sh` auto-switches to **hub-gameplay-completion** (Phase 2).

## Scope honesty

This phase produces a **graded procedural world**. Phase 2 adds the **playable demo** in `Assets/Scripts/`. Do not promise unattended AAA ship.

## Session spine

1. **Read generated truth**
   - `Assets/EnvironmentKit/Generated/CaveBuildGeneratedJsonManifest.json`
   - Active rung from `CaveBuildLadderContext.json` or `CaveBuildAgentPrompt.md`
   - `CaveBuildQualityReport.json`
   - [docs/CURSOR_BOT_BACKLOG.md](../../../docs/CURSOR_BOT_BACKLOG.md) Streams A–B

2. **Pick exactly one work stream** (highest priority blocker).

3. **Implement smallest fix** — prefer `Packages/com.cursor.environment-authoring-kit/Editor/`.

4. **Do not** start FullWorld rebuild unless human requested.

5. **Verify before done** — After edits, session harness runs compile verification. Session **fails** (exit 4) if `CaveBuildCompileDiagnostics.json` has verified CS errors. Open Unity or set `UNITY_PATH` so diagnostics refresh. Do not claim the rung is fixed until compile is clean and Unity re-grades.

## Work stream routing

| Symptom (from reports) | Where to edit |
|------------------------|---------------|
| Terrain NavMesh / trails | `SurfaceTerrainTileExpansion.cs`, `CaveBuildSurfacePipeline` |
| Cave floor / headroom / path | `SplineLavaTubeCaveGenerator`, `CaveMazeVolumeBuilder` |
| Water floating | `UndergroundRiver` builders |
| Entrance / mouth seal | `CaveSurfaceEntranceBuilder`, `ground_placement` |
| Mob spawns / combat spaces | `HumanoidCombatSpawner`, `CaveCombatSetupUtility` |

## SDK invocation

```bash
./Tools/cursor-bot/run-session.sh --stream
```

## Hard stops

- `compile_gate` with verified CS errors — fix compile only.
- `CaveBuildDoNotPrompt.md` violations — stop and re-read.
- Unity build queue active — wait; do not nest builds.

## References

- [CURSOR_BOT_PHASED_MISSION.md](../../../docs/CURSOR_BOT_PHASED_MISSION.md)
- [AGENTS.md](../../../AGENTS.md)
- [hub-gameplay-completion](../hub-gameplay-completion/SKILL.md) — Phase 2 after gate
