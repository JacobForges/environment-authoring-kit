#!/usr/bin/env python3
"""Producer v3 compose — parallel segments, keyframe holds, master-only grade, cache, narrator."""
from __future__ import annotations

import importlib.util
import json
import os
import platform
import shutil
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from typing import Any, Callable

_TOOLS = Path(__file__).resolve().parent


def _load(name: str, filename: str):
    spec = importlib.util.spec_from_file_location(name, _TOOLS / filename)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def open_recap_video(path: Path) -> None:
    """Open finalized recap MP4 in the default macOS player (QuickTime)."""
    p = Path(path).expanduser()
    if sys.platform != "darwin" or not p.is_file() or p.stat().st_size <= 1024:
        return
    print("Opening recap video...")
    subprocess.run(["open", str(p)], check=False)


def _milestone_wants_play_broll(m: dict[str, Any]) -> bool:
    blob = " ".join(
        str(m.get(k) or "") for k in ("chapter", "phase", "line1", "captionKey", "sub")
    ).lower()
    return any(
        token in blob
        for token in ("play disk", "playtest", "play mode", "locomotion readability")
    )


def _resolve_playthrough_movs(run_dir: Path) -> list[Path]:
    hybrid = _load("hybrid", "hybrid_recap_common.py")
    return hybrid.resolve_playthrough_recordings(run_dir)


def _apply_bot_avatar_overlay(
    ffmpeg: str,
    final_silent: Path,
    wav: Path,
    work: Path,
    run_dir: Path,
    spec: dict[str, Any],
) -> Path:
    """Layer Jacob Adkins's Bot lip-sync avatar before narration mux."""
    if not (spec.get("botAvatarOverlay", True) and spec.get("botAvatarLipSync", True)):
        return final_silent
    narr = _load("narrator", "demo-recap-narrator.py")
    if not wav.is_file() or not narr.wav_is_audible(wav):
        print("Bot avatar: skipped (no audible narration.wav)", flush=True)
        return final_silent
    bot_mod = _load("bot", "jacob-adkins-bot.py")
    envkit = _load("envkit", "envkit_paths.py")
    ffprobe = narr.find_ffprobe(ffmpeg)
    approved_root = envkit.approved_dir()
    bot_mod.ensure_animated_avatar_asset(approved_root, ffmpeg)
    avatar_export = run_dir / "JacobAdkinsBot-Avatar"
    bot_video = work / "_final_video_bot.mp4"
    lipsync_mov = work / "_bot_lipsync" / f"{bot_mod.LIPSYNC_BASENAME}.mov"
    lipsync_manifest = work / "_bot_lipsync" / f"{bot_mod.LIPSYNC_MANIFEST_BASENAME}.json"
    if lipsync_mov.is_file() and lipsync_mov.stat().st_size > 1024 * 1024:
        if bot_mod.lipsync_cache_matches_spec(lipsync_manifest, spec):
            print(f"Reusing lip-sync avatar → {lipsync_mov.name}", flush=True)
            bot_mod.apply_animated_avatar_to_video(
                ffmpeg, final_silent, bot_video, lipsync_mov, spec=spec,
            )
            if bot_video.is_file():
                avatar_export.mkdir(parents=True, exist_ok=True)
                try:
                    shutil.copy2(lipsync_mov, avatar_export / lipsync_mov.name)
                except OSError as exc:
                    print(f"WARNING: avatar export copy skipped: {exc}", flush=True)
                return bot_video
        else:
            print(
                "Stale lip-sync cache (procedural vs Unity cyborg) — regenerating avatar…",
                flush=True,
            )
            shutil.rmtree(work / "_bot_lipsync", ignore_errors=True)
    if bot_mod.generate_lipsync_avatar_for_narration(
        wav,
        final_silent,
        bot_video,
        ffmpeg,
        ffprobe,
        layer_export=avatar_export,
        spec=spec,
    ):
        print(f"Lip-sync layered avatar baked → {bot_video}", flush=True)
        return bot_video
    print("Bot avatar: lip-sync generation failed — video without avatar", flush=True)
    return final_silent


def find_ffmpeg() -> str:
    for c in ("ffmpeg", "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg"):
        try:
            subprocess.run([c, "-version"], capture_output=True, check=True)
            return c
        except (FileNotFoundError, subprocess.CalledProcessError):
            continue
    raise SystemExit("ffmpeg not found")


def prep_vf(spec: dict[str, Any]) -> str:
    color = _load("color", "demo-recap-color.py")
    return color.prep_vf(spec)


def _encode_args(spec: dict[str, Any]) -> list[str]:
    producer = _load("producer", "demo-recap-producer.py")
    return producer.ffmpeg_encode_args(spec)


def _configure_canvas(spec: dict[str, Any]) -> None:
    w = int(spec.get("outputWidth", 1920))
    h = int(spec.get("outputHeight", 1080))
    compose = _load("compose", "compose-demo-recap.py")
    compose.configure_canvas(w, h)


def timeline_stretch_sec(spec: dict[str, Any], sec: float) -> float:
    """videoPlaybackFactor < 1 → slower playback → longer clip duration."""
    factor = float(spec.get("videoPlaybackFactor", 1.0))
    if factor <= 0 or abs(factor - 1.0) < 0.01:
        return sec
    return sec / factor


