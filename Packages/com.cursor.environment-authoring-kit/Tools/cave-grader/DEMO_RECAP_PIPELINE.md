# Demo recap pipeline (cave-grader)

Two recap paths:

| Path | Output | Narration |
|------|--------|-----------|
| **Producer** (recommended) | `DemoRecapPresentation.mp4` | Personal Voice + full script — [PERSONAL_VOICE_NARRATION.md](PERSONAL_VOICE_NARRATION.md) |
| **Legacy smart compose** | `DemoRecap.mp4` | Optional; milestone-driven |

---

## Unity (after Smart timelapse)

When recording stops, Environment Kit can run **`run-producer-recap.py`** automatically.

| Menu | What |
|------|------|
| **Window → Environment Kit → Rebuild Demo Recap From Last Capture** | Full producer encode |
| **Window → Environment Kit → Rebuild Producer Recap Preview (Desktop)** | ~2 min desktop preview |

**Brand assets** (copied into each new capture):

`~/Hub/Library/EnvironmentKit/DemoRecapApproved/`

- `ApprovedIntro.png`, `ApprovedOutro.png`, `ApprovedCards.json`, portrait

Requires **python3** on PATH. For OpenCV callout boxes:

```bash
python3 -m pip install opencv-python-headless numpy
```

Or install from `requirements-recap.txt`. Legacy fallback: `compose-smart-recap.py` → `DemoRecap.mp4` if producer fails.

**Personal Voice:** Unity’s embedded shell often cannot use Personal Voice. For narration tests, use Terminal — see `run-recap-terminal.sh`.

---

## Terminal (Cursor / CI)

```bash
cd ~/Hub/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
export HUB_ROOT=~/Hub

# Producer preview (Personal Voice — use Terminal.app)
bash run-recap-terminal.sh ~/Hub/Library/EnvironmentKit/DemoCapture/<timestamp> --preview

# Full producer encode
bash run-recap-terminal.sh ~/Hub/Library/EnvironmentKit/DemoCapture/<timestamp>

# Optional: same without Terminal wrapper (if Personal Voice already works in this shell)
python3 run-producer-recap.py ~/Hub/Library/EnvironmentKit/DemoCapture/<timestamp> --preview
python3 run-producer-recap.py --capture ~/Hub/Library/EnvironmentKit/DemoCapture/<timestamp>

# AI milestones + legacy compose
python3 run-demo-recap-pipeline.py \
  ~/Hub/Library/EnvironmentKit/DemoCapture/<timestamp> \
  --checkpoints 15 --subbeats 10 --rebuild-milestones
```

Flags (`run-producer-recap.py`):

| Flag | Meaning |
|------|---------|
| `--preview` | Short desktop sample (`~/Desktop/DemoRecap-Card-Preview/DirectorPreview.mp4`) |
| `--no-narrator` | Grade silent video only (does **not** finalize shared preview) |
| `--narration-only` | Reuse segments + graded video → Cursor script from captions → Personal Voice → mux |
| `--regen-narration-script` | Force new `DemoRecapFullNarration.json` |
| `--remux-only` | Mux existing `narration.wav` (only if script + wav already done) |
| `--no-cursor` | OpenCV boxes only (no API) |
| `--install-deps` | pip install opencv / edge-tts into current python3 |

**Finalize gate:** With `requireNarrationBeforeFinalize` (default in `ApprovedCards.json`), `DirectorPreview.mp4` is written only when **Cursor script + narration audio** succeed. Silent graded video stays in `<capture>/_presentation_compose_preview120/_final_video.mp4`.

**Recommended two-step (Terminal.app):**

```bash
# 1) Silent video (review pacing)
bash run-recap-terminal.sh <capture> --preview --no-narrator

# 2) Script (from captions + exact video length) + Personal Voice + finalize
bash run-recap-terminal.sh <capture> --preview --narration-only
```

Or one shot (same gate — will not open a silent “final” preview):

```bash
bash run-recap-terminal.sh <capture> --preview
```

Health check: `python3 recap-doctor.py <capture_folder>`

---

## Cursor API stack (milestones / grade)

1. **Milestones** (`demo-recap-pipeline.ts milestones`) — parallel agents (default ×4): caption + annotation box per PNG  
2. **Quality ladder** (`grade`) — rubric loop up to 3×  
3. **Compose** — producer uses `compose-presentation-recap.py`; legacy uses `compose-smart-recap.py`

Requires `CURSOR_API_KEY` in `Tools/cave-grader/.env` or Hub settings.

`CAVE_RECAP_CONCURRENCY=4` (max 8). Example: 12 milestones ≈ 3 waves × ~1–3 min → **~5–12 min** AI time.

---

## Producer timeline (typical)

- **15 checkpoints** (~12s holds) + **10 subbeats** (~5.5s) when using dense milestone grid  
- **fullScript** narration: one voice track; captions on screen only  
- **speech_first** sync: holds extend to fit speech (no speeding up voice)

Tune holds/FPS in capture `DemoRecapTimeline.json` (merged from `ApprovedCards.json` via `apply-approved-cards.py`).

Visual style notes: [RECAP_VIDEO_GUIDE.md](RECAP_VIDEO_GUIDE.md)

---

## Artifacts

| File | Role |
|------|------|
| `DemoRecapDirector.json` | AI captions |
| `DemoRecapVision.json` | AI regions |
| `DemoRecapFullNarration.json` | Full-script narration text (producer) |
| `DemoRecapQuality.json` | Caption quality ladder |
| `DemoRecapTimeline.json` | Compose spec |
| `DemoRecapPresentation.mp4` | Producer output |
| `DemoRecap.mp4` | Legacy smart output |
| `_narration/*.wav` | Intermediate narration (when retained) |

---

## Broadcast / motion settings (legacy smart path)

| Feature | Setting |
|---------|---------|
| True color | gamma 0.98, light vignette |
| Cinematic camera | `cinematicCamera: true` |
| Motion video | timelapse PNGs × `framesPerSource` |
| Presentation mode | `recapMode: presentation`, `sceneFit: contain` — see RECAP_VIDEO_GUIDE |
