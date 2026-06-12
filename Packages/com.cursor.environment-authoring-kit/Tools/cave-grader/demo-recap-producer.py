"""Producer / director helpers — captions, spec preset, safe concat."""
from __future__ import annotations

import hashlib
import json
import platform
import subprocess
from pathlib import Path
from typing import Any


EMERALD = [24, 235, 158]

PRODUCER_SPEC_DEFAULTS: dict[str, Any] = {
    "recapMode": "presentation_pro",
    "sceneFit": "contain",
    "cinematicCamera": False,
    "holdCinematicMotion": False,
    "cinematicEffects": True,
    "javafxEffects": True,
    "videoPlaybackFactor": 1.0,
    "outputFps": 30.0,
    "framesPerSource": 1,
    "milestoneHoldSec": 20.0,
    "subbeatHoldSec": 20.0,
    "minMilestoneHoldSec": 20.0,
    "introSec": 9.0,
    "outroSec": 10.0,
    "targetDurationSec": 480.0,
    "maxCheckpoints": 8,
    "maxSubbeats": 8,
    "narrationSyncMode": "speech_first",
    "timelapseSecPerFrame": 0.09,
    "videoEnhance": True,
    "noTransitions": False,
    "segmentFadeSec": 0.35,
    "segmentXfadeSec": 0.5,
    "lectureMode": True,
    "showAnnotations": False,
    "forceLocalCaptions": True,
    "botAvatarOverlay": True,
    "botAvatarLipSync": True,
    "presentationPlayModeBroll": True,
    "playModeBrollSec": 42.0,
    "tlTailMaxFrames": 48,
    "annotationSource": "opencv",
    "aiRegionsOnly": False,
    "chapterFocusAnnotations": False,
    "portfolioTitleCards": False,
    "introNoPortrait": True,
    "introAccent": EMERALD,
    "outroAccent": EMERALD,
    "minTimelapseGapFrames": 6,
    "annotationDelaySec": 5.0,
    "annotationFadeSec": 0.65,
    "captionLine1FadeSec": 2.5,
    "captionLine2DelaySec": 3.0,
    "captionBulletStaggerSec": 1.8,
    "captionBulletFadeSec": 2.0,
    "captionLine3DelaySec": 11.0,
    "captionFutureFadeSec": 2.0,
    "captionReadPauseSec": 7.0,
    "staticSceneMotion": True,
    "disablePanMotion": True,
    "syncHoldToNarration": True,
    "fullScriptAlignToMilestones": False,
    "narrationPlanMode": "estimate",
    "maxMilestoneHoldSec": 20.0,
    "maxSubbeatHoldSec": 12.0,
    "narrationTailPadSec": 1.8,
    "sceneUpscale": 1.0,
    "timelapseEncodeFps": 30,
    "tlMaxFrames": 72,
    "tlMaxSec": 7.0,
    "tlMinSec": 4.5,
    "gradeAtMasterOnly": True,
    "segmentCache": True,
    "parallelWorkers": 4,
    "holdKeyframeMode": True,
    "sceneDiffHints": True,
    "narratorEnabled": True,
    "narratorEngine": "auto",
    "narratorVoice": "Andrew",
    "narratorStartOffsetSec": 5.5,
    "introNarratorOffsetSec": 1.0,
    "outroNarratorOffsetSec": 1.0,
    "narrateIntro": True,
    "narrateOutro": True,
    "narratorRate": 186,
    "narratorPersonalSayRate": 186,
    "narratorPersonalHumanize": True,
    "narratorPauseSentMs": 320,
    "narratorPauseCommaMs": 140,
    "narratorPolishPersonal": False,
    "narratorNaturalDelivery": False,
    "narratorNaturalPauses": False,
    "narratorPersonalLoudnorm": False,
    "narratorEdgeRate": "+8%",
    "narratorSpeechPace": 1.0,
    "narratorFitPaceBias": 1.0,
    "narratorMaxFitAtempo": 1.0,
    "narratorMinFitAtempo": 1.0,
    "narratorSpeechFadeInSec": 0.0,
    "narratorSpeechFadeOutSec": 0.0,
    "narratorHumanize": True,
    "narratorVolume": 1.0,
    "loudnorm": True,
    "producerVersion": 3,
    "outputWidth": 1920,
    "outputHeight": 1080,
    "encodeCrf": 18,
    "encodePreset": "slow",
    "encodeTune": "film",
    "encodeCodec": "auto",
    "encodeVtbQuality": 65,
}


