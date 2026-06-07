#!/usr/bin/env python3
"""
Auto-edit a long timelapse into a narrated pipeline documentary.

Input: run folder with
  timelapse/tl_000001.png ...  (captured every N seconds during build)
  DemoRecapTimeline.json       (milestones + captions)

Output: DemoRecap.mp4 — fast motion between milestones, holds + captions on milestones.
"""
from __future__ import annotations

import importlib.util
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

_COMPOSE_PATH = Path(__file__).resolve().parent / "compose-demo-recap.py"
_spec = importlib.util.spec_from_file_location("demo_compose", _COMPOSE_PATH)
demo_compose = importlib.util.module_from_spec(_spec)
assert _spec.loader
_spec.loader.exec_module(demo_compose)

_visual_spec = importlib.util.spec_from_file_location(
    "demo_recap_visual_enhance",
    Path(__file__).resolve().parent / "demo-recap-visual-enhance.py",
)
visual = importlib.util.module_from_spec(_visual_spec)
assert _visual_spec.loader
_visual_spec.loader.exec_module(visual)

_cap_spec = importlib.util.spec_from_file_location(
    "demo_recap_captions",
    Path(__file__).resolve().parent / "demo-recap-captions.py",
)
captions_mod = importlib.util.module_from_spec(_cap_spec)
assert _cap_spec.loader
_cap_spec.loader.exec_module(captions_mod)


def find_ffmpeg() -> str:
    return demo_compose.find_ffmpeg()


def resolve_card_image(run_dir: Path, spec: dict[str, Any], *keys: str) -> Path | None:
    for key in keys:
        raw = spec.get(key)
        if not raw:
            continue
        p = Path(str(raw)).expanduser()
        if not p.is_absolute():
            p = run_dir / p
        if p.is_file():
            return p.resolve()
    return None


def list_timelapse_frames(folder: Path) -> list[Path]:
    for pattern in ("tl_*.png", "t_*.png", "frame_*.png"):
        hits = sorted(folder.glob(pattern))
        if hits:
            return hits
    return []


def segment_duration_estimate(
    frame_count: int, input_fps: float, speedup: float, output_fps: float, use_minterp: bool
) -> float:
    src_sec = frame_count / max(input_fps, 0.1)
    sped = src_sec / max(speedup, 1.0)
    if use_minterp:
        return max(sped, frame_count / output_fps)
    return sped


def build_dense_timelapse_chunk(
    ffmpeg: str,
    frames: list[Path],
    out: Path,
    work: Path,
    output_fps: float,
    frames_per_source: int = 8,
    *,
    video_enhance: bool = True,
    cinematic_camera: bool = True,
    chunk_seed: int = 0,
) -> None:
    """Use every capture PNG; repeat each frame for fluid 120fps motion (no fades, no skip)."""
    if not frames:
        return
    work.mkdir(parents=True, exist_ok=True)
    seq = work / "seq"
    if seq.exists():
        for f in seq.glob("*.png"):
            f.unlink()
    else:
        seq.mkdir(parents=True)
    out_idx = 0
    use_pil_cinematic = cinematic_camera and len(frames) <= 96
    if use_pil_cinematic:
        total_out = len(frames) * max(1, frames_per_source)
        for fi, src in enumerate(frames):
            base = demo_compose.Image.open(src)
            for _rep in range(max(1, frames_per_source)):
                t = out_idx / max(1, total_out - 1)
                frame = visual.true_color_grade(base.convert("RGB"))
                frame = visual.apply_cinematic_motion(
                    frame, t, mode="auto", seed=chunk_seed + fi
                )
                (seq / f"f_{out_idx:06d}.png").parent.mkdir(parents=True, exist_ok=True)
                frame.save(seq / f"f_{out_idx:06d}.png")
                out_idx += 1
    else:
        for i, src in enumerate(frames):
            link = seq / f"f_{i:06d}.png"
            if link.exists() or link.is_symlink():
                link.unlink()
            link.symlink_to(src.resolve())
        out_idx = len(frames)
    input_fps = output_fps / max(1, frames_per_source) if use_pil_cinematic else min(
        24.0, output_fps / max(1, frames_per_source)
    )
    vf = visual.ffmpeg_timelapse_enhance_chain(1.0, output_fps, out_idx, enabled=video_enhance)
    if cinematic_camera and not use_pil_cinematic:
        # Slow dolly via zoompan (all frames — no per-PNG PIL loop).
        vf += (
            ",zoompan=z='min(1.0+0.0008*on,1.12)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)'"
            f":d=1:s=1280x720:fps={output_fps:.3f}"
        )
    elif use_pil_cinematic:
        vf += f",fps={output_fps:.3f}"
    subprocess.run(
        [
            ffmpeg,
            "-y",
            "-framerate",
            f"{input_fps:.4f}",
            "-start_number",
            "0",
            "-i",
            str(seq / "f_%06d.png"),
            "-frames:v",
            str(out_idx if use_pil_cinematic else len(frames)),
            "-vf",
            vf,
            "-c:v",
            "libx264",
            "-crf",
            "15",
            "-preset",
            "medium",
            "-pix_fmt",
            "yuv420p",
            "-r",
            str(output_fps),
            str(out),
        ],
        check=True,
        capture_output=True,
    )


