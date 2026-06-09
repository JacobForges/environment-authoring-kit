#!/usr/bin/env python3
"""Mux Personal Voice onto HybridRecap-ApprovalPreview — hear new character style before full redo."""
from __future__ import annotations

import importlib.util
import json
import shutil
import sys
from pathlib import Path

_TOOLS = Path(__file__).resolve().parent
OUT_DIR = Path.home() / "Desktop" / "HybridRecap-ApprovalPreview"


def _load(name: str, file: str):
    spec = importlib.util.spec_from_file_location(name, _TOOLS / file)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def ordered_preview_segments(work: Path) -> list[Path]:
    names = sorted(p.name for p in work.glob("seg_*.mp4"))
    if not names:
        names = [
            "seg_00_intro.mp4",
            "seg_01_screen_1.mp4",
            "seg_02_screen_2.mp4",
            "seg_03_hold_1.mp4",
            "seg_04_screen_3.mp4",
            "seg_05_screen_4.mp4",
            "seg_06_outro.mp4",
        ]
    paths = [work / n for n in names]
    missing = [p.name for p in paths if not p.is_file()]
    if missing:
        raise SystemExit(f"Missing preview segments in {work}: {', '.join(missing)}")
    return paths


def mux_preview_voice(capture: Path | None = None) -> int:
    work = OUT_DIR / "_work"
    silent = OUT_DIR / "HybridRecap-ApprovalPreview-SILENT.mp4"
    script_path = OUT_DIR / "DemoRecapPreviewNarration.json"
    if not work.is_dir() or not silent.is_file():
        print(f"Run compose first — missing {work} or {silent}", file=sys.stderr)
        return 1
    if not script_path.is_file():
        print(f"Missing {script_path}", file=sys.stderr)
        return 1

    hybrid = _load("hybrid", "hybrid_recap_common.py")
    narr = _load("narr", "demo-recap-narrator.py")
    compose = _load("compose", "compose-presentation-recap.py")
    envkit = _load("envkit", "envkit_paths.py")

    payload = json.loads(script_path.read_text(encoding="utf-8"))
    full_script = (payload.get("script") or "").strip()
    segment_cues = payload.get("segmentCues") or []
    if not full_script and not segment_cues:
        print("Preview script empty", file=sys.stderr)
        return 1

    capture = capture.expanduser().resolve() if capture else None
    tl_spec: dict = {}
    milestones: list = []
    if capture and (capture / "DemoRecapTimeline.json").is_file():
        tl_spec = json.loads((capture / "DemoRecapTimeline.json").read_text(encoding="utf-8"))
        milestones = tl_spec.get("milestones") or []

    spec = narr.flatten_narrator_personal_settings(tl_spec)
    if envkit.approved_cards_path().is_file():
        spec = narr.flatten_narrator_personal_settings(
            {**spec, **json.loads(envkit.approved_cards_path().read_text(encoding="utf-8"))}
        )
    density = float(payload.get("narrationScriptDensityMultiplier", 2.0))
    spec.update({
        "narrationMode": "fullScript",
        "narratorIgnoreCaptions": True,
        "narrationSyncMode": "speech_first",
        "narrationScriptDensityMultiplier": density,
        "narratorBotPersona": payload.get("persona", "Jacob Adkins Bot"),
        "mapCompletionStatus": payload.get("mapCompletionStatus", "partial"),
        "fullScriptAlignToMilestones": False,
        "narratorEnabled": True,
        "narratorEngine": "personal",
        "narratorRequirePersonal": True,
        "composeWorkDir": str(work),
        "narrationTailPadSec": 0.10,
        "narrationPlanPaddingSec": 0.08,
        "narrationSpeechSafetyPadSec": 0.18,
        "narrationInterCueGapSec": 0.0,
        "narrationTrimLeadSec": 0.01,
        "narrationTrimTailSec": 0.0,
        "narratorVolume": 2.5,
        "narratorSeamlessTimeline": bool(payload.get("narratorSeamlessTimeline", True)),
        "introNarratorOffsetSec": 0.02,
        "timelapseNarratorOffsetSec": 0.02,
        "outroNarratorOffsetSec": 0.02,
    })

    ffmpeg = hybrid.find_ffmpeg()
    ffprobe = hybrid.find_ffprobe()
    clips = ordered_preview_segments(work)
    clip_labels = [n.replace(".mp4", "").replace("seg_", "") for n in [p.name for p in clips]]
    hold_clip_indices = list(range(len(clips)))

    if len(milestones) < len(clips):
        milestones = milestones + [
            {"line1": lbl, "chapter": "Preview", "beatKind": "checkpoint"}
            for lbl in clip_labels[len(milestones):]
        ]

    if segment_cues and len(segment_cues) >= len(clips):
        narration_cues, cue_labels = narr.build_segment_aligned_cues(
            clips, list(segment_cues)[: len(clips)], spec, clip_labels
        )
    else:
        narration_cues, cue_labels = narr.build_full_script_narration_cues(
            clips, milestones[: len(clips)], hold_clip_indices, full_script, spec, clip_labels
        )

    plan_dir = work / "_narr_plan"
    plan_dir.mkdir(parents=True, exist_ok=True)
    spec["_narrPlanDir"] = str(plan_dir)
    voice = str(spec.get("narratorVoice", "Andrew"))
    rate = narr.personal_say_rate(spec)
    engine = narr.pick_speech_engine(spec, capture)
    pname = narr.resolve_personal_voice_name(spec, capture)
    print(
        f"Preview voice: Personal Voice ({pname or 'Jacob Adkins'}), sayRate={rate} wpm "
        f"(updated ApprovedCards character ladder)",
        flush=True,
    )

    pv_ready, pv_reason = narr.personal_voice_capture_ready(spec, capture)
    if not pv_ready:
        print(f"Personal Voice: {pv_reason}", flush=True)

    for ci, cue in sorted(narration_cues.items()):
        if ci >= len(clips):
            continue
        text, offset = cue[0], float(cue[1])
        m = milestones[ci] if ci < len(milestones) else None
        speech = narr.measure_speech_duration(
            text, voice=voice, rate=rate, engine=engine,
            spec=spec, run_dir=capture, plan_dir=plan_dir,
        )
        need = narr.clip_sec_for_narration(speech, spec, start_offset_sec=offset, milestone=m)
        cur = hybrid.probe_duration(clips[ci], ffprobe)
        label = cue_labels.get(ci, clip_labels[ci])
        if need > cur + 0.05:
            new_dur = hybrid.extend_segment_duration(ffmpeg, clips[ci], need, ffprobe=ffprobe)
            print(f"  Sync {ci + 1}/{len(clips)} ({label}): speech {speech:.1f}s → {cur:.1f}s → {new_dur:.1f}s", flush=True)
        elif cur > need + 0.18:
            new_dur = hybrid.trim_segment_duration(ffmpeg, clips[ci], need, ffprobe=ffprobe)
            print(f"  Sync {ci + 1}/{len(clips)} ({label}): speech {speech:.1f}s trimmed {cur:.1f}s → {new_dur:.1f}s", flush=True)
        else:
            print(f"  Sync {ci + 1}/{len(clips)} ({label}): speech {speech:.1f}s fits {cur:.1f}s", flush=True)

    for name in ("narration.wav", "_full_narr_raw.wav"):
        p = work / name
        if p.is_file():
            p.unlink()
    for d in (work / "_narr_parts", work / "_narr_plan"):
        if d.is_dir():
            shutil.rmtree(d, ignore_errors=True)

    wav = work / "narration.wav"
    narr_ok = narr.build_narration_for_clips(
        clips, milestones[: len(clips)], wav,
        voice=voice, rate=rate, narration_cues=narration_cues,
        engine=engine, spec={**spec, "_narrationCueLabels": cue_labels, "_narrPlanDir": str(plan_dir)},
        run_dir=capture, hold_indices=hold_clip_indices,
    )
    if not narr_ok:
        print("ERROR: preview narration failed", file=sys.stderr)
        return 1

    final_silent = work / "_final_preview.mp4"
    hybrid.concat_segments(ffmpeg, clips, final_silent)
    final_video = final_silent
    bot_spec = importlib.util.spec_from_file_location("bot", _TOOLS / "jacob-adkins-bot.py")
    bot_mod = importlib.util.module_from_spec(bot_spec)
    assert bot_spec.loader
    bot_spec.loader.exec_module(bot_mod)
    bot_video = work / "_final_preview_bot.mp4"
    lipsync = bot_mod.generate_lipsync_avatar_for_narration(
        wav, final_silent, bot_video, ffmpeg, ffprobe,
        layer_export=OUT_DIR / "JacobAdkinsBot-Avatar",
        spec=spec,
    )
    if lipsync and bot_video.is_file():
        final_video = bot_video
        print(f"Lip-sync layered avatar baked → {bot_video}", flush=True)

    out = OUT_DIR / "HybridRecap-ApprovalPreview.mp4"
    if not narr.remux_preview_with_narration(work, out, spec=spec, video=final_video):
        return 1

    meta_path = OUT_DIR / "preview-meta.json"
    meta = json.loads(meta_path.read_text()) if meta_path.is_file() else {}
    meta.update({
        "finalMp4": str(out),
        "narrationUsed": True,
        "persona": spec.get("narratorBotPersona", "Jacob Adkins Bot"),
        "voiceCharacter": "Jacob Adkins Bot — excited younger hyper-realistic",
        "sayRateWpm": rate,
    })
    meta_path.write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8")

    dur = hybrid.probe_duration(out, ffprobe)
    print(f"Voice preview ready: {out} ({dur:.1f}s)")
    compose.open_recap_video(out)
    return 0


def main() -> int:
    cap = Path(sys.argv[1]).expanduser().resolve() if len(sys.argv) > 1 else None
    return mux_preview_voice(cap)


if __name__ == "__main__":
    raise SystemExit(main())