def _reuse_cached_segment(
    narration_only: bool,
    spec: dict[str, Any],
    path: Path,
    label: str,
    *,
    planned_sec: float | None = None,
) -> bool:
    if not narration_only or not path.is_file() or path.stat().st_size <= 1024:
        return False
    if spec.get("syncHoldToNarration", True) and (
        label in ("intro", "outro") or label.startswith("hold")
    ):
        try:
            narr = _load("narrator", "demo-recap-narrator.py")
            dur = narr.probe_duration(path)
            if planned_sec is not None and planned_sec > 0:
                return abs(dur - float(planned_sec)) <= 1.25
            if label.startswith("hold"):
                return dur >= 8.0
            return dur >= 6.0
        except Exception:
            return False
    return True


def png_clip(
    ffmpeg: str,
    png: Path,
    out: Path,
    sec: float,
    fps: int,
    fade_in=0.0,
    fade_out=0.0,
    *,
    spec: dict[str, Any] | None = None,
) -> None:
    vf = f"fps={fps}"
    if fade_in:
        vf += f",fade=t=in:st=0:d={fade_in:.2f}"
    if fade_out:
        vf += f",fade=t=out:st={max(0, sec - fade_out):.2f}:d={fade_out:.2f}"
    subprocess.run(
        [
            ffmpeg, "-y", "-loop", "1", "-t", str(sec), "-i", str(png),
            "-vf", vf, *_encode_args(spec or {}), str(out),
        ],
        check=True,
        capture_output=True,
    )


def pick_timelapse_frames(
    frames: list[Path],
    max_f: int,
    *,
    chunk_start: int = 0,
    milestone_frames: list[int] | None = None,
) -> list[Path]:
    timeline = _load("timeline", "demo-recap-timeline.py")
    return timeline.pick_timelapse_keyframes(
        frames,
        max_f,
        chunk_start=chunk_start,
        milestone_frames=milestone_frames,
    )


def _clear_seq_pngs(seq: Path) -> None:
    if not seq.exists():
        return
    for f in seq.glob("*.png"):
        if f.name.startswith("._"):
            continue
        try:
            f.unlink()
        except FileNotFoundError:
            pass


def symlink_seq(seq: Path, picked: list[Path]) -> None:
    seq.mkdir(parents=True, exist_ok=True)
    _clear_seq_pngs(seq)
    for i, src in enumerate(picked):
        dst = seq / f"f_{i:06d}.png"
        dst.symlink_to(src.resolve())


def _timelapse_duration_sec(picked: list[Path], spec: dict[str, Any]) -> float:
    timeline = _load("timeline", "demo-recap-timeline.py")
    sec = timeline.timelapse_sec_for_frame_gap(len(picked), spec)
    return timeline_stretch_sec(spec, sec)


def _encode_timelapse(
    ffmpeg: str, picked: list[Path], out: Path, spec: dict[str, Any], fps: int, work: Path
) -> None:
    sec = _timelapse_duration_sec(picked, spec)
    seq = work / "seq"
    symlink_seq(seq, picked)
    input_fps = max(6.0, min(12.0, len(picked) / sec))
    enc_fps = min(fps, int(spec.get("timelapseEncodeFps", 60)))
    vf = _timelapse_vf(spec, enc_fps, sec, fade_in=0.8, fade_out=0.7)
    subprocess.run(
        [
            ffmpeg, "-y",
            "-framerate", f"{input_fps:.3f}",
            "-start_number", "0",
            "-i", str(seq / "f_%06d.png"),
            "-vf", vf, "-t", str(sec),
            *_encode_args(spec), str(out),
        ],
        check=True,
        capture_output=True,
    )


def timelapse_segment(
    ffmpeg: str,
    frames: list[Path],
    out: Path,
    spec: dict[str, Any],
    fps: int,
    cache_dir: Path | None = None,
    *,
    chunk_start: int = 0,
) -> None:
    if len(frames) < 2:
        png_clip(
            ffmpeg,
            frames[0],
            out,
            float(spec.get("tlMinSec", 4.5)),
            fps,
            fade_in=0.5,
            spec=spec,
        )
        return
    picked = pick_timelapse_frames(
        frames,
        int(spec.get("tlMaxFrames", 72)),
        chunk_start=chunk_start,
        milestone_frames=spec.get("_milestoneFrames"),
    )

    def build(o: Path) -> None:
        w = o.parent / f"_w_{o.stem}"
        _encode_timelapse(ffmpeg, picked, o, spec, fps, w)

    if cache_dir and spec.get("segmentCache", True):
        cache = _load("cache", "demo-recap-segment-cache.py")
        key = cache.segment_key(spec, "tl", f"{picked[0].name}_{len(picked)}_{picked[-1].name}", picked)
        cache.build_or_cache(cache_dir, key, build, out)
    else:
        build(out)


def bridge_segment(ffmpeg: str, frames: list[Path], out: Path, spec: dict[str, Any], fps: int) -> None:
    picked = frames[: min(24, len(frames))]
    sec = timeline_stretch_sec(spec, 5.5)
    w = out.parent / f"_w_{out.stem}"
    seq = w / "seq"
    symlink_seq(seq, picked)
    input_fps = max(4.0, len(picked) / sec)
    enc_fps = min(fps, int(spec.get("timelapseEncodeFps", 60)))
    vf = _timelapse_vf(spec, enc_fps, sec, fade_in=1.0, fade_out=0.8)
    subprocess.run(
        [
            ffmpeg, "-y",
            "-framerate", f"{input_fps:.3f}",
            "-i", str(seq / "f_%06d.png"),
            "-vf", vf, "-t", str(sec),
            *_encode_args(spec), str(out),
        ],
        check=True,
        capture_output=True,
    )