def build_timelapse_segment(
    ffmpeg: str,
    frames: list[Path],
    out: Path,
    capture_fps: float,
    speedup: float,
    work: Path,
    output_fps: float = 30.0,
    *,
    video_enhance: bool = True,
    segment_fade_sec: float = 0.45,
    no_segment_fades: bool = False,
) -> None:
    """Encode frame range as sped-up video (real motion, not single-image hold)."""
    if not frames:
        return
    work.mkdir(parents=True, exist_ok=True)
    # ffmpeg image2 needs contiguous indices — symlink tl_000000, tl_000001...
    seq = work / "seq"
    if seq.exists():
        for f in seq.glob("*.png"):
            f.unlink()
    else:
        seq.mkdir(parents=True)
    start = int(frames[0].stem.split("_")[-1]) if "_" in frames[0].stem else 0
    for i, src in enumerate(frames):
        link = seq / f"f_{i:06d}.png"
        if link.exists() or link.is_symlink():
            link.unlink()
        link.symlink_to(src.resolve())

    pts = 1.0 / max(speedup, 1.0)
    input_fps = min(24.0, max(capture_fps * 3.0, 10.0))
    use_minterp = len(frames) <= 120
    vf = visual.ffmpeg_timelapse_enhance_chain(
        pts, output_fps, len(frames), enabled=video_enhance
    )
    dur = segment_duration_estimate(len(frames), input_fps, speedup, output_fps, use_minterp)
    if not no_segment_fades and segment_fade_sec > 0.01:
        vf = visual.append_edge_fades(vf, dur, segment_fade_sec)
    subprocess.run(
        [
            ffmpeg,
            "-y",
            "-framerate",
            str(input_fps),
            "-start_number",
            "0",
            "-i",
            str(seq / "f_%06d.png"),
            "-frames:v",
            str(len(frames)),
            "-vf",
            vf,
            "-c:v",
            "libx264",
            "-crf",
            "16",
            "-preset",
            "medium",
            "-pix_fmt",
            "yuv420p",
            "-r",
            str(output_fps),
            str(out),
        ],
        check=True,
        capture_output=True,
    )


