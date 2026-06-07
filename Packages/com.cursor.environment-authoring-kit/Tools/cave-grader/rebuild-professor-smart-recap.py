#!/usr/bin/env python3
"""
Rebuild a timelapse DemoRecap with dense professor milestones + Hub AI captions + 30fps smart compose.

Usage:
  python3 rebuild-professor-smart-recap.py /path/to/DemoCapture/run [--milestones 42] [--no-ai]
"""
from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

PROVIDER_NAMES = [
    "Cursor",
    "GoogleGemini",
    "AnthropicClaude",
    "OpenAICompatible",
    "OpenRouter",
    "LocalOllama",
    "LocalLmStudio",
    "CustomEndpoint",
]

CHAPTERS = [
    ("Session bootstrap", "surface_build", "editor queue + capture cadence", [72, 138, 220]),
    ("Grid contract", "surface_build", "nine-tile / fullworld layout", [90, 168, 120]),
    ("Seam invariants", "surface_build", "tile borders + height continuity", [110, 175, 200]),
    ("Play disk grading", "surface_build", "central flat + trail approach", [200, 140, 80]),
    ("Terrain meat loop", "surface_build", "heightfield passes + bench queue", [210, 120, 90]),
    ("Foothills envelope", "surface_build", "outer relief without breaking disk", [150, 110, 210]),
    ("Mountain ring", "surface_build", "annex massing + silhouette", [130, 100, 190]),
    ("Labyrinth annex", "surface_build", "south maze carve (non-star layout)", [100, 190, 230]),
    ("Trail bench pressure", "surface_build", "radial trail + queue depth", [220, 90, 90]),
    ("Surface lock pending", "surface_build", "pre-cave surface still authoritative", [72, 138, 220]),
    ("Pipeline step 1/122", "surface_build", "additive surface phase still running", [90, 168, 120]),
    ("Recording tail", "surface_build", "last Scene view before editor stop", [220, 90, 90]),
]


def resolve_hub() -> Path:
    hub = Path(os.environ.get("HUB_ROOT", "")).expanduser()
    if hub.is_dir() and (hub / "Assets").is_dir():
        return hub
    hub = Path(__file__).resolve().parent
    while hub != hub.parent and not (hub / "Assets").is_dir():
        hub = hub.parent
    return hub


def load_dotenv(hub: Path) -> None:
    env_path = hub / "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/.env"
    if not env_path.is_file():
        return
    for line in env_path.read_text().splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, val = line.partition("=")
        key = key.strip()
        val = val.strip().strip('"').strip("'")
        if key and key not in os.environ:
            os.environ[key] = val


def load_hub_ai_env(hub: Path) -> None:
    load_dotenv(hub)
    asset = (
        hub
        / "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/CaveBuildCursorSettings.asset"
    )
    if not asset.is_file():
        return
    text = asset.read_text()
    m = re.search(r"aiProvider:\s*(\d+)", text)
    provider_idx = int(m.group(1)) if m else 0
    provider = PROVIDER_NAMES[provider_idx] if 0 <= provider_idx < len(PROVIDER_NAMES) else "Cursor"
    os.environ.setdefault("CAVE_AI_PROVIDER", provider)

    def pick(field: str, default: str = "") -> str:
        mm = re.search(rf"{field}:\s*(.+)", text)
        return mm.group(1).strip() if mm else default

    if provider == "OpenAICompatible":
        os.environ.setdefault("CAVE_ACTIVE_BASE_URL", pick("openAiCompatibleBaseUrl"))
        os.environ.setdefault("CAVE_ACTIVE_MODEL", pick("openAiModelId", "gpt-4.1-mini"))
    elif provider == "LocalOllama":
        os.environ.setdefault("CAVE_ACTIVE_BASE_URL", "http://localhost:11434/v1")
        os.environ.setdefault("CAVE_ACTIVE_MODEL", pick("ollamaModelId", "qwen2.5-coder:14b"))
        os.environ.setdefault("CAVE_ACTIVE_API_KEY", os.environ.get("CAVE_ACTIVE_API_KEY", "ollama"))
    elif provider == "Cursor":
        os.environ.setdefault("CAVE_ACTIVE_MODEL", pick("modelId", "auto"))
        os.environ.setdefault(
            "CAVE_ACTIVE_API_KEY",
            os.environ.get("CURSOR_API_KEY", os.environ.get("CAVE_ACTIVE_API_KEY", "")),
        )
    elif provider == "GoogleGemini":
        os.environ.setdefault("CAVE_ACTIVE_MODEL", pick("googleModelId", "gemini-2.5-flash"))
    elif provider == "AnthropicClaude":
        os.environ.setdefault("CAVE_ACTIVE_MODEL", pick("anthropicModelId", "claude-3-7-sonnet-latest"))
    elif provider == "OpenRouter":
        os.environ.setdefault("CAVE_ACTIVE_MODEL", pick("openRouterModelId", "openai/gpt-4.1-mini"))

    os.environ.setdefault("HUB_ROOT", str(hub))
    os.environ.setdefault("TSX_DISABLE_IPC", "1")