def resolve_encode_codec(spec: dict[str, Any] | None = None) -> str:
    spec = spec or {}
    codec = str(spec.get("encodeCodec", PRODUCER_SPEC_DEFAULTS["encodeCodec"])).strip().lower()
    if codec in ("", "auto"):
        return "h264_videotoolbox" if platform.system() == "Darwin" else "libx264"
    return codec


def ffmpeg_encode_args(spec: dict[str, Any] | None = None) -> list[str]:
    """macOS: VideoToolbox (fast). Else libx264 crf 18, slow preset, film tune."""
    spec = spec or {}
    codec = resolve_encode_codec(spec)
    if codec == "h264_videotoolbox":
        qv = int(spec.get("encodeVtbQuality", PRODUCER_SPEC_DEFAULTS["encodeVtbQuality"]))
        return [
            "-c:v",
            "h264_videotoolbox",
            "-q:v",
            str(max(1, min(100, qv))),
            "-profile:v",
            "high",
            "-pix_fmt",
            "yuv420p",
            "-allow_sw",
            "1",
        ]
    crf = int(spec.get("encodeCrf", PRODUCER_SPEC_DEFAULTS["encodeCrf"]))
    preset = str(spec.get("encodePreset", PRODUCER_SPEC_DEFAULTS["encodePreset"]))
    tune = str(spec.get("encodeTune", PRODUCER_SPEC_DEFAULTS["encodeTune"]))
    return [
        "-c:v",
        "libx264",
        "-pix_fmt",
        "yuv420p",
        "-crf",
        str(crf),
        "-preset",
        preset,
        "-tune",
        tune,
    ]


def dedupe_captions(milestones: list[dict[str, Any]]) -> None:
    """Mark duplicate subbeats for narration skip — keep on-screen captions."""
    seen: set[str] = set()
    for m in milestones:
        m.pop("_skipNarration", None)
        key = hashlib.sha256(
            f"{m.get('line1','')}|{m.get('line2','')}|{m.get('beatKind')}".encode()
        ).hexdigest()[:16]
        if key in seen and m.get("beatKind") == "subbeat":
            m["_skipNarration"] = True
        seen.add(key)


def producer_chapter_line(m: dict[str, Any]) -> None:
    """Tighter broadcast copy when AI director did not run."""
    chapter = (m.get("chapter") or "").lower()
    sub = (m.get("subAction") or m.get("sub") or "").lower()
    if m.get("beatKind") == "subbeat":
        return
    templates = {
        "session bootstrap": (
            "Session online — editor queue and first terrain commits.",
            "We are proving capture cadence and additive benches before grid contract work.",
            "Watch for real heightfield change, not frozen holds.",
        ),
        "grid contract": (
            "The nine-tile play disk is the layout anchor.",
            "Every later sculpt pass assumes shared edges on this 3×3 module.",
            "Blue gizmo marks the active tile; white guides show snap alignment.",
        ),
        "seam invariants": (
            "Seam passes weld border heights between tiles.",
            "Invisible walls start here if corners drift — compare borders beat to beat.",
            "",
        ),
        "play disk": (
            "Play disk grading shapes the walkable center.",
            "Outer wilderness can get noisy; the disk must stay readable for movement.",
            "",
        ),
        "terrain meat": (
            "Terrain meat adds paced height across disk and approaches.",
            "Long flat holds usually mean queue depth, not a failed phase.",
            "",
        ),
        "foothills": (
            "Foothills bridge the disk into wilderness approaches.",
            "This pass sets whether annex carving gets clean corridors later.",
            "",
        ),
        "mountain ring": (
            "Mountain massing frames sightlines toward the cave mouth.",
            "Relief that fights collision is a grading fail even if the aerial view looks dramatic.",
            "",
        ),
        "labyrinth annex": (
            "South annex labyrinth should read as branches, not a hub star.",
            "Annex-local carve scope is an invariant — global stars are a regression.",
            "",
        ),
        "trail bench": (
            "Radial trail benches often dominate queue depth.",
            "If nothing moves for minutes, one bench label may be spamming the queue.",
            "",
        ),
        "surface phase": (
            "Surface heightfield remains authoritative until lock.",
            "Cave geometry must not mutate the audited surface prematurely.",
            "",
        ),
        "final capture": (
            "Final Scene view before the session ended.",
            "Preserve this frame — it outranks logs overwritten on restart.",
            "",
        ),
    }
    for key, (l1, l2, l3) in templates.items():
        if key in chapter and not m.get("line1"):
            m["line1"] = l1
            m["line2"] = l2
            m["line3"] = l3
            return


