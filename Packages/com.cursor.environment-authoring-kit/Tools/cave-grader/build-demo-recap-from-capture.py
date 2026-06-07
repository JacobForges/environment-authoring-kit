#!/usr/bin/env python3
"""
Compose DemoRecap from a capture run folder (your real Scene view PNGs).

Usage:
  python3 build-demo-recap-from-capture.py [run_folder] [--ai]
  python3 build-demo-recap-from-capture.py --latest [--ai]

Reads frames/frame_*.png + DemoRecapFrameManifest.json (or DemoRecapCompose.json).
With --ai, runs demo-recap-narrate.ts when Hub AI credentials are in the environment.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path


def resolve_hub() -> Path:
    hub = Path(os.environ.get("HUB_ROOT", "")).expanduser()
    if hub.is_dir() and (hub / "Assets").is_dir():
        return hub
    hub = Path(__file__).resolve().parent
    while hub != hub.parent and not (hub / "Assets").is_dir():
        hub = hub.parent
    return hub


def find_latest_run(hub: Path) -> Path | None:
    root = hub / "Library" / "EnvironmentKit" / "DemoCapture"
    if not root.is_dir():
        return None
    best: Path | None = None
    best_mtime = 0.0
    for d in root.iterdir():
        if not d.is_dir() or d.name.startswith("_"):
            continue
        frames = d / "frames"
        if not frames.is_dir():
            continue
        mt = frames.stat().st_mtime
        if mt > best_mtime and list(frames.glob("frame_*.png")):
            best_mtime = mt
            best = d
    return best


def load_frames(run: Path) -> tuple[list[dict], str]:
    for name in ("DemoRecapFrameManifest.json", "DemoRecapCompose.json"):
        p = run / name
        if p.is_file():
            data = json.loads(p.read_text())
            frames = data.get("frames", [])
            mode = data.get("buildMode", "build")
            if frames:
                return frames, mode

    pngs = sorted((run / "frames").glob("frame_*.png"))
    if not pngs:
        pngs = sorted(run.glob("frame_*.png"))
    return (
        [{"image": str(p), "phase": "", "sub": "", "line1": f"Checkpoint {i + 1}", "line2": "", "line3": ""} for i, p in enumerate(pngs)],
        "build",
    )


def maybe_ai_narrate(hub: Path, run: Path, frames: list[dict], build_mode: str) -> None:
    script = hub / "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/demo-recap-narrate.ts"
    if not script.is_file():
        script = Path(__file__).resolve().parent / "demo-recap-narrate.ts"
    req = run / "DemoRecapNarrateRequest.json"
    body = {"buildMode": build_mode, "frames": [{"i": i, **{k: f.get(k, "") for k in ("phase", "sub", "line1", "line2", "line3")}} for i, f in enumerate(frames)]}
    req.write_text(json.dumps(body))
    tools = script.parent
    tsx = tools / "node_modules/tsx/dist/cli.mjs"
    import shutil

    node = shutil.which("node") or "node"
    args = [node, str(tsx), str(script), str(req)] if tsx.is_file() else ["npx", "--yes", "tsx", str(script), str(req)]
    proc = subprocess.run(args, capture_output=True, text=True, cwd=tools, env=os.environ.copy())
    if proc.returncode != 0:
        print("AI narration skipped:", proc.stderr or proc.stdout, file=sys.stderr)
        return
    parsed = json.loads(proc.stdout)
    by_i = {c["i"]: c for c in parsed.get("captions", [])}
    for i, fr in enumerate(frames):
        c = by_i.get(i)
        if not c:
            continue
        fr["line1"] = c.get("line1", fr.get("line1", ""))
        fr["line2"] = c.get("line2", fr.get("line2", ""))
        fr["line3"] = c.get("line3", fr.get("line3", ""))


def main() -> int:
    hub = resolve_hub()
    use_ai = "--ai" in sys.argv
    args = [a for a in sys.argv[1:] if not a.startswith("--")]

    if args:
        run = Path(args[0]).expanduser()
    else:
        run = find_latest_run(hub)
        if run is None:
            print("No capture run with frames/ found. Run a build with demo recording on first.", file=sys.stderr)
            return 1

    frames, build_mode = load_frames(run)
    if not frames:
        print(f"No frames in {run}", file=sys.stderr)
        return 1

    if use_ai:
        maybe_ai_narrate(hub, run, frames, build_mode)

    spec = {
        "slideDuration": 3.2,
        "xfadeDuration": 0.42,
        "introTitle": "World Build Recap",
        "introSubtitle": f"Your Scene view — {build_mode} build with AI-narrated captions",
        "frames": frames,
    }
    spec_path = run / "DemoRecapCompose.json"
    spec_path.write_text(json.dumps(spec, indent=2))

    compose_py = Path(__file__).resolve().parent / "compose-demo-recap.py"
    out = run / "DemoRecap.mp4"
    env = {**os.environ, "HUB_ROOT": str(hub)}
    subprocess.run([sys.executable, str(compose_py), str(spec_path), str(out)], check=True, env=env)
    print(str(out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