def build_milestone_hold(
    ffmpeg: str,
    frame_path: Path,
    milestone: dict,
    out: Path,
    hold_sec: float,
    index: int,
    total: int,
    output_fps: float = 30.0,
    work: Path | None = None,
    *,
    video_enhance: bool = True,
    segment_fade_sec: float = 0.55,
    annotation_delay_sec: float = 3.0,
    annotation_fade_sec: float = 0.65,
    caption_line2_delay: float = 0.55,
    caption_line3_delay: float = 1.05,
    lecture_mode: bool = True,
    ai_regions_only: bool = True,
    chapter_focus_annotations: bool = False,
    show_annotations: bool = False,
    cinematic_camera: bool = False,
) -> None:
    img = demo_compose.Image.open(frame_path)
    accent = tuple(milestone.get("accent", [72, 138, 220]))
    chapter = milestone.get("chapter") or milestone.get("phase") or f"Milestone {index + 1}"
    meta = None if lecture_mode else " · ".join(
        p for p in (milestone.get("phase"), milestone.get("sub")) if p
    )[:160]

    total_frames = max(2, int(hold_sec * output_fps))
    delay_frames = min(total_frames - 1, int(annotation_delay_sec * output_fps))
    fade_frames = max(1, int(annotation_fade_sec * output_fps))

    seq_dir = (work or out.parent) / f"hold_{index:03d}_frames" / "seq"
    if seq_dir.exists():
        for f in seq_dir.glob("*.png"):
            f.unlink()
    else:
        seq_dir.mkdir(parents=True)

    l2_f = int(caption_line2_delay * output_fps)
    l3_f = int(caption_line3_delay * output_fps)
    l1_fade = max(1, int(0.35 * output_fps))

    for fi in range(total_frames):
        t = fi / output_fps
        line1_a = min(1.0, fi / l1_fade)
        line2_a = min(1.0, max(0, fi - l2_f) / max(1, l1_fade)) if fi >= l2_f else 0.0
        line3_a = min(1.0, max(0, fi - l3_f) / max(1, l1_fade)) if fi >= l3_f else 0.0
        if fi < delay_frames:
            ann_a = 0.0
        elif fi < delay_frames + fade_frames:
            ann_a = (fi - delay_frames) / fade_frames
        else:
            ann_a = 1.0
        cam_t = fi / max(1, total_frames - 1) if cinematic_camera else None
        captioned = demo_compose.render_captioned_frame(
            img,
            line1=milestone.get("line1", ""),
            line2=milestone.get("line2", ""),
            line3=milestone.get("line3"),
            chapter=str(chapter)[:52],
            index=index,
            total=total,
            accent=accent,
            meta=meta or None,
            milestone=milestone,
            annotate=show_annotations and ann_a > 0.02,
            annotation_alpha=ann_a,
            line1_alpha=line1_a,
            line2_alpha=line2_a,
            line3_alpha=line3_a,
            lecture_mode=lecture_mode,
            ai_regions_only=ai_regions_only,
            chapter_focus_annotations=chapter_focus_annotations,
            cinematic_t=cam_t,
            cinematic_seed=index,
        )
        captioned.save(seq_dir / f"f_{fi:06d}.png")

    vf = "scale=1280:720"
    if video_enhance:
        vf = visual.ffmpeg_broadcast_color_vf(enabled=True)
    vf = visual.append_edge_fades(vf, hold_sec, segment_fade_sec)
    subprocess.run(
        [
            ffmpeg,
            "-y",
            "-framerate",
            str(output_fps),
            "-start_number",
            "0",
            "-i",
            str(seq_dir / "f_%06d.png"),
            "-frames:v",
            str(total_frames),
            "-vf",
            vf,
            "-c:v",
            "libx264",
            "-crf",
            "15",
            "-preset",
            "medium",
            "-pix_fmt",
            "yuv420p",
            "-r",
            str(output_fps),
            str(out),
        ],
        check=True,
        capture_output=True,
    )


def concat_with_xfades(
    ffmpeg: str,
    segments: list[Path],
    output: Path,
    xfade_sec: float,
    output_fps: float,
) -> None:
    """Chain xfade merges for smoother transitions (used when segment count is modest)."""
    if len(segments) < 2 or xfade_sec <= 0.01:
        concat_segments(ffmpeg, segments, output)
        return
    transitions = ("fade", "smoothleft", "fade", "circleopen", "fadeblack")
    merged = segments[0]
    work = output.parent
    for i in range(1, len(segments)):
        nxt = work / f"_xfade_{i:04d}.mp4"
        tr = transitions[i % len(transitions)]
        demo_compose.xfade_merge(ffmpeg, merged, segments[i], nxt, xfade_sec, tr)
        if merged != segments[0]:
            merged.unlink(missing_ok=True)
        merged = nxt
    if merged != output:
        merged.replace(output)


def concat_segments(ffmpeg: str, segments: list[Path], output: Path) -> None:
    lst = output.parent / "smart_segments.txt"
    lst.write_text("\n".join(f"file '{s.as_posix()}'" for s in segments) + "\n")
    subprocess.run(
        [
            ffmpeg,
            "-y",
            "-f",
            "concat",
            "-safe",
            "0",
            "-i",
            str(lst),
            "-c",
            "copy",
            str(output),
        ],
        check=True,
        capture_output=True,
    )


