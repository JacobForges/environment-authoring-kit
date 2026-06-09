#!/usr/bin/env python3
"""
Full hybrid recap (~10:12) — stable camera only.

No fast timelapse scrub (Scene orbit reads as panicking camera).
Structure: intro → [screen B-roll + PNG holds] × 4 chapters → outro.
Caption cards + JavaFX on every segment when javafxEffects=true.
"""
from __future__ import annotations

import importlib.util
import json
import shutil
import subprocess
import sys
from pathlib import Path

from envkit_paths import approved_dir

_TOOLS = Path(__file__).resolve().parent
from hybrid_recap_common import (
    HYBRID_MAX_DURATION_SEC,
    TARGET_DURATION_SEC,
    chapter_clips,
    concat_segments,
    compose_fps,
    find_ffmpeg,
    find_ffprobe,
    list_timelapse_frames,
    pick_evenly,
    probe_duration,
    resolve_screen_recording,
    screen_segment,
    set_compose_fps,
    write_timeline,
)

CHAPTERS = [
    ("Opening", "Build session", "Hub queued FullWorld on tagged Ground."),
    ("Flat grid", "Ground lay", "Extended grid tiles placed one step at a time."),
    ("Terraform", "Directional sculpt", "Wilderness tiles terraform with paced micro-steps."),
    ("Late band", "Titan and polish", "Hollow Titan, props, and cave macro after terrain."),
]

INTRO_SEC = 10.0
OUTRO_SEC = 7.0
HOLDS_PER_CHAPTER = 3
HOLD_SEC = 8.0


def _max_screen_sec(target: float, intro: float, outro: float, holds: int, hold_sec: float) -> float:
    budget = target - intro - outro - holds * hold_sec
    return max(18.0, budget / 4.0)


def _load(name: str, file: str):
    spec = importlib.util.spec_from_file_location(name, _TOOLS / file)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def _javafx_polish_segment(ffmpeg: str, seg: Path, *, enabled: bool, spec: dict) -> None:
    if not enabled or not seg.is_file():
        return
    jfx = _load("javafx", "demo-recap-javafx-effects.py")
    fx = seg.with_suffix(".fx.mp4")
    strength = float(spec.get("javafxFxStrength", 1.35))
    vf = jfx.javafx_ffmpeg_vf(strength=strength)
    fps = compose_fps()
    enc_fps = min(fps, int(spec.get("timelapseEncodeFps", fps)))
    if enc_fps > fps:
        vf = f"{vf},minterpolate=fps={enc_fps}:mi_mode=blend"
    vf = f"{vf},fps={enc_fps}"
    dur = probe_duration(seg)
    try:
        subprocess.run(
            [
                ffmpeg, "-y", "-i", str(seg),
                "-vf", vf,
                "-t", f"{dur:.3f}",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "19",
                "-preset", "veryfast", "-an", str(fx),
            ],
            check=True,
            capture_output=True,
            text=True,
        )
        fx.replace(seg)
        print(f"  JavaFX polish → {seg.name} ({enc_fps} fps)", flush=True)
    except subprocess.CalledProcessError as exc:
        tail = (exc.stderr or exc.stdout or "")[-600:]
        print(f"  WARNING: JavaFX polish failed on {seg.name}: {tail}", flush=True)