def list_timelapse(run: Path) -> list[Path]:
    for sub in ("timelapse", "."):
        folder = run / sub if sub != "." else run
        for pattern in ("tl_*.png", "t_*.png"):
            hits = sorted(folder.glob(pattern))
            if hits:
                return hits
    return []


def frame_index(path: Path) -> int:
    stem = path.stem
    if "_" in stem:
        try:
            return int(stem.split("_")[-1]) - 1
        except ValueError:
            pass
    return 0


def professor_draft(chapter: str, phase: str, sub: str, progress: float) -> tuple[str, str, str]:
    pct = int(progress * 100)
    line1 = f"{chapter} — observe the Scene view at ~{pct}% of this recording."
    notes = {
        "Session bootstrap": (
            "We begin by stabilizing capture cadence and editor queue policy: the timelapse is evidence, not decoration.",
            "Note whether terrain jobs are paced; flooding the queue early usually means later phases never arrive.",
        ),
        "Grid contract": (
            "The nine-tile fullworld grid is a contract: each tile must agree on edge heights before meat passes amplify error.",
            "If seams flicker between holds, fix grid/flatten invariants before foothills — otherwise you sculpt noise.",
        ),
        "Seam invariants": (
            "Seam passes exist to prevent discontinuities that players feel as 'invisible walls' at tile borders.",
            "Compare corner vertices across holds; a single bad seam propagates through every later mountain bench.",
        ),
        "Play disk grading": (
            "The play disk is gameplay-first relief: too flat reads dull, too rough breaks locomotion budgets.",
            "Watch the central disk stay readable while outer noise increases — that separation is intentional pipeline order.",
        ),
        "Terrain meat loop": (
            "Terrain meat is mostly paced heightmap work; long plateaus in the timelapse usually mean queue depth, not failure.",
            "Teach students to read editor load indirectly: motion slows when radial trail benches stack behind surface jobs.",
        ),
        "Foothills envelope": (
            "Foothills translate disk logic into wilderness without breaking the hub approach corridors.",
            "If annex silhouettes sharpen here, mountain passes are about to compete for the same editor time slice.",
        ),
        "Mountain ring": (
            "Mountain ring massing frames the world; it must respect labyrinth exclusions and mouth sightlines later.",
            "Ask whether relief reads at gameplay scale — aerial beauty that fights collision is a grading failure mode.",
        ),
        "Labyrinth annex": (
            "The south annex labyrinth should read as maze branches, not a hub star — layout audits belong in this chapter.",
            "Students should verify carve scope stayed annex-local; global stars are a common regression when queues backlog.",
        ),
        "Trail bench pressure": (
            "Radial trail benches often dominate queue depth in additive surface builds — this is a scheduling lesson.",
            "When holds look similar for minutes, check whether one bench label is spamming; safeguards cap pending depth.",
        ),
        "Surface lock pending": (
            "Until surface lock fires, cave geometry must not mutate authoritative height — treat surface as source of truth.",
            "Full AAA rebuild later should be non-additive; additive sessions can stall on surface step 1/N for hours.",
        ),
        "Pipeline step 1/122": (
            "Staying on surface step 1/122 means downstream cave phases never started — interpret the timeline accordingly.",
            "This recording ended during surface_build; captions should not imply cave meat or props that never ran.",
        ),
        "Recording tail": (
            "The final hold is forensic: compare against layout audit before you rerun Full AAA Rebuild.",
            "Preserve captures after editor disconnect — they are the only honest artifact when logs were overwritten.",
        ),
    }
    line2, line3 = notes.get(
        chapter,
        (
            f"Pedagogy for {sub}: treat {phase} as an ordered dependency in procedural authoring.",
            "Compare frame-to-frame: relief, queue pressure, and annex readability should change gradually.",
        ),
    )
    return line1, line2, line3