def compute_speedups(
    frame_count: int,
    milestone_indices: list[int],
    capture_interval: float,
    target_sec: float,
    intro_sec: float,
    outro_sec: float,
    hold_sec: float,
) -> list[float]:
    """Per gap between milestones (and ends), return speedup factor."""
    if frame_count <= 0:
        return []
    ms = sorted(set(i for i in milestone_indices if 0 <= i < frame_count))
    if not ms:
        ms = [0, frame_count - 1]
    if ms[0] != 0:
        ms = [0] + ms
    if ms[-1] != frame_count - 1:
        ms.append(frame_count - 1)

    gaps: list[tuple[int, int]] = []
    for a, b in zip(ms, ms[1:]):
        if b > a:
            gaps.append((a, b))

    hold_total = hold_sec * len(ms)
    budget = max(20.0, target_sec - intro_sec - outro_sec - hold_total)
    source_lens = [max(1, b - a) * capture_interval for a, b in gaps]
    source_total = sum(source_lens) or 1.0
    speeds = []
    for sl in source_lens:
        out_time = max(2.0, budget * (sl / source_total))
        speeds.append(max(1.5, sl / out_time))
    return speeds, ms, gaps


def compose_smart(run_dir: Path, output: Path, spec: dict) -> Path:
    ffmpeg = find_ffmpeg()
    tl_dir = run_dir / "timelapse"
    if not tl_dir.is_dir():
        tl_dir = run_dir / "frames"
    frames = list_timelapse_frames(tl_dir)
    if len(frames) < 8:
        raise RuntimeError(f"Need at least 8 timelapse frames in {tl_dir}, found {len(frames)}")

    capture_interval = float(spec.get("captureIntervalSec", 2.0))
    capture_fps = 1.0 / capture_interval
    target_sec = float(spec.get("targetDurationSec", 480))
    hold_sec = float(spec.get("milestoneHoldSec", 4.8))
    subbeat_hold_sec = float(spec.get("subbeatHoldSec", hold_sec * 0.45))
    subbeat_ann_delay = float(spec.get("subbeatAnnotationDelaySec", 1.8))
    intro_sec = float(spec.get("introSec", 2.4))
    outro_sec = float(spec.get("outroSec", 2.6))
    output_fps = float(spec.get("outputFps", 80))
    min_gap_frames = int(spec.get("minTimelapseGapFrames", 8))
    video_enhance = bool(spec.get("videoEnhance", True))
    segment_fade_sec = float(spec.get("segmentFadeSec", 0.45))
    segment_xfade_sec = float(spec.get("segmentXfadeSec", 0.55))
    annotation_delay_sec = float(spec.get("annotationDelaySec", 3.0))
    annotation_fade_sec = float(spec.get("annotationFadeSec", 0.65))
    lecture_mode = bool(spec.get("lectureMode", True))
    ai_regions_only = bool(spec.get("aiRegionsOnly", True))
    chapter_focus = bool(spec.get("chapterFocusAnnotations", not spec.get("cursorVision", True)))
    show_annotations = bool(spec.get("showAnnotations", False))
    professional_motion = spec.get("recapMode") == "professional_motion"
    no_transitions = bool(spec.get("noTransitions", professional_motion))
    frames_per_source = int(spec.get("framesPerSource", 8))
    caption_line2_delay = float(spec.get("captionLine2DelaySec", 0.55))
    caption_line3_delay = float(spec.get("captionLine3DelaySec", 1.05))
    if no_transitions:
        segment_xfade_sec = 0.0
        segment_fade_sec = 0.0
    cinematic_camera = bool(spec.get("cinematicCamera", True))
    if professional_motion:
        min_gap_frames = max(1, int(spec.get("minTimelapseGapFrames", 1)))

    milestones = sorted(spec.get("milestones", []), key=lambda m: int(m.get("frame", 0)))
    if not milestones:
        milestones = [{"frame": 0, "line1": "Build recording", "line2": "Timelapse with auto edit", "line3": ""}]
    for m in milestones:
        m["frame"] = min(max(0, int(m.get("frame", 0))), len(frames) - 1)
        captions_mod.fill_milestone_captions(m)
    ms_idx = [int(m["frame"]) for m in milestones]

    work_raw = spec.get("composeWorkDir") or os.environ.get("DEMO_RECAP_WORK_DIR")
    if work_raw:
        work = Path(str(work_raw)).expanduser()
    else:
        work = run_dir / "_smart_compose"
    work.mkdir(parents=True, exist_ok=True)
    for old in work.glob("*.mp4"):
        old.unlink()

    speeds, ms_list, gaps = compute_speedups(
        len(frames), ms_idx, capture_interval, target_sec, intro_sec, outro_sec, hold_sec
    )

    segments: list[Path] = []
    seg_i = 0

    intro_png = work / "intro.png"
    custom_intro = resolve_card_image(
        run_dir, spec, "introCardImage", "approvedIntroImage", "introImage"
    )
    if custom_intro:
        shutil.copy2(custom_intro, intro_png)
        print(f"Intro card: {custom_intro.name}")
    elif spec.get("portfolioTitleCards", True):
        import importlib.util

        tc_path = Path(__file__).resolve().parent / "demo-recap-title-cards.py"
        tc_spec = importlib.util.spec_from_file_location("demo_recap_title_cards", tc_path)
        tc_mod = importlib.util.module_from_spec(tc_spec)
        assert tc_spec.loader
        tc_spec.loader.exec_module(tc_mod)
        tc_mod.build_intro_card(run_dir, frames, spec, milestones).save(intro_png)
    else:
        demo_compose.render_title_card(
            spec.get("introTitle", "World Build Recap"),
            spec.get("introSubtitle", "Full pipeline timelapse — auto-edited with educational milestones"),
            accent=(72, 138, 220),
            footer="Environment Kit · smart edit",
        ).save(intro_png)
    intro_mp4 = work / f"seg_{seg_i:03d}.mp4"
    subprocess.run(
        [
            ffmpeg,
            "-y",
            "-loop",
            "1",
            "-t",
            str(intro_sec),
            "-i",
            str(intro_png),
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            "-r",
            str(output_fps),
            str(intro_mp4),
        ],
        check=True,
        capture_output=True,
    )
    segments.append(intro_mp4)
    seg_i += 1

    total_m = len(milestones)
    gap_speed_iter = iter(speeds)

    for mi, m in enumerate(milestones):
        fidx = min(max(0, int(m.get("frame", 0))), len(frames) - 1)

        # timelapse chunk BEFORE this milestone (from previous index)
        prev_idx = 0 if mi == 0 else min(max(0, int(milestones[mi - 1].get("frame", 0))), len(frames) - 1)
        if fidx > prev_idx + min_gap_frames:
            chunk = frames[prev_idx:fidx]
            fast_mp4 = work / f"seg_{seg_i:03d}.mp4"
            if professional_motion:
                build_dense_timelapse_chunk(
                    ffmpeg,
                    chunk,
                    fast_mp4,
                    work / f"seq_{seg_i}",
                    output_fps,
                    frames_per_source=frames_per_source,
                    video_enhance=video_enhance,
                    cinematic_camera=cinematic_camera,
                    chunk_seed=mi,
                )
            else:
                speed = next(gap_speed_iter, 8.0)
                build_timelapse_segment(
                    ffmpeg,
                    chunk,
                    fast_mp4,
                    capture_fps,
                    speed,
                    work / f"seq_{seg_i}",
                    output_fps,
                    video_enhance=video_enhance,
                    segment_fade_sec=segment_fade_sec,
                    no_segment_fades=no_transitions,
                )
            segments.append(fast_mp4)
            seg_i += 1

        is_sub = m.get("beatKind") == "subbeat"
        beat_hold = float(m.get("holdSec") or (subbeat_hold_sec if is_sub else hold_sec))
        beat_ann_delay = subbeat_ann_delay if is_sub else annotation_delay_sec
        beat_l2 = 0.35 if is_sub else caption_line2_delay
        beat_l3 = 0.0 if is_sub else caption_line3_delay

        hold_mp4 = work / f"seg_{seg_i:03d}.mp4"
        build_milestone_hold(
            ffmpeg,
            frames[fidx],
            m,
            hold_mp4,
            beat_hold,
            mi,
            total_m,
            output_fps,
            work,
            video_enhance=video_enhance,
            segment_fade_sec=0.0 if no_transitions else max(segment_fade_sec, 0.55),
            annotation_delay_sec=beat_ann_delay,
            annotation_fade_sec=annotation_fade_sec,
            caption_line2_delay=beat_l2,
            caption_line3_delay=beat_l3,
            lecture_mode=lecture_mode,
            ai_regions_only=ai_regions_only,
            chapter_focus_annotations=chapter_focus,
            show_annotations=show_annotations,
            cinematic_camera=False,
        )
        segments.append(hold_mp4)
        seg_i += 1

    # tail after last milestone
    last_idx = min(max(0, int(milestones[-1].get("frame", 0))), len(frames) - 1)
    if len(frames) - last_idx > min_gap_frames:
        chunk = frames[last_idx:]
        fast_mp4 = work / f"seg_{seg_i:03d}.mp4"
        if professional_motion:
            build_dense_timelapse_chunk(
                ffmpeg,
                chunk,
                fast_mp4,
                work / f"seq_{seg_i}",
                output_fps,
                frames_per_source=frames_per_source,
                video_enhance=video_enhance,
                cinematic_camera=cinematic_camera,
                chunk_seed=len(milestones) + 1,
            )
        else:
            speed = next(gap_speed_iter, 10.0)
            build_timelapse_segment(
                ffmpeg,
                chunk,
                fast_mp4,
                capture_fps,
                speed,
                work / f"seq_{seg_i}",
                output_fps,
                video_enhance=video_enhance,
                segment_fade_sec=segment_fade_sec,
                no_segment_fades=no_transitions,
            )
        segments.append(fast_mp4)
        seg_i += 1

    outro_png = work / "outro.png"
    custom_outro = resolve_card_image(
        run_dir, spec, "outroCardImage", "approvedOutroImage", "outroImage"
    )
    if custom_outro:
        shutil.copy2(custom_outro, outro_png)
        print(f"Outro card: {custom_outro.name}")
    elif spec.get("portfolioTitleCards", True):
        import importlib.util

        tc_path = Path(__file__).resolve().parent / "demo-recap-title-cards.py"
        tc_spec = importlib.util.spec_from_file_location("demo_recap_title_cards", tc_path)
        tc_mod = importlib.util.module_from_spec(tc_spec)
        assert tc_spec.loader
        tc_spec.loader.exec_module(tc_mod)
        tc_mod.build_outro_card(run_dir, frames, spec, milestones).save(outro_png)
    else:
        demo_compose.render_title_card(
            spec.get("outroTitle", "Pipeline complete"),
            spec.get("outroSubtitle", "Scene view matches where the build stopped"),
            accent=(90, 180, 140),
            footer="No manual edit required",
        ).save(outro_png)
    outro_mp4 = work / f"seg_{seg_i:03d}.mp4"
    subprocess.run(
        [
            ffmpeg,
            "-y",
            "-loop",
            "1",
            "-t",
            str(outro_sec),
            "-i",
            str(outro_png),
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            "-r",
            str(output_fps),
            str(outro_mp4),
        ],
        check=True,
        capture_output=True,
    )
    segments.append(outro_mp4)

    output.parent.mkdir(parents=True, exist_ok=True)
    if segment_xfade_sec > 0.01 and len(segments) <= 36:
        concat_with_xfades(ffmpeg, segments, output, segment_xfade_sec, output_fps)
    else:
        concat_segments(ffmpeg, segments, output)
    return output


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: compose-smart-recap.py <run_folder> [output.mp4]", file=sys.stderr)
        return 1
    run_dir = Path(sys.argv[1]).expanduser()
    output = Path(sys.argv[2]) if len(sys.argv) > 2 else run_dir / "DemoRecap.mp4"
    spec_path = run_dir / "DemoRecapTimeline.json"
    if not spec_path.is_file():
        print(f"Missing {spec_path}", file=sys.stderr)
        return 1
    spec = json.loads(spec_path.read_text())
    compose_smart(run_dir, output, spec)
    print(str(output))
    return 0


if __name__ == "__main__":
    sys.exit(main())
