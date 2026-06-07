#!/usr/bin/env python3
"""
Producer-grade recap: emerald cards + Cursor director (vision) + Personal Voice narration.

  python3 run-producer-recap.py <capture_folder>              # full presentation
  python3 run-producer-recap.py --capture <capture_folder>    # same (optional flag)
  python3 run-producer-recap.py <capture_folder> --preview    # 120fps slowed director sample on Desktop
  python3 run-producer-recap.py <capture_folder> --preview --narration-only
  python3 run-producer-recap.py <capture_folder> --preview --no-narrator   # silent video only (not shared preview)
  python3 run-producer-recap.py <capture_folder> --no-cursor    # OpenCV boxes only (no API)
  python3 run-producer-recap.py <capture_folder> --opencv-only

Narration: macOS Personal Voice headless — bash run-recap-headless.sh <capture> --narration-only
  (Terminal fallback only if RECAP_NARRATION_TERMINAL_FALLBACK=1)

Config: DemoRecapApproved/ApprovedCards.json under ENVIRONMENT_KIT_DATA_ROOT (external Lexar when mounted)
Cursor director: CURSOR_API_KEY in Tools/cave-grader/.env (reads each milestone PNG).
"""
from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
from pathlib import Path


def load(name: str, file: str):
    p = Path(__file__).resolve().parent / file
    spec = importlib.util.spec_from_file_location(name, p)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def list_frames(run: Path) -> list[Path]:
    d = run / "timelapse"
    hits = sorted(d.glob("tl_*.png"))
    return hits


def resolve_capture_run(argv: list[str]) -> tuple[Path, list[str]]:
    """First positional path, or ``--capture <dir>`` (common mistake). Returns (run, remaining flags)."""
    args = [a for a in argv[1:] if a not in ("--capture", "-c")]
    # --capture /path or --capture=/path
    for i, a in enumerate(argv[1:], start=1):
        if a in ("--capture", "-c") and i < len(argv) - 1:
            run = Path(argv[i + 1]).expanduser().resolve()
            rest = [x for j, x in enumerate(argv[1:], start=1) if j not in (i, i + 1)]
            return run, rest
        if a.startswith("--capture="):
            run = Path(a.split("=", 1)[1]).expanduser().resolve()
            rest = [x for x in argv[1:] if x != a]
            return run, rest
    if not args or args[0].startswith("-"):
        return Path(), argv[1:]
    run = Path(args[0]).expanduser().resolve()
    return run, args[1:]


def ensure_edge_tts(*, auto_install: bool = False) -> bool:
    narr = load("narrator", "demo-recap-narrator.py")
    if narr.edge_tts_available():
        return True
    if auto_install:
        return narr.install_edge_tts()
    return False


