#!/usr/bin/env python3
"""Audit recap video clip timeline vs narration cues (diagnose voice/video drift)."""
from __future__ import annotations

import json
import sys
from pathlib import Path

_TOOLS = Path(__file__).resolve().parent


def load(name: str, file_name: str):
    import importlib.util

    spec = importlib.util.spec_from_file_location(name, _TOOLS / file_name)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def main() -> int:
    if len(sys.argv) < 2:
        print("Usage: python3 audit-recap-sync.py <capture_folder>", file=sys.stderr)
        return 1
    run = Path(sys.argv[1]).expanduser().resolve()
    narr = load("narr", "demo-recap-narrator.py")
    compose = load("compose", "compose-presentation-recap.py")

    tl_path = run / "DemoRecapTimeline.json"
    if not tl_path.is_file():
        print(f"Missing {tl_path}", file=sys.stderr)
        return 1
    spec = json.loads(tl_path.read_text(encoding="utf-8"))
    spec = narr.flatten_narrator_personal_settings(spec)
    milestones = spec.get("milestones") or []
    work = run / "_presentation_compose"
    clips: list[Path] = []
    if (work / "seg_intro.mp4").is_file():
        clips.append(work / "seg_intro.mp4")
    seg_dir = work / "segments"
    if seg_dir.is_dir():
        clips.extend(sorted(seg_dir.glob("*.mp4")))
    if (work / "seg_outro.mp4").is_file():
        clips.append(work / "seg_outro.mp4")
    if not clips:
        print("No segment mp4s found — run compose first.", file=sys.stderr)
        return 1

    hold_indices = list(range(1, 1 + len(list(seg_dir.glob("*.mp4"))))) if seg_dir.is_dir() else []
    full_script = narr.resolve_full_narration_script(run, spec, milestones)
    if not full_script:
        print("No full narration script — using milestone captions only.", file=sys.stderr)
        full_script = " ".join(narr.milestone_narration_cue_text(m, spec) for m in milestones)

    rows = narr.audit_clip_narration_sync(clips, milestones, hold_indices, full_script, spec)
    out = run / "RecapNarrationSyncAudit.json"
    payload = {
        "capture": str(run),
        "clipCount": len(clips),
        "fullScriptWords": len(full_script.split()),
        "alignToMilestones": bool(spec.get("fullScriptAlignToMilestones", True)),
        "clips": rows,
    }
    out.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {out} ({len(rows)} clips)")
    for row in rows[:8]:
        cue = "✓" if row["hasCue"] else "·"
        print(
            f"  {cue} t={row['videoStartSec']:6.1f}s dur={row['videoDurSec']:5.1f}s "
            f"{row['label'][:36]}"
        )
    if len(rows) > 8:
        print(f"  … {len(rows) - 8} more in JSON")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