def _caption_stagger_sec(spec: dict[str, Any]) -> tuple[float, float, float, float, float, float, float]:
    """Key fade, bullet base delay, stagger, bullet fade, future delay, future fade, read dwell."""
    return (
        float(spec.get("captionLine1FadeSec", 2.5)),
        float(spec.get("captionLine2DelaySec", 3.0)),
        float(spec.get("captionBulletStaggerSec", 1.8)),
        float(spec.get("captionBulletFadeSec", 2.0)),
        float(spec.get("captionLine3DelaySec", 11.0)),
        float(spec.get("captionFutureFadeSec", 2.0)),
        float(spec.get("captionReadPauseSec", 7.0)),
    )


def _bullet_alpha_at(t_sec: float, bi: int, spec: dict[str, Any]) -> float:
    _, base, stagger, fade, _, _, _ = _caption_stagger_sec(spec)
    start = base + bi * stagger
    return min(1.0, max(0.0, (t_sec - start) / max(0.01, fade)))


def _hold_keyframe_indices(
    total_frames: int,
    l2_f: int,
    l3_f: int,
    delay_f: int,
    fade_f: int,
    l1_fade: int,
    read_pause_f: int = 0,
    *,
    fps: int = 30,
    spec: dict[str, Any] | None = None,
) -> list[int]:
    keys: set[int] = {
        0,
        total_frames - 1,
        l1_fade,
        l2_f,
        l3_f,
        min(total_frames - 1, l3_f + l1_fade),
        min(total_frames - 1, l3_f + l1_fade + read_pause_f),
        delay_f,
        min(total_frames - 1, delay_f + fade_f),
    }
    if spec:
        l1s, b0, stagger, bfade, f0, ffade, _dwell = _caption_stagger_sec(spec)
        l1_f = max(1, int(l1s * fps))
        keys.update(range(0, min(total_frames, l1_f + 1)))
        for bi in range(3):
            start = int((b0 + bi * stagger) * fps)
            end = min(total_frames, start + max(1, int(bfade * fps)) + 1)
            keys.update(range(start, end))
        f_start = int(f0 * fps)
        keys.update(range(f_start, min(total_frames, f_start + max(1, int(ffade * fps)) + 1)))
        dwell_start = min(total_frames - 1, f_start + max(1, int(ffade * fps)))
        step = max(1, fps // 2)
        keys.update(range(dwell_start, total_frames, step))
    return sorted(i for i in keys if 0 <= i < total_frames)


def _timelapse_vf(spec: dict[str, Any], enc_fps: int, sec: float, *, fade_in: float, fade_out: float) -> str:
    base = prep_vf(spec)
    if spec.get("staticSceneMotion", True):
        return (
            f"{base},fps={enc_fps:.3f},"
            f"fade=t=in:st=0:d={fade_in:.2f},"
            f"fade=t=out:st={max(0.0, sec - fade_out):.2f}:d={fade_out:.2f}"
        )
    return (
        f"{base},minterpolate=fps={enc_fps}:mi_mode=blend,"
        f"fade=t=in:st=0:d={fade_in:.2f},"
        f"fade=t=out:st={max(0.0, sec - fade_out):.2f}:d={fade_out:.2f}"
    )


def hold_segment(
    ffmpeg: str,
    frame_path: Path,
    milestone: dict[str, Any],
    out: Path,
    spec: dict[str, Any],
    index: int,
    total: int,
    fps: int,
    work: Path,
    cache_dir: Path | None = None,
) -> None:
    def build(o: Path) -> None:
        _hold_segment_build(ffmpeg, frame_path, milestone, o, spec, index, total, fps, work)

    if cache_dir and spec.get("segmentCache", True):
        cache = _load("cache", "demo-recap-segment-cache.py")
        planned = milestone.get("_plannedHoldSec") or spec.get("milestoneHoldSec", 14)
        key = cache.segment_key(
            spec,
            "hold",
            f"{index}_{float(planned):.2f}_{milestone.get('line1','')[:40]}_{frame_path.name}",
            [frame_path],
        )
        cache.build_or_cache(cache_dir, key, build, out)
    else:
        build(out)


def _hold_segment_build(
    ffmpeg: str,
    frame_path: Path,
    milestone: dict[str, Any],
    out: Path,
    spec: dict[str, Any],
    index: int,
    total: int,
    fps: int,
    work: Path,
) -> None:
    compose = _load("compose", "compose-demo-recap.py")
    visual = _load("visual", "demo-recap-visual-enhance.py")
    hold_sec = float(
        milestone.get("_plannedHoldSec")
        or milestone.get("holdSec")
        or (spec.get("subbeatHoldSec", 8.0) if milestone.get("beatKind") == "subbeat" else spec.get("milestoneHoldSec", 14))
    )
    hold_sec = timeline_stretch_sec(spec, hold_sec)
    use_cine = bool(spec.get("cinematicCamera", False)) and bool(spec.get("holdCinematicMotion", True))
    show_ann = bool(spec.get("showAnnotations", True))
    ann_delay = float(spec.get("annotationDelaySec", 5.0))
    ann_fade = float(spec.get("annotationFadeSec", 0.65))
    l1s, l2, _, _, l3, ffade, _dwell = _caption_stagger_sec(spec)

    img = compose.Image.open(frame_path).convert("RGB")
    if spec.get("videoEnhance", True):
        img = visual.enhance_scene_image(
            img, upscale=float(spec.get("sceneUpscale", 2.0))
        )

    total_frames = max(2, int(hold_sec * fps))
    delay_f = min(total_frames - 1, int(ann_delay * fps))
    fade_f = max(1, int(ann_fade * fps))
    l2_f, l3_f = int(l2 * fps), int(l3 * fps)
    l1_fade = max(1, int(l1s * fps))
    read_pause_f = max(1, int(_dwell * fps))
    use_opencv = milestone.get("annotationSource") in ("opencv", "cursor") and milestone.get("regions")
    chapter_focus = bool(spec.get("chapterFocusAnnotations", False))
    keyframe_mode = bool(spec.get("holdKeyframeMode", True))

    seq_dir = work / f"hold_{index:03d}" / "seq"
    seq_dir.mkdir(parents=True, exist_ok=True)
    _clear_seq_pngs(seq_dir)

    indices = (
        _hold_keyframe_indices(
            total_frames, l2_f, l3_f, delay_f, fade_f, l1_fade, read_pause_f, fps=fps, spec=spec
        )
        if keyframe_mode
        else list(range(total_frames))
    )

    for seq_i, fi in enumerate(indices):
        t_sec = fi / max(1, fps)
        line1_a = min(1.0, t_sec / max(0.01, l1s))
        bullet_alphas = [_bullet_alpha_at(t_sec, bi, spec) for bi in range(3)]
        line2_a = max(bullet_alphas) if bullet_alphas else 0.0
        line3_a = min(1.0, max(0.0, (t_sec - l3) / max(0.01, ffade)))
        if fi < delay_f:
            ann_a = 0.0
        elif fi < delay_f + fade_f:
            ann_a = (fi - delay_f) / fade_f
        else:
            ann_a = 1.0
        cine_t = (fi / max(1, total_frames - 1)) if use_cine else None
        fr = compose.render_captioned_frame(
            img,
            line1=milestone.get("captionKey") or milestone.get("line1", ""),
            line2=milestone.get("line2", ""),
            line3=milestone.get("captionFuture") or milestone.get("line3"),
            caption_bullets=milestone.get("captionBullets"),
            future_hint=milestone.get("captionFuture") or milestone.get("line3"),
            chapter=str(milestone.get("chapter", ""))[:52],
            index=index,
            total=total,
            accent=tuple(milestone.get("accent", [24, 235, 158])),
            milestone=milestone,
            annotate=show_ann and ann_a > 0.02,
            annotation_alpha=ann_a,
            line1_alpha=line1_a,
            line2_alpha=line2_a,
            line3_alpha=line3_a,
            lecture_mode=bool(spec.get("lectureMode", True)),
            ai_regions_only=not use_opencv,
            chapter_focus_annotations=chapter_focus and not use_opencv,
            scene_fit=str(spec.get("sceneFit", "contain")),
            cinematic_t=cine_t,
            cinematic_seed=index,
            javafx_effects=bool(spec.get("javafxEffects", True)),
            frame_index=fi,
            total_frames=total_frames,
            javafx_spec=spec,
            bullet_alphas=bullet_alphas,
            hold_time_sec=t_sec,
        )
        fr.save(seq_dir / f"f_{seq_i:06d}.png")

    n_keys = len(indices)
    if keyframe_mode and n_keys < total_frames:
        # Spread keyframes across full hold — never use a high min fps (that truncates to ~1s).
        input_fps = max(0.2, n_keys / max(hold_sec, 0.5))
        enc_fps = min(fps, int(spec.get("timelapseEncodeFps", 60)))
        if spec.get("staticSceneMotion", True):
            vf = f"{prep_vf(spec)},fps={enc_fps}"
        else:
            vf = f"{prep_vf(spec)},minterpolate=fps={enc_fps}:mi_mode=blend"
    else:
        input_fps = fps
        enc_fps = min(fps, int(spec.get("timelapseEncodeFps", 60)))
        vf = prep_vf(spec) + f",fps={enc_fps}"

    subprocess.run(
        [
            ffmpeg, "-y", "-framerate", f"{input_fps:.3f}",
            "-start_number", "0",
            "-i", str(seq_dir / "f_%06d.png"),
            "-vf", vf, "-t", str(hold_sec),
            *_encode_args(spec), str(out),
        ],
        check=True,
        capture_output=True,
    )


def resolve_card(run_dir: Path, spec: dict[str, Any], *keys: str) -> Path | None:
    for k in keys:
        raw = spec.get(k)
        if not raw:
            continue
        p = Path(str(raw)).expanduser()
        if not p.is_absolute():
            p = run_dir / p
        if p.is_file():
            return p.resolve()
    return None


def _run_parallel(tasks: list[tuple[str, Callable[[], None]]], workers: int) -> None:
    if workers <= 1 or len(tasks) <= 1:
        for label, fn in tasks:
            fn()
        return
    with ThreadPoolExecutor(max_workers=workers) as pool:
        futs = {pool.submit(fn): label for label, fn in tasks}
        for fut in as_completed(futs):
            label = futs[fut]
            try:
                fut.result()
            except Exception as e:
                raise RuntimeError(f"Segment failed ({label}): {e}") from e


def compose_presentation(
    run_dir: Path,
    output: Path,
    spec: dict[str, Any],
    *,
    milestone_filter: list[int] | None = None,
    narration_only: bool = False,
) -> Path:
    return _compose_presentation_impl(
        run_dir, output, spec, milestone_filter=milestone_filter, narration_only=narration_only
    )[0]


def _compose_presentation_impl(
    run_dir: Path,
    output: Path,
    spec: dict[str, Any],
    *,
    milestone_filter: list[int] | None = None,
    narration_only: bool = False,
) -> tuple[Path, list[Path], list[int]]:
    ffmpeg = find_ffmpeg()
    _configure_canvas(spec)
    fps = int(spec.get("outputFps", 30))
    workers = int(spec.get("parallelWorkers", 4))
    frames = sorted((run_dir / "timelapse").glob("tl_*.png"))
    if not frames:
        raise SystemExit("timelapse/tl_*.png required")

    captions = _load("captions", "demo-recap-captions.py")
    opencv = _load("opencv", "demo-recap-opencv-annotate.py")
    milestones = sorted(spec.get("milestones", []), key=lambda m: int(m.get("frame", 0)))
    all_milestones = milestones
    if milestone_filter is not None:
        milestones = [all_milestones[i] for i in milestone_filter if 0 <= i < len(all_milestones)]
    for m in milestones:
        m["frame"] = min(max(0, int(m.get("frame", 0))), len(frames) - 1)
        captions.fill_milestone_captions(m)
        captions.fill_milestone_narrator_script(m, force=True)
    opencv.sync_region_labels_from_timeline(milestones)
    spec["_milestoneFrames"] = [int(m.get("frame", 0)) for m in all_milestones]

    work = Path(str(spec.get("composeWorkDir") or run_dir / "_presentation_compose")).expanduser()
    work.mkdir(parents=True, exist_ok=True)
    cache_dir = work / "segment_cache"
    if not narration_only:
        for old in work.glob("seg_*.mp4"):
            old.unlink()

    intro = resolve_card(run_dir, spec, "introCardImage", "approvedIntroImage")
    outro = resolve_card(run_dir, spec, "outroCardImage", "approvedOutroImage")
    if not intro or not outro:
        raise SystemExit("ApprovedIntro.png / ApprovedOutro.png required")

    plan_dir = work / "_narr_plan"
    narr_mod = None
    voice = str(spec.get("narratorVoice", "Andrew"))
    narr_mod = _load("narrator", "demo-recap-narrator.py")
    spec = narr_mod.flatten_narrator_personal_settings(spec)
    rate = narr_mod.personal_say_rate(spec)
    engine = str(spec.get("narratorEngine", "auto"))
    use_full_script = narr_mod.uses_full_script_narration(spec)
    if use_full_script:
        spec = {**spec, "syncHoldToNarration": False}
        print("Narration: full Cursor script mode — holds sized for captions only (voice reads whole script)")

    if bool(spec.get("narratorEnabled", True)) and spec.get("syncHoldToNarration", True):
        engine = narr_mod.pick_speech_engine(spec, run_dir)
        sync_mode = str(spec.get("narrationSyncMode", "estimate")).lower()
        spec["_narrPlanDir"] = str(plan_dir)
        if sync_mode == "speech_first":
            print(
                "Speech-first sync: synthesize each cue → measure duration → encode "
                "video holds to match (perfect lip-slot sync)…"
            )
            narr_mod.apply_speech_first_sync_plan(
                milestones,
                spec,
                voice=voice,
                rate=rate,
                engine=engine,
                run_dir=run_dir,
                plan_dir=plan_dir,
            )
        else:
            plan_mode = str(spec.get("narrationPlanMode", "estimate")).lower()
            if plan_mode in ("estimate", "fast", "words"):
                print("Planning clip durations (word estimate — fast)…")
            else:
                print("Planning clip durations (synthesizing each cue for timing)…")
            for m in milestones:
                m["_plannedHoldSec"] = narr_mod.planned_hold_sec(
                    m, spec, voice=voice, rate=rate, engine=engine, run_dir=run_dir, plan_dir=plan_dir
                )
            spec["_plannedIntroSec"] = narr_mod.planned_intro_sec(
                spec,
                narr_mod.intro_narration_script(spec),
                voice=voice,
                rate=rate,
                engine=engine,
                run_dir=run_dir,
                plan_dir=plan_dir,
            )
            spec["_plannedOutroSec"] = narr_mod.planned_outro_sec(
                spec,
                narr_mod.outro_narration_script(spec),
                voice=voice,
                rate=rate,
                engine=engine,
                run_dir=run_dir,
                plan_dir=plan_dir,
            )

    clip_plan: list[tuple[str, Path, Callable[[], None]]] = []
    hold_clip_indices: list[int] = []
    clip_idx = 0

    intro_mp4 = work / "seg_intro.mp4"

    intro_sec = timeline_stretch_sec(
        spec, float(spec.get("_plannedIntroSec") or spec.get("introSec", 9.0))
    )

    def do_intro() -> None:
        if _reuse_cached_segment(
            narration_only, spec, intro_mp4, "intro", planned_sec=intro_sec
        ):
            return
        png_clip(ffmpeg, intro, intro_mp4, intro_sec, fps, fade_out=0.9, spec=spec)

    clip_plan.append(("intro", intro_mp4, do_intro))
    clip_idx += 1

    bridge_mp4 = work / "seg_bridge.mp4"

    def do_bridge() -> None:
        if _reuse_cached_segment(narration_only, spec, bridge_mp4, "bridge"):
            return
        bridge_segment(ffmpeg, frames, bridge_mp4, spec, fps)

    clip_plan.append(("bridge", bridge_mp4, do_bridge))
    clip_idx += 1

    playthroughs: list[Path] = []
    playthrough_idx = 0
    if spec.get("presentationPlayModeBroll", True):
        playthroughs = _resolve_playthrough_movs(run_dir)
        if playthroughs:
            print(
                f"Play Mode B-roll: {len(playthroughs)} screen recording(s) → inserts after play-disk beats",
                flush=True,
            )
        else:
            print(
                "Play Mode B-roll: no uploads/playthroughs/*.mp4|*.mov — Scene view only",
                flush=True,
            )

    prev_f = 0
    for mi, m in enumerate(milestones):
        fidx = int(m["frame"])
        if mi > 0 and fidx > prev_f + int(spec.get("minTimelapseGapFrames", 6)):
            tl = work / f"seg_tl_{mi:03d}.mp4"
            chunk = frames[prev_f:fidx]

            def do_tl(tl=tl, chunk=chunk, chunk_start=prev_f) -> None:
                if _reuse_cached_segment(narration_only, spec, tl, f"tl_{mi}"):
                    return
                timelapse_segment(
                    ffmpeg, chunk, tl, spec, fps, cache_dir, chunk_start=chunk_start
                )

            clip_plan.append((f"tl_{mi}", tl, do_tl))
            clip_idx += 1
        hold = work / f"seg_hold_{mi:03d}.mp4"

        planned_hold = float(
            m.get("_plannedHoldSec")
            or (
                spec.get("subbeatHoldSec", 8.0)
                if m.get("beatKind") == "subbeat"
                else spec.get("milestoneHoldSec", 14)
            )
        )
        planned_hold = timeline_stretch_sec(spec, planned_hold)

        def do_hold(hold=hold, m=m, mi=mi, planned_hold=planned_hold) -> None:
            if _reuse_cached_segment(
                narration_only,
                spec,
                hold,
                f"hold_{mi}",
                planned_sec=planned_hold,
            ):
                return
            hold_segment(ffmpeg, frames[fidx], m, hold, spec, mi, len(milestones), fps, work, cache_dir)

        clip_plan.append((f"hold_{mi}", hold, do_hold))
        hold_clip_indices.append(clip_idx)
        clip_idx += 1

        if playthroughs and _milestone_wants_play_broll(m):
            mov = playthroughs[playthrough_idx % len(playthroughs)]
            playthrough_idx += 1
            play_out = work / f"seg_play_{mi:03d}.mp4"
            broll_sec = float(spec.get("playModeBrollSec", 42.0))
            start_sec = float(m.get("playthroughStartSec", 0.0))

            def do_play(
                play_out=play_out,
                mov=mov,
                start_sec=start_sec,
                broll_sec=broll_sec,
                mi=mi,
            ) -> None:
                if _reuse_cached_segment(narration_only, spec, play_out, f"play_{mi}"):
                    return
                hybrid = _load("hybrid", "hybrid_recap_common.py")
                hybrid.screen_segment(ffmpeg, mov, play_out, start_sec, broll_sec)

            clip_plan.append((f"play_{mi}", play_out, do_play))
            clip_idx += 1

        prev_f = fidx

    if prev_f < len(frames) - 8:
        tail_frames = frames[prev_f:]
        if len(tail_frames) > 400:
            tail_frames = pick_timelapse_frames(
                tail_frames,
                int(spec.get("tlTailMaxFrames", 48)),
                chunk_start=prev_f,
                milestone_frames=spec.get("_milestoneFrames"),
            )
        tl = work / "seg_tail.mp4"

        def do_tail(tail_frames=tail_frames, chunk_start=prev_f) -> None:
            if _reuse_cached_segment(narration_only, spec, tl, "tail"):
                return
            timelapse_segment(
                ffmpeg, tail_frames, tl, spec, fps, cache_dir, chunk_start=chunk_start
            )

        clip_plan.append(("tail", tl, do_tail))
        clip_idx += 1

    out_mp4 = work / "seg_outro.mp4"

    outro_sec = timeline_stretch_sec(
        spec, float(spec.get("_plannedOutroSec") or spec.get("outroSec", 10.0))
    )

    def do_outro() -> None:
        if _reuse_cached_segment(
            narration_only, spec, out_mp4, "outro", planned_sec=outro_sec
        ):
            return
        png_clip(ffmpeg, outro, out_mp4, outro_sec, fps, fade_in=0.4, spec=spec)

    clip_plan.append(("outro", out_mp4, do_outro))

    if narration_only:
        print("Narration-only: reusing existing video segments…")
    else:
        print(f"Encoding {len(clip_plan)} segments (parallel={min(workers, len(clip_plan))})…")
    _run_parallel([(label, fn) for label, _, fn in clip_plan], workers)

    clips = [p for _, p, _ in clip_plan]
    clip_labels = [label for label, _, _ in clip_plan]
    final = work / "_final_video.mp4"
    must_reconcat = not narration_only or spec.get("syncHoldToNarration", True)
    if narration_only and final.is_file() and final.stat().st_size > 1024 and not must_reconcat:
        print("Narration-only: reusing graded master video")
    else:
        raw = work / "_concat_raw.mp4"
        producer = _load("producer", "demo-recap-producer.py")
        xfade = 0.0 if spec.get("noTransitions") else float(spec.get("segmentXfadeSec", 0.42))
        producer.concat_segments_safe(ffmpeg, clips, raw, xfade_sec=xfade, output_fps=float(fps))
        color = _load("color", "demo-recap-color.py")
        master_vf = color.master_with_optional_lut(_TOOLS, spec)
        subprocess.run(
            [
                ffmpeg, "-y", "-i", str(raw),
                "-vf", f"{master_vf},fps={fps}",
                *_encode_args(spec),
                str(final),
            ],
            check=True,
            capture_output=True,
        )

    must_finalize_narration = narr_mod.require_narration_before_finalize(spec)

    if bool(spec.get("narratorEnabled", True)):
        narr = _load("narrator", "demo-recap-narrator.py")
        wav = work / "narration.wav"
        engine = str(spec.get("narratorEngine", "auto"))
        voice = str(spec.get("narratorVoice", "Andrew"))
        rate = narr_mod.personal_say_rate(spec)
        narr_ok = False
        full_script = ""
        pv_ready, pv_reason = narr.personal_voice_capture_ready(spec, run_dir)
        if narr.is_personal_narration(spec) and not pv_ready:
            print(f"Personal Voice: {pv_reason}", flush=True)

        if narr.try_apply_preserved_personal_voice(final, wav, work, spec):
            narr_ok = True
        elif use_full_script:
            script_after_video = bool(spec.get("narrationScriptAfterVideo", True))
            if script_after_video:
                director = _load("cursor_dir", "demo-recap-cursor-director.py")
                regen = bool(spec.get("regenNarrationScript"))
                ensure_full = director.ensure_full_narration_script(
                    run_dir,
                    milestones,
                    spec,
                    final,
                    build_mode=str(spec.get("buildMode", "FullWorld additive surface_build")),
                    regen=regen,
                    prefer_local=narration_only,
                )
                if ensure_full:
                    spec = {**spec, "fullNarrationScript": ensure_full}
            full_script = narr.resolve_full_narration_script(run_dir, spec, milestones)
            align_to_clips = bool(
                spec.get("fullScriptAlignToMilestones", False)
            )
            if full_script and align_to_clips:
                audit_rows = narr.audit_clip_narration_sync(
                    clips, milestones, hold_clip_indices, full_script, spec, clip_labels
                )
                audit_path = run_dir / "RecapNarrationSyncAudit.json"
                audit_path.write_text(
                    json.dumps({"clips": audit_rows, "fullScriptWords": len(full_script.split())}, indent=2)
                    + "\n",
                    encoding="utf-8",
                )
                print(
                    f"Narration sync audit: {len(audit_rows)} clips → {audit_path.name}",
                    flush=True,
                )
            if full_script:
                if not pv_ready:
                    print(f"Personal Voice: {pv_reason}", flush=True)
                if align_to_clips:
                    print(
                        "Full-script sync: Personal Voice per video clip "
                        "(voice aligned to on-screen holds, not one monolithic track)…",
                        flush=True,
                    )
                    narration_cues, cue_labels = narr.build_full_script_narration_cues(
                        clips, milestones, hold_clip_indices, full_script, spec, clip_labels
                    )
                    narr_spec = {
                        **spec,
                        "_narrationCueLabels": cue_labels,
                        "_narrPlanDir": str(plan_dir),
                    }
                    narr_ok = narr.build_narration_for_clips(
                        clips,
                        milestones,
                        wav,
                        voice=voice,
                        rate=rate,
                        narration_cues=narration_cues,
                        engine=engine,
                        spec=narr_spec,
                        run_dir=run_dir,
                        hold_indices=hold_clip_indices,
                    )
                else:
                    print(
                        "Full-script sync: one continuous Personal Voice capture "
                        "(natural speech flow, not per-clip staccato)…",
                        flush=True,
                    )
                    narr_ok = narr.build_narration_for_full_video(
                        final,
                        full_script,
                        wav,
                        voice=voice,
                        rate=rate,
                        engine=engine,
                        spec=spec,
                        run_dir=run_dir,
                        milestones=all_milestones,
                    )
                if narr_ok and narr.is_personal_narration(spec):
                    method = str(spec.get("_lastCaptureMethod") or "say+mysay")
                    narr.write_narration_capture_record(
                        run_dir,
                        capture_method=method,
                        voice_name=str(narr.resolve_personal_voice_name(spec, run_dir) or voice),
                        spec=spec,
                    )
            else:
                print(
                    "ERROR: fullScript mode but no narration script — "
                    "set CURSOR_API_KEY, run after graded video, or add DemoRecapFullNarration.json",
                    file=sys.stderr,
                )
        else:
            narration_cues: dict[int, tuple[str, float]] = {}
            cue_labels: dict[int, str] = {}
            if spec.get("narrateIntro", True):
                narration_cues[0] = (
                    narr.intro_narration_script(spec),
                    float(spec.get("introNarratorOffsetSec", 0.6)),
                )
                cue_labels[0] = "intro card"
            for hi, clip_i in enumerate(hold_clip_indices):
                if hi < len(milestones):
                    m = milestones[hi]
                    if m.get("_skipNarration"):
                        continue
                    off = float(
                        m.get("_plannedNarratorOffsetSec")
                        or narr.hold_narrator_offset_sec(spec, m)
                    )
                    narration_cues[clip_i] = (narr.beat_narration_script(m, spec), off)
                    cue_labels[clip_i] = f"hold {hi + 1}"
            if spec.get("narrateOutro", True):
                out_i = len(clips) - 1
                narration_cues[out_i] = (
                    narr.outro_narration_script(spec),
                    float(spec.get("outroNarratorOffsetSec", 0.6)),
                )
                cue_labels[out_i] = "outro card"
            narr_spec = {
                **spec,
                "_narrationCueLabels": cue_labels,
                "_narrPlanDir": str(plan_dir),
            }
            parts_dir = work / "_narr_parts"
            if (
                narr.should_preserve_personal_capture(spec)
                and parts_dir.is_dir()
                and sorted(parts_dir.glob("p_*.wav"))
                and not spec.get("forceRegenNarration")
            ):
                if narr.concat_narration_parts(parts_dir, wav, spec):
                    spec["_preservedPersonalVoice"] = True
                    narr_ok = True
                    print(
                        "Preserved per-clip Personal Voice (_narr_parts; no regen)",
                        flush=True,
                    )
            if not narr_ok and (pv_ready or narr.is_personal_narration(spec)):
                narr_ok = narr.build_narration_for_clips(
                    clips,
                    milestones,
                    wav,
                    voice=voice,
                    rate=rate,
                    narration_cues=narration_cues,
                    engine=engine,
                    spec=narr_spec,
                    run_dir=run_dir,
                )

        finalized = False
        if narr_ok:
            try:
                video_for_mux = _apply_bot_avatar_overlay(
                    ffmpeg, final, wav, work, run_dir, spec
                )
                narr.mux_narration_video_only(
                    video_for_mux,
                    wav,
                    output,
                    volume=float(spec.get("narratorVolume", 1.0)),
                    spec=spec,
                )
                finalized = narr.mp4_has_audio_stream(output)
                if finalized:
                    print(f"Finalized {output} (video + narration / {engine})")
            except subprocess.CalledProcessError as exc:
                print(f"Narrator mux failed: {exc}", file=sys.stderr)

        # Remux cached narration only when Personal Voice was verified for this capture.
        if not finalized and wav.is_file() and narr.wav_is_audible(wav):
            has_script = bool(full_script) if use_full_script else True
            verified = narr.narration_wav_is_verified_personal(work, spec)
            if has_script and verified and narr.remux_preview_with_narration(work, output, spec=spec):
                finalized = narr.mp4_has_audio_stream(output)
            elif has_script and not verified and narr.is_personal_narration(spec):
                print(
                    "WARN: skipping cached narration.wav — not verified Personal Voice "
                    "(will not mux wrong voice over your recap)",
                    file=sys.stderr,
                )

        if must_finalize_narration and not finalized:
            silent_out = work / "_preview_pending_narration.mp4"
            shutil.copy2(final, silent_out)
            if output.is_file():
                try:
                    output.unlink()
                except OSError:
                    pass
            preview_flag = " --preview" if "_preview" in str(work) else ""
            raise SystemExit(
                "Recap NOT finalized for sharing — Personal Voice narration incomplete.\n"
                f"  Graded silent video: {final}\n"
                f"  Pending mux target: {output}\n"
                "Fix: run headless narration, then:\n"
                f"    bash run-recap-headless.sh {run_dir}{preview_flag} --narration-only"
            )
        if not finalized:
            if wav.is_file() and narr.wav_is_audible(wav):
                print(
                    f"WARN: narration mux failed; not overwriting {output} with silent video "
                    f"(audible {wav.name} still in work dir)",
                    file=sys.stderr,
                )
            else:
                shutil.copy2(final, output)
                print(f"Wrote {output} (video only — narration not required for this run)", file=sys.stderr)
    else:
        shutil.copy2(final, output)
        print(f"Wrote {output} (producer v3, narrator disabled)")

    if output.is_file() and output.stat().st_size > 1024:
        narr = _load("narrator", "demo-recap-narrator.py")
        if bool(spec.get("narratorEnabled", True)) and narr.require_narration_before_finalize(spec):
            if narr.mp4_has_audio_stream(output):
                open_recap_video(output)
        else:
            open_recap_video(output)

    return output, clips, hold_clip_indices


def main() -> int:
    run = Path(sys.argv[1]).expanduser().resolve()
    out = run / "DemoRecapPresentation.mp4"
    if len(sys.argv) > 2:
        out = Path(sys.argv[2]).expanduser()
    spec = json.loads((run / "DemoRecapTimeline.json").read_text())
    compose_presentation(run, out, spec)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
