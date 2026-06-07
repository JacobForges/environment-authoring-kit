#!/usr/bin/env python3
"""
Optional Stable Diffusion polish for demo recap timelapse PNGs.

Does NOT replace the default Personal Voice recap pipeline. Outputs a sibling
``timelapse_sd/`` folder; pass ``--sd-enhance`` to compose scripts when wired.

Supports Automatic1111 / Forge and ComfyUI-compatible img2img HTTP APIs.

Environment:
  SD_API_URL          Base URL (default http://127.0.0.1:7860)
  SD_API_PROVIDER     a1111 | comfy  (default a1111)
  SD_MODEL            Checkpoint name for A1111 (optional)
  SD_PROMPT           Positive prompt override
  SD_NEGATIVE_PROMPT  Negative prompt override
  SD_DENOISE          img2img denoise 0–1 (default 0.28)
  SD_STRENGTH         Alias for SD_DENOISE
  SD_STEPS            Sampling steps (default 18)
  SD_CFG_SCALE        CFG scale (default 5.5)
  SD_SAMPLER          Sampler name (default DPM++ 2M Karras)
  SD_MAX_FRAMES       Cap processed frames (default 0 = all)
  SD_SKIP_EXISTING    1 to skip files already in timelapse_sd/

Examples:
  python3 sd-enhance-recap.py /path/to/DemoCapture/20260605-224357
  SD_API_URL=http://127.0.0.1:8188 SD_API_PROVIDER=comfy \\
    python3 sd-enhance-recap.py /path/to/run --milestone-only
"""
from __future__ import annotations

import argparse
import base64
import json
import os
import sys
import time
import urllib.error
import urllib.request
from io import BytesIO
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print("Install Pillow: pip3 install --user pillow", file=sys.stderr)
    raise


DEFAULT_POSITIVE = (
    "cinematic aerial game world build timelapse, crisp terrain detail, "
    "natural lighting, documentary color grade, sharp focus, no text, no watermark"
)
DEFAULT_NEGATIVE = (
    "blurry, low quality, watermark, text, logo, oversaturated, cartoon, "
    "duplicate, deformed, ugly, noisy, jpeg artifacts"
)


def env_float(name: str, default: float) -> float:
    raw = os.environ.get(name, "").strip()
    if not raw:
        return default
    try:
        return float(raw)
    except ValueError:
        return default


def env_int(name: str, default: int) -> int:
    raw = os.environ.get(name, "").strip()
    if not raw:
        return default
    try:
        return int(raw)
    except ValueError:
        return default


def api_base() -> str:
    return os.environ.get("SD_API_URL", "http://127.0.0.1:7860").rstrip("/")


def provider() -> str:
    return os.environ.get("SD_API_PROVIDER", "a1111").strip().lower()


def post_json(url: str, payload: dict, timeout: float = 600.0) -> dict:
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        body = resp.read()
    return json.loads(body.decode("utf-8"))