def build_milestones(frame_count: int, build_mode: str, count: int) -> list[dict]:
    milestones: list[dict] = []
    for i in range(count):
        if count <= 1:
            fi = 0
        else:
            fi = round(i * (frame_count - 1) / (count - 1))
        progress = fi / max(frame_count - 1, 1)
        chapter, phase, sub, accent = CHAPTERS[min(int(progress * len(CHAPTERS)), len(CHAPTERS) - 1)]
        line1, line2, line3 = professor_draft(chapter, phase, sub, progress)
        milestones.append(
            {
                "frame": fi,
                "chapter": chapter,
                "phase": phase,
                "sub": sub,
                "line1": line1,
                "line2": line2,
                "line3": line3,
                "accent": accent,
            }
        )
    return milestones


def run_ai_narrate(hub: Path, run: Path, milestones: list[dict], build_mode: str) -> bool:
    script = Path(__file__).resolve().parent / "demo-recap-narrate.ts"
    req = run / "DemoRecapNarrateRequest.json"
    body = {
        "buildMode": build_mode,
        "frames": [
            {
                "i": i,
                "phase": m.get("phase", ""),
                "sub": m.get("sub", ""),
                "line1": m.get("line1", ""),
                "line2": m.get("line2", ""),
                "line3": m.get("line3", ""),
            }
            for i, m in enumerate(milestones)
        ],
    }
    req.write_text(json.dumps(body, indent=2))
    tools = script.parent
    tsx = tools / "node_modules/tsx/dist/cli.mjs"
    node = shutil.which("node") or "node"
    if tsx.is_file():
        args = [node, "--import", "tsx", str(script), str(req)]
    else:
        args = ["npx", "--yes", "tsx", str(script), str(req)]
    env = os.environ.copy()
    env["HUB_ROOT"] = str(hub)
    env["TSX_DISABLE_IPC"] = "1"
    proc = subprocess.run(args, capture_output=True, text=True, cwd=tools, env=env, timeout=600)
    if proc.returncode != 0:
        print("AI narration failed:", proc.stderr or proc.stdout, file=sys.stderr)
        return False
    (run / "DemoRecapNarrateResponse.json").write_text(proc.stdout)
    parsed = json.loads(proc.stdout)
    by_i = {c["i"]: c for c in parsed.get("captions", [])}
    applied = 0
    for i, m in enumerate(milestones):
        c = by_i.get(i)
        if not c:
            continue
        m["line1"] = c.get("line1", m.get("line1", ""))
        m["line2"] = c.get("line2", m.get("line2", ""))
        m["line3"] = c.get("line3", m.get("line3", ""))
        applied += 1
    print(f"AI captions applied to {applied}/{len(milestones)} milestones via {env.get('CAVE_AI_PROVIDER')}.")
    return applied > 0


