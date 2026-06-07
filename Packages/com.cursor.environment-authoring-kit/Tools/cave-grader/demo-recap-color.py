"""Single authority color — prep (letterbox) vs master grade (applied once at end)."""
from __future__ import annotations

from pathlib import Path
from typing import Any


# Documentary earth tone — applied ONLY on final mux (producer v3).
MASTER_GRADE_VF = (
    "eq=gamma=1.06:contrast=0.94:saturation=1.18:brightness=-0.06,"
    "colorbalance=rs=-0.04:gs=0.06:bs=0.10:rm=0.0:gm=0.02:bm=0.05,"
    "hue=s=1.05,"
    "unsharp=5:5:0.20:5:5:0.0"
)

LOUDNORM_VF = "loudnorm=I=-16:TP=-1.5:LRA=11:print_format=summary"


def output_dimensions(spec: dict[str, Any] | None = None) -> tuple[int, int]:
    spec = spec or {}
    w = int(spec.get("outputWidth", 1920))
    h = int(spec.get("outputHeight", 1080))
    return w, h


def prep_vf(spec: dict[str, Any] | None = None) -> str:
    """Letterbox + 2× upscale/downscale for sharper motion segments (default 1080p)."""
    spec = spec or {}
    w, h = output_dimensions(spec)
    up_w, up_h = w * 2, h * 2
    base = (
        f"scale={up_w}:{up_h}:force_original_aspect_ratio=decrease:flags=lanczos,"
        f"pad={up_w}:{up_h}:(ow-iw)/2:(oh-ih)/2:color=0x0E1218,"
        f"scale={w}:{h}:flags=lanczos"
    )
    if spec.get("videoEnhance", True):
        return base + ",unsharp=5:5:0.18:5:5:0.0"
    return base


def master_filter_chain(spec: dict[str, Any] | None = None, *, loudnorm: bool = True) -> str:
    _ = spec
    chain = MASTER_GRADE_VF
    if loudnorm and spec is not None and spec.get("loudnorm", True):
        chain += f",{LOUDNORM_VF}"
    return chain


def lut_path(tools_dir: Path) -> Path | None:
    p = tools_dir / "assets" / "recap_master.cube"
    return p if p.is_file() else None


def master_with_optional_lut(tools_dir: Path, spec: dict[str, Any] | None = None) -> str:
    """Video-only master grade (loudnorm is audio — applied at narrator mux, not here)."""
    spec = spec or {}
    lut = lut_path(tools_dir)
    if lut and spec.get("useCubeLut", False):
        chain = f"lut3d=file='{lut}'"
    else:
        chain = MASTER_GRADE_VF
    if spec.get("cinematicEffects", True):
        chain += ",noise=alls=3:allf=t+u"
    return chain
