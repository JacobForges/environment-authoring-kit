# Hub — Agent operating guide

Unity 6 project for **Environment Authoring Kit**: procedural Florida karst surface + lava-tube cave worlds with graded, partially automated builds. Agents climb **Phase 1 world ladder → Phase 2 playable demo → Phase 3 polish**.

## Read first (mandatory)

1. [docs/PUBLIC_REPO_SCOPE.md](docs/PUBLIC_REPO_SCOPE.md) — what is committed vs local-only.
2. [docs/CURSOR_BOT_PHASED_MISSION.md](docs/CURSOR_BOT_PHASED_MISSION.md) — which phase is active.
3. `Assets/EnvironmentKit/Generated/CaveBuildGeneratedJsonManifest.json` — then reports it lists.
4. `Assets/EnvironmentKit/Generated/CaveBuildDoNotPrompt.md` and `CaveBuildNextStepsPrompt.md` — hard constraints (Phase 1).
5. [docs/CURSOR_BOT_BACKLOG.md](docs/CURSOR_BOT_BACKLOG.md) — prioritized work streams.

## Golden rules

- **One rung / one milestone per session.** Phase 1: active ladder rung. Phase 2: one G1–G8 milestone. Smallest correct diff.
- **Never kick off Full AAA Rebuild** unless the human explicitly asked.
- **JSON on disk is truth.** Phase 1: `Assets/EnvironmentKit/Generated/`. Phase 2: `Assets/Scripts/HubGameProgress.json`.
- **Unity stays responsive.** No blocking Node during active Hub builds.
- **Bot order (editor, paced):** Surface trails → cave mouth → underground route.
- **No secrets in git.** API keys in `Packages/.../cave-grader/.env` (gitignored).

## Project layout

| Path | Role |
|------|------|
| `Packages/com.cursor.environment-authoring-kit/` | UPM package — editor pipeline, grading, Hub |
| `Assets/Scripts/` | Consumer gameplay (Phase 2) + `HubGameProgress.json` |
| `Assets/EnvironmentKit/Generated/` | Per-build output (gitignored; read locally) |
| `Packages/.../Tools/cave-grader/` | Cursor SDK grader |
| `Tools/cursor-bot/` | Phased mission orchestration |

## How to run one agent pass

```bash
cd /Users/jacob/Hub
./Tools/cursor-bot/run-session.sh --stream
```

**Run until playable demo** (production loop — multi-fix post-pass, stall detection, narrative milestones):

```bash
export UNITY_PATH="/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity"
./Tools/cursor-bot/run-until-demo.sh --stream
```

See [BOT_PRODUCTION_50.md](docs/BOT_PRODUCTION_50.md) for all 50 production features.

Auto-routes: **world** (post_build) until grade ≥ 85 + `buildAcceptable`, then **gameplay** milestones.

**Session verify:** `./run-session.sh` does **not** mark done until compile checks pass. If verified CS errors remain, exit code **4** — run compile_gate or fix errors, then retry. Set `UNITY_PATH` to refresh diagnostics via batchmode before the done flag.

Force a phase:

```bash
./Tools/cursor-bot/run-session.sh --stream --workflow=gameplay
./Tools/cursor-bot/run-session.sh --stream --workflow=post_build
```

Check phase:

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
node --import tsx mission-phase.ts
```

Unity side:

- **Window → Environment Kit → Hub** — monitor builds
- **Cave Build Grader** — re-grade after Phase 1 fixes
- **Play Mode** — verify Phase 2 milestones

## What “done” means

| Milestone | Acceptance |
|-----------|------------|
| **Phase 1 exit** | `CaveBuildQualityReport.json` → `buildAcceptable: true`, grade ≥ **85** |
| **Phase 2 exit** | `HubGameProgress.json` → `demoReady: true` — [GAMEPLAY_DEMO_ACCEPTANCE.md](docs/GAMEPLAY_DEMO_ACCEPTANCE.md) |
| **Ship-grade world** | Grade ≥ 95, NavMesh entrance→goal (EnvKit metric, separate from demo) |
| **Phase 3** | Polish / recap / perf (Stream E) |

## Skills

| Phase | Skill |
|-------|--------|
| 1 — World | [.cursor/skills/hub-game-completion/SKILL.md](.cursor/skills/hub-game-completion/SKILL.md) |
| 2 — Gameplay | [.cursor/skills/hub-gameplay-completion/SKILL.md](.cursor/skills/hub-gameplay-completion/SKILL.md) |

Architecture: [docs/CURSOR_BOT_ARCHITECTURE.md](docs/CURSOR_BOT_ARCHITECTURE.md).