def main() -> int:
    hub = resolve_hub()
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    pipeline = Path(__file__).resolve().parent / "run-demo-recap-pipeline.py"
    if pipeline.is_file() and "--legacy" not in sys.argv:
        extra = [a for a in sys.argv[1:] if a.startswith("--")]
        if "--no-ai" in extra:
            extra = [*(e for e in extra if e != "--no-ai"), "--draft-only"]
        cmd = [sys.executable, str(pipeline), *args, *extra]
        print("Delegating to run-demo-recap-pipeline.py …")
        return subprocess.call(cmd, env={**os.environ, "HUB_ROOT": str(hub)})

    use_ai = "--no-ai" not in sys.argv
    count = 42
    for i, a in enumerate(sys.argv[1:]):
        if a == "--milestones" and i + 2 < len(sys.argv):
            try:
                count = max(8, int(sys.argv[i + 2]))
            except ValueError:
                pass

    if not args:
        print("usage: rebuild-professor-smart-recap.py <run_folder> [--milestones N] [--no-ai]", file=sys.stderr)
        return 1

    run = Path(args[0]).expanduser()
    frames = list_timelapse(run)
    if len(frames) < 8:
        print(f"Need timelapse PNGs in {run}/timelapse (found {len(frames)}).", file=sys.stderr)
        return 1

    frame_count = len(frames)
    print(f"Timelapse frames: {frame_count}")

    existing = {}
    tl_path = run / "DemoRecapTimeline.json"
    if tl_path.is_file():
        existing = json.loads(tl_path.read_text())

    build_mode = existing.get("buildMode", "FullWorld additive surface_build")
    reuse = (
        existing.get("milestones")
        and len(existing["milestones"]) >= 8
        and "--rebuild-milestones" not in sys.argv
    )
    if reuse:
        milestones = existing["milestones"]
        print(f"Reusing {len(milestones)} milestones from DemoRecapTimeline.json")
    else:
        milestones = build_milestones(frame_count, build_mode, count)

    if use_ai:
        load_hub_ai_env(hub)
        if not run_ai_narrate(hub, run, milestones, build_mode):
            print("Continuing with professor draft captions.", file=sys.stderr)

    spec = {
        "captureIntervalSec": existing.get("captureIntervalSec", 2.0),
        "targetDurationSec": float(existing.get("targetDurationSec", 900)),
        "milestoneHoldSec": float(existing.get("milestoneHoldSec", 7.0)),
        "introSec": float(existing.get("introSec", 2.8)),
        "outroSec": float(existing.get("outroSec", 3.0)),
        "outputFps": float(existing.get("outputFps", 80)),
        "minTimelapseGapFrames": int(existing.get("minTimelapseGapFrames", 8)),
        "videoEnhance": existing.get("videoEnhance", True),
        "segmentFadeSec": float(existing.get("segmentFadeSec", 0.5)),
        "segmentXfadeSec": float(existing.get("segmentXfadeSec", 0.0)),
        "introTitle": existing.get("introTitle", "World Build Recap"),
        "introSubtitle": existing.get(
            "introSubtitle",
            "80fps enhanced recap — upscaled color, annotations, professor captions",
        ),
        "outroTitle": existing.get("outroTitle", "Recording preserved"),
        "outroSubtitle": existing.get(
            "outroSubtitle",
            "Circles mark what each caption discusses in the Scene view",
        ),
        "buildMode": build_mode,
        "milestones": milestones,
    }
    if "--premium" in sys.argv or "--enhanced" in sys.argv:
        spec["outputFps"] = 80
        spec["milestoneHoldSec"] = 7.0
        spec["targetDurationSec"] = 900
        spec["videoEnhance"] = True
        spec["segmentFadeSec"] = 0.5
    tl_path.write_text(json.dumps(spec, indent=2) + "\n")

    compose = Path(__file__).resolve().parent / "compose-smart-recap.py"
    out = run / "DemoRecap.mp4"
    env = {**os.environ, "HUB_ROOT": str(hub)}
    print(
        f"Composing smart recap ({spec['outputFps']:.0f} fps, "
        f"hold {spec['milestoneHoldSec']:.1f}s, enhance={spec['videoEnhance']})…"
    )
    subprocess.run([sys.executable, str(compose), str(run), str(out)], check=True, env=env)
    print(str(out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
