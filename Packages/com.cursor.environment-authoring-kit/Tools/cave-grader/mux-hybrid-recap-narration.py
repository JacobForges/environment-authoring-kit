#!/usr/bin/env python3
"""
Mux Personal Voice onto the full hybrid recap silent master.

Speech-first: synthesize each clip cue → measure duration → extend segment (tpad) if
speech exceeds the slot → re-concat → mux. Personal Voice never time-stretched or clipped.

Uses DemoRecapFullNarration.json (Cursor ~2051-word script), NOT preview/producer path.
"""
from __future__ import annotations

import importlib.util
import json
import shutil
import sys
from pathlib import Path

_TOOLS = Path(__file__).resolve().parent


def _load(name: str, file: str):
    spec = importlib.util.spec_from_file_location(name, _TOOLS / file)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def _apply_narrator_defaults(spec: dict, run: Path, approved_path: Path) -> dict:
    narr = _load("narr", "demo-recap-narrator.py")
    return narr.apply_recap_narrator_defaults(
        spec,
        run_dir=run,
        approved_path=approved_path if approved_path.is_file() else None,
    )


def _clear_prior_narration(work: Path) -> None:
    for name in ("narration.wav", "_full_narr_raw.wav"):
        p = work / name
        if p.is_file():
            p.unlink()
    parts = work / "_narr_parts"
    if parts.is_dir():
        shutil.rmtree(parts, ignore_errors=True)


def _remux_audio_only(capture: Path, spec: dict, work: Path) -> int:
    """Re-mux narration onto existing bot video — fast volume/loudness fix."""
    hybrid = _load("hybrid", "hybrid_recap_common.py")
    narr = _load("narr", "demo-recap-narrator.py")
    compose = _load("compose", "compose-presentation-recap.py")
    envkit = _load("envkit", "envkit_paths.py")

    spec = _apply_narrator_defaults(spec, capture, envkit.approved_cards_path())
    spec["composeWorkDir"] = str(work)
    wav = work / "narration.wav"
    bot_video = work / "_final_video_bot.mp4"
    final_video = bot_video if bot_video.is_file() else work / "_final_video.mp4"
    if not wav.is_file() or not final_video.is_file():
        print("ERROR: missing narration.wav or graded video for remux", file=sys.stderr)
        return 1

    vol = narr.narrator_mux_volume(spec)
    loud = narr.narration_mux_loudnorm(spec)
    print(
        f"Remux audio only: volume={vol}x, loudnorm={'on' if loud else 'off'} "
        f"→ {final_video.name}",
        flush=True,
    )

    out_capture = capture / "HybridRecapPresentation.mp4"
    if not narr.remux_preview_with_narration(work, out_capture, spec=spec, video=final_video):
        return 1

    desktop = Path.home() / "Desktop" / "HybridRecap-Full"
    desktop.mkdir(exist_ok=True)
    try:
        shutil.copy2(out_capture, desktop / "HybridRecapPresentation.mp4")
    except OSError as exc:
        print(f"WARNING: Desktop copy skipped: {exc}", flush=True)

    ffprobe = hybrid.find_ffprobe()
    video_dur = hybrid.probe_duration(final_video, ffprobe)
    print(f"Remuxed hybrid recap: {out_capture} ({video_dur:.1f}s)")
    compose.open_recap_video(out_capture)
    return 0


