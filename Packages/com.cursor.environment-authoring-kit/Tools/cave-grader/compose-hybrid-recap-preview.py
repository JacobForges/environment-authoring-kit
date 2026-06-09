#!/usr/bin/env python3
"""~90s concept preview: screen B-roll at chapter boundaries + 3s PNG holds + optional narration."""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
W, H, FPS = 1920, 1080, 30
CRF, PRESET = 20, "veryfast"

# Chapter-boundary screen clips (additive B-roll) from long Desktop recording
SCREEN_CLIPS = [
    {"label": "session_start", "start": 2.0, "end": 14.0},
    {"label": "grid_build", "start": 58.0, "end": 70.0},
    {"label": "mid_terraform", "start": 118.0, "end": 130.0},
    {"label": "late_polish", "start": 198.0, "end": 210.0},
]

PREVIEW_NARRATION = [
    "This is the hybrid recap preview — your screen recording, cut at chapter boundaries.",
    "Unity keeps capturing timelapse stills while the world generates.",
    "Here the flat grid lands — then we hold a milestone frame for three seconds.",
    "Back to live build footage while terrain and props queue in the background.",
    "Still shots stay additive — they do not replace your milestone PNGs.",
    "Personal Voice narration rides on top — screen audio stays muted.",
    "The full deliverable targets ten minutes twelve seconds — this is a short taste.",
]


def find_ffmpeg() -> str:
    for c in ("ffmpeg", "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg"):
        try:
            subprocess.run([c, "-version"], capture_output=True, check=True)
            return c
        except (FileNotFoundError, subprocess.CalledProcessError):
            continue
    raise SystemExit("ffmpeg not found")


def pick_milestone_frames(tl_dir: Path, count: int = 5) -> list[Path]:
    frames = sorted(tl_dir.glob("tl_*.png"))
    if not frames:
        raise SystemExit(f"No timelapse PNGs in {tl_dir}")
    if len(frames) <= count:
        return frames
    idx = [int(i * (len(frames) - 1) / (count - 1)) for i in range(count)]
    return [frames[i] for i in idx]


def encode_args() -> list[str]:
    return [
        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", str(CRF),
        "-preset", PRESET, "-tune", "film", "-r", str(FPS),
    ]


def vf_canvas(caption: str = "") -> str:
    return (
        f"scale={W}:{H}:force_original_aspect_ratio=decrease,"
        f"pad={W}:{H}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1"
    )


def png_segment(ffmpeg: str, png: Path, out: Path, sec: float, caption: str = "") -> None:
    subprocess.run(
        [
            ffmpeg, "-y", "-loop", "1", "-framerate", str(FPS), "-i", str(png),
            "-vf", vf_canvas(caption), "-t", f"{sec:.3f}", *encode_args(),
            "-an", str(out),
        ],
        check=True,
        capture_output=True,
    )


def screen_segment(ffmpeg: str, mov: Path, out: Path, start: float, end: float, caption: str = "") -> None:
    dur = max(0.5, end - start)
    subprocess.run(
        [
            ffmpeg, "-y", "-ss", f"{start:.3f}", "-t", f"{dur:.3f}",
            "-i", str(mov), "-vf", vf_canvas(caption), *encode_args(),
            "-an", str(out),
        ],
        check=True,
        capture_output=True,
    )


def timelapse_segment(ffmpeg: str, frames: list[Path], out: Path, sec: float) -> None:
    if len(frames) < 2:
        png_segment(ffmpeg, frames[0], out, sec)
        return
    w = out.parent / "_tl_work"
    w.mkdir(exist_ok=True)
    seq = w / "seq"
    if seq.exists():
        for p in seq.glob("f_*.png"):
            p.unlink()
    else:
        seq.mkdir()
    for i, fr in enumerate(frames):
        dst = seq / f"f_{i + 1:06d}.png"
        if dst.exists() or dst.is_symlink():
            dst.unlink()
        dst.symlink_to(fr.resolve())
    rate = max(4.0, len(frames) / sec)
    subprocess.run(
        [
            ffmpeg, "-y", "-framerate", f"{rate:.3f}", "-i", str(seq / "f_%06d.png"),
            "-vf", f"{vf_canvas()},fade=t=in:st=0:d=0.4,fade=t=out:st={max(0, sec - 0.5):.2f}:d=0.5",
            "-t", f"{sec:.3f}", *encode_args(), "-an", str(out),
        ],
        check=True,
        capture_output=True,
    )


def concat_segments(ffmpeg: str, parts: list[Path], out: Path) -> None:
    lst = out.parent / "_concat_list.txt"
    lst.write_text("\n".join(f"file '{p.resolve()}'" for p in parts) + "\n")
    subprocess.run(
        [ffmpeg, "-y", "-f", "concat", "-safe", "0", "-i", str(lst), "-c", "copy", str(out)],
        check=True,
        capture_output=True,
    )


