---
name: hub-gameplay-completion
description: >-
  Phase 2 Hub bot — build playable demo gameplay in Assets/Scripts after the world
  pipeline reaches Beta (buildAcceptable, grade ≥ 85). Use when HubGameProgress
  milestones are pending or demoReady is false.
---

# Hub gameplay completion (Phase 2)

## When to use this skill

Use when **mission phase = gameplay**:

- `CaveBuildQualityReport.json` → `buildAcceptable: true` and `overallScore` ≥ **85**
- `Assets/Scripts/HubGameProgress.json` → `demoReady: false`

If world reports still fail, use **hub-game-completion** (Phase 1) instead.

## Scope

Implement **one milestone** (G1–G8) per session from [HubGameProgress.json](../../../Assets/Scripts/HubGameProgress.json). Smallest diff in `Assets/Scripts/` + minimal scene/prefab wiring.

**Do not** rewrite EnvKit editor pipeline unless a milestone explicitly requires a one-line runtime hook.

## Session spine

1. Read `Assets/Scripts/HubGameProgress.json` — pick **lowest-number pending** milestone (G1 before G2, …).
2. Read [docs/GAMEPLAY_DEMO_ACCEPTANCE.md](../../../docs/GAMEPLAY_DEMO_ACCEPTANCE.md) for that milestone’s acceptance.
3. Read [docs/CURSOR_BOT_BACKLOG.md](../../../docs/CURSOR_BOT_BACKLOG.md) Stream F table.
4. Implement **only that milestone**.
5. Update `HubGameProgress.json`: set milestone `status` to `"done"` or `"blocked"` with `notes` if stuck.
6. Set `demoReady: true` **only** when G8 passes and human criteria in GAMEPLAY_DEMO_ACCEPTANCE are met.

## Milestone routing

| ID | Edit primarily |
|----|----------------|
| G1 | `Assets/Scripts/Hub.asmdef`, folder layout |
| G2 | `MainScene.unity` or player prefab under `Assets/`, wire `EnvironmentAuthoringKit.Cave.PlayerController` |
| G3 | `Assets/Scripts/CombatStats.cs` — HP, damage, death events (EnvKit reflects this type) |
| G4 | Scene anchors, portal triggers, optional `SurfaceTrail` follower |
| G5 | `Assets/Scripts/CaveGoalTrigger.cs` or similar at route end |
| G6 | Ensure spawner + player weapon/melee hookup; test with one `HumanoidCombatSpawner` mob |
| G7 | `Assets/Scripts/DemoHud.cs` — OnGUI or uGUI canvas |
| G8 | Mark done after full smoke; set `demoReady: true` |

## EnvKit types to reuse (do not duplicate)

| Type | Package path |
|------|----------------|
| `PlayerController` | `Runtime/Cave/PlayerController.cs` |
| `PlayerCameraRig` | `Runtime/Cave/PlayerCameraRig.cs` |
| `HumanoidCombatSpawner` | `Runtime/Cave/HumanoidCombatSpawner.cs` |
| `WorldItemInventory` | `Runtime/World/WorldItemInventory` |
| `CavePlayerMovementGuard` | `Runtime/Cave/CavePlayerMovementGuard.cs` |

## SDK invocation

Phase 2 auto-selected when world gate passes:

```bash
./Tools/cursor-bot/run-session.sh --stream
```

Force gameplay phase:

```bash
./Tools/cursor-bot/run-session.sh --stream --workflow=gameplay
```

## Hard stops

- Do not start Full AAA Rebuild.
- Do not mark `demoReady: true` without G1–G7 done and G8 smoke described.
- If `CombatStats` compile breaks EnvKit reflection paths, fix compile first (G3).

## References

- [CURSOR_BOT_PHASED_MISSION.md](../../../docs/CURSOR_BOT_PHASED_MISSION.md)
- [AGENTS.md](../../../AGENTS.md)
- [REQUIREMENTS.md](../../../REQUIREMENTS.md) §2 vision (RO/Zelda dungeon exploration)
