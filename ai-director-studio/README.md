# AI Director Studio

Standalone app for turning **your own video and images** into narrated recap films — using the same AI Director stack from Environment Kit (brief Q&A, compose, voice persona, script studio, export) **without Unity or Hub**.

## What it is

AI Director Studio is a local web app + Python API that:

1. **Ingests media** — drop a screen recording or image sequence; ffmpeg extracts `timelapse/tl_*.png` frames.
2. **Runs the Director brief** — chat with the AI to shape tone, audience, and pacing (Cursor API).
3. **Composes a silent film** — segments, crossfades, captions via the vendored recap compose pipeline.
4. **Adds narration** — script review + Personal Voice / edge-tts path from the original director stack.
5. **Exports** — preview MP4, segment downloads, YouTube metadata.

The code repo is **small** (UI + server). Your **projects** (frames, timelines, rendered MP4s) live under `DIRECTOR_DATA_ROOT` — not in git.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Browser  →  ai-director/ React UI  (Media, Preview, …)   │
└───────────────────────────┬─────────────────────────────────┘
                            │ HTTP :8767
┌───────────────────────────▼─────────────────────────────────┐
│  ai-director-server.py  +  recap-dashboard-server.py (base) │
│    /api/project/*     ingest, create                         │
│    /api/director/*    brief, chat, voice, script             │
│    /api/capture/*     timeline, compose jobs                 │
└───────────────────────────┬─────────────────────────────────┘
                            │
        ┌───────────────────┼───────────────────┐
        ▼                   ▼                   ▼
  ingest_media.py    director_session.py   compose-presentation-recap.py
  generic_timeline   build_planner (LLM)   run-producer-recap.py
                     planner-llm.ts         demo-recap-pipeline.ts
```

`STANDALONE=1` (set by `start.sh`) skips Unity compose gates — card verification auto-passes so you can compose after ingest.

## Install

**Prerequisites:** Python 3.10+, Node 18+, [ffmpeg](https://ffmpeg.org/) (`brew install ffmpeg` on macOS).

```bash
cd ai-director-studio

# Python deps (venv — macOS has no bare `pip` on PATH)
python3 -m venv venv
source venv/bin/activate
python3 -m pip install -r requirements.txt

# Node deps (Cursor SDK + tsx for LLM scripts)
npm install
cd ai-director && npm install && cd ..

# Secrets — copy the example file, then edit .env (do NOT `cp` to CURSOR_API_KEY)
cp .env.example .env
# Open .env and set:  CURSOR_API_KEY=sk-...   (required for AI features)
```

`start.sh` creates `venv/` automatically on first launch if you skip the manual venv step above.

## Launch

```bash
bash start.sh   # use bash, not zsh — script uses bash-isms
```

Opens `http://127.0.0.1:8767/` on macOS. Options:

```bash
bash start.sh --no-open                    # don't open browser
bash start.sh ~/Library/AIDirectorStudio/projects/20250614-120000-my-recap
```

Dev UI (hot reload) while server runs:

```bash
cd ai-director && npm run dev   # http://127.0.0.1:5174 — proxy API to :8767 or open built dist
```

## Upload workflow

1. Open the **Media** tab.
2. Enter a project name → **New project** (creates a folder under `DIRECTOR_DATA_ROOT/projects/`).
3. Drag-drop a `.mp4`/`.mov` or images onto the drop zone.
4. Server writes frames to `{project}/timelapse/tl_*.png` and `DemoRecapTimeline.json`.
5. Switch to **Preview** → verify cards (auto in standalone) → **Start silent compose**.
6. Complete **Voice** + **Script** tabs → export narrated `DemoRecapPresentation.mp4`.

You can also copy files manually into a project folder before opening it in the UI.

## Data folder vs code repo

| Location | Contents |
|----------|----------|
| **This git repo** | Server, React UI, compose scripts |
| **`DIRECTOR_DATA_ROOT`** (default `~/Library/AIDirectorStudio`) | `projects/`, `approved/`, `.recap-tmp/`, server PID logs |

Set `DIRECTOR_DATA_ROOT` in `.env` to use an external drive. The repo stays portable; data stays local.

## How it reuses Environment Kit director code

~80% vendored from `cave-grader/`:

- **UI:** `ai-director/` React app (Briefing, Script Studio, tabs)
- **API:** `ai-director-server.py`, `recap-dashboard-server.py`, `director_session.py`
- **Compose:** `compose-presentation-recap.py`, `hybrid_recap_common.py`, `run-producer-recap.py`
- **AI:** `planner-llm.ts`, `demo-recap-pipeline.ts`, `demo-recap-cursor-director.py`

**New standalone pieces:**

- `server/director_paths.py` — data root + `projects/` (no `HUB_ROOT`/Unity)
- `server/ingest_media.py` — video → frames, images → `tl_*.png`
- `server/generic_timeline.py` — timeline for arbitrary media
- `server/build_planner.py` — slim LLM bridge (no Unity planner catalog)
- `POST /api/project/create`, `POST /api/project/ingest`

## License

See [LICENSE](LICENSE) (Environment Authoring Kit terms — educational/personal non-commercial; commercial use requires permission).

## Push as new GitHub repo

```bash
cd ai-director-studio
git init
git add .
git commit -m "Initial AI Director Studio scaffold"
git branch -M main
git remote add origin git@github.com:YOUR_USER/ai-director-studio.git
git push -u origin main
```

Do **not** commit `.env` or `projects/`.
