# Deep Train Academy — intro cinematic pipeline

Maps production techniques to **what runs today** vs **Phase 2 (in-engine)**.

## Technique map

| Technique | What it does | Our implementation |
|-----------|--------------|-------------------|
| **Frame blending** | Smooth pixel mix between frames | **120fps `xfade`** on 4 interp segments per gap (25→50→75→100%) |
| **Motion vector interpolation** | Hyper-accurate in-between frames | **`minterpolate` (MCI)** on each 1.4s interp segment |
| **Camera path smoothing** | Dolly along defined track | **Cosine-ease center dolly** — 1.2s hold + 2.0s push, zoom 1.0→1.010 |
| **Depth mapping** | Per-frame depth for parallax | **Phase 2** — Unity depth pass |
| **Render pass layering** | FG / BG / FX separate | **Phase 2** — Unity Recorder multi-pass |
| **Virtual production** | Match lighting from engine | **Phase 2** — in-engine scenes |
| **Temporal denoising** | Reduce flicker/grain | **`hqdn3d`** on final concat |

## Current pipeline (from 10 PNGs)

```
intro_01..10.png
    ↓
[Beat] 3.2s hold + gentle cosine dolly
    ↓
[Gap] 4× interp clips @ 1.4s each (25→50→75→100% blend + MCI)
      OR drop real clip: intro_clips/gap_01_02.mp4 … gap_09_10.mp4
    ↓
Concat → final grade (brightness + contrast + sharpen, NO vignette) → intro.mp4
```

**Look pass:** `brightness +0.04`, `contrast 1.10`, subtle center sharpen. No vignette.

## How to add real movie clips

| Step | Action |
|------|--------|
| 1 | Generate img2vid or Unity Recorder motion clips per story beat |
| 2 | Export silent MP4s to `Assets/StreamingAssets/DeepTrainAcademy/intro_clips/` |
| 3 | Name them `gap_01_02.mp4` through `gap_09_10.mp4` |
| 4 | Run **Build intro.mp4** — real clips replace synthetic interpolation per gap |

## Output spec

| | |
|--|--|
| Resolution | 1920×1080 |
| Frame rate | **72 fps** |
| Duration | **~82.4s** content (10×3.2s beats + 36×1.4s interp) |
| Codec | H.264 CRF 15, silent, full quality (no artificial size cap) |
| Path | `Assets/StreamingAssets/DeepTrainAcademy/intro.mp4` |

## Motion tuning

Edit in `build-intro-mp4.sh`:

- `SEG_S=1.4` — length of each interp segment (4 per gap)
- `HOLD_S` / `PUSH_S` — beat stability vs dolly length
- `ZOOM_END=1.010` — lower = calmer camera
