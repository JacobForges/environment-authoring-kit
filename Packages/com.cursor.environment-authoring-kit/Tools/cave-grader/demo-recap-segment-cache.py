"""Cache encoded segments — skip re-encode when inputs + spec unchanged."""
from __future__ import annotations

import hashlib
import json
import shutil
from pathlib import Path
from typing import Any, Callable


PRODUCER_CACHE_VERSION = "v4b-holdlen"


def spec_fingerprint(spec: dict[str, Any]) -> str:
    keys = (
        "producerVersion",
        "sceneFit",
        "tlMaxFrames",
        "tlMaxSec",
        "tlTailMaxFrames",
        "milestoneHoldSec",
        "syncHoldToNarration",
        "videoPlaybackFactor",
        "outputFps",
        "captionLine3DelaySec",
        "captionReadPauseSec",
        "introSec",
        "outroSec",
        "gradeAtMasterOnly",
        "showAnnotations",
    )
    blob = {k: spec.get(k) for k in keys if k in spec}
    blob["v"] = PRODUCER_CACHE_VERSION
    return hashlib.sha256(json.dumps(blob, sort_keys=True).encode()).hexdigest()[:12]


def segment_key(
    spec: dict[str, Any],
    kind: str,
    extra: str,
    source_paths: list[Path] | None = None,
) -> str:
    h = hashlib.sha256()
    h.update(spec_fingerprint(spec).encode())
    h.update(kind.encode())
    h.update(extra.encode())
    for p in source_paths or []:
        if p.is_file():
            st = p.stat()
            h.update(f"{p.name}:{st.st_size}:{int(st.st_mtime)}".encode())
    return h.hexdigest()[:20]


def get_cached(cache_dir: Path, key: str) -> Path | None:
    p = cache_dir / f"{key}.mp4"
    return p if p.is_file() and p.stat().st_size > 1024 else None


def put_cached(cache_dir: Path, key: str, built: Path) -> Path:
    cache_dir.mkdir(parents=True, exist_ok=True)
    dest = cache_dir / f"{key}.mp4"
    shutil.copy2(built, dest)
    return dest


def build_or_cache(
    cache_dir: Path,
    key: str,
    build_fn: Callable[[Path], None],
    out: Path,
) -> Path:
    hit = get_cached(cache_dir, key)
    if hit:
        shutil.copy2(hit, out)
        return out
    build_fn(out)
    put_cached(cache_dir, key, out)
    return out
