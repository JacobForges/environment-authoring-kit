# Cursor bot — phased mission (world → playable demo)

The Hub bot’s default job is **not** “one ladder rung forever.” It follows three phases until a **playable demo** exists.

## Phase routing (automatic)

`./Tools/cursor-bot/run-session.sh` picks the phase unless you pass `--workflow=post_build|gameplay|terrain|pre_build`.

| Phase | When active | Bot fixes |
|-------|-------------|-----------|
| **1 — World pipeline** | `CaveBuildQualityReport.json` missing, `buildAcceptable: false`, or `overallScore` < **85** | Active cave/surface grading rung (Streams A–B) |
| **2 — Gameplay demo** | World gate passed **and** `Assets/Scripts/HubGameProgress.json` → `demoReady: false` | One gameplay milestone G1–G8 (Stream F) |
| **3 — Polish** | `demoReady: true` | Stream E (perf, recap, art) — human-directed |

Check current phase:

```bash
cd /Users/jacob/Hub/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
node --import tsx mission-phase.ts
```

## Human gates (unchanged)

Agents still **must not** start **Full AAA Rebuild** or other destructive full builds unless you explicitly ask in chat.

After every bot pass (single session):

1. Unity recompiles (if C# changed)
2. **Re-grade** (Cave Build Grader) for Phase 1, or **Play Mode smoke test** for Phase 2
3. Run `./Tools/cursor-bot/run-session.sh --stream` again

**Or use the loop** (no manual restart):

```bash
export UNITY_PATH="/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity"
./Tools/cursor-bot/run-until-demo.sh --stream
```

## Playable demo definition

See [GAMEPLAY_DEMO_ACCEPTANCE.md](./GAMEPLAY_DEMO_ACCEPTANCE.md).

Summary: spawn on surface → walk trail → enter cave portal → reach goal marker → survive one combat encounter → no cheats.

## Skills

| Phase | Skill |
|-------|--------|
| 1 | [.cursor/skills/hub-game-completion/SKILL.md](../.cursor/skills/hub-game-completion/SKILL.md) |
| 2 | [.cursor/skills/hub-gameplay-completion/SKILL.md](../.cursor/skills/hub-gameplay-completion/SKILL.md) |

## Progress files

| File | Role |
|------|------|
| `Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json` | World gate (Phase 1 exit) |
| `Assets/Scripts/HubGameProgress.json` | Gameplay milestones (Phase 2 exit) |
| [docs/CURSOR_BOT_BACKLOG.md](./CURSOR_BOT_BACKLOG.md) | Full task list |
