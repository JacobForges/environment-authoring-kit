#!/usr/bin/env python3
"""
~45s sample: presentation style (letterboxed scene, no harsh zoom, fade between beats).
Compare to CardsPreview.mp4 — this shows how the middle of the recap could look.
"""
from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
from pathlib import Path

W = 1280
FPS = 30
# More source frames + longer segments = smoother motion in preview.
TL_MAX_FRAMES = 120
TL_MIN_SEC = 6.0


def load_compose():
    p = Path(__file__).resolve().parent / "compose-demo-recap.py"
    spec = importlib.util.spec_from_file_location("compose", p)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def load_visual():
    p = Path(__file__).resolve().parent / "demo-recap-visual-enhance.py"
    spec = importlib.util.spec_from_file_location("visual", p)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def load_captions():
    p = Path(__file__).resolve().parent / "demo-recap-captions.py"
    spec = importlib.util.spec_from_file_location("captions", p)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def find_ffmpeg() -> str:
    for c in ("ffmpeg", "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg"):
        try:
            subprocess.run([c, "-version"], capture_output=True, check=True)
            return c
        except (FileNotFoundError, subprocess.CalledProcessError):
            continue
    raise SystemExit("ffmpeg not found")


def png_to_clip(
    ffmpeg: str,
    png: Path,
    out: Path,
    sec: float,
    *,
    fade_in: float = 0.0,
    fade_out: float = 0.0,
) -> None:
    vf = f"fps={FPS}"
    if fade_in > 0:
        vf += f",fade=t=in:st=0:d={fade_in:.2f}"
    if fade_out > 0:
        st = max(0.0, sec - fade_out)
        vf += f",fade=t=out:st={st:.2f}:d={fade_out:.2f}"
    subprocess.run(
        [
            ffmpeg,
            "-y",
            "-loop",
            "1",
            "-t",
            str(sec),
            "-i",
            str(png),
            "-vf",
            vf,
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            "-crf",
            "18",
            str(out),
        ],
        check=True,
        capture_output=True,
    )


def scene_vf_base() -> str:
    """Letterbox + deeper earth tones (less blown sand, richer shadows)."""
    return (
        "scale=2560:1440:force_original_aspect_ratio=decrease:flags=lanczos,"
        "pad=2560:1440:(ow-iw)/2:(oh-ih)/2:color=0x0E1218,"
        "scale=1280:720:flags=lanczos,"
        "eq=gamma=1.07:contrast=0.96:saturation=1.20:brightness=-0.07,"
        "colorbalance=rs=-0.03:gs=0.05:bs=0.08:rm=0.0:gm=0.02:bm=0.04,"
        "hue=s=1.04"
    )


def _symlink_seq(seq: Path, picked: list[Path]) -> None:
    seq.mkdir(parents=True, exist_ok=True)
    for f in seq.glob("f_*.png"):
        f.unlink()
    for i, src in enumerate(picked):
        dst = seq / f"f_{i:06d}.png"
        if dst.exists() or dst.is_symlink():
            dst.unlink()
        dst.symlink_to(src.resolve())


def bridge_after_intro(ffmpeg: str, frames: list[Path], out: Path, sec: float = 4.5) -> None:
    """Blend the first N captures so the open eases in without a single-frame pop."""
    n_src = min(24, len(frames))
    picked = frames[:n_src]
    work = out.parent / "_bridge_seq"
    seq = work / "seq"
    _symlink_seq(seq, picked)
    input_fps = max(4.0, n_src / max(sec, 1.0))
    subprocess.run(
        [
            ffmpeg,
            "-y",
            "-framerate",
            f"{input_fps:.3f}",
            "-start_number",
            "0",
            "-i",
            str(seq / "f_%06d.png"),
            "-vf",
            (
                f"{scene_vf_base()},"
                f"minterpolate=fps={FPS}:mi_mode=blend,"
                "fade=t=in:st=0:d=1.0,"
                f"fade=t=out:st={max(0.0, sec - 0.8):.2f}:d=0.8"
            ),
            "-t",
            str(sec),
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            "-crf",
            "17",
            str(out),
        ],
        check=True,
        capture_output=True,
    )