def _repolish_hybrid_video(capture: Path, spec: dict, work: Path) -> int:
    """Rebuild holds with caption+JavaFX at current durations; polish screens; remux."""
    hybrid = _load("hybrid", "hybrid_recap_common.py")
    narr = _load("narr", "demo-recap-narrator.py")
    compose = _load("compose", "compose-presentation-recap.py")
    compose_hybrid = _load("chybrid", "compose-hybrid-recap.py")
    envkit = _load("envkit", "envkit_paths.py")

    spec = _apply_narrator_defaults(spec, capture, envkit.approved_cards_path())
    spec["composeWorkDir"] = str(work)
    ffmpeg = hybrid.find_ffmpeg()
    ffprobe = hybrid.find_ffprobe()
    fps = int(spec.get("hybridComposeFps", 60))
    hybrid.set_compose_fps(fps)
    javafx_on = bool(spec.get("javafxEffects", True))
    compose._configure_canvas(spec)

    milestones = spec.get("milestones") or []
    clips = hybrid.ordered_hybrid_segment_paths(work)
    if len(milestones) < len(clips):
        print(f"WARNING: {len(milestones)} milestones vs {len(clips)} clips", flush=True)

    frames = hybrid.list_timelapse_frames(capture / "timelapse")
    approved = envkit.approved_dir()
    intro_png = approved / "ApprovedIntro.png"
    outro_png = approved / "ApprovedOutro.png"
    if not intro_png.is_file():
        intro_png = frames[0]
    if not outro_png.is_file():
        outro_png = frames[-1]

    total_holds = sum(1 for m in milestones if m.get("kind") == "hold" or m.get("beatKind") == "checkpoint")
    hold_i = 0
    print(f"Repolish hybrid video: {fps} fps, JavaFX={'on' if javafx_on else 'off'}", flush=True)

    for ci, seg in enumerate(clips):
        m = milestones[ci] if ci < len(milestones) else {}
        dur = hybrid.probe_duration(seg, ffprobe)
        kind = str(m.get("kind") or m.get("beatKind") or "")
        label = seg.name

        if kind in ("intro", "outro") or kind == "hold" or "hold" in label:
            if kind == "intro":
                png = intro_png
            elif kind == "outro":
                png = outro_png
            else:
                frame_idx = int(m.get("frame", hold_i % len(frames)))
                png = frames[min(frame_idx, len(frames) - 1)]
                hold_i += 1
            meta = {**m, "_plannedHoldSec": dur}
            print(f"  Caption+JavaFX hold {label} ({dur:.1f}s)…", flush=True)
            compose.hold_segment(
                ffmpeg, png, meta, seg, spec,
                hold_i, max(total_holds, 1), fps, work,
            )
            got = hybrid.probe_duration(seg, ffprobe)
            if got + 0.2 < dur:
                hybrid.extend_segment_duration(ffmpeg, seg, dur, ffprobe=ffprobe)
            elif got > dur + 0.25:
                hybrid.trim_segment_duration(ffmpeg, seg, dur, ffprobe=ffprobe)
        elif javafx_on:
            print(f"  JavaFX screen polish {label} ({dur:.1f}s)…", flush=True)
            compose_hybrid._javafx_polish_segment(ffmpeg, seg, enabled=True, spec=spec)

    final_silent = work / "_final_video.mp4"
    hybrid.concat_segments(ffmpeg, clips, final_silent)
    wav = work / "narration.wav"
    if not wav.is_file():
        print("ERROR: missing narration.wav — run full mux first", file=sys.stderr)
        return 1

    final_video = final_silent
    if spec.get("botAvatarOverlay", True) and spec.get("botAvatarLipSync", True):
        bot_spec = importlib.util.spec_from_file_location("bot", _TOOLS / "jacob-adkins-bot.py")
        bot_mod = importlib.util.module_from_spec(bot_spec)
        assert bot_spec.loader
        bot_spec.loader.exec_module(bot_mod)
        bot_video = work / "_final_video_bot.mp4"
        lipsync_mov = work / "_bot_lipsync" / f"{bot_mod.LIPSYNC_BASENAME}.mov"
        if lipsync_mov.is_file() and lipsync_mov.stat().st_size > 1024 * 1024:
            print(f"Reusing lip-sync avatar → {lipsync_mov.name}", flush=True)
            bot_mod.apply_animated_avatar_to_video(
                ffmpeg, final_silent, bot_video, lipsync_mov, spec=spec,
            )
            if bot_video.is_file():
                final_video = bot_video
        else:
            print("WARNING: no lip-sync mov — video without bot overlay", flush=True)

    out_capture = capture / "HybridRecapPresentation.mp4"
    if not narr.remux_preview_with_narration(work, out_capture, spec=spec, video=final_video):
        return 1

    desktop = Path.home() / "Desktop" / "HybridRecap-Full"
    desktop.mkdir(exist_ok=True)
    try:
        shutil.copy2(out_capture, desktop / "HybridRecapPresentation.mp4")
    except OSError as exc:
        print(f"WARNING: Desktop copy skipped: {exc}", flush=True)

    video_dur = hybrid.probe_duration(final_video, ffprobe)
    print(f"Repolished hybrid recap: {out_capture} ({video_dur:.1f}s)")
    compose.open_recap_video(out_capture)
    return 0


