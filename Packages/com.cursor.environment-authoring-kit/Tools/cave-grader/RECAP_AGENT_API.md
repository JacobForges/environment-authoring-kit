# Recap Agent API

Local HTTP API on **http://127.0.0.1:8765** for the recap dashboard and Cursor agents.
Start with:

```bash
cd ~/Hub/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
bash start-recap-dashboard.sh /path/to/DemoCapture/<timestamp>
```

Unity also starts this automatically when recording stops (unless **Skip recap review** is on).

## Rules

- **`videoPlaybackFactor` must stay `1.0`** — PATCH/PUT reject other values (Personal Voice sync).
- **Personal Voice narration** always runs in **Terminal.app** via `run-recap-terminal.sh`.
- Compose preview / narration-only routes spawn the same shell scripts the dashboard buttons use.

## Health & status

```bash
curl -s http://127.0.0.1:8765/api/health | python3 -m json.tool
curl -s 'http://127.0.0.1:8765/api/capture/status?path=/Volumes/Lexar/EnvironmentKit-Hub/DemoCapture/20260605-224357'
curl -s http://127.0.0.1:8765/api/agent/status
```

## Timeline

```bash
# Read
curl -s 'http://127.0.0.1:8765/api/capture/timeline?path=/path/to/capture'

# Full replace (PUT)
curl -s -X PUT 'http://127.0.0.1:8765/api/capture/timeline?path=/path/to/capture' \
  -H 'Content-Type: application/json' \
  -d @DemoRecapTimeline.json

# Partial merge (PATCH) — preferred for agents
curl -s -X PATCH 'http://127.0.0.1:8765/api/capture/timeline?path=/path/to/capture' \
  -H 'Content-Type: application/json' \
  -d '{"milestoneHoldSec": 4.2, "subbeatHoldSec": 1.8}'
```

## Compose triggers

```bash
# Silent preview (headless --no-narrator)
curl -s -X POST http://127.0.0.1:8765/api/capture/compose/preview \
  -H 'Content-Type: application/json' \
  -d '{"capture":"/path/to/capture"}'

# Narration-only (Terminal Personal Voice)
curl -s -X POST http://127.0.0.1:8765/api/capture/compose/narration-only \
  -H 'Content-Type: application/json' \
  -d '{"capture":"/path/to/capture"}'

# Generic (legacy)
curl -s -X POST http://127.0.0.1:8765/api/compose \
  -H 'Content-Type: application/json' \
  -d '{"capture":"/path/to/capture","preview":true,"noNarrator":true}'
```

Poll job state via `GET /api/capture/status` → `composeJob`.

## Artifacts

```bash
curl -s 'http://127.0.0.1:8765/api/capture/artifacts?path=/path/to/capture' | python3 -m json.tool
```

Returns paths for timeline, narration, OpenCV JSON, director JSON, silent shell, presentation MP4, plus ffprobe durations.

## Unity compose gate

When recording stops, Unity registers a gate and polls until the user proceeds:

```bash
# Unity registers (automatic)
curl -s -X POST 'http://127.0.0.1:8765/api/capture/compose/gate?path=/path/to/capture' \
  -H 'Content-Type: application/json' \
  -d '{"waiting":true}'

# Poll
curl -s 'http://127.0.0.1:8765/api/capture/compose/gate?path=/path/to/capture'

# User / agent proceeds → Unity starts compose
curl -s -X POST 'http://127.0.0.1:8765/api/capture/compose/gate/proceed?path=/path/to/capture' \
  -H 'Content-Type: application/json' \
  -d '{}'
```

Hub toggle **Skip recap review** bypasses the gate in Unity without calling the API.

## Agent ping & commands

Agents should ping periodically so the dashboard shows **Agent connected**:

```bash
curl -s -X POST http://127.0.0.1:8765/api/agent/ping \
  -H 'Content-Type: application/json' \
  -d '{"agent":"cursor","note":"tuning recap pacing"}'
```

Structured command router (single endpoint for Cursor chat agents):

```bash
curl -s -X POST http://127.0.0.1:8765/api/agent/command \
  -H 'Content-Type: application/json' \
  -d '{
    "agent": "cursor",
    "command": "patch_timeline",
    "capture": "/Volumes/Lexar/EnvironmentKit-Hub/DemoCapture/20260605-224357",
    "params": {"milestoneHoldSec": 4.0}
  }'
```

| `command` | `params` | Effect |
|-----------|----------|--------|
| `ping` | — | Updates agent heartbeat |
| `get_status` | — | Capture status snapshot |
| `get_timeline` | — | Full `DemoRecapTimeline.json` |
| `get_artifacts` | — | Paths + ffprobe + OpenCV summary |
| `patch_timeline` | pacing fields | PATCH merge into timeline |
| `save_timeline` | full spec | PUT replace timeline |
| `compose_preview` | — | Silent preview compose |
| `compose_narration_only` | — | Terminal Personal Voice pass |
| `proceed_compose` | — | Clear Unity gate |
| `get_gate` | — | Current gate state |

## How Cursor agents should use this

When the user asks to edit a recap while the dashboard server is running:

1. `GET /api/capture/artifacts` — discover MP4 paths and milestone count.
2. `GET /api/capture/timeline` — read pacing.
3. `PATCH /api/capture/timeline` — adjust holds (never change `videoPlaybackFactor`).
4. `POST /api/capture/compose/preview` — rebuild silent shell if needed.
5. `POST /api/capture/compose/narration-only` — remux Personal Voice in Terminal.
6. `POST /api/capture/compose/gate/proceed` — if Unity is waiting on review.

Ping on session start so the React UI shows agent activity.

See also: `RECAP_PIPELINE.md`, `recap-dashboard/README.md`.