def segment_to_gif(ffmpeg: str, mp4: Path, gif: Path, max_sec: float = 3.0) -> None:
    subprocess.run(
        [
            ffmpeg, "-y", "-t", f"{max_sec:.2f}", "-i", str(mp4),
            "-vf", f"fps=15,scale=960:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse",
            "-loop", "0", str(gif),
        ],
        check=True,
        capture_output=True,
    )


def try_mux_narration(ffmpeg: str, video: Path, wav: Path, out: Path) -> bool:
    if not wav.is_file() or wav.stat().st_size < 1024:
        return False
    subprocess.run(
        [
            ffmpeg, "-y", "-i", str(video), "-i", str(wav),
            "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
            "-shortest", str(out),
        ],
        check=True,
        capture_output=True,
    )
    return True


def synthesize_narration(capture: Path, work: Path) -> Path | None:
    """Personal Voice preview — skipped here; mux via run-recap-terminal after silent preview."""
    return None


def main() -> None:
    capture = Path(sys.argv[1]) if len(sys.argv) > 1 else None
    if not capture or not capture.is_dir():
        raise SystemExit(f"Usage: {Path(__file__).name} /path/to/DemoCapture/<timestamp>")

    movs = sorted(Path.home().glob("Desktop/Screen Recording 2026-06-07*.mov"), key=lambda p: p.stat().st_size, reverse=True)
    if not movs:
        raise SystemExit("No Screen Recording 2026-06-07*.mov on Desktop")
    mov = movs[0]

    tl = capture / "timelapse"
    milestones = pick_milestone_frames(tl, 5)
    ffmpeg = find_ffmpeg()

    out_dir = Path.home() / "Desktop" / "HybridRecap-ConceptPreview"
    work = out_dir / "_work"
    gif_dir = out_dir / "gifs"
    out_dir.mkdir(exist_ok=True)
    work.mkdir(exist_ok=True)
    gif_dir.mkdir(exist_ok=True)

    uploads = capture / "uploads"
    uploads.mkdir(exist_ok=True)
    linked = uploads / "build-screen.mov"
    if not linked.exists():
        linked.symlink_to(mov.resolve())

    parts: list[Path] = []
    gif_sources: list[tuple[str, Path]] = []

    title = work / "seg_00_title.png.mp4"
    png_segment(ffmpeg, milestones[0], title, 3.0, "Hybrid recap concept preview")
    parts.append(title)
    gif_sources.append(("01_title_still", title))

    for i, clip in enumerate(SCREEN_CLIPS):
        sc = work / f"seg_{i + 1:02d}_screen_{clip['label']}.mp4"
        screen_segment(ffmpeg, mov, sc, clip["start"], clip["end"], f"B-roll · {clip['label'].replace('_', ' ')}")
        parts.append(sc)
        gif_sources.append((f"{i + 2:02d}_screen_{clip['label']}", sc))

        still = work / f"seg_{i + 1:02d}_hold_{clip['label']}.mp4"
        png = milestones[min(i + 1, len(milestones) - 1)]
        png_segment(ffmpeg, png, still, 3.0, f"Milestone still · 3s")
        parts.append(still)
        gif_sources.append((f"{i + 2:02d}b_still_{clip['label']}", still))

        if i == 1:
            bridge = work / "seg_bridge_timelapse.mp4"
            mid = milestones[1:4]
            timelapse_segment(ffmpeg, mid, bridge, 8.0)
            parts.append(bridge)

    silent = out_dir / "HybridRecap-ConceptPreview-SILENT.mp4"
    concat_segments(ffmpeg, parts, silent)

    wav = synthesize_narration(capture, work)
    final = out_dir / "HybridRecap-ConceptPreview.mp4"
    if wav and try_mux_narration(ffmpeg, silent, wav, final):
        playable = final
    else:
        playable = silent
        final = silent

    for name, seg in gif_sources[:6]:
        segment_to_gif(ffmpeg, seg, gif_dir / f"{name}.gif", 3.0)

    meta = {
        "capture": str(capture),
        "screenRecording": str(mov),
        "silentMp4": str(silent),
        "finalMp4": str(playable),
        "gifFolder": str(gif_dir),
        "narrationUsed": wav is not None and playable != silent,
        "targetFullDurationSec": 612,
        "previewNote": "Additive B-roll + 3s PNG holds; full build targets 10:12 with Personal Voice.",
    }
    (out_dir / "preview-meta.json").write_text(json.dumps(meta, indent=2) + "\n")

    print(f"Silent: {silent}")
    print(f"Play:   {playable}")
    print(f"GIFs:   {gif_dir}")
    if sys.platform == "darwin":
        subprocess.run(["open", str(playable)], check=False)


if __name__ == "__main__":
    main()