def _finish_mux_from_checkpoint(capture: Path, spec: dict, work: Path) -> int:
    """Resume after voice + lip-sync render — overlay bot and remux only."""
    hybrid = _load("hybrid", "hybrid_recap_common.py")
    narr = _load("narr", "demo-recap-narrator.py")
    compose = _load("compose", "compose-presentation-recap.py")
    envkit = _load("envkit", "envkit_paths.py")

    spec = _apply_narrator_defaults(spec, capture, envkit.approved_cards_path())
    spec["composeWorkDir"] = str(work)
    ffmpeg = hybrid.find_ffmpeg()
    ffprobe = hybrid.find_ffprobe()

    final_silent = work / "_final_video.mp4"
    wav = work / "narration.wav"
    if not final_silent.is_file() or not wav.is_file():
        print("ERROR: checkpoint missing _final_video.mp4 or narration.wav", file=sys.stderr)
        return 1

    video_dur = hybrid.probe_duration(final_silent, ffprobe)
    spec["targetDurationSec"] = video_dur
    spec["fullNarrationTargetSec"] = video_dur
    print(f"Finish checkpoint: {video_dur:.1f}s video, narration ready", flush=True)

    final_video = final_silent
    if spec.get("botAvatarOverlay", True) and spec.get("botAvatarLipSync", True):
        bot_spec = importlib.util.spec_from_file_location("bot", _TOOLS / "jacob-adkins-bot.py")
        bot_mod = importlib.util.module_from_spec(bot_spec)
        assert bot_spec.loader
        bot_spec.loader.exec_module(bot_mod)
        avatar_export = capture / "JacobAdkinsBot-Avatar"
        bot_video = work / "_final_video_bot.mp4"
        lipsync_mov = work / "_bot_lipsync" / f"{bot_mod.LIPSYNC_BASENAME}.mov"
        if lipsync_mov.is_file() and lipsync_mov.stat().st_size > 1024 * 1024:
            print(f"Reusing lip-sync avatar mov → {lipsync_mov}", flush=True)
            bot_mod.apply_animated_avatar_to_video(
                ffmpeg, final_silent, bot_video, lipsync_mov, spec=spec,
            )
            if bot_video.is_file():
                final_video = bot_video
                avatar_export.mkdir(parents=True, exist_ok=True)
                try:
                    shutil.copy2(lipsync_mov, avatar_export / lipsync_mov.name)
                except OSError as exc:
                    print(f"WARNING: avatar export copy skipped: {exc}", flush=True)
                print(f"Lip-sync layered avatar baked → {bot_video}", flush=True)
        else:
            print("WARNING: no lip-sync mov — remuxing without bot overlay", flush=True)

    out_capture = capture / "HybridRecapPresentation.mp4"
    if not narr.remux_preview_with_narration(work, out_capture, spec=spec, video=final_video):
        return 1

    synced_silent = capture / "HybridRecapPresentation-SYNCED-SILENT.mp4"
    shutil.copy2(final_silent, synced_silent)
    shutil.copy2(final_silent, capture / "HybridRecapPresentation-SILENT.mp4")

    desktop = Path.home() / "Desktop" / "HybridRecap-Full"
    desktop.mkdir(exist_ok=True)
    try:
        shutil.copy2(out_capture, desktop / "HybridRecapPresentation.mp4")
        shutil.copy2(final_silent, desktop / "HybridRecapPresentation-SYNCED-SILENT.mp4")
    except OSError as exc:
        print(f"WARNING: Desktop copy skipped (disk full?): {exc}", flush=True)
        print(f"Watch final video on Lexar: {out_capture}", flush=True)

    print(f"Final hybrid recap: {out_capture} ({video_dur:.1f}s)")
    compose.open_recap_video(out_capture)
    return 0


