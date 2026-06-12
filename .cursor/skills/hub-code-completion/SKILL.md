---
name: hub-code-completion
description: >-
  Hub Code Bot — finish Assets/Scripts wiring (competition, challenge, multiplayer,
  demo). Use with Tools/code-bot. Never edits world generation pipeline.
---

# Hub code completion (Code Bot)

## When to use

- User runs `./Tools/code-bot/run-session.sh` or `run-until-complete.sh`
- `Assets/Scripts/HubCodeProgress.json` has pending tasks or `codeReady: false`
- Wiring, integration, compile fixes in consumer gameplay code

**Not for:** terrain/cave builds → use **hub-game-completion** + `Tools/cursor-bot/`.

## Scope

**One task** per session from `HubCodeProgress.json` (`CODE_TASK_ORDER` in `Tools/code-bot/code-progress.ts`).

Edit only:
- `Assets/Scripts/**`
- `Assets/Scripts/Editor/**` (setup menus, smoke exporters)

**Forbidden:**
- `Packages/com.cursor.environment-authoring-kit/Editor/Blockout/`
- Full AAA Rebuild / paced world builds
- Hand-editing large `.unity` YAML — use `Game → Setup …` editor scripts

## Session spine

1. Read `Assets/Scripts/HubCodeProgress.json` — lowest pending task in order.
2. Read task `acceptance` + `files` hints.
3. Trace runtime: bootstrap → title menu → spawn → HUD → commands.
4. Implement smallest correct diff.
5. Update task `status`: `done` or `blocked` + `notes`.
6. Set `codeReady: true` only when **all** tasks are `done` and compile is clean.

## Key integration points

| System | Entry types |
|--------|-------------|
| Demo | `MainSceneGameplaySetup`, `HubGameProgress.json` G1–G8 |
| Competition | `CompetitionBootstrap`, `CompetitionTitleMenuBridge`, `AgentAiStack` |
| Chat | `AgentCommandParser`, `CompetitionChatBootstrap` |
| Combat / death | `AgentVitalityDeathBridge`, `AgentDeathHandler`, `CompetitionAgentCombat` |
| Lineage | `AgentEcosystemService`, `AgentEcosystemShrineUi` |
| Challenge | `ChallengeBootstrap`, `ChallengeArenaController`, `ChallengeChatBridge` |
| Multiplayer | `MainScenePortfolioMenuSetup`, `CompetitionAgentNetworkPawn` |
| Training | `CompetitionAdapterTrainRunner`, `AgentTrainReportUi` |

## Verify

```bash
export UNITY_PATH="…"
./Tools/competition-models/run-hub-smoke.sh
```

Or Unity batch compile export via `session-verify` (code bot runs this after each session).

## SDK

```bash
./Tools/code-bot/run-session.sh --stream
./Tools/code-bot/run-until-complete.sh --stream
```

## References

- `Tools/code-bot/README.md`
- `Assets/Scripts/HubCodeProgress.json`
- `docs/GAMEPLAY_DEMO_ACCEPTANCE.md`