def ensure_opencv(*, auto_install: bool = False) -> bool:
    """Return True if cv2 is importable in this Python (venv)."""
    try:
        import cv2  # noqa: F401

        return True
    except ImportError:
        if auto_install or "--install-opencv" in sys.argv or "--install-deps" in sys.argv:
            print("Installing opencv-python-headless + numpy into this Python…")
            subprocess.run(
                [
                    sys.executable,
                    "-m",
                    "pip",
                    "install",
                    "-q",
                    "opencv-python-headless>=4.8",
                    "numpy>=1.24",
                ],
                check=False,
            )
            try:
                import cv2  # noqa: F401

                return True
            except ImportError:
                pass
        print(
            "\nOpenCV not found in this Python — using fixed chapter boxes (fallback).\n"
            "For vision-based callouts (blue tile gizmo, grid core), run:\n"
            f"  {sys.executable} -m pip install opencv-python-headless numpy\n"
            "Or re-run with:  --install-opencv\n",
            file=sys.stderr,
        )
        return False


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    run, _extra = resolve_capture_run(sys.argv)
    flags = set(sys.argv[1:])
    preview = "--preview" in flags
    narration_only = "--narration-only" in flags
    remux_only = "--remux-only" in flags
    opencv_only = "--opencv-only" in flags

    if not run or str(run) in ("--capture", "-c", "--preview", "--narration-only", "--remux-only"):
        print(
            "Usage: python3 run-producer-recap.py <capture_folder> [--preview] [--narration-only]\n"
            "   or: python3 run-producer-recap.py --capture <capture_folder> [--preview] …",
            file=sys.stderr,
        )
        return 1

    envkit = load("envkit_paths", "envkit_paths.py")
    approved_path = envkit.approved_cards_path()
    compose = load("compose_pres", "compose-presentation-recap.py")

    if remux_only:
        narr_mod = load("narrator", "demo-recap-narrator.py")
        remux_spec: dict = {}
        if approved_path.is_file():
            remux_spec = narr_mod.flatten_narrator_personal_settings(
                json.loads(approved_path.read_text(encoding="utf-8"))
            )
        work = Path(
            str(remux_spec.get("composeWorkDir") or run / "_presentation_compose_preview120")
        ).expanduser()
        if not work.is_dir():
            work = run / "_presentation_compose_preview120"
        out = Path.home() / "Desktop" / "DemoRecap-Card-Preview" / "DirectorPreview.mp4"
        if not narr_mod.remux_preview_with_narration(work, out, spec=remux_spec):
            return 1
        compose.open_recap_video(out)
        return 0

    frames = list_frames(run)
    if len(frames) < 8:
        tl = run / "timelapse"
        print(
            f"Need timelapse PNGs in {tl}/tl_*.png (found {len(frames)}).\n"
            f"Capture folder: {run}\n"
            "Pass the folder path as the first argument, not only --capture:\n"
            f"  python3 run-producer-recap.py {run} --preview --narration-only",
            file=sys.stderr,
        )
        return 1

    install_deps = "--install-opencv" in sys.argv or "--install-deps" in sys.argv
    cards = load("cards", "apply-approved-cards.py")
    producer = load("producer", "demo-recap-producer.py")
    opencv = load("opencv", "demo-recap-opencv-annotate.py")
    captions = load("captions", "demo-recap-captions.py")
    timeline = load("timeline", "demo-recap-timeline.py")
    scene_diff = load("diff", "demo-recap-scene-diff.py")

    card_fields = cards.apply_to_timeline(run)
    print("Cards: intro c / outro b wired")

    existing = {}
    tl_path = run / "DemoRecapTimeline.json"
    if tl_path.is_file():
        existing = json.loads(tl_path.read_text())

    need_rebuild = "--rebuild-milestones" in sys.argv
    if preview and existing.get("milestones"):
        n_sub_existing = sum(1 for m in existing["milestones"] if m.get("beatKind") == "subbeat")
        if n_sub_existing < 8:
            need_rebuild = True
            print("Preview: rebuilding timeline for more subbeat captions (use --rebuild-milestones to force)")
    if existing.get("milestones") and len(existing["milestones"]) >= 6 and not need_rebuild:
        milestones = existing["milestones"]
    else:
        ck = (
            8
            if preview
            else int(existing.get("maxCheckpoints") or producer.PRODUCER_SPEC_DEFAULTS["maxCheckpoints"])
        )
        sb = (
            10
            if preview
            else int(existing.get("maxSubbeats") or producer.PRODUCER_SPEC_DEFAULTS["maxSubbeats"])
        )
        milestones = timeline.build_layered_timeline(
            len(frames),
            existing.get("buildMode", "FullWorld additive surface_build"),
            checkpoints=ck,
            subbeats=sb,
        )

    hub = Path(__file__).resolve().parent
    while hub != hub.parent and not (hub / "Assets").is_dir():
        hub = hub.parent
    director = load("cursor_dir", "demo-recap-cursor-director.py")
    director.load_hub_ai_env(hub)
    use_cursor = (
        "--no-cursor" not in sys.argv
        and not narration_only
        and not remux_only
        and director.has_cursor_api_key()
    )
    if narration_only:
        print("Narration-only: skipping Cursor director and OpenCV (reusing timeline milestones)")
    elif use_cursor:
        picks = None
        if preview:
            picks = timeline.preview_milestone_indices(milestones)
        director.apply_cursor_milestones_director(
            hub,
            run,
            milestones,
            frames,
            build_mode=existing.get("buildMode", "FullWorld additive surface_build"),
            milestone_indices=picks,
        )
    else:
        has_cv2 = ensure_opencv(auto_install=install_deps)
        if has_cv2 and not opencv.HAS_CV2:
            opencv = load("opencv", "demo-recap-opencv-annotate.py")
        mode = "OpenCV vision" if opencv.HAS_CV2 else "chapter fallback (install opencv for smart boxes)"
        print(f"Annotate: {mode}… (add CURSOR_API_KEY to .env for Cursor director)")
        opencv.apply_opencv_to_milestones(run, milestones, frames)

    n_labels = opencv.sync_region_labels_from_timeline(milestones)
    if n_labels:
        print(f"Annotations: synced {n_labels} region label(s) to timeline phase/sub")

    for m in milestones:
        captions.fill_milestone_captions(m)
        producer.producer_chapter_line(m)
    producer.dedupe_captions(milestones)
    if existing.get("sceneDiffHints", producer.PRODUCER_SPEC_DEFAULTS.get("sceneDiffHints", True)):
        scene_diff.enrich_milestones_with_diff(frames, milestones)

    spec = producer.build_producer_spec(run, milestones, card_fields=card_fields, existing=existing)
    narr_mod = load("narrator", "demo-recap-narrator.py")
    spec = narr_mod.flatten_narrator_personal_settings(spec)
    spec.setdefault("narrationMode", "fullScript")
    spec.setdefault("narratorIgnoreCaptions", True)
    spec.setdefault("cursorFullNarration", True)
    pname = narr_mod.resolve_personal_voice_name(spec, run)
    spec["narratorEngine"] = "personal"
    spec["narratorRequirePersonal"] = True
    spec.setdefault("voiceHelpers", {"preset": "ladderDocumentary", "sayRate": 186.24})
    spec.setdefault("voiceLadder", {"enabled": True})
    spec.setdefault("narratorPersonalDelivery", "ladderDocumentary")
    spec.setdefault("narratorPersonalHumanize", True)
    spec.setdefault("narratorRawPersonalVoice", False)
    spec.setdefault("narratorNaturalDelivery", False)
    spec.setdefault("narratorPolishPersonal", False)
    spec.setdefault("narratorNaturalPauses", False)
    spec.setdefault("narratorPersonalLoudnorm", False)
    if pname:
        spec["narratorPersonalVoice"] = pname
    if approved_path.is_file():
        spec = narr_mod.flatten_narrator_personal_settings(
            {**spec, **json.loads(approved_path.read_text(encoding="utf-8"))}
        )

    if narr_mod.uses_full_script_narration(spec):
        est = float(spec.get("targetDurationSec") or existing.get("targetDurationSec") or 480)
        n_m = len(milestones)
        est = max(
            est,
            float(spec.get("introSec", 9))
            + float(spec.get("outroSec", 10))
            + n_m * float(spec.get("milestoneHoldSec", 12)),
        )
        script_milestones = milestones
        if preview:
            picks = timeline.preview_milestone_indices(milestones)
            script_milestones = [milestones[i] for i in picks if 0 <= i < len(milestones)]
        if narration_only:
            work_vid = Path(
                str(
                    spec.get("composeWorkDir")
                    or existing.get("composeWorkDir")
                    or run / "_presentation_compose_preview120"
                )
            ).expanduser()
            final_vid = work_vid / "_final_video.mp4"
            if final_vid.is_file():
                try:
                    vd = narr_mod.probe_duration(final_vid)
                    if vd > 30:
                        est = vd
                        print(f"Narration-only: script target {vd:.1f}s from graded preview video")
                except Exception:
                    pass
        spec["fullNarrationTargetSec"] = est
        full_json = run / "DemoRecapFullNarration.json"
        regen_script = "--regen-narration-script" in flags
        script_after_video = bool(spec.get("narrationScriptAfterVideo", True))
        need_script = regen_script or not full_json.is_file()
        if narration_only and need_script and not regen_script:
            print(
                "Narration-only: building local voice script (Personal Voice next — not waiting on Cursor Agent)",
                flush=True,
            )
            local = narr_mod.write_local_full_narration_script(
                run, script_milestones, spec, target_sec=est
            )
            if local:
                spec["fullNarrationScript"] = local
            spec["fullScriptAlignToMilestones"] = True
        elif director.has_cursor_api_key() and need_script and not script_after_video:
            script = director.apply_cursor_full_narration_script(
                hub,
                run,
                script_milestones,
                spec,
                build_mode=existing.get("buildMode", "FullWorld additive surface_build"),
            )
            if script:
                spec["fullNarrationScript"] = script
        elif full_json.is_file():
            print("Full narration: reusing DemoRecapFullNarration.json")
        elif narration_only and not director.has_cursor_api_key():
            print(
                "ERROR: --narration-only needs DemoRecapFullNarration.json or CURSOR_API_KEY "
                "to write the script.",
                file=sys.stderr,
            )

    if preview:
        spec.update(
            {
                "outputFps": 60,
                "timelapseEncodeFps": 60,
                "videoPlaybackFactor": 1.0,
                "videoEnhance": True,
                "sceneUpscale": 2.0,
                "narrationSyncMode": "speech_first",
                "narrationMode": "fullScript",
                "narratorIgnoreCaptions": True,
                "narrationPlanMode": "estimate",
                "timelapseSecPerFrame": 0.09,
                "maxMilestoneHoldSec": 18.0,
                "maxSubbeatHoldSec": 11.0,
                "composeWorkDir": str(run / "_presentation_compose_preview120"),
                "milestoneHoldSec": 12.0,
                "introSec": 9.0,
                "outroSec": 10.0,
                "captionReadPauseSec": 2.5,
                "segmentXfadeSec": 0.5,
                "cinematicCamera": True,
                "holdCinematicMotion": True,
                "cinematicEffects": True,
                "syncHoldToNarration": True,
                "tlMaxFrames": 56,
            }
        )
        spec = narr_mod.flatten_narrator_personal_settings(spec)
        print(
            f"Preview profile: {int(spec['outputFps'])}fps encode, "
            f"{spec['videoPlaybackFactor']:.0%} playback speed, narration-synced holds"
        )
    if narration_only:
        spec["narratorEnabled"] = True
        work = Path(str(spec.get("composeWorkDir") or run / "_presentation_compose")).expanduser()
        for name in ("narration.wav", "_full_narr_raw.wav"):
            p = work / name
            if p.is_file():
                p.unlink()
        parts = work / "_narr_parts"
        if parts.is_dir():
            import shutil

            shutil.rmtree(parts, ignore_errors=True)
        print("Narration-only: cleared prior narration (clip-sync regen)…", flush=True)
    narrator_on = "--no-narrator" not in sys.argv and spec.get("narratorEnabled", True)
    if narrator_on:
        say_r = narr_mod.personal_say_rate(spec)
        print(
            f"Narrator: Personal Voice ({pname or 'Jacob Adkins'}), sayRate={say_r} wpm "
            f"(from ApprovedCards narratorPersonalSettings.sayRate)"
        )
    if "--no-narrator" in sys.argv:
        spec["narratorEnabled"] = False
    # Full encode: keep 1.0 playback unless --stretch-to-target (8min target is script-only by default).
    if not preview and "--stretch-to-target" not in flags:
        spec["videoPlaybackFactor"] = 1.0
    producer.write_timeline(run, spec)
    (run / "DemoRecapOpenCV.json").write_text(
        json.dumps(
            [{"i": m.get("i"), "frame": m.get("frame"), "regions": m.get("regions", [])} for m in milestones],
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )

    if opencv_only:
        print(f"Wrote {run / 'DemoRecapOpenCV.json'}")
        return 0

    if preview:
        preview_name = (
            "DirectorPreview-silent.mp4"
            if "--no-narrator" in flags
            else "DirectorPreview.mp4"
        )
        out = Path.home() / "Desktop" / "DemoRecap-Card-Preview" / preview_name
        out.parent.mkdir(parents=True, exist_ok=True)
        picks = timeline.preview_milestone_indices(milestones)
        n_sub = sum(1 for i in picks if milestones[i].get("beatKind") == "subbeat")
        n_cp = len(picks) - n_sub
        print(f"Preview timeline: {n_cp} checkpoints + {n_sub} subbeats ({len(picks)} holds)")
        try:
            compose.compose_presentation(
                run, out, spec, milestone_filter=list(picks), narration_only=narration_only
            )
        except SystemExit as exc:
            print(exc, file=sys.stderr)
            return 1
        if narr_mod.require_narration_before_finalize(spec) and not narr_mod.mp4_has_audio_stream(
            out
        ):
            print(f"Preview not opened — finalize narration first: {out}", file=sys.stderr)
            return 1
        compose.open_recap_video(out)
        print(out)
        return 0

    out = run / "DemoRecapPresentation.mp4"
    try:
        compose.compose_presentation(run, out, spec, narration_only=narration_only)
    except SystemExit as exc:
        print(exc, file=sys.stderr)
        return 1
    if narr_mod.require_narration_before_finalize(spec) and not narr_mod.mp4_has_audio_stream(
        out
    ):
        return 1
    compose.open_recap_video(out)
    print(out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