def mux_hybrid_narration(
    capture: Path,
    *,
    finish_only: bool = False,
    remux_audio_only: bool = False,
    repolish_video: bool = False,
) -> int:
    capture = capture.expanduser().resolve()
    tl_path = capture / "DemoRecapTimeline.json"
    if not tl_path.is_file():
        print(f"Missing {tl_path}", file=sys.stderr)
        return 1

    spec = json.loads(tl_path.read_text(encoding="utf-8"))
    if spec.get("recapMode") != "hybrid_screencast":
        print(
            f"ERROR: recapMode is {spec.get('recapMode')!r}, expected hybrid_screencast",
            file=sys.stderr,
        )
        return 1

    work = capture / "_hybrid_compose"
    if remux_audio_only:
        if not work.is_dir():
            print(f"Missing {work}", file=sys.stderr)
            return 1
        return _remux_audio_only(capture, spec, work)
    if repolish_video:
        if not work.is_dir():
            print(f"Missing {work}", file=sys.stderr)
            return 1
        return _repolish_hybrid_video(capture, spec, work)
    if finish_only:
        if not work.is_dir():
            print(f"Missing {work}", file=sys.stderr)
            return 1
        return _finish_mux_from_checkpoint(capture, spec, work)

    if not work.is_dir():
        print(f"Missing {work} — run compose-hybrid-recap.py first", file=sys.stderr)
        return 1

    hybrid = _load("hybrid", "hybrid_recap_common.py")
    narr = _load("narr", "demo-recap-narrator.py")
    compose = _load("compose", "compose-presentation-recap.py")
    envkit = _load("envkit", "envkit_paths.py")

    spec = _apply_narrator_defaults(spec, capture, envkit.approved_cards_path())
    milestones = spec.get("milestones") or []
    spec["composeWorkDir"] = str(work)

    full_script = narr.resolve_full_narration_script(capture, spec, milestones)
    if not full_script:
        print(
            "ERROR: no DemoRecapFullNarration.json — run run-hybrid-recap-full.py script step first",
            file=sys.stderr,
        )
        return 1
    print(f"Full script: {len(full_script.split())} words (Cursor script, not preview)", flush=True)

    ffmpeg = hybrid.find_ffmpeg()
    ffprobe = hybrid.find_ffprobe()
    clips = hybrid.ordered_hybrid_segment_paths(work)
    if len(milestones) != len(clips):
        print(
            f"WARNING: {len(milestones)} milestones vs {len(clips)} segments — using min alignment",
            flush=True,
        )

    hold_clip_indices = list(range(len(clips)))
    clip_labels = [
        str(m.get("kind") or m.get("beatKind") or f"clip_{i}")
        for i, m in enumerate(milestones[: len(clips)])
    ]
    if len(clip_labels) < len(clips):
        clip_labels.extend(f"clip_{i}" for i in range(len(clip_labels), len(clips)))

    narration_cues, cue_labels = narr.build_full_script_narration_cues(
        clips, milestones, hold_clip_indices, full_script, spec, clip_labels
    )

    plan_dir = work / "_narr_plan"
    plan_dir.mkdir(parents=True, exist_ok=True)
    spec["_narrPlanDir"] = str(plan_dir)

    voice = str(spec.get("narratorVoice", "Andrew"))
    rate = narr.personal_say_rate(spec)
    engine = narr.pick_speech_engine(spec, capture)
    pname = narr.resolve_personal_voice_name(spec, capture)
    print(
        f"Speech-first hybrid sync: Personal Voice ({pname or 'Jacob Adkins'}), "
        f"sayRate={rate} wpm — extend segments so voice finishes…",
        flush=True,
    )

    pv_ready, pv_reason = narr.personal_voice_capture_ready(spec, capture)
    if not pv_ready:
        print(f"Personal Voice: {pv_reason}", flush=True)

    sync_rows: list[dict] = []
    extended = 0
    for ci, cue in sorted(narration_cues.items()):
        if ci >= len(clips):
            continue
        text, offset = cue[0], float(cue[1])
        m = milestones[ci] if ci < len(milestones) else None
        speech = narr.measure_speech_duration(
            text,
            voice=voice,
            rate=rate,
            engine=engine,
            spec=spec,
            run_dir=capture,
            plan_dir=plan_dir,
        )
        label = cue_labels.get(ci, clip_labels[ci] if ci < len(clip_labels) else f"clip_{ci}")
        need = narr.clip_sec_for_narration(
            speech, spec, start_offset_sec=offset, milestone=m, clip_label=label
        )
        cur = hybrid.probe_duration(clips[ci], ffprobe)
        row = {
            "clip": ci,
            "label": label,
            "videoDurBeforeSec": round(cur, 2),
            "speechSec": round(speech, 2),
            "offsetSec": round(offset, 2),
            "neededSec": round(need, 2),
            "extended": False,
        }
        if need > cur + 0.05:
            new_dur = hybrid.extend_segment_duration(ffmpeg, clips[ci], need, ffprobe=ffprobe)
            row["extended"] = True
            row["videoDurAfterSec"] = round(new_dur, 2)
            extended += 1
            print(
                f"  Sync {ci + 1}/{len(clips)} ({label}): speech {speech:.1f}s → "
                f"extend {cur:.1f}s → {new_dur:.1f}s",
                flush=True,
            )
        elif cur > need + 0.18:
            new_dur = hybrid.trim_segment_duration(ffmpeg, clips[ci], need, ffprobe=ffprobe)
            row["videoDurAfterSec"] = round(new_dur, 2)
            print(
                f"  Sync {ci + 1}/{len(clips)} ({label}): speech {speech:.1f}s trimmed "
                f"{cur:.1f}s → {new_dur:.1f}s",
                flush=True,
            )
        else:
            row["videoDurAfterSec"] = round(cur, 2)
            print(
                f"  Sync {ci + 1}/{len(clips)} ({label}): speech {speech:.1f}s fits in {cur:.1f}s",
                flush=True,
            )
        sync_rows.append(row)

    print(f"Extended {extended}/{len(narration_cues)} clip(s) for speech-first sync", flush=True)

    _clear_prior_narration(work)
    wav = work / "narration.wav"
    narr_spec = {**spec, "_narrationCueLabels": cue_labels, "_narrPlanDir": str(plan_dir)}
    narr_ok = narr.build_narration_for_clips(
        clips,
        milestones,
        wav,
        voice=voice,
        rate=rate,
        narration_cues=narration_cues,
        engine=engine,
        spec=narr_spec,
        run_dir=capture,
        hold_indices=hold_clip_indices,
    )
    if not narr_ok:
        print("ERROR: narration generation failed", file=sys.stderr)
        return 1

    final_silent = work / "_final_video.mp4"
    hybrid.concat_segments(ffmpeg, clips, final_silent)
    video_dur = hybrid.probe_duration(final_silent, ffprobe)
    spec["targetDurationSec"] = video_dur
    spec["fullNarrationTargetSec"] = video_dur

    final_video = final_silent
    if spec.get("botAvatarOverlay", True) and spec.get("botAvatarLipSync", True):
        bot_spec = importlib.util.spec_from_file_location("bot", _TOOLS / "jacob-adkins-bot.py")
        bot_mod = importlib.util.module_from_spec(bot_spec)
        assert bot_spec.loader
        bot_spec.loader.exec_module(bot_mod)
        approved_root = envkit.approved_cards_path().parent
        bot_mod.ensure_animated_avatar_asset(approved_root, ffmpeg)
        avatar_export = capture / "JacobAdkinsBot-Avatar"
        bot_video = work / "_final_video_bot.mp4"
        lipsync_mov = work / "_bot_lipsync" / f"{bot_mod.LIPSYNC_BASENAME}.mov"
        if lipsync_mov.is_file() and lipsync_mov.stat().st_size > 1024 * 1024:
            print(f"Reusing lip-sync avatar mov → {lipsync_mov}", flush=True)
            bot_mod.apply_animated_avatar_to_video(
                ffmpeg, final_silent, bot_video, lipsync_mov, spec=spec,
            )
            bot_ok = bot_video.is_file()
            if bot_ok:
                avatar_export.mkdir(parents=True, exist_ok=True)
                try:
                    shutil.copy2(lipsync_mov, avatar_export / lipsync_mov.name)
                except OSError as exc:
                    print(f"WARNING: avatar export copy skipped: {exc}", flush=True)
        elif bot_mod.generate_lipsync_avatar_for_narration(
            wav, final_silent, bot_video, ffmpeg, ffprobe,
            layer_export=avatar_export,
            spec=spec,
        ):
            bot_ok = True
        else:
            bot_ok = False
        if bot_ok:
            final_video = bot_video
            print(f"Lip-sync layered avatar baked → {bot_video}", flush=True)

    out_capture = capture / "HybridRecapPresentation.mp4"
    if not narr.remux_preview_with_narration(
        work, out_capture, spec=spec, video=final_video
    ):
        return 1

    synced_silent = capture / "HybridRecapPresentation-SYNCED-SILENT.mp4"
    shutil.copy2(final_silent, synced_silent)
    shutil.copy2(final_silent, capture / "HybridRecapPresentation-SILENT.mp4")

    desktop = Path.home() / "Desktop" / "HybridRecap-Full"
    desktop.mkdir(exist_ok=True)
    try:
        shutil.copy2(out_capture, desktop / "HybridRecapPresentation.mp4")
        shutil.copy2(final_silent, desktop / "HybridRecapPresentation-SYNCED-SILENT.mp4")
    except OSError as exc:
        print(f"WARNING: Desktop copy skipped (disk full?): {exc}", flush=True)
        print(f"Watch final video on Lexar: {out_capture}", flush=True)
    audit = {
        "recapMode": "hybrid_screencast",
        "narrationSyncMode": "speech_first",
        "fullScriptWords": len(full_script.split()),
        "videoDurationSec": round(video_dur, 3),
        "segmentsExtended": extended,
        "clips": sync_rows,
    }
    audit_path = capture / "RecapNarrationSyncAudit.json"
    audit_path.write_text(json.dumps(audit, indent=2) + "\n", encoding="utf-8")
    try:
        shutil.copy2(audit_path, desktop / "RecapNarrationSyncAudit.json")
    except OSError:
        pass

    tl_path.write_text(json.dumps({**spec, "milestones": milestones}, indent=2) + "\n", encoding="utf-8")
    print(f"Final hybrid recap: {out_capture} ({video_dur:.1f}s)")
    print(f"Desktop: {desktop / 'HybridRecapPresentation.mp4'}")
    compose.open_recap_video(out_capture)
    return 0


def main() -> int:
    if len(sys.argv) < 2:
        print(
            f"Usage: python3 {Path(__file__).name} /path/to/DemoCapture/<timestamp> "
            "[--finish-only] [--remux-audio-only] [--repolish-video]",
            file=sys.stderr,
        )
        return 1
    finish_only = "--finish-only" in sys.argv[2:]
    remux_audio_only = "--remux-audio-only" in sys.argv[2:]
    repolish_video = "--repolish-video" in sys.argv[2:]
    capture_arg = next(a for a in sys.argv[1:] if not a.startswith("-"))
    return mux_hybrid_narration(
        Path(capture_arg),
        finish_only=finish_only,
        remux_audio_only=remux_audio_only,
        repolish_video=repolish_video,
    )


if __name__ == "__main__":
    raise SystemExit(main())
