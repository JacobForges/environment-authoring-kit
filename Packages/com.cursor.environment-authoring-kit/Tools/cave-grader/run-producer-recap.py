#!/usr/bin/env python3
"""
Producer-grade recap: emerald cards + Cursor director (vision) + Personal Voice narration.

  python3 run-producer-recap.py <capture_folder>              # full presentation
  python3 run-producer-recap.py --capture <capture_folder>    # same (optional flag)
  python3 run-producer-recap.py <capture_folder> --preview    # 120fps slowed director sample on Desktop
  python3 run-producer-recap.py <capture_folder> --preview --narration-only
  python3 run-producer-recap.py <capture_folder> --preview --avatar-only   # cyborg overlay only; keeps natural voice
  python3 run-producer-recap.py <capture_folder> --preview --force-regen-narration  # discard preserved voice
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
import shutil
import subprocess
import sys
from pathlib import Path


def avatar_only_preview(run: Path) -> int:
    """Re-layer Unity cyborg avatar + remux. Keeps graded video + narration.wav untouched."""
    envkit = load("envkit_paths", "envkit_paths.py")
    envkit.ensure_recap_process_env()
    narr = load("narrator", "demo-recap-narrator.py")
    compose = load("compose_pres", "compose-presentation-recap.py")
    approved_path = envkit.approved_cards_path()
    spec: dict = {}
    if approved_path.is_file():
        spec = narr.flatten_narrator_personal_settings(
            json.loads(approved_path.read_text(encoding="utf-8"))
        )
    spec = {
        **spec,
        "botAvatarRequireUnity": True,
        "botAvatarAllowProceduralFallback": False,
        "_avatarSwapOnly": True,
    }
    work = run / "_presentation_compose_preview120"
    final_silent = work / "_final_video.mp4"
    out = run / "DirectorPreview.mp4"
    if not final_silent.is_file():
        print(f"ERROR: missing graded video {final_silent}", file=sys.stderr)
        return 1
    narr_wav = work / "narration.wav"
    if not narr_wav.is_file() or not narr.wav_is_audible(narr_wav):
        print(f"ERROR: need audible {narr_wav} (avatar swap does not regen voice)", file=sys.stderr)
        return 1
    ffmpeg = compose.find_ffmpeg()
    lipsync_work = work / "_bot_lipsync"
    shutil.rmtree(lipsync_work, ignore_errors=True)
    (work / "_final_video_bot.mp4").unlink(missing_ok=True)
    print("Avatar-only: Unity cyborg swap (narration + video locked)…", flush=True)
    video_for_mux = compose._apply_bot_avatar_overlay(
        ffmpeg, final_silent, narr_wav, work, run, spec
    )
    if video_for_mux == final_silent:
        print("ERROR: Unity cyborg overlay failed — DirectorPreview not updated.", file=sys.stderr)
        return 1
    vol = max(narr.narrator_mux_volume(spec), 1.25)
    mux_wav = work / "_narration_mux.wav"
    shutil.copy2(narr_wav, mux_wav)
    narr.mux_narration_video_only(video_for_mux, mux_wav, out, volume=vol, spec=spec)
    if not out.is_file() or out.stat().st_size < 1024:
        print(f"ERROR: avatar-only mux failed → {out}", file=sys.stderr)
        return 1
    mirror = envkit.preview_mirror_dir() / "DirectorPreview.mp4"
    try:
        shutil.copy2(out, mirror)
        print(f"Preview mirror (Lexar): {mirror}", flush=True)
    except OSError as exc:
        print(f"Lexar mirror skipped ({exc})", flush=True)
    compose.open_recap_video(out)
    print(out)
    return 0


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
    envkit = load("envkit_paths", "envkit_paths.py")
    data_root = envkit.ensure_recap_process_env()
    if str(data_root).startswith("/Volumes/"):
        print(f"Storage: external {data_root}", flush=True)
    else:
        free = envkit.mac_data_volume_free_gb()
        if free is not None and free < 2.0:
            print(
                f"WARNING: Mac disk low ({free:.1f} GiB free) — mount Lexar or set "
                f"{envkit.ENV_VAR}=/Volumes/Lexar/EnvironmentKit-Hub",
                file=sys.stderr,
                flush=True,
            )
    run, _extra = resolve_capture_run(sys.argv)
    flags = set(sys.argv[1:])
    preview = "--preview" in flags
    narration_only = "--narration-only" in flags
    avatar_only = "--avatar-only" in flags
    force_regen_narration = "--force-regen-narration" in flags
    remux_only = "--remux-only" in flags
    opencv_only = "--opencv-only" in flags

    if not run or str(run) in ("--capture", "-c", "--preview", "--narration-only", "--remux-only"):
        print(
            "Usage: python3 run-producer-recap.py <capture_folder> [--preview] [--narration-only]\n"
            "   or: python3 run-producer-recap.py --capture <capture_folder> [--preview] …",
            file=sys.stderr,
        )
        return 1

    tl_path = run / "DemoRecapTimeline.json"
    hybrid_existing: dict = {}
    if tl_path.is_file():
        try:
            hybrid_existing = json.loads(tl_path.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            hybrid_existing = {}

    if (
        not preview
        and narration_only
        and hybrid_existing.get("recapMode") == "hybrid_screencast"
        and (run / "_hybrid_compose").is_dir()
        and (run / "HybridRecapPresentation-SILENT.mp4").is_file()
    ):
        print("Hybrid screencast: routing to mux-hybrid-recap-narration.py (not producer preview)", flush=True)
        hybrid_mux = load("hybrid_mux", "mux-hybrid-recap-narration.py")
        return hybrid_mux.mux_hybrid_narration(run)

    envkit = load("envkit_paths", "envkit_paths.py")
    approved_path = envkit.approved_cards_path()
    compose = load("compose_pres", "compose-presentation-recap.py")

    if avatar_only:
        if not preview:
            print("ERROR: --avatar-only requires --preview", file=sys.stderr)
            return 1
        return avatar_only_preview(run)

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

    force_local = (
        "--local-captions" in sys.argv
        or card_fields.get("forceLocalCaptions", True)
        or existing.get("forceLocalCaptions", True)
    )
    for m in milestones:
        captions.fill_milestone_captions(m, force=force_local or narration_only)
        captions.fill_milestone_narrator_script(m, force=True)
        producer.producer_chapter_line(m)
    producer.dedupe_captions(milestones)
    if existing.get("sceneDiffHints", producer.PRODUCER_SPEC_DEFAULTS.get("sceneDiffHints", True)):
        scene_diff.enrich_milestones_with_diff(frames, milestones)

    spec = producer.build_producer_spec(run, milestones, card_fields=card_fields, existing=existing)
    narr_mod = load("narrator", "demo-recap-narrator.py")
    spec = narr_mod.apply_recap_narrator_defaults(
        spec, run_dir=run, approved_path=approved_path if approved_path.is_file() else None
    )
    if force_regen_narration:
        spec["forceRegenNarration"] = True
        spec["narratorPreserveCapture"] = False

    if narr_mod.uses_full_script_narration(spec):
        est = float(spec.get("targetDurationSec") or existing.get("targetDurationSec") or 480)
        n_m = len(milestones)
        est = max(
            est,
            float(spec.get("introSec", 9))
            + float(spec.get("outroSec", 10))
            + n_m * float(spec.get("milestoneHoldSec", 12)),
        )
        # Full-script narration uses every milestone beat (video runtime is graded whole timeline).
        script_milestones = milestones
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
        if regen_script:
            spec["regenNarrationScript"] = True
            if full_json.is_file():
                full_json.unlink()
                print("Regenerating DemoRecapFullNarration.json from narrator beats…", flush=True)
        script_after_video = bool(spec.get("narrationScriptAfterVideo", True))
        need_script = regen_script or not full_json.is_file()
        if narration_only and need_script:
            print(
                "Narration-only: building local voice script (Personal Voice next — not waiting on Cursor Agent)",
                flush=True,
            )
            local = narr_mod.write_local_full_narration_script(
                run, script_milestones, spec, target_sec=est
            )
            if local:
                spec["fullNarrationScript"] = local
            spec.setdefault("fullScriptAlignToMilestones", False)
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
                "recapMode": "presentation_pro",
                "outputFps": 30,
                "timelapseEncodeFps": 30,
                "videoPlaybackFactor": 1.0,
                "videoEnhance": True,
                "sceneUpscale": 1.0,
                "framesPerSource": 1,
                "showAnnotations": False,
                "narrationSyncMode": "speech_first",
                "narrationMode": "fullScript",
                "narratorIgnoreCaptions": True,
                "narrationPlanMode": "estimate",
                "timelapseSecPerFrame": 0.09,
                "maxMilestoneHoldSec": 28.0,
                "maxSubbeatHoldSec": 28.0,
                "minMilestoneHoldSec": 20.0,
                "composeWorkDir": str(run / "_presentation_compose_preview120"),
                "milestoneHoldSec": 20.0,
                "subbeatHoldSec": 20.0,
                "introSec": 9.0,
                "outroSec": 10.0,
                "captionLine1FadeSec": 2.5,
                "captionLine2DelaySec": 3.0,
                "captionBulletStaggerSec": 1.8,
                "captionBulletFadeSec": 2.0,
                "captionLine3DelaySec": 11.0,
                "captionFutureFadeSec": 2.0,
                "captionReadPauseSec": 7.0,
                "segmentXfadeSec": 0.5,
                "cinematicCamera": False,
                "holdCinematicMotion": False,
                "staticSceneMotion": True,
                "disablePanMotion": True,
                "cinematicEffects": True,
                "javafxEffects": True,
                "javafxFxStrength": 1.4,
                "forceLocalCaptions": True,
                "botAvatarOverlay": True,
                "botAvatarLipSync": True,
                "presentationPlayModeBroll": True,
                "playModeBrollSec": 42.0,
                "syncHoldToNarration": False,
                "tlMaxFrames": 48,
                "encodeCodec": "auto",
            }
        )
        spec = narr_mod.flatten_narrator_personal_settings(spec)
        codec = producer.resolve_encode_codec(spec)
        print(
            f"Preview profile (style A): {int(spec['outputFps'])}fps, {codec}, "
            f"{spec['videoPlaybackFactor']:.0%} playback, fewer holds"
        )
    if narration_only:
        spec["narratorEnabled"] = True
        work = Path(str(spec.get("composeWorkDir") or run / "_presentation_compose")).expanduser()
        if not spec.get("narratorPreserveCapture", True):
            for name in ("narration.wav", "_full_narr_raw.wav"):
                p = work / name
                if p.is_file():
                    p.unlink()
            parts = work / "_narr_parts"
            if parts.is_dir():
                shutil.rmtree(parts, ignore_errors=True)
            print("Narration-only: cleared prior narration (forced regen)…", flush=True)
        else:
            print(
                "Narration-only: preserving Personal Voice capture "
                "(no delete, no time-stretch)…",
                flush=True,
            )
    narrator_on = "--no-narrator" not in sys.argv and spec.get("narratorEnabled", True)
    if narrator_on:
        say_r = narr_mod.personal_say_rate(spec)
        pname = str(
            spec.get("narratorPersonalVoice") or spec.get("personalVoiceName") or ""
        ).strip()
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
        # Output on capture folder (Lexar). Mirror to EnvKit DesktopMirror, not Mac Desktop.
        out = run / preview_name
        desktop_mirror = envkit.preview_mirror_dir() / preview_name
        mac_mirror = Path.home() / "Desktop" / "DemoRecap-Card-Preview" / preview_name
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
        import shutil

        try:
            shutil.copy2(out, desktop_mirror)
            print(f"Preview mirror (Lexar): {desktop_mirror}")
        except OSError as exc:
            print(f"Lexar mirror skipped ({exc}) — preview at {out}")
        free = envkit.mac_data_volume_free_gb()
        if free is not None and free >= 1.0:
            try:
                mac_mirror.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(out, mac_mirror)
                print(f"Mac Desktop mirror: {mac_mirror}")
            except OSError as exc:
                print(f"Mac Desktop mirror skipped ({exc})")
        else:
            print("Mac Desktop mirror skipped — disk low; open Lexar path above")
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
