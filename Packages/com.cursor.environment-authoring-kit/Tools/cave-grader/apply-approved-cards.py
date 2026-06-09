#!/usr/bin/env python3
"""Bake ApprovedCards.json into ApprovedIntro/Outro PNGs and timeline card paths."""
from __future__ import annotations

import argparse
import importlib.util
import json
import shutil
from pathlib import Path
from typing import Any

from PIL import Image

EMERALD_ACCENT = (24, 235, 158)

from envkit_paths import approved_dir

APPROVED_DIR = approved_dir()

# Canonical filenames from emerald pick session (intro c / outro b). Copy-only — never regen in pipeline.
CANONICAL_INTRO = "_approval_intro_c.png"
CANONICAL_OUTRO = "_approval_outro_b.png"
CANONICAL_PORTRAIT = "_approval_portrait.png"


def resolve_approved_intro(approved: Path, intro_id: str) -> Path | None:
    # Prefer canonical emerald pick (_approval_intro_c) over flattened ApprovedIntro.png.
    for name in (CANONICAL_INTRO, f"_approval_intro_{intro_id}.png", "ApprovedIntro.png"):
        p = approved / name
        if p.is_file():
            return p
    return None


def resolve_approved_outro(approved: Path, outro_id: str) -> Path | None:
    # Prefer canonical emerald pick (_approval_outro_b) over flattened ApprovedOutro.png.
    for name in (CANONICAL_OUTRO, f"_approval_outro_{outro_id}.png", "ApprovedOutro.png"):
        p = approved / name
        if p.is_file():
            return p
    return None


def sync_approved_assets_to_run(run_dir: Path, *, intro_id: str, outro_id: str) -> None:
    """Copy user's approved cards into capture run — no regeneration."""
    intro_src = resolve_approved_intro(APPROVED_DIR, intro_id)
    outro_src = resolve_approved_outro(APPROVED_DIR, outro_id)
    if not intro_src or not outro_src:
        missing = []
        if not intro_src:
            missing.append(f"{CANONICAL_INTRO} or ApprovedIntro.png")
        if not outro_src:
            missing.append(f"{CANONICAL_OUTRO} or ApprovedOutro.png")
        raise SystemExit(
            f"Approved intro/outro missing in {APPROVED_DIR}: {', '.join(missing)}. "
            "Open the recap dashboard, verify intro/outro/portrait cards, then proceed — "
            "do not auto-regenerate."
        )
    shutil.copy2(intro_src, run_dir / "ApprovedIntro.png")
    shutil.copy2(outro_src, run_dir / "ApprovedOutro.png")
    print(f"Cards: copied intro from {intro_src.name}, outro from {outro_src.name}")

    for asset in (CANONICAL_PORTRAIT, "DemoRecapPortrait.png", "DemoRecapSignature.png", "ApprovedCards.json"):
        src = APPROVED_DIR / asset
        if src.is_file():
            shutil.copy2(src, run_dir / asset)
    portrait = APPROVED_DIR / CANONICAL_PORTRAIT
    if portrait.is_file():
        shutil.copy2(portrait, run_dir / "DemoRecapPortrait.png")


def _load_emerald():
    p = Path(__file__).resolve().parent / "demo-recap-emerald-variants.py"
    spec = importlib.util.spec_from_file_location("emerald", p)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def resolve_portrait(run_dir: Path) -> Path:
    """Headshot for outro — signature is composited separately, not baked into this file."""
    for base in (APPROVED_DIR, run_dir):
        for name in ("_approval_portrait.png", "DemoRecapPortrait.png"):
            p = base / name
            if p.is_file():
                return p
    for name in ("_approval_portrait.png", "DemoRecapPortrait.png", "_preview_portrait-only.png"):
        p = run_dir / name
        if p.is_file():
            return p
    raise SystemExit(f"No portrait in {run_dir} or {APPROVED_DIR}")


def resolve_signature(run_dir: Path) -> Path | None:
    for base in (run_dir, APPROVED_DIR):
        for name in (
            "DemoRecapSignature.png",
            "DemoRecapSignature-from-card.png",
        ):
            p = base / name
            if p.is_file():
                return p
    return None


