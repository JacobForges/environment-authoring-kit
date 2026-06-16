# Hub Cursor bot (orchestration)

Thin wrapper around the existing **cave-grader** Cursor SDK integration.

## Prerequisites

1. Unity 6 project open at `/Users/jacob/Hub` (for re-grade; SDK can edit C# without editor).
2. Node 18+ and `npm install` in `Packages/com.cursor.environment-authoring-kit/Tools/cave-grader`.
3. `CURSOR_API_KEY` in `Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/.env` (see `.env.example`).

## Pipeline audit (read-only — no agent, no world build)

Checks disk, JSON gates, compile diagnostics, route probes, gameplay progress, competition smoke (with `--unity`):

```bash
chmod +x Tools/cursor-bot/run-pipeline-audit.sh
./Tools/cursor-bot/run-pipeline-audit.sh          # Tier 0 — JSON only
export UNITY_PATH="/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity"
./Tools/cursor-bot/run-pipeline-audit.sh --unity  # Tier 2 — Unity probes (close editor first)
```

Report: `Assets/EnvironmentKit/Generated/HubBotPipelineAudit.json`

`run-session.sh` and `run-until-demo.sh` run the audit automatically before each agent pass (`CAVE_SKIP_PIPELINE_AUDIT=1` to disable).

## One agent session (auto-routes Phase 1 → 2)

```bash
chmod +x Tools/cursor-bot/run-session.sh Tools/cursor-bot/run-until-demo.sh
./Tools/cursor-bot/run-session.sh --stream
```

## Run until playable demo (recommended)

Loops agent sessions through **world gate → gameplay G1–G8** until `HubGameProgress.json` → `demoReady: true`:

```bash
export UNITY_PATH="/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity"
./Tools/cursor-bot/run-until-demo.sh --stream
```

Each iteration: agent fix → Unity post-pass (compile + scene fix + re-grade) → next session. Max sessions: `CAVE_UNTIL_DEMO_MAX` (default 64).

Prints `[cursor-bot] mission phase=world → workflow=post_build` (or `gameplay` when world gate passes).

Options pass through to `run-grade-and-fix.sh` (`--cloud`, `--local-only`, `--workflow=terrain|gameplay|post_build`).

Check phase:

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
node --import tsx mission-phase.ts
```

## Watch mode (during Unity builds)

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
npm run watch-grade
```

## Production playbooks (20)

Each bot session gets a **Hub bot setup** block plus one matching playbook from `Tools/cursor-bot/playbooks/`:

`MISSING_SPLINE`, `MOUTH_DEPTH_50M`, `SPARSE_BLOCK_TUNNEL`, `GEOMETRY_VOID`, `COMPILE_GATE`, `ROUTE_PROBE_FAIL`, `SURFACE_ROUTE_FAIL`, `PERF_TRI_BUDGET`, `LAYOUT_AUDIT_SEAMS`, `STALE_CHECKPOINT`, `PLANNER_FAST_DEMO`, `POST_BUILD_PLAYTHROUGH`, `TERRAIN_SEAM_NINETILE`, `NAVMESH_PARTIAL`, `PROP_FLOATERS`, `PREBUILD_GATE_BLOCK`, `DEMO_RECAP_COMPOSE`, `DISK_FULL_PACED`, `GAMEPLAY_MILESTONE`, `GAMEPLAY_SMOKE_FAIL`, `COMPETITION_SMOKE_FAIL`, `STEP_COUNTER_ETC`, `EXTERNAL_STORAGE`.

Playbook selection reads `CaveBuildActiveSessionConfig.json` when issue text has no trigger match (fast demo / caves-off sessions).

## Cursor Automation

Import draft from [automation-draft.json](./automation-draft.json) in the Automations editor. Set `gitConfig.repo` to your Hub remote if using cloud agents. Adjust cron to your timezone.

## Docs

- [docs/CURSOR_BOT_ARCHITECTURE.md](../../docs/CURSOR_BOT_ARCHITECTURE.md)
- [docs/CURSOR_BOT_BACKLOG.md](../../docs/CURSOR_BOT_BACKLOG.md)
- [docs/PLANNER_SESSION.md](../../docs/PLANNER_SESSION.md) — layout-first builds (AI optional)
- [docs/PIPELINE_TRUTH.md](../../docs/PIPELINE_TRUTH.md) — 122 steps, geo 1–15, meat at 65
- [AGENTS.md](../../AGENTS.md)

## License / Copyright

- Tooling in this folder is licensed under `Tools/LICENSE_JACOBFORGES_TOOL_NONCOMMERCIAL.md`.
- Copyright (c) JacobForges.
- The game/project code and content remain proprietary and protected.
