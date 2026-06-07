# Demo recap — how to make it look better

## Is FFmpeg the wrong tool?

**No.** FFmpeg is what most automated pipelines use for the final MP4 (YouTube, broadcast, AI recap tools). The problem is not FFmpeg — it is the **recipe**:

| What hurt your video | Fix |
|----------------------|-----|
| `scale=crop` + dolly zoom 14% | **Letterbox** full Scene view (`sceneFit: contain`), motion ≤3% |
| 60fps + `minterpolate` on thousands of PNGs | **30fps**, drop motion interpolation on long chunks |
| `framesPerSource: 5` + PIL zoom per frame | **1–2** repeats, or plain timelapse between slides |
| PIL sharpen + ffmpeg `eq` + `unsharp` | **One** grade pass |
| 25 beats, AI captions failed → duplicate text | **8–12** beats, fix `CURSOR_API_KEY`, subbeat-specific copy |
| Wrong green boxes | **Off** until vision runs on exact milestone PNGs |

PowerPoint-like ≠ abandon FFmpeg. It means **slides + fades + light Ken Burns**, assembled with FFmpeg `xfade` / `zoompan` (or a thin helper like [still-motion](https://pypi.org/project/still-motion/) that generates the filter graph for you).

## Three styles (pick one)

### A. Presentation (recommended next)

- Intro/outro cards (done — emerald c / b).
- Between acts: **short timelapse** (letterboxed, no zoom).
- Each milestone: **hold 6–8s** like a slide — title bar, caption bar, **full scene visible**.
- **Fade** between segments (`xfade=transition=fade` or `fadeblack`).
- **No** annotations until vision is verified.

Timeline flags:

```json
"recapMode": "presentation",
"sceneFit": "contain",
"cinematicCamera": false,
"outputFps": 30,
"framesPerSource": 1,
"showAnnotations": false,
"segmentXfadeSec": 0.5
```

### B. Documentary timelapse

- Faster timelapse, fewer holds (3–4s), lower thirds only.
- Subtle Ken Burns: `zoompan` with **≤5%** zoom over 5s, upscale source first (reduces jitter).

### C. Hybrid (portfolio)

- Presentation slides for **chapter changes** (Grid, Play disk, Mountains…).
- Timelapse only inside a chapter.
- 10–12 total segments → ~8–15 min video instead of 60+ min of holds.

## Content (captions) — separate from FFmpeg

Bad copy will ruin any tool. Before a long encode:

1. Run AI director with working API (`milestones` mode, concurrency 4).
2. Quality gate (`DemoRecapQuality.json`) — reject banned / duplicate lines.
3. Subbeats must use `SUBBEAT_COPY` in `demo-recap-captions.py`, not checkpoint text.

## Tools beyond raw FFmpeg

| Tool | Role |
|------|------|
| **FFmpeg** | Encode, `xfade`, `zoompan`, `eq`, concat — keep this |
| **Pillow** | Slide layout (you already have this) |
| **still-motion** (Python) | Ken Burns + slideshow + xfade without hand-writing filter graphs |
| **Remotion** | React-based motion graphics — heavier, best for branded templates |
| **DaVinci / Premiere** | Manual polish only if you abandon automation |

For your “no manual NLE” goal, stay on **FFmpeg + Pillow**; add **still-motion** only if Ken Burns gets painful to tune.

## Try the sample

```bash
python3 ~/Hub/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/preview-presentation-sample.py \
  ~/Hub/Library/EnvironmentKit/DemoCapture/20260602-174525
```

Output: `~/Desktop/DemoRecap-Card-Preview/PresentationStyleSample.mp4`

Compare to `CardsPreview.mp4` (cards only). If the sample feels right, we wire `recapMode: presentation` into `compose-smart-recap.py` for the full rebuild.

---

## Narration (Personal Voice)

Producer recaps use **macOS Personal Voice** + the **voice ladder** (not edge-tts for approved builds).

| Topic | Doc |
|-------|-----|
| Setup, word queue, breath cue, full-script sync | [PERSONAL_VOICE_NARRATION.md](PERSONAL_VOICE_NARRATION.md) |
| Per-helper tuning (spatialHarmony, autoEq25, …) | [VOICE_HELPERS.md](VOICE_HELPERS.md) |
| macOS permissions | [open-personal-voice-settings.md](open-personal-voice-settings.md) |
| Producer CLI + Unity menus | [DEMO_RECAP_PIPELINE.md](DEMO_RECAP_PIPELINE.md) |

**Config:** `~/Hub/Library/EnvironmentKit/DemoRecapApproved/ApprovedCards.json`  
**Test:** `python3 prepare-narrator-voice.py --test` in **Terminal.app**  
**Runtime:** Long test phrases with word queue often take **20–35+ minutes** — expected.