def build_producer_spec(
    run_dir: Path,
    milestones: list[dict[str, Any]],
    *,
    card_fields: dict[str, Any],
    existing: dict[str, Any] | None = None,
) -> dict[str, Any]:
    ex = existing or {}
    spec: dict[str, Any] = {**PRODUCER_SPEC_DEFAULTS, **ex}
    spec.update(card_fields)
    if int(spec.get("producerVersion", 0)) >= 3:
        spec["outputFps"] = PRODUCER_SPEC_DEFAULTS["outputFps"]
        for key in (
            "milestoneHoldSec",
            "subbeatHoldSec",
            "introSec",
            "outroSec",
            "annotationDelaySec",
            "annotationFadeSec",
            "captionLine1FadeSec",
            "captionLine2DelaySec",
            "captionLine3DelaySec",
            "captionReadPauseSec",
            "syncHoldToNarration",
            "narrationTailPadSec",
            "videoPlaybackFactor",
            "timelapseSecPerFrame",
            "minTimelapseGapFrames",
            "tlMaxFrames",
            "tlMaxSec",
            "tlMinSec",
            "segmentXfadeSec",
            "outputWidth",
            "outputHeight",
            "encodeCrf",
            "encodePreset",
            "encodeTune",
            "cinematicCamera",
            "holdCinematicMotion",
            "narratorStartOffsetSec",
            "introNarratorOffsetSec",
            "outroNarratorOffsetSec",
            # narrator rate / pauses / voice: controlled by ApprovedCards.json — do not reset here
        ):
            spec[key] = PRODUCER_SPEC_DEFAULTS[key]
    # Re-apply approved narrator overrides after v3 timing lock (card_fields may include flatten)
    for key, val in card_fields.items():
        if key.startswith("narrator") or key == "narratorPersonalSettings":
            spec[key] = val
    spec["milestones"] = milestones
    spec["buildMode"] = ex.get("buildMode", "FullWorld additive surface_build")
    return spec


def concat_segments_safe(
    ffmpeg: str,
    clips: list[Path],
    out: Path,
    *,
    xfade_sec: float = 0.0,
    output_fps: float = 30.0,
) -> None:
    """Concat segments; optional xfade chain for smoother director cuts."""
    if not clips:
        raise ValueError("no clips")
    if len(clips) == 1:
        subprocess.run(["cp", str(clips[0]), str(out)], check=True)
        return
    if xfade_sec > 0.05 and len(clips) <= 24:
        import importlib.util

        tools = Path(__file__).resolve().parent
        spec = importlib.util.spec_from_file_location("smart", tools / "compose-smart-recap.py")
        smart = importlib.util.module_from_spec(spec)
        assert spec.loader
        spec.loader.exec_module(smart)
        smart.concat_with_xfades(ffmpeg, clips, out, xfade_sec, output_fps)
        return
    lst = out.parent / "concat_list.txt"
    lst.write_text("".join(f"file '{p.resolve()}'\n" for p in clips), encoding="utf-8")
    subprocess.run(
        [ffmpeg, "-y", "-f", "concat", "-safe", "0", "-i", str(lst), *ffmpeg_encode_args(), str(out)],
        check=True,
        capture_output=True,
    )


def write_timeline(run_dir: Path, spec: dict[str, Any]) -> Path:
    p = run_dir / "DemoRecapTimeline.json"
    p.write_text(json.dumps(spec, indent=2) + "\n")
    return p