def get_json(url: str, timeout: float = 30.0) -> dict:
    with urllib.request.urlopen(url, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def ping_api() -> None:
    base = api_base()
    prov = provider()
    if prov == "comfy":
        get_json(f"{base}/system_stats")
        return
    get_json(f"{base}/sdapi/v1/options")


def png_to_b64(path: Path) -> str:
    return base64.b64encode(path.read_bytes()).decode("ascii")


def b64_to_png(data: str, out: Path) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(base64.b64decode(data))


def resize_for_api(img: Image.Image, max_side: int = 1280) -> Image.Image:
    w, h = img.size
    if max(w, h) <= max_side:
        return img
    scale = max_side / float(max(w, h))
    nw, nh = max(1, int(w * scale)), max(1, int(h * scale))
    return img.resize((nw, nh), Image.Resampling.LANCZOS)


def image_path_to_b64(path: Path) -> str:
    with Image.open(path) as img:
        img = resize_for_api(img.convert("RGB"))
        buf = BytesIO()
        img.save(buf, format="PNG")
    return base64.b64encode(buf.getvalue()).decode("ascii")


def enhance_a1111(path: Path, out: Path) -> None:
    base = api_base()
    model = os.environ.get("SD_MODEL", "").strip()
    if model:
        post_json(f"{base}/sdapi/v1/options", {"sd_model_checkpoint": model})

    denoise = env_float("SD_DENOISE", env_float("SD_STRENGTH", 0.28))
    payload = {
        "init_images": [image_path_to_b64(path)],
        "prompt": os.environ.get("SD_PROMPT", DEFAULT_POSITIVE),
        "negative_prompt": os.environ.get("SD_NEGATIVE_PROMPT", DEFAULT_NEGATIVE),
        "denoising_strength": denoise,
        "steps": env_int("SD_STEPS", 18),
        "cfg_scale": env_float("SD_CFG_SCALE", 5.5),
        "sampler_name": os.environ.get("SD_SAMPLER", "DPM++ 2M Karras"),
        "width": 1280,
        "height": 720,
        "restore_faces": False,
    }
    result = post_json(f"{base}/sdapi/v1/img2img", payload)
    images = result.get("images") or []
    if not images:
        raise RuntimeError(f"A1111 img2img returned no images for {path.name}")
    b64_to_png(images[0], out)


def enhance_comfy(path: Path, out: Path) -> None:
    """Minimal ComfyUI img2img via default workflow node names — customize in env if needed."""
    base = api_base()
    workflow_path = os.environ.get("SD_COMFY_WORKFLOW", "").strip()
    if workflow_path:
        workflow = json.loads(Path(workflow_path).read_text(encoding="utf-8"))
        # Caller supplies a full workflow JSON with {{IMAGE_B64}} placeholders.
        blob = json.dumps(workflow).replace("{{IMAGE_B64}}", image_path_to_b64(path))
        workflow = json.loads(blob)
    else:
        raise RuntimeError(
            "ComfyUI mode needs SD_COMFY_WORKFLOW pointing at an img2img workflow JSON "
            "(export from ComfyUI; use {{IMAGE_B64}} for the init image)."
        )

    queued = post_json(f"{base}/prompt", {"prompt": workflow})
    prompt_id = queued.get("prompt_id")
    if not prompt_id:
        raise RuntimeError("ComfyUI /prompt did not return prompt_id")

    deadline = time.time() + 900.0
    while time.time() < deadline:
        hist = get_json(f"{base}/history/{prompt_id}")
        entry = hist.get(prompt_id)
        if entry and entry.get("outputs"):
            for node_out in entry["outputs"].values():
                for img_meta in node_out.get("images", []):
                    view = (
                        f"{base}/view?filename={img_meta['filename']}"
                        f"&subfolder={img_meta.get('subfolder', '')}"
                        f"&type={img_meta.get('type', 'output')}"
                    )
                    with urllib.request.urlopen(view, timeout=120) as resp:
                        out.parent.mkdir(parents=True, exist_ok=True)
                        out.write_bytes(resp.read())
                    return
        time.sleep(1.2)
    raise RuntimeError(f"ComfyUI prompt {prompt_id} timed out")


def list_timelapse_frames(run_dir: Path) -> list[Path]:
    tl = run_dir / "timelapse"
    if not tl.is_dir():
        raise FileNotFoundError(f"Missing timelapse folder: {tl}")
    frames = sorted(tl.glob("tl_*.png"))
    if not frames:
        raise FileNotFoundError(f"No tl_*.png frames in {tl}")
    return frames


def milestone_frame_indices(run_dir: Path) -> set[int]:
    timeline = run_dir / "DemoRecapTimeline.json"
    if not timeline.is_file():
        return set()
    try:
        data = json.loads(timeline.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return set()
    indices: set[int] = set()
    for m in data.get("milestones") or []:
        if isinstance(m, dict) and isinstance(m.get("frame"), int):
            indices.add(int(m["frame"]))
    return indices


def frame_index(path: Path) -> int:
    stem = path.stem
    if "_" in stem:
        try:
            return int(stem.rsplit("_", 1)[-1])
        except ValueError:
            pass
    return 0


def enhance_frame(path: Path, out: Path) -> None:
    prov = provider()
    if prov == "comfy":
        enhance_comfy(path, out)
    else:
        enhance_a1111(path, out)


def write_manifest(run_dir: Path, out_dir: Path, sources: list[Path]) -> None:
    manifest = {
        "provider": provider(),
        "api": api_base(),
        "denoise": env_float("SD_DENOISE", env_float("SD_STRENGTH", 0.28)),
        "frames": [
            {
                "source": str(src),
                "enhanced": str(out_dir / src.name),
            }
            for src in sources
        ],
    }
    (run_dir / "timelapse_sd_manifest.json").write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Optional SD img2img polish for recap timelapse PNGs.")
    parser.add_argument("run_dir", type=Path, help="DemoCapture run folder (contains timelapse/)")
    parser.add_argument(
        "--milestone-only",
        action="store_true",
        help="Only enhance frames referenced in DemoRecapTimeline.json milestones",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="List frames that would be enhanced without calling the API",
    )
    args = parser.parse_args(argv)

    run_dir = args.run_dir.expanduser().resolve()
    if not run_dir.is_dir():
        print(f"ERROR: not a directory: {run_dir}", file=sys.stderr)
        return 2

    frames = list_timelapse_frames(run_dir)
    if args.milestone_only:
        wanted = milestone_frame_indices(run_dir)
        if wanted:
            frames = [f for f in frames if frame_index(f) in wanted]

    cap = env_int("SD_MAX_FRAMES", 0)
    if cap > 0:
        frames = frames[:cap]

    out_dir = run_dir / "timelapse_sd"
    skip_existing = os.environ.get("SD_SKIP_EXISTING", "1").strip() not in ("0", "false", "no")

    print(f"SD enhance: {len(frames)} frame(s) → {out_dir}")
    print(f"  API: {api_base()} ({provider()})")

    if args.dry_run:
        for f in frames:
            print(f"  would enhance {f.name}")
        return 0

    try:
        ping_api()
    except (urllib.error.URLError, TimeoutError, RuntimeError) as exc:
        print(
            f"ERROR: SD API not reachable at {api_base()}: {exc}\n"
            "Start Automatic1111/Forge (default :7860) or ComfyUI (:8188) and set SD_API_URL.",
            file=sys.stderr,
        )
        return 3

    out_dir.mkdir(parents=True, exist_ok=True)
    processed: list[Path] = []
    for i, src in enumerate(frames, start=1):
        dst = out_dir / src.name
        if skip_existing and dst.is_file():
            print(f"[{i}/{len(frames)}] skip existing {dst.name}")
            processed.append(src)
            continue
        print(f"[{i}/{len(frames)}] {src.name} …")
        try:
            enhance_frame(src, dst)
            processed.append(src)
        except Exception as exc:
            print(f"  WARN: {src.name} failed: {exc}", file=sys.stderr)

    if not processed:
        print("No frames enhanced.", file=sys.stderr)
        return 4

    write_manifest(run_dir, out_dir, processed)
    print(f"Done — {len(processed)} frame(s) in {out_dir}")
    print("Default recap pipeline unchanged. To use enhanced frames, re-run compose with --sd-enhance (when wired).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