def main() -> int:
    capture = Path(sys.argv[1]).expanduser().resolve() if len(sys.argv) > 1 else None
    if not capture or not capture.is_dir():
        print(f"Usage: {Path(__file__).name} /path/to/DemoCapture/<timestamp>", file=sys.stderr)
        return 1

    ffmpeg = find_ffmpeg()
    ffprobe = find_ffprobe()
    frames = list_timelapse_frames(capture / "timelapse")
    mov = resolve_screen_recording(capture)
    clips = chapter_clips(mov, ffmpeg, count=4)

    approved = approved_dir()
    cards_path = approved / "ApprovedCards.json"
    spec: dict = {}
    if cards_path.is_file():
        spec = json.loads(cards_path.read_text(encoding="utf-8"))
    javafx_on = bool(spec.get("javafxEffects", True))
    target_sec = float(spec.get("hybridTargetDurationSec", TARGET_DURATION_SEC))
    fps = int(spec.get("hybridComposeFps", 30))
    set_compose_fps(fps)
    screen_cap = _max_screen_sec(target_sec, INTRO_SEC, OUTRO_SEC, 4 * HOLDS_PER_CHAPTER, HOLD_SEC)
    print(
        f"Hybrid compose: {fps} fps, target {target_sec:.0f}s, screen cap {screen_cap:.0f}s, "
        f"JavaFX={'on' if javafx_on else 'off'}",
        flush=True,
    )

    pres = _load("pres", "compose-presentation-recap.py")
    pres._configure_canvas(spec)

    intro_png = approved / "ApprovedIntro.png"
    outro_png = approved / "ApprovedOutro.png"
    if not intro_png.is_file():
        intro_png = frames[0]
    if not outro_png.is_file():
        outro_png = frames[-1]

    work = capture / "_hybrid_compose"
    work.mkdir(exist_ok=True)
    for old in work.glob("seg_*.mp4"):
        old.unlink(missing_ok=True)

    milestone_pngs = pick_evenly(frames, 12)
    plan: list[dict] = []
    part_files: list[Path] = []
    hold_index = 0
    total_holds = 4 * HOLDS_PER_CHAPTER

    def add_segment(kind: str, path: Path, sec: float, meta: dict) -> None:
        actual = probe_duration(path, ffprobe)
        if abs(actual - sec) > 1.5:
            print(f"  warn {path.name}: wanted {sec:.1f}s got {actual:.1f}s", flush=True)
        plan.append({**meta, "kind": kind, "durationSec": actual, "file": path.name})
        part_files.append(path)

    intro_out = work / "seg_intro.mp4"
    intro_meta = {
        "beatKind": "intro",
        "chapter": "Intro",
        "phase": "World Build Recap",
        "line1": "Environment Kit — FullWorld hybrid recap",
        "line2": "Screen recording plus milestone stills",
        "line3": "Stable camera — no orbit scrub",
        "frame": 0,
        "_plannedHoldSec": INTRO_SEC,
    }
    pres.hold_segment(
        ffmpeg, intro_png, intro_meta, intro_out, spec,
        hold_index, total_holds + 2, fps, work,
    )
    add_segment("intro", intro_out, INTRO_SEC, intro_meta)

    png_i = 0
    for ch in range(4):
        ch_title, phase, sub = CHAPTERS[ch]
        clip = clips[ch]
        sc_out = work / f"seg_screen_{ch + 1}.mp4"
        sc_dur = min(float(clip["duration"]), screen_cap)
        screen_segment(ffmpeg, mov, sc_out, clip["start"], sc_dur)
        _javafx_polish_segment(ffmpeg, sc_out, enabled=javafx_on, spec=spec)
        add_segment("screencast", sc_out, sc_dur, {
            "beatKind": "screencast",
            "chapter": clip["chapter"],
            "phase": "Live screen B-roll",
            "sub": clip["label"],
            "line1": f"Screen · {clip['label'].replace('_', ' ')}",
            "line2": "Muted — Personal Voice only",
            "line3": phase,
            "screencastStart": clip["start"],
            "screencastDuration": sc_dur,
        })

        for h in range(HOLDS_PER_CHAPTER):
            png = milestone_pngs[png_i % len(milestone_pngs)]
            png_i += 1
            hold_index += 1
            hold_out = work / f"seg_hold_{ch + 1}_{h + 1}.mp4"
            hold_meta = {
                "beatKind": "checkpoint",
                "chapter": ch_title,
                "phase": "Milestone still",
                "sub": png.name,
                "line1": f"Milestone · {png.stem}",
                "line2": sub,
                "line3": "Additive PNG hold — no camera motion",
                "frame": frames.index(png),
                "_plannedHoldSec": HOLD_SEC,
            }
            pres.hold_segment(
                ffmpeg, png, hold_meta, hold_out, spec,
                hold_index, total_holds, fps, work,
            )
            add_segment("hold", hold_out, HOLD_SEC, hold_meta)

    outro_out = work / "seg_outro.mp4"
    outro_meta = {
        "beatKind": "outro",
        "chapter": "Outro",
        "phase": "Build complete",
        "line1": "Eight to nine minute portfolio target",
        "line2": "Cursor narration script follows",
        "line3": "Personal Voice mux in Terminal",
        "frame": len(frames) - 1,
        "_plannedHoldSec": OUTRO_SEC,
    }
    pres.hold_segment(
        ffmpeg, outro_png, outro_meta, outro_out, spec,
        total_holds + 1, total_holds + 2, fps, work,
    )
    add_segment("outro", outro_out, OUTRO_SEC, outro_meta)

    silent = capture / "HybridRecapPresentation-SILENT.mp4"
    concat_segments(ffmpeg, part_files, silent)
    actual = probe_duration(silent, ffprobe)

    write_timeline(capture, plan, {
        **spec,
        "fullNarrationTargetSec": actual,
        "targetDurationSec": actual,
        "buildMode": "FullWorld hybrid screencast recap",
        "hybridScreenRecording": str(mov),
        "hybridStableCamera": True,
        "cinematicCamera": False,
        "holdCinematicMotion": False,
        "hybridComposeFps": fps,
        "hybridTargetDurationSec": target_sec,
        "hybridMaxDurationSec": float(spec.get("hybridMaxDurationSec", HYBRID_MAX_DURATION_SEC)),
        "hybridMaxScreenClipSec": screen_cap,
    })

    out_folder = Path.home() / "Desktop" / "HybridRecap-Full"
    out_folder.mkdir(exist_ok=True)
    desktop_copy = out_folder / "HybridRecapPresentation-SILENT.mp4"
    shutil.copy2(silent, desktop_copy)
    (out_folder / "hybrid-meta.json").write_text(
        json.dumps({
            "silentMp4": str(silent),
            "durationSec": actual,
            "targetSec": TARGET_DURATION_SEC,
            "stableCamera": True,
            "segmentCount": len(plan),
            "composeFps": fps,
            "javafxEffects": javafx_on,
        }, indent=2) + "\n",
    )

    print(f"Silent master: {silent}")
    print(f"Duration: {actual:.1f}s (target {TARGET_DURATION_SEC:.0f}s)")
    print(f"Desktop: {desktop_copy}")
    if abs(actual - TARGET_DURATION_SEC) > 25:
        print("WARNING: duration drift — check segment probes above", flush=True)
    if sys.platform == "darwin":
        subprocess.run(["open", str(desktop_copy)], check=False)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