def timelapse_clip(ffmpeg: str, frames: list[Path], out: Path, sec: float | None = None) -> None:
    if len(frames) < 2:
        png_to_clip(ffmpeg, frames[0], out, sec or 4.0, fade_in=0.5)
        return
    work = out.parent / f"_tl_{out.stem}"
    seq = work / "seq"
    n_avail = len(frames)
    target = min(TL_MAX_FRAMES, n_avail)
    step = max(1, n_avail // target)
    picked = frames[::step]
    if len(picked) > TL_MAX_FRAMES:
        picked = picked[:TL_MAX_FRAMES]
    _symlink_seq(seq, picked)
    n = len(picked)
    # ~0.12s per source frame, longer clip when we have more frames.
    sec = sec if sec is not None else max(TL_MIN_SEC, n * 0.12)
    input_fps = max(5.0, min(18.0, n / sec))
    vf = (
        f"{scene_vf_base()},"
        f"minterpolate=fps={FPS}:mi_mode=blend:mc_mode=aobmc,"
        "fade=t=in:st=0:d=0.7,"
        f"fade=t=out:st={max(0.0, sec - 0.6):.2f}:d=0.6"
    )
    subprocess.run(
        [
            ffmpeg,
            "-y",
            "-framerate",
            f"{input_fps:.3f}",
            "-start_number",
            "0",
            "-i",
            str(seq / "f_%06d.png"),
            "-vf",
            vf,
            "-t",
            str(sec),
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            "-crf",
            "17",
            str(out),
        ],
        check=True,
        capture_output=True,
    )


def polish_final(ffmpeg: str, src: Path, out: Path) -> None:
    """Light unified grade on the concatenated preview."""
    subprocess.run(
        [
            ffmpeg,
            "-y",
            "-i",
            str(src),
            "-vf",
            "eq=gamma=1.02:contrast=1.0:saturation=1.05:brightness=-0.02",
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            "-crf",
            "17",
            str(out),
        ],
        check=True,
        capture_output=True,
    )


def xfade_concat(ffmpeg: str, clips: list[Path], out: Path, fade: float = 0.6) -> None:
    if len(clips) == 1:
        subprocess.run(["cp", str(clips[0]), str(out)], check=True)
        return
    # Chain pairwise xfade (fine for short previews).
    current = clips[0]
    work = out.parent / "_xfade"
    work.mkdir(exist_ok=True)
    for i, nxt in enumerate(clips[1:], start=1):
        merged = work / f"m{i:02d}.mp4"
        dur = float(
            subprocess.check_output(
                [
                    "ffprobe",
                    "-v",
                    "error",
                    "-show_entries",
                    "format=duration",
                    "-of",
                    "default=noprint_wrappers=1:nokey=1",
                    str(current),
                ],
                text=True,
            ).strip()
        )
        offset = max(0.1, dur - fade)
        subprocess.run(
            [
                ffmpeg,
                "-y",
                "-i",
                str(current),
                "-i",
                str(nxt),
                "-filter_complex",
                f"[0:v][1:v]xfade=transition=fade:duration={fade}:offset={offset:.3f},format=yuv420p[v]",
                "-map",
                "[v]",
                "-c:v",
                "libx264",
                "-crf",
                "18",
                str(merged),
            ],
            check=True,
            capture_output=True,
        )
        current = merged
    subprocess.run(["cp", str(current), str(out)], check=True)


def main() -> int:
    run = Path(sys.argv[1]).expanduser().resolve()
    compose = load_compose()
    captions = load_captions()
    visual = load_visual()
    ffmpeg = find_ffmpeg()

    spec = json.loads((run / "DemoRecapTimeline.json").read_text())
    milestones = spec.get("milestones", [])
    frames = sorted((run / "timelapse").glob("tl_*.png"))
    if not frames:
        raise SystemExit("No timelapse frames")

    # Four spaced beats — not all 25.
    picks = [0, len(milestones) // 4, len(milestones) // 2, (3 * len(milestones)) // 4]
    picks = sorted(set(min(i, len(milestones) - 1) for i in picks))

    desktop = Path.home() / "Desktop" / "DemoRecap-Card-Preview"
    desktop.mkdir(parents=True, exist_ok=True)
    work = run / "_presentation_preview"
    work.mkdir(exist_ok=True)
    clips: list[Path] = []

    png_to_clip(
        ffmpeg,
        run / "ApprovedIntro.png",
        work / "c00.mp4",
        5.0,
        fade_out=0.9,
    )
    clips.append(work / "c00.mp4")

    bridge_after_intro(ffmpeg, frames, work / "bridge.mp4", 4.5)
    clips.append(work / "bridge.mp4")

    prev_f = 0
    for pi, mi in enumerate(picks):
        m = dict(milestones[mi])
        captions.fill_milestone_captions(m)
        fidx = min(int(m.get("frame", 0)), len(frames) - 1)
        # Skip timelapse right after intro — early frames jump too much and feel choppy.
        if pi > 0 and fidx > prev_f + 8:
            timelapse_clip(ffmpeg, frames[prev_f:fidx], work / f"tl{pi}.mp4")
            clips.append(work / f"tl{pi}.mp4")

        img = visual.true_color_grade(compose.Image.open(frames[fidx]).convert("RGB"))
        slide = compose.render_captioned_frame(
            img,
            line1=m.get("line1", ""),
            line2=m.get("line2", ""),
            line3=m.get("line3"),
            chapter=str(m.get("chapter", ""))[:52],
            index=mi,
            total=len(milestones),
            accent=tuple(m.get("accent", [24, 235, 158])),
            milestone=m,
            annotate=False,
            annotation_alpha=0.0,
            line1_alpha=1.0,
            line2_alpha=1.0,
            line3_alpha=1.0,
            lecture_mode=True,
            scene_fit="contain",
            cinematic_t=None,
        )
        sp = work / f"slide_{pi}.png"
        slide.save(sp)
        png_to_clip(ffmpeg, sp, work / f"c{pi+1:02d}.mp4", 5.0)
        clips.append(work / f"c{pi+1:02d}.mp4")
        prev_f = fidx

    png_to_clip(ffmpeg, run / "ApprovedOutro.png", work / "c99.mp4", 5.0)
    clips.append(work / "c99.mp4")

    raw = desktop / "PresentationStyleSample_raw.mp4"
    out = desktop / "PresentationStyleSample.mp4"
    xfade_concat(ffmpeg, clips, raw, fade=0.85)
    polish_final(ffmpeg, raw, out)
    raw.unlink(missing_ok=True)

    print(out)
    print(f"Timelapse uses up to {TL_MAX_FRAMES} frames per segment with blend interpolation.")
    if sys.platform == "darwin":
        subprocess.run(["open", str(out)], check=False)
        subprocess.run(["open", str(desktop)], check=False)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