def generate_cards(
    run_dir: Path,
    intro_id: str,
    outro_id: str,
    *,
    display_scale: float = 1.0,
) -> tuple[Path, Path]:
    em = _load_emerald()
    frames = sorted((run_dir / "timelapse").glob("tl_*.png"))
    if not frames:
        raise SystemExit("timelapse/tl_*.png required")
    intro_bg, outro_bg = em.pick_concept_frame_paths(frames, None)
    portrait = Image.open(resolve_portrait(run_dir))
    sig_path = resolve_signature(run_dir)
    signature = Image.open(sig_path) if sig_path else None

    intro_v = next(v for v in em.INTRO_VARIANTS if v["id"] == intro_id)
    outro_v = next(v for v in em.OUTRO_VARIANTS if v["id"] == outro_id)

    intro_path = run_dir / "ApprovedIntro.png"
    outro_path = run_dir / "ApprovedOutro.png"
    em.build_intro(
        intro_bg, intro_v, em.EMERALD_STYLE, display_scale=display_scale
    ).save(intro_path, quality=95)
    em.build_outro(
        outro_bg,
        portrait,
        outro_v,
        em.EMERALD_STYLE,
        signature=signature,
        signature_max_h=72,
        display_scale=display_scale,
    ).save(outro_path, quality=95)
    return intro_path, outro_path


def card_spec_fields(run_dir: Path) -> dict[str, Any]:
    intro = (run_dir / "ApprovedIntro.png").resolve()
    outro = (run_dir / "ApprovedOutro.png").resolve()
    return {
        "introCardImage": str(intro),
        "outroCardImage": str(outro),
        "approvedIntroImage": str(intro),
        "approvedOutroImage": str(outro),
        "introNoPortrait": True,
        "portfolioTitleCards": False,
        "introAccent": list(EMERALD_ACCENT),
        "outroAccent": list(EMERALD_ACCENT),
        "introTitle": "World Build Recap",
        "chapterFocusAnnotations": False,
        "cursorVision": False,
        "showAnnotations": True,
        "annotationSource": "opencv",
        "narratorEnabled": True,
        "narratorVoice": "Andrew",
        "introVariant": "c",
        "outroVariant": "b",
        "narrateIntro": True,
        "narrateOutro": True,
        "introNarratorOffsetSec": 1.0,
        "outroNarratorOffsetSec": 1.0,
        "narratorStartOffsetSec": 5.5,
        "milestoneHoldSec": 11.0,
        "subbeatHoldSec": 8.0,
        "introSec": 9.0,
        "outroSec": 10.0,
        "captionLine1FadeSec": 1.1,
        "captionLine2DelaySec": 1.5,
        "captionLine3DelaySec": 3.0,
        "captionReadPauseSec": 2.0,
        "annotationDelaySec": 5.0,
        "narratorSpeechPace": 1.0,
        "narratorHumanize": True,
        "narratorPersonalHumanize": True,
        "narratorEdgeRate": "+8%",
        "tlMaxFrames": 72,
        "tlMaxSec": 7.0,
        "tlTailMaxFrames": 48,
        "producerVersion": 3,
        "gradeAtMasterOnly": True,
        "narratorEngine": "personal",
        "narratorRequirePersonal": True,
        "narratorPolishPersonal": False,
        "narratorNaturalDelivery": False,
        "narratorNaturalPauses": False,
        "narratorPersonalLoudnorm": False,
    }


def apply_to_timeline(run_dir: Path, *, regen: bool = False) -> dict[str, Any]:
    import importlib.util

    narr_path = Path(__file__).resolve().parent / "demo-recap-narrator.py"
    ns = importlib.util.spec_from_file_location("narr", narr_path)
    narr = importlib.util.module_from_spec(ns)
    assert ns.loader
    ns.loader.exec_module(narr)

    cards_path = run_dir / "ApprovedCards.json"
    intro_id, outro_id = "c", "b"
    approved_raw: dict = {}
    if cards_path.is_file():
        approved_raw = json.loads(cards_path.read_text(encoding="utf-8"))
        intro_id = str(approved_raw.get("introVariant", intro_id))
        outro_id = str(approved_raw.get("outroVariant", outro_id))

    try:
        card_scale = float(approved_raw.get("captionCardDisplayScale", 1.0) or 1.0)
    except (TypeError, ValueError):
        card_scale = 1.0
    card_scale = max(0.5, min(1.0, card_scale))

    if regen:
        generate_cards(run_dir, intro_id, outro_id, display_scale=card_scale)
    else:
        sync_approved_assets_to_run(run_dir, intro_id=intro_id, outro_id=outro_id)

    fields = card_spec_fields(run_dir)
    fields = narr.flatten_narrator_personal_settings({**fields, **approved_raw})
    tl = run_dir / "DemoRecapTimeline.json"
    if tl.is_file():
        spec = json.loads(tl.read_text())
        spec.update(fields)
        tl.write_text(json.dumps(spec, indent=2) + "\n")
    return fields


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("run_dir", type=Path)
    ap.add_argument("--regen", action="store_true", help="Rebuild PNGs from emerald variants")
    args = ap.parse_args()
    fields = apply_to_timeline(args.run_dir.expanduser().resolve(), regen=args.regen)
    print(json.dumps(fields, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
