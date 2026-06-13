# Hub Code Bot (wiring & completion)

**Separate from the world bot** (`Tools/cursor-bot/`). This bot finishes **code integration** in `Assets/Scripts/` — spawn flows, chat commands, combat, lineage, challenge, multiplayer, training UI.

It does **not** grade terrain, rebuild caves, or edit `Packages/.../Editor/Blockout/`.

## Prerequisites

Same as world bot: Node 18+, `npm install` in cave-grader, `CURSOR_API_KEY` in `Packages/.../cave-grader/.env`.

## Progress file

`Assets/Scripts/HubCodeProgress.json` — task list + `codeReady` gate.

## One session

```bash
chmod +x Tools/code-bot/run-session.sh Tools/code-bot/run-until-complete.sh
./Tools/code-bot/run-session.sh --stream
```

Force a task:

```bash
./Tools/code-bot/run-session.sh --stream --task=comp_chat_commands
```

## Loop until done

```bash
export UNITY_PATH="/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity"
./Tools/code-bot/run-until-complete.sh --stream
```

Each iteration runs **Unity post-pass** (compile + `CompetitionSmoke` + auto-mark verified tasks in `HubCodeProgress.json`), then one agent session.

Editor menu: **Game → Code Bot → Run Post Pass**

Report: `Assets/EnvironmentKit/Generated/CodeBotPostPassReport.json`

### macOS batchmode (`Abort trap: 6`)

Unity 6 **Bee IPC** often aborts in `-batchmode` on recent macOS (even with the editor closed). This is a Unity/platform issue, not your project.

- **`./Tools/code-bot/run-post-pass.sh`** skips batch on macOS by default; use **Game → Code Bot → Run Post Pass** in the editor for full smoke.
- Force batch: `CODE_BOT_FORCE_UNITY_BATCH=1 ./Tools/code-bot/run-post-pass.sh`

## World bot vs code bot

| Bot | Path | Fixes |
|-----|------|-------|
| **World** | `Tools/cursor-bot/` | Cave/surface pipeline, grades, route probes |
| **Code** | `Tools/code-bot/` | Gameplay + competition + challenge wiring |

Skill: `.cursor/skills/hub-code-completion/SKILL.md`
