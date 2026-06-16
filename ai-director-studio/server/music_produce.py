"""Pop/trap vocal production — pitch correct, FX, mix with instrumental."""
from __future__ import annotations

import json
import shutil
import subprocess
import threading
from pathlib import Path
from typing import Any

from music_project import music_dir, read_music_project, update_music_project

_PRODUCE_LOCK = threading.Lock()
_ACTIVE: dict[str, bool] = {}

# Chromatic major/minor scale degrees (semitones from root)
SCALES = {
    "major": [0, 2, 4, 5, 7, 9, 11],
    "minor": [0, 2, 3, 5, 7, 8, 10],
}

NOTE_TO_SEMITONE = {
    "c": 0, "c#": 1, "db": 1, "d": 2, "d#": 3, "eb": 3,
    "e": 4, "f": 5, "f#": 6, "gb": 6, "g": 7, "g#": 8,
    "ab": 8, "a": 9, "a#": 10, "bb": 10, "b": 11,
}


def _ffmpeg() -> str:
    for candidate in ("/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "ffmpeg"):
        if shutil.which(candidate) or Path(candidate).is_file():
            return candidate
    raise RuntimeError("ffmpeg not found")


def _rubberband() -> str | None:
    for candidate in ("rubberband", "/opt/homebrew/bin/rubberband"):
        if shutil.which(candidate):
            return candidate
    return None


def _set_progress(project: Path, progress: int, message: str, *, running: bool = True) -> None:
    update_music_project(
        project,
        {"status": {"produceRunning": running, "produceProgress": progress, "produceMessage": message}},
    )


def _parse_key(key: str) -> tuple[int, str]:
    k = (key or "auto").strip().lower()
    if k == "auto" or not k:
        return 0, "minor"
    for note, semi in sorted(NOTE_TO_SEMITONE.items(), key=lambda x: -len(x[0])):
        if k.startswith(note):
            scale = "major" if "maj" in k else "minor"
            return semi, scale
    return 0, "minor"


def _snap_midi_to_scale(midi: float, root: int, scale_name: str) -> float:
    if midi <= 0 or midi != midi:  # NaN
        return midi
    scale = SCALES.get(scale_name, SCALES["minor"])
    pc = midi % 12
    octave = midi - pc
    root_pc = root % 12
    best = pc
    best_dist = 99.0
    for deg in scale:
        target = (root_pc + deg) % 12
        dist = min(abs(pc - target), 12 - abs(pc - target))
        if dist < best_dist:
            best_dist = dist
            best = target
    return octave + best


def _detect_pitch_librosa(y, sr: float):
    import numpy as np
    import librosa

    f0, voiced_flag, _ = librosa.pyin(
        y,
        fmin=librosa.note_to_hz("C2"),
        fmax=librosa.note_to_hz("C7"),
        sr=sr,
    )
    return f0, voiced_flag


def _pitch_shift_rubberband(src: Path, dest: Path, semitones: float) -> bool:
    rb = _rubberband()
    if not rb or abs(semitones) < 0.05:
        return False
    try:
        subprocess.run(
            [rb, "-p", f"{semitones:.3f}", str(src), str(dest)],
            check=True,
            capture_output=True,
            timeout=300,
        )
        return dest.is_file()
    except (subprocess.CalledProcessError, OSError):
        return False


def _pitch_shift_ffmpeg(src: Path, dest: Path, semitones: float) -> None:
    # rubberband filter in ffmpeg if available, else asetrate+atempo hack
    ratio = 2 ** (semitones / 12.0)
    filt = f"rubberband=pitch={ratio:.6f}" if semitones else "anull"
    try:
        subprocess.run(
            [_ffmpeg(), "-y", "-i", str(src), "-af", filt, str(dest)],
            check=True,
            capture_output=True,
            timeout=300,
        )
    except subprocess.CalledProcessError:
        # Fallback: coarse pitch via asetrate (quality lower)
        subprocess.run(
            [
                _ffmpeg(), "-y", "-i", str(src),
                "-af", f"asetrate=44100*{ratio:.6f},aresample=44100,atempo={1/ratio:.6f}",
                str(dest),
            ],
            check=True,
            capture_output=True,
            timeout=300,
        )


def _apply_fx_ffmpeg(src: Path, dest: Path) -> None:
    # Pop/trap chain: comp → light reverb → loudnorm limiter
    chain = (
        "acompressor=threshold=-18dB:ratio=4:attack=5:release=80:makeup=6,"
        "aecho=0.8:0.88:15:0.25,"
        "loudnorm=I=-10:TP=-1.5:LRA=7"
    )
    subprocess.run(
        [_ffmpeg(), "-y", "-i", str(src), "-af", chain, str(dest)],
        check=True,
        capture_output=True,
        timeout=300,
    )


def _mix_vocal_double(vocal: Path, out: Path, detune_cents: float = 15.0) -> None:
    """Stack light detuned duplicate for trap width."""
    ratio = 2 ** (detune_cents / 1200.0)
    filt = (
        f"[0:a]volume=1.0[v0];"
        f"[0:a]rubberband=pitch={ratio:.6f},volume=0.38[v1];"
        f"[v0][v1]amix=inputs=2:duration=first:dropout_transition=0[out]"
    )
    try:
        subprocess.run(
            [_ffmpeg(), "-y", "-i", str(vocal), "-filter_complex", filt, "-map", "[out]", str(out)],
            check=True,
            capture_output=True,
            timeout=300,
        )
    except subprocess.CalledProcessError:
        shutil.copy2(vocal, out)


def _mix_with_instrumental(vocal: Path, instrumental: Path, out_wav: Path, out_mp3: Path) -> None:
    subprocess.run(
        [
            _ffmpeg(), "-y",
            "-i", str(instrumental),
            "-i", str(vocal),
            "-filter_complex", "[0:a]volume=0.85[inst];[1:a]volume=1.0[vox];[inst][vox]amix=inputs=2:duration=longest:dropout_transition=0",
            str(out_wav),
        ],
        check=True,
        capture_output=True,
        timeout=300,
    )
    subprocess.run(
        [_ffmpeg(), "-y", "-i", str(out_wav), "-codec:a", "libmp3lame", "-q:a", "2", str(out_mp3)],
        check=True,
        capture_output=True,
        timeout=120,
    )


def produce_sync(project: Path) -> dict[str, Any]:
    """Run full pop/trap production chain synchronously."""
    doc = read_music_project(project)
    if not doc:
        return {"ok": False, "error": "not a music project"}
    mdir = music_dir(project)
    dry = mdir / "vocal_dry.wav"
    if not dry.is_file():
        raw = mdir / "vocal_raw.webm"
        if raw.is_file():
            subprocess.run(
                [_ffmpeg(), "-y", "-i", str(raw), "-ar", "44100", "-ac", "1", str(dry)],
                check=True,
                capture_output=True,
                timeout=120,
            )
    if not dry.is_file():
        return {"ok": False, "error": "record a vocal take first"}

    key_raw = str(doc.get("key") or "auto")
    scale = str(doc.get("scale") or "minor")
    root, parsed_scale = _parse_key(key_raw)
    if scale not in SCALES:
        scale = parsed_scale

    _set_progress(project, 5, "Loading vocal…")

    import numpy as np
    import soundfile as sf
    import librosa

    y, sr = librosa.load(str(dry), sr=44100, mono=True)
    _set_progress(project, 15, "Detecting pitch…")

    f0, voiced = _detect_pitch_librosa(y, sr)
    corrected = y.copy()
    chunk_ms = 40
    chunk = int(sr * chunk_ms / 1000)
    tuned_segments: list[np.ndarray] = []
    pos = 0

    _set_progress(project, 30, "Snapping to scale…")
    while pos < len(y):
        end = min(pos + chunk, len(y))
        seg_f0 = f0[pos:end]
        seg_voiced = voiced[pos:end] if voiced is not None else np.ones(end - pos, dtype=bool)
        seg = y[pos:end]
        if seg_voiced is not None and np.any(seg_voiced):
            valid = seg_f0[seg_voiced & np.isfinite(seg_f0)]
            if len(valid) > 0:
                median_hz = float(np.median(valid))
                if median_hz > 50:
                    midi = librosa.hz_to_midi(median_hz)
                    snapped = _snap_midi_to_scale(midi, root, scale)
                    shift = snapped - midi
                    if abs(shift) > 0.08:
                        seg_path = mdir / f"_chunk_{pos}.wav"
                        out_path = mdir / f"_chunk_{pos}_t.wav"
                        sf.write(str(seg_path), seg, sr)
                        if not _pitch_shift_rubberband(seg_path, out_path, shift):
                            _pitch_shift_ffmpeg(seg_path, out_path, shift)
                        seg, _ = librosa.load(str(out_path), sr=sr, mono=True)
                        seg_path.unlink(missing_ok=True)
                        out_path.unlink(missing_ok=True)
        tuned_segments.append(seg)
        pos = end

    tuned = np.concatenate(tuned_segments) if tuned_segments else y
    tuned_path = mdir / "vocal_tuned.wav"
    sf.write(str(tuned_path), tuned, sr)

    _set_progress(project, 55, "Applying pop/trap FX…")
    fx_path = mdir / "vocal_fx.wav"
    _apply_fx_ffmpeg(tuned_path, fx_path)

    _set_progress(project, 70, "Stacking vocal…")
    doubled = mdir / "vocal_produced.wav"
    _mix_vocal_double(fx_path, doubled)

    instrumental = mdir / "instrumental.mp3"
    master_wav = mdir / "SongMaster.wav"
    master_mp3 = mdir / "SongMaster.mp3"
    if instrumental.is_file():
        _set_progress(project, 85, "Mixing with instrumental…")
        _mix_with_instrumental(doubled, instrumental, master_wav, master_mp3)
    else:
        shutil.copy2(doubled, master_wav)
        subprocess.run(
            [_ffmpeg(), "-y", "-i", str(doubled), "-codec:a", "libmp3lame", "-q:a", "2", str(master_mp3)],
            check=True,
            capture_output=True,
            timeout=120,
        )

    _set_progress(project, 100, "Done — sing bad, sound pro.", running=False)
    update_music_project(project, {"status": {"produced": True, "produceRunning": False}})
    return {
        "ok": True,
        "vocalDry": str(dry),
        "vocalProduced": str(doubled),
        "songMasterWav": str(master_wav),
        "songMasterMp3": str(master_mp3),
    }


def produce_async(project: Path) -> dict[str, Any]:
    key = str(project.resolve())
    with _PRODUCE_LOCK:
        if _ACTIVE.get(key):
            return {"ok": True, "started": False, "message": "produce already running"}
        _ACTIVE[key] = True

    def _run() -> None:
        try:
            produce_sync(project)
        except Exception as exc:
            _set_progress(project, 0, f"Error: {exc}", running=False)
        finally:
            with _PRODUCE_LOCK:
                _ACTIVE.pop(key, None)

    threading.Thread(target=_run, daemon=True).start()
    return {"ok": True, "started": True}


def create_music_video_project(music_project: Path) -> dict[str, Any]:
    """Hand off to video director flow with performance footage + song master."""
    from director_paths import create_project
    import ingest_media

    doc = read_music_project(music_project)
    if not doc:
        return {"ok": False, "error": "not a music project"}
    mdir = music_dir(music_project)
    perf = mdir / "performance.mp4"
    master = mdir / "SongMaster.wav"
    if not perf.is_file():
        return {"ok": False, "error": "record performance video for music video handoff"}
    if not master.is_file():
        master = mdir / "SongMaster.mp3"
    if not master.is_file():
        return {"ok": False, "error": "produce the song first"}

    name = f"{doc.get('name', 'track')}-mv"
    video_project = create_project(name)
    shutil.copy2(perf, video_project / "uploads" / "performance.mp4")
    shutil.copy2(master, video_project / "uploads" / "SongMaster.wav")

    ingest_result = ingest_media.ingest_files(
        video_project,
        [video_project / "uploads" / "performance.mp4"],
        title=name,
    )

    # Music-video preset session
    import director_session as director

    session_doc = {
        "version": 2,
        "checklistVersion": 2,
        "capture": str(video_project),
        "phase": "qna",
        "preset": "music-video",
        "createdAt": director._utc(),
        "messages": [
            {
                "role": "assistant",
                "content": (
                    "Welcome to **music video pre-production** — your performance footage and mastered track are loaded.\n\n"
                    "I'll help you plan cuts, lip-sync moments, color, and social export for a pop/trap visual.\n\n"
                    "**First question:** what's the vibe — performance video, narrative mini-film, or lyric visual?"
                ),
            }
        ],
        "checklist": [dict(c) for c in director.DEFAULT_CHECKLIST[:12]],
        "brief": {
            "format": "music-video",
            "songMaster": str(video_project / "uploads" / "SongMaster.wav"),
            "sourceMusicProject": str(music_project),
        },
        "voicePersona": dict(director.VOICE_PERSONA_DEFAULTS),
        "exportApproved": False,
        "footageSummary": {
            "source": "music_video_handoff",
            "frameCount": ingest_result.get("frameCount", 0),
            "hasVideo": True,
            "hasImages": False,
            "phase": "user_media",
        },
    }
    from envkit_paths import atomic_write_text
    import json

    atomic_write_text(video_project / "DirectorSession.json", json.dumps(session_doc, indent=2))

    return {
        "ok": True,
        "videoProject": str(video_project),
        "ingest": ingest_result,
        "preset": "music-video",
    }
