# Bot production — 50 features (implemented)

Run the production loop:

```bash
export UNITY_PATH="/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity"
export CAVE_BOT_SUPERVISOR=1
./Tools/cursor-bot/run-until-demo.sh --stream
```

## Orchestration (1–10)

| # | Feature | Location |
|---|---------|----------|
| 1 | Dual-loop supervisor (agent + Unity multi-fix post-pass) | `until-demo.ts`, `CaveBuildBotProductionBatch.cs` |
| 2 | Rung budget per post-pass (`CAVE_RUNG_BUDGET`, default 5) | `CaveBuildBotProductionBatch.cs` |
| 3 | Stall detector (3× same rung/issue) | `bot-session-checkpoint.ts`, `bot-supervisor.ts` |
| 4 | Phase checkpoint JSON | `CaveBuildBotCheckpoint.json` |
| 5 | Parallel worktrees | Document-only — use git worktree manually |
| 6 | Time-boxed sessions (`CAVE_SESSION_TIMEOUT_MIN`) | `until-demo.ts` spawn timeout |
| 7 | Player-impact priority | Meat loop uses `FixPriority` + playbook escalation |
| 8 | Auto rollback on score drop (`CAVE_ROLLBACK_DROP`) | `bot-supervisor.ts` |
| 9 | Cap-aware iterations | `bot-production-config.ts` |
| 10 | Nightly vs interactive (`CAVE_BOT_MODE=nightly`) | `bot-production-config.ts` |

## Verification (11–20)

| # | Feature | Location |
|---|---------|----------|
| 11 | Play Mode smoke tests | `Assets/Tests/PlayMode/HubDemoSmokeTests.cs` |
| 12 | Route probe hard gate | `CaveBuildBotProductionBatch.cs` |
| 13 | Screenshot diff | Future — use Visual Shell Audit JSON |
| 14 | Performance tri budget | `CaveBuildBotProductionBatch.cs` (800k default) |
| 15 | Compile freshness SLA | `session-verify.ts` + Unity export |
| 16 | Scene save in post-pass | `CaveBuildBotProductionBatch.cs` |
| 17 | Milestone acceptance tests | PlayMode tests + `HubDemoSmokeExporter` |
| 18 | G8 human gate (`ready_for_human`) | `gameplay-prompt-ladder.ts` |
| 19 | Asset import regression | Re-run compile export after large pulls |
| 20 | Dual rubric demo 75 / ship 85 | `mission-phase.ts`, `CAVE_DEMO_WORLD_GATE=1` |

## Narrative (21–35)

| # | Feature | Location |
|---|---------|----------|
| 21 | Story bible JSON | `Assets/Narrative/HubStoryBible.json` |
| 22 | Three-act arc | `NarrativeActDirector.cs` |
| 23 | Diegetic walkie tutorial | `WalkieTalkieTutorial.cs` |
| 24 | Bark system | `NarrationBarkPlayer.cs`, `BarkDatabase.json` |
| 25 | Environmental inspectables | `InspectableStoryProp.cs` |
| 26 | Lumen companion drone | `LumenCompanionDrone.cs` |
| 27 | Branching micro-choices | `ChoiceGate.cs` |
| 28 | Hollow Titan myth variants | `HubStoryBible.json` |
| 29 | Combat narration | `CombatNarrationDirector.cs` |
| 30 | Demo epilogue stinger | `DemoEpilogueDirector.cs` |
| 31 | Localization CSV | `LocalizedStringTable.csv` |
| 32 | Narrative pacing by play time | `BarkDatabase.json` min/maxPlaySec |
| 33 | Distant NPC silhouette | Story bible note — place in scene |
| 34 | Audio log collectibles | `AudioLogPickup.cs` |
| 35 | Tone slider in bible | `HubStoryBible.json` tone field |

## Gameplay (36–45)

| # | Feature | Location |
|---|---------|----------|
| 36 | First-minute director | `FirstMinuteDirector.cs` |
| 37 | 3-hit combat tuning | `DemoCombatTuning.cs` |
| 38 | Fail-soft checkpoints | `CheckpointVolume.cs`, `VoidFallRecovery` |
| 39 | Objective HUD | `DemoObjectiveHud.cs` |
| 40 | Surface trail beacons | `BarkTrigger.cs` |
| 41 | Portal payoff VFX | `PortalPayoffVfx.cs` |
| 42 | Enemy telegraph | `EnemyTelegraph.cs` |
| 43 | Tutorial pickup | `TutorialPickup.cs` |
| 44 | Golden demo seed | `Assets/EnvironmentKit/Recipes/golden-demo-seed.json` |
| 45 | Demo build profile | `HubDemoProfile.cs`, `HUB_DEMO` define |

## Observability (46–50)

| # | Feature | Location |
|---|---------|----------|
| 46 | Session dashboard | `Logs/bot-session-summary.md`, `Logs/bot-sessions/` |
| 47 | Slack/webhook | `CAVE_BOT_WEBHOOK_URL` |
| 48 | Token budget | `CAVE_DAILY_TOKEN_BUDGET` |
| 49 | Fix attribution | Playbooks + `// [bot:rung:…]` convention |
| 50 | Playbook library | `Tools/cursor-bot/playbooks/`, `bot-playbooks/index.ts` |
