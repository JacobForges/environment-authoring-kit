#!/usr/bin/env python3
"""AI build planner — Q&A, concept image, optional web research, session compile."""
from __future__ import annotations

import json
import os
import platform
import re
import shutil
import subprocess
import threading
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable

TOOLS = Path(__file__).resolve().parent
PLANNER_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildPlannerSession.json")  # legacy read/migrate
BRIEF_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildPlannerBrief.json")
ACTIVE_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildActiveSessionConfig.json")
STATE_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildWizardState.json")
CONCEPT_REL = Path(
    "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_pending-review/planner-session/concept.png"
)
CONCEPT_DENSITY_REL = Path(
    "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_pending-review/planner-session/concept-density.png"
)

from envkit_paths import atomic_write_text, planner_session_path, planner_session_write_path  # noqa: E402
from planner_asset_catalog import (  # noqa: E402
    CARD_SCHEMA_VERSION,
    CONCEPT_DENSITY_DETAIL_REL,
    CONCEPT_LAYOUT_DETAIL_REL,
    COUNT_ADJUST_KINDS,
    apply_card_counts_to_brief,
    approved_cards_manifest,
    build_asset_cards,
    build_density_card,
    build_layout_card,
    cards_for_public,
    card_can_generate_mesh,
    card_wants_ai_mesh,
    concept_cards_schema_stale,
    generate_mesh_for_concept_card,
    kit_catalog_thumb_count,
    load_or_build_catalog,
    ensure_card_image_timestamps,
    mesh_source_label,
    prop_kind_for_card,
    normalize_generated_prop_cards,
    refresh_bad_card_thumbnails,
    repair_missing_card_thumbnails,
    resolve_card_thumbnail,
    _is_placeholder_thumb,
    _is_bad_saved_thumb,
    _placeholder_thumb,
    _placeholder_thumb,
    maybe_background_export_kit_catalog,
    props_need_unity_thumbs,
    cards_missing_previews,
    ensure_card_thumbnail,
    refresh_prop_cards_from_catalog,
    refresh_concept_card_thumbnails,
    refresh_ai_mesh_card_previews,
    revise_asset_card,
    sync_concept_cards,
    thumbnail_after_mesh_generation,
)
from planner_card_job_queue import (
    bootstrap_preview_jobs,
    bootstrap_ai_mesh_jobs,
    enqueue_card_job,
    kick_card_queue,
    paced_queue_busy,
    _ensure_worker,
)


def _utc() -> str:
    return datetime.now(timezone.utc).isoformat()


def _resolve_unity_app_bundle() -> Path | None:
    """Best-effort Unity.app path for macOS foreground focus."""
    unity_path = os.environ.get("UNITY_PATH", "").strip()
    if unity_path:
        app = Path(unity_path).expanduser()
        if app.suffix == ".app" and app.is_dir():
            return app
        if app.name == "Unity" and app.parent.name == "MacOS":
            bundle = app.parent.parent.parent
            if bundle.suffix == ".app" and bundle.is_dir():
                return bundle

    hub = os.environ.get("HUB_ROOT", "").strip()
    if hub:
        version_file = Path(hub).expanduser() / "ProjectSettings/ProjectVersion.txt"
        if version_file.is_file():
            for line in version_file.read_text(encoding="utf-8").splitlines():
                if line.startswith("m_EditorVersion:"):
                    version = line.split(":", 1)[1].strip().split()[0]
                    candidate = Path(f"/Applications/Unity/Hub/Editor/{version}/Unity.app")
                    if candidate.is_dir():
                        return candidate

    editor_root = Path("/Applications/Unity/Hub/Editor")
    if editor_root.is_dir():
        for child in sorted(editor_root.iterdir(), reverse=True):
            candidate = child / "Unity.app"
            if candidate.is_dir():
                return candidate
    return None


def _activate_unity_editor() -> None:
    """Bring Unity Editor to the foreground so finalize polling can start the build immediately."""
    if platform.system() != "Darwin":
        return

    try:
        bundle = _resolve_unity_app_bundle()
        if bundle is not None:
            subprocess.Popen(
                ["open", "-a", str(bundle)],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
            return
        subprocess.Popen(
            ["open", "-a", "Unity"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
    except OSError:
        pass


_DOTENV_FORCE_KEYS = frozenset(
    {"CURSOR_API_KEY", "CAVE_CURSOR_MODEL", "CAVE_AI_PROVIDER", "HUB_ROOT"}
)


def load_dotenv(hub: Path | None = None) -> None:
    """Load cave-grader/.env; planner AI keys always come from the file when set."""
    p = TOOLS / ".env"
    if not p.is_file():
        if hub and not os.environ.get("HUB_ROOT"):
            os.environ["HUB_ROOT"] = str(hub)
        return
    for line in p.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        k, _, v = line.partition("=")
        k, v = k.strip(), v.strip().strip('"').strip("'")
        if not k:
            continue
        if k in _DOTENV_FORCE_KEYS or k not in os.environ:
            os.environ[k] = v
    if hub and not os.environ.get("HUB_ROOT"):
        os.environ["HUB_ROOT"] = str(hub)


def cursor_api_configured(hub: Path | None = None) -> bool:
    load_dotenv(hub)
    return bool(os.environ.get("CURSOR_API_KEY", "").strip())


def _planner_subprocess_env(hub: Path | None = None) -> dict[str, str]:
    """Subprocess env with cave-grader/.env applied for @cursor/sdk planner scripts."""
    load_dotenv(hub)
    env = _augment_path_env()
    from envkit_paths import ENV_VAR, ensure_planner_process_env, resolve_envkit_root

    root = resolve_envkit_root()
    env[ENV_VAR] = str(root)
    if hub is not None:
        env["HUB_ROOT"] = str(Path(hub).expanduser().resolve())
    elif not env.get("HUB_ROOT"):
        env["HUB_ROOT"] = str(Path.cwd())
    # tsx creates IPC pipes under os.tmpdir(); external volumes (Lexar) often return ENOTSUP on listen.
    tmp = ensure_planner_process_env()
    env["TMPDIR"] = str(tmp)
    env["TEMP"] = str(tmp)
    env["TMP"] = str(tmp)
    return env


def _augment_path_env(base: dict[str, str] | None = None) -> dict[str, str]:
    """Unity-launched wizard often has a minimal PATH — ensure node/npm are discoverable."""
    env = dict(base if base is not None else os.environ)
    prefixes: list[str] = [
        "/usr/local/bin",
        "/opt/homebrew/bin",
        os.path.expanduser("~/.volta/bin"),
    ]
    nvm_root = os.path.expanduser("~/.nvm/versions/node")
    if os.path.isdir(nvm_root):
        for name in sorted(os.listdir(nvm_root), reverse=True):
            prefixes.append(os.path.join(nvm_root, name, "bin"))
            break
    current = env.get("PATH", "")
    seen = set()
    merged: list[str] = []
    for p in prefixes + ([current] if current else []):
        if not p or p in seen:
            continue
        seen.add(p)
        if os.path.isdir(p) or p == current:
            merged.append(p)
    env["PATH"] = os.pathsep.join(merged)
    return env


def _resolve_node(env: dict[str, str] | None = None) -> str:
    env = _augment_path_env(env)
    found = shutil.which("node", path=env["PATH"])
    if found:
        return found
    for fixed in ("/usr/local/bin/node", "/opt/homebrew/bin/node"):
        if os.path.isfile(fixed) and os.access(fixed, os.X_OK):
            return fixed
    raise RuntimeError(
        "Node.js not found — install Node (https://nodejs.org or `brew install node`) "
        "so the AI planner can reach the AI assistant."
    )


def _tsx_argv(script: Path, *args: str | Path) -> list[str]:
    """Run TypeScript planner scripts via local tsx (no npx required)."""
    tsx_cli = TOOLS / "node_modules" / "tsx" / "dist" / "cli.mjs"
    node = _resolve_node()
    tail = [str(a) for a in args]
    if tsx_cli.is_file():
        return [node, str(tsx_cli), str(script), *tail]
    npx = shutil.which("npx", path=_augment_path_env()["PATH"])
    if npx:
        return [npx, "--yes", "tsx", str(script), *tail]
    return [node, "--import", "tsx", str(script), *tail]


def _session_path(hub: Path) -> Path:
    return planner_session_path(hub)


def _read_session(hub: Path) -> dict[str, Any]:
    import time

    p = _session_path(hub)
    if not p.is_file():
        return {}
    for attempt in range(4):
        try:
            return json.loads(p.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            if attempt >= 3:
                return {}
            time.sleep(0.04 * (attempt + 1))
    return {}


_SESSION_CORE_KEYS = (
    "phase",
    "conceptCards",
    "messages",
    "config",
    "conceptImageRel",
    "conceptBrief",
    "checklist",
    "buildScope",
    "conceptImageTs",
    "streamingText",
    "awaitingConceptApproval",
)


def _write_session(hub: Path, doc: dict[str, Any]) -> None:
    p = _session_path(hub)
    p.parent.mkdir(parents=True, exist_ok=True)
    if p.is_file():
        try:
            existing = json.loads(p.read_text(encoding="utf-8"))
        except Exception:
            existing = {}
        if existing.get("conceptCards") and not doc.get("conceptCards"):
            merged = dict(existing)
            merged.update(doc)
            doc = merged
        elif existing.get("phase") and not doc.get("phase"):
            for key in _SESSION_CORE_KEYS:
                if key in existing and key not in doc:
                    doc[key] = existing[key]
    doc["updatedUtc"] = _utc()
    p = planner_session_write_path(hub)
    atomic_write_text(p, json.dumps(doc, indent=2) + "\n")
    legacy = hub.expanduser().resolve() / PLANNER_REL
    if legacy.is_file() and legacy.resolve() != p.resolve():
        try:
            legacy.unlink()
        except OSError:
            pass


def _defaults_config() -> dict[str, Any]:
    return {
        "version": 2,
        "label": "AI planned build",
        "tileCount": 9,
        "randomSeedEachBuild": True,
        "seed": 0,
        "playDiskProps": True,
        "hollowTitan": False,
        "enhancementPhases": False,
        "prePlacementResearch": False,
        "sequentialTerrain": True,
        "surfaceTerrainPasses": 4,
        "outerRingMountains": True,
        "mountainLabyrinth": False,
        "surfaceTrails": True,
        "surfaceWater": False,
        "agentInvokes": False,
        "runPostBuildResearch": False,
        "preBuildReloop": False,
        "randomLandPlacement": True,
        "floatingTiles": False,
        "import3DObjects": True,
        "proLevelWorld": False,
        "use3DCaveSystem": True,
    }


def _normalize_tile_count(raw: int, *, floating: bool = False) -> int:
    if floating:
        return 13
    tc = int(raw)
    if tc <= 9:
        return 9
    if tc == 13:
        return 13
    if tc == 81:
        return 81
    if tc >= 289:
        return 289
    return 9


def _normalize_config(cfg: dict[str, Any]) -> dict[str, Any]:
    out = {**_defaults_config(), **(cfg or {})}
    floating = bool(out.get("floatingTiles"))
    out["tileCount"] = _normalize_tile_count(out.get("tileCount", 9), floating=floating)
    out["surfaceTerrainPasses"] = max(2, min(12, int(out.get("surfaceTerrainPasses", 4))))
    for key in (
        "playDiskProps",
        "hollowTitan",
        "enhancementPhases",
        "prePlacementResearch",
        "sequentialTerrain",
        "outerRingMountains",
        "mountainLabyrinth",
        "surfaceTrails",
        "surfaceWater",
        "agentInvokes",
        "runPostBuildResearch",
        "preBuildReloop",
        "randomLandPlacement",
        "floatingTiles",
        "import3DObjects",
        "proLevelWorld",
        "use3DCaveSystem",
        "randomSeedEachBuild",
    ):
        if key in out:
            out[key] = bool(out[key])

    if out["tileCount"] == 9:
        out["floatingTiles"] = False
        out["enhancementPhases"] = False
        out["prePlacementResearch"] = False
        out["preBuildReloop"] = False
        out["outerRingMountains"] = False
        out["mountainLabyrinth"] = False
        out["agentInvokes"] = False
        out["runPostBuildResearch"] = False
        out["hollowTitan"] = False
        out["surfaceTerrainPasses"] = min(3, int(out.get("surfaceTerrainPasses", 4)))

    # Floating-islands social demo — strip AAA bloat the LLM sometimes leaves on.
    if (
        out.get("floatingTiles")
        and out.get("tileCount") == 13
        and not out.get("proLevelWorld")
    ):
        out["enhancementPhases"] = False
        out["prePlacementResearch"] = False
        # 1 tile/step — safer on 8–16 GB RAM (parallel heightmaps OOM on Neo-class laptops).
        out["sequentialTerrain"] = True
        out["preBuildReloop"] = False
        out["outerRingMountains"] = False
        out["mountainLabyrinth"] = False
        out["agentInvokes"] = False
        out["runPostBuildResearch"] = False
        out["hollowTitan"] = False
        out["surfaceTerrainPasses"] = min(4, int(out.get("surfaceTerrainPasses", 4)))
        out["use3DCaveSystem"] = bool(out.get("use3DCaveSystem"))

    return out


def _clear_streaming(doc: dict[str, Any]) -> None:
    doc.pop("streamingText", None)
    doc.pop("streamingRole", None)


def _planner_stream_display(raw: str) -> str:
    """Show only assistantMessage while planner JSON streams — never raw checklist metadata."""
    raw = (raw or "").strip()
    if not raw:
        return ""
    if not raw.startswith("{"):
        return raw
    try:
        parsed = _parse_llm_json(raw)
        msg = parsed.get("assistantMessage")
        if isinstance(msg, str) and msg.strip():
            return msg.strip()
    except Exception:
        pass
    match = re.search(r'"assistantMessage"\s*:\s*"((?:[^"\\]|\\.)*)', raw, re.DOTALL)
    if match:
        try:
            return json.loads(f'"{match.group(1)}"').strip()
        except json.JSONDecodeError:
            return match.group(1).replace("\\n", "\n").replace('\\"', '"').strip()
    return ""


def _sanitize_chat_content(content: str) -> str:
    """Strip accidental planner JSON blobs from stored chat bubbles."""
    text = (content or "").strip()
    if not text.startswith("{"):
        return content
    if '"checklist"' not in text and '"assistantMessage"' not in text:
        return content
    try:
        parsed = _parse_llm_json(text)
        msg = parsed.get("assistantMessage")
        if isinstance(msg, str) and msg.strip():
            return msg.strip()
    except Exception:
        pass
    if '"assistantMessage"' in text:
        extracted = _planner_stream_display(text)
        if extracted:
            return extracted
    if any(
        key in text
        for key in ('"checklist"', '"qnaComplete"', '"brief"', '"teachingMoment"', '"sessionConfig"')
    ):
        return ""
    return content


def _public_streaming(doc: dict[str, Any]) -> tuple[str | None, str | None]:
    role = doc.get("streamingRole")
    raw = doc.get("streamingText")
    if not raw:
        return None, role
    if role == "assistant":
        return _planner_stream_display(str(raw)) or None, role
    if role == "script":
        return _planner_stream_display(str(raw)) or None, role
    return str(raw), role


def _set_streaming(doc: dict[str, Any], role: str, text: str, *, json_response: bool = False) -> None:
    doc["streamingRole"] = role
    display = (
        _planner_stream_display(text)
        if role in ("assistant", "script") and json_response
        else text
    )
    doc["streamingText"] = display


def _llm_prompt_once(script: Path, req_path: str, env: dict[str, str]) -> str:
    proc = subprocess.run(
        _tsx_argv(script, req_path),
        cwd=TOOLS,
        capture_output=True,
        text=True,
        timeout=300,
        env=env,
    )
    line = (proc.stdout or proc.stderr or "").strip().splitlines()[-1] if proc.stdout or proc.stderr else ""
    if not line:
        raise RuntimeError(proc.stderr or "AI planner returned no output")
    data = json.loads(line)
    if data.get("error"):
        raise RuntimeError(str(data["error"]))
    text = data.get("text", "")
    if not text:
        raise RuntimeError("AI planner returned empty text")
    return text


def _llm_stream_once(
    hub: Path,
    script: Path,
    req_path: str,
    env: dict[str, str],
    stream_doc: dict[str, Any] | None,
    stream_role: str,
    *,
    json_response: bool,
    stream_persist: Callable[[dict[str, Any]], None] | None = None,
) -> str:
    stream_env = dict(env)
    stream_env["PYTHONUNBUFFERED"] = "1"
    proc = subprocess.Popen(
        _tsx_argv(script, req_path),
        cwd=TOOLS,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        bufsize=1,
        env=stream_env,
    )
    accumulated = ""
    last_stream_write = 0.0

    def _persist() -> None:
        if stream_doc is None:
            return
        if stream_persist is not None:
            stream_persist(stream_doc)
        else:
            _write_session(hub, stream_doc)

    assert proc.stdout is not None
    for line in proc.stdout:
        line = line.strip()
        if not line:
            continue
        try:
            data = json.loads(line)
        except json.JSONDecodeError:
            continue
        if data.get("type") == "error":
            raise RuntimeError(str(data.get("error")))
        if data.get("type") == "delta":
            accumulated = str(data.get("accumulated") or accumulated + str(data.get("text") or ""))
            if stream_doc is not None:
                _set_streaming(stream_doc, stream_role, accumulated, json_response=json_response)
                import time as _time

                now = _time.time()
                if now - last_stream_write >= 0.3:
                    _persist()
                    last_stream_write = now
        elif data.get("type") == "done":
            accumulated = str(data.get("text") or accumulated)

    err = proc.stderr.read() if proc.stderr else ""
    code = proc.wait(timeout=300)
    if code != 0:
        raise RuntimeError(err or f"AI planner exited {code}")
    if not accumulated.strip():
        raise RuntimeError(err or "AI planner returned empty text")
    if stream_doc is not None:
        _clear_streaming(stream_doc)
        _persist()
    return accumulated.strip()


def _llm_messages(
    hub: Path,
    system: str,
    messages: list[dict[str, str]],
    *,
    stream_doc: dict[str, Any] | None = None,
    stream_role: str = "assistant",
    json_response: bool = True,
    stream_persist: Callable[[dict[str, Any]], None] | None = None,
) -> str:
    load_dotenv(hub)
    api_key = os.environ.get("CURSOR_API_KEY", "").strip()
    if not api_key:
        raise RuntimeError(
            "CURSOR_API_KEY missing — set it in Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/.env"
        )

    script = TOOLS / "planner-llm.ts"
    if not script.is_file():
        raise RuntimeError(f"planner-llm.ts not found at {script}")

    import tempfile

    use_stream = stream_doc is not None
    payload = {
        "hubRoot": str(hub),
        "system": system,
        "messages": messages,
        "mode": "agent" if use_stream else "prompt",
        "stream": use_stream,
        "plainText": not json_response,
    }
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as tmp:
        json.dump(payload, tmp)
        req_path = tmp.name

    try:
        env = _planner_subprocess_env(hub)
        if not use_stream:
            return _llm_prompt_once(script, req_path, env)

        if stream_doc is not None:
            stream_doc["cursorWorking"] = True
            _set_streaming(stream_doc, stream_role, "", json_response=json_response)
            if stream_persist is not None:
                stream_persist(stream_doc)
            else:
                _write_session(hub, stream_doc)

        try:
            return _llm_stream_once(
                hub,
                script,
                req_path,
                env,
                stream_doc,
                stream_role,
                json_response=json_response,
                stream_persist=stream_persist,
            )
        except Exception as stream_err:
            if stream_doc is not None:
                _clear_streaming(stream_doc)
            fallback = dict(payload)
            fallback["mode"] = "prompt"
            fallback["stream"] = False
            with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as fb:
                json.dump(fallback, fb)
                fb_path = fb.name
            try:
                return _llm_prompt_once(script, fb_path, env)
            except Exception:
                raise stream_err from None
            finally:
                try:
                    os.unlink(fb_path)
                except OSError:
                    pass
    finally:
        if stream_doc is not None:
            _clear_streaming(stream_doc)
            try:
                # Director (and other callers) pass stream_persist for non-planner JSON;
                # never mirror that doc into CaveBuildPlannerSession.json.
                if stream_persist is not None:
                    stream_persist(stream_doc)
                else:
                    _write_session(hub, stream_doc)
            except OSError:
                pass
        try:
            os.unlink(req_path)
        except OSError:
            pass


SYSTEM_QNA = """You are the Environment Kit AI Build Planner for a Unity procedural cave/surface world pipeline.

You are ALSO a patient senior level-design mentor. The user is learning production skills that normally take years.
Every reply must teach something real before you ask the next question.

ALWAYS preserve: 9 play tiles centered (3×3 play disk) — never relocate the play disk.

## Terrain tab scope (CRITICAL — terrain ONLY)
This chat is **terrain / world build only** (tile footprint, surface trails, terrain passes, planner grid markers, sessionConfig).
Do **NOT** plan: MainScene dialog trees, hybrid Talk+Shop, quest blockers, cave room graphs, maze grids, interior loot, music, or video scripts.
Redirect off-topic requests to the correct wizard tab by name (Surface content, Caves, Mazes, Interior, Atmosphere, Music, Video).
For checklist **npcs** / **enemies** / **props** here: only counts and planner marker labels for the procedural world — not dialog or shop inventory.

## Scope presets (small / medium / large — MUST match sessionConfig.tileCount)
- **small (9):** layoutPlan covers **play disk only** — every marker on play rows 0–2, cols 0–2. No outer-ring markers.
- **medium (81):** layoutPlan must include **play disk markers** plus **ringProps** for foothill/peak rings (prop/trail density per ring), and trails reaching at least one outer ring edge.
- **large (289):** layoutPlan must include play disk + **ringProps** for rings 2–8 with decreasing prop density; note horizon/open rings.
- **floatingTiles (13):** use island markers (N/E/S/W) + play disk; no 81-tile mountain shell.
Never reuse a generic 3×3-only layout when tileCount is 81 or 289 — the concept image must show the **full scoped grid** for that build.

## Checklist interview (STRICT — required while qnaComplete is false)
- The session checklist has numbered options 1–15. Ask about **every** pending id before finishing Q&A.
- Ask **ONE** focused question per turn. Tie it to the **NEXT PENDING** checklist id from the state block below.
- Include **teachingMoment** (2–4 sentences): explain ONE production concept — why it matters in Unity/EnvKit, what breaks if ignored, and how their answer changes the build.
- When the user answers, set that checklist id `done:true` with a **specific** `decision` (not generic placeholders).
- **LOCKED items** (done + locked in state): NEVER change `done`, `decision`, or `label` — omit them from your checklist array or echo them exactly.
- Reopen a topic **only** if the user said e.g. "change checklist option 6" or "revise checklist item tech_jump".
- Set `qnaComplete:true` **only** when (a) **every** checklist item is `done:true`, **or** (b) the user's **latest** message says **move on** / **skip remaining** / **proceed to concept**.
- If any checklist item is still pending and the user did not say move on, `qnaComplete` **must** be false — even if you think the plan is ready.
- Do **not** skip tech_* learning topics; each needs a real user answer (or explicit move on).
- Record numeric specs in brief.layoutPlan.technicalSpecs when known:
  { plateauHeightM, platformHeightM, platformHopM, hubGapM, jumpHeightM, fallRespawn }

## Unity build contract (critical — chat alone does NOT build the world)
On finalize, Unity reads:
1) sessionConfig booleans → pipeline mode (tiles, caves, props, speed path)
2) brief.layoutPlan.markers + technicalSpecs → plateaus, platforms, spawn, fall volume

layoutPlan must list EVERY spawn, platform (plat-* labels), maze plateau, hub prop, NPC, enemy, collectible with row/col/slot/label.

When ready, set qnaComplete true and include:
- brief: {
    title, summary, userGoals[], researchQueries[] (3-5 web search strings if research useful),
    layoutPlan: {
      gridNote: "include +35u plateaus, +12u platforms, gap meters",
      playDisk: { labyrinth: bool, labyrinthNote: "which cells are maze vs hub" },
      technicalSpecs: { plateauHeightM, platformHeightM, platformHopM, hubGapM, jumpHeightM, fallRespawn },
      markers: [
        { kind: "spawn|prop|npc|enemy|collectible", zone: "play|island", row, col, dir, slot, label }
      ],
      ringProps: [ { ring: 2|3|4|5|6|7|8, density: "none|low|medium|high", trails: bool, label } ],
      trails: [ { from: "play-south|play-center", to: "island-N|foothill|peak|...", label } ],
      islands: [ { dir: "N|E|S|W", label, enemies, npcs, props, collectibles } ],
      caveMouths: [ { pos: "play-north-center", label } ]
    }
  }
- sessionConfig: object matching these keys:
  label, tileCount (9, 13, 81, or 289), randomSeedEachBuild, playDiskProps, hollowTitan, enhancementPhases,
  prePlacementResearch, sequentialTerrain, surfaceTerrainPasses, outerRingMountains, mountainLabyrinth,
  surfaceTrails, surfaceWater, agentInvokes, runPostBuildResearch, preBuildReloop,
  randomLandPlacement, floatingTiles, import3DObjects, proLevelWorld, use3DCaveSystem

Checklist ids (content + technical learning):
play_disk, scope, props, npcs, enemies, puzzles, terrain, speed,
tech_jump, tech_seams, tech_navmesh, tech_spawn, tech_seed, tech_collision, tech_perf

Mark done:true ONLY when the user explicitly answered that topic in this conversation (not defaults).
Never mark all items done in one turn unless each was truly answered in prior turns.

Respond JSON only:
{
  "assistantMessage": "markdown shown in chat — include teachingMoment content here too",
  "teachingMoment": "optional short mentor aside (also woven into assistantMessage)",
  "qnaComplete": false,
  "brief": null,
  "sessionConfig": null,
  "checklist": []
}
"""

WORLD_PRESETS: dict[str, dict[str, Any]] = {
    "small": {
        "title": "Small world",
        "kickoff": (
            "Minimum play disk demo: 9-tile centered 3×3 play disk only, no outer rings, "
            "fast social clip build, dense props on play disk, minimal terrain scope."
        ),
        "description": (
            "9-tile play disk (3×3) only — no outer rings or floating islands; play-disk props on; "
            "3 terrain passes; labyrinth on play disk; fastest recordable demo."
        ),
        "sessionConfig": {
            "label": "Small world — 9-tile play disk",
            "tileCount": 9,
            "floatingTiles": False,
            "playDiskProps": True,
            "outerRingMountains": False,
            "mountainLabyrinth": False,
            "enhancementPhases": False,
            "sequentialTerrain": True,
            "surfaceTerrainPasses": 3,
            "surfaceTrails": True,
            "surfaceWater": False,
            "proLevelWorld": False,
            "import3DObjects": True,
            "use3DCaveSystem": True,
        },
        "technicalSpecs": {
            "plateauHeightM": 80,
            "platformHeightM": 12,
            "platformHopM": 2.5,
            "hubGapM": 3.5,
            "jumpHeightM": 1.8,
            "fallRespawn": "play-center respawn after 4m fall",
        },
    },
    "medium": {
        "title": "Medium world",
        "kickoff": (
            "Medium 81-tile Florida karst world: full surface grid around the centered 9 play "
            "tiles, surface trails, buried cave mouth, balanced prop scatter, standard demo quality."
        ),
        "description": (
            "81-tile FullWorld shell; 9 play tiles centered; trails + cave mouth; moderate prop "
            "density; optional outer features light; 4 terrain passes; good for playable demo."
        ),
        "sessionConfig": {
            "label": "Medium world — 81-tile demo",
            "tileCount": 81,
            "floatingTiles": False,
            "playDiskProps": True,
            "outerRingMountains": False,
            "mountainLabyrinth": False,
            "enhancementPhases": False,
            "sequentialTerrain": True,
            "surfaceTerrainPasses": 4,
            "surfaceTrails": True,
            "surfaceWater": False,
            "proLevelWorld": False,
            "import3DObjects": True,
            "use3DCaveSystem": True,
        },
        "technicalSpecs": {
            "plateauHeightM": 80,
            "platformHeightM": 12,
            "platformHopM": 2.5,
            "hubGapM": 4.0,
            "jumpHeightM": 2.0,
            "fallRespawn": "play-center respawn at -6m",
        },
    },
    "large": {
        "title": "Large world",
        "kickoff": (
            "Large 289-tile extended open world: 9 play tiles centered, wide wilderness ring, "
            "outer terrain, heavy prop coverage, longer build time acceptable for exploration scale."
        ),
        "description": (
            "289-tile extended grid; 9 play tiles centered; outer ring terrain; higher prop budget; "
            "6 terrain passes; trails across extended map; cave + surface at scale."
        ),
        "sessionConfig": {
            "label": "Large world — 289-tile open world",
            "tileCount": 289,
            "floatingTiles": False,
            "playDiskProps": True,
            "outerRingMountains": True,
            "mountainLabyrinth": False,
            "enhancementPhases": False,
            "sequentialTerrain": True,
            "surfaceTerrainPasses": 6,
            "surfaceTrails": True,
            "surfaceWater": False,
            "proLevelWorld": False,
            "import3DObjects": True,
            "use3DCaveSystem": True,
        },
        "technicalSpecs": {
            "plateauHeightM": 80,
            "platformHeightM": 12,
            "platformHopM": 2.8,
            "hubGapM": 4.5,
            "jumpHeightM": 2.2,
            "fallRespawn": "nearest trail checkpoint respawn",
        },
    },
}

SYSTEM_AUTO_USER = """You simulate the USER in the Environment Kit AI Build Planner Q&A chat.
The real user chose a world-size preset and wants you to answer the planner's latest question.

Output **plain text only** — no JSON, no markdown code fences, no role labels. Write 2–5 sentences
as if the user typed them in chat. Be specific: meters, counts, feature names, yes/no choices.

## Preset: {title}
{description}

## Pipeline defaults (stay consistent with these):
{config_lines}

## Technical specs (use when tech_* topics come up):
{tech_lines}

## Topic keyword hints (weave at least one word from the line matching the current topic):
- scope: goals, footprint, demo intent, tile count
- props: prop, scatter, tree, vegetation, dense
- npcs: npc, character, villager
- enemies: enemy, combat, monster
- puzzles: labyrinth, collectible, puzzle, interact, maze
- terrain: island, trail, cave, mountain, water, ring
- speed: demo, fast, tier, clip, production
- tech_jump: jump, gap, meter, platform spacing, hop
- tech_seams: seam, plateau, tile offset, floating tile, heightmap
- tech_navmesh: navmesh, walkable, bake
- tech_spawn: spawn, respawn, fall, kill volume
- tech_seed: seed, random, deterministic
- tech_collision: collider, trigger, collision
- tech_perf: perf, fps, budget, lod, dense

Current checklist topic to address: **{next_topic}** ({next_label})
{keyword_hint}

Answer ONLY the planner's last message. Do not ask questions back.
Do NOT repeat or paraphrase any prior user answer listed below.
{prior_block}
{retry_block}
"""

AUTO_REPLY_MAX_ATTEMPTS = 4
AUTO_REPLY_DUPE_THRESHOLD = 0.82


def _normalize_for_dedupe(text: str) -> str:
    t = re.sub(r"\s+", " ", (text or "").strip().lower())
    t = re.sub(r"[^\w\s]", "", t)
    return t


def _reply_similarity(a: str, b: str) -> float:
    from difflib import SequenceMatcher

    na, nb = _normalize_for_dedupe(a), _normalize_for_dedupe(b)
    if not na or not nb:
        return 0.0
    if na == nb:
        return 1.0
    shorter, longer = (na, nb) if len(na) <= len(nb) else (nb, na)
    if len(shorter) >= 24 and shorter in longer:
        return 0.96
    return SequenceMatcher(None, na, nb).ratio()


def _collect_prior_user_replies(doc: dict[str, Any]) -> list[str]:
    return [
        str(m.get("content") or "").strip()
        for m in doc.get("messages") or []
        if m.get("role") == "user" and str(m.get("content") or "").strip()
    ]


def _is_duplicate_auto_reply(
    candidate: str,
    doc: dict[str, Any],
    *,
    threshold: float = AUTO_REPLY_DUPE_THRESHOLD,
) -> tuple[bool, str]:
    cand = (candidate or "").strip()
    if len(cand) < 8:
        return True, "empty or too short"

    for i, prev in enumerate(_collect_prior_user_replies(doc)):
        sim = _reply_similarity(cand, prev)
        if sim >= threshold:
            return True, f"duplicate of prior user reply #{i + 1} (similarity {sim:.0%})"

    assistant = _last_assistant_message(doc)
    if assistant and _reply_similarity(cand, assistant) >= 0.78:
        return True, "echoes planner question"

    return False, ""


def _format_prior_replies_block(doc: dict[str, Any]) -> str:
    prior = _collect_prior_user_replies(doc)
    if not prior:
        return ""
    lines = ["\n## Prior user answers (do NOT repeat or lightly rephrase):"]
    for i, p in enumerate(prior[-6:], start=max(1, len(prior) - 5)):
        snippet = p.replace("\n", " ").strip()[:220]
        lines.append(f"{i}. {snippet}")
    return "\n".join(lines)


def _fallback_auto_reply(preset: str, topic_id: str) -> str:
    """Deterministic unique answer when the LLM keeps repeating itself."""
    spec = _preset_or_raise(preset)
    cfg = spec.get("sessionConfig") or {}
    tech = spec.get("technicalSpecs") or {}
    tiles = int(cfg.get("tileCount", 9))
    floating = bool(cfg.get("floatingTiles"))
    title = spec["title"]

    fallbacks: dict[str, str] = {
        "scope": (
            f"{title}: centered 9-tile play disk (3×3)"
            + (f", {tiles}-tile shell" if tiles > 9 else " only — no outer rings")
            + (
                f", {'floating cardinal islands + fast social clip' if floating else ('minimal play disk demo' if tiles == 9 else 'full surface grid + buried cave demo')}. "
                f"Goal is a recordable playable slice with clear spawn-to-cave loop."
            )
        ),
        "props": (
            f"Dense prop scatter on play disk — target ~85% vegetation coverage per tile with wider tree spacing; "
            f"{'lighter props on outer shell tiles' if tiles > 81 else ('play disk tiles only' if tiles == 9 else 'cardinal island hop pads get bush clusters')}. "
            "Use kit vegetation prefabs; keep trail corridors readable."
        ),
        "npcs": (
            "One friendly npc host at play-center hub (guide character), no crowd sim. "
            "NPC uses walkable navmesh only — stands near spawn marker, faces cave mouth."
        ),
        "enemies": (
            "Light enemy combat: 2–3 enemy spawn markers on cardinal islands only, not on hub maze. "
            "Simple melee patrol loops; combat optional for demo clip."
        ),
        "puzzles": (
            "Play-disk labyrinth maze with one collectible at hub and one per cardinal island bridge landing. "
            "Interactable pickup props only — no block-puzzle gates."
        ),
        "terrain": (
            f"{'Four floating cardinal islands at +80u plateau with +12u hop platforms; no mountain ring.' if floating else ('9-tile flat play disk only; no outer wilderness.' if tiles == 9 else f'{tiles}-tile Florida karst surface with trails to cave mouth;' + (' outer wilderness ring, no water plane.' if tiles > 81 else ' no outer mountains, cave mouth north-center.'))}"
        ),
        "speed": (
            f"{'Fast social demo tier — 3 terrain passes, skip AAA enhancement phases.' if floating else 'Balanced demo tier — 4–6 terrain passes acceptable; prioritize playable loop over ship-grade polish.'}"
        ),
        "tech_jump": (
            f"Platform hop gaps {tech.get('platformHopM', 2.5)}m with {tech.get('jumpHeightM', 2.0)}m apex clearance; "
            f"hub-to-island hops use {tech.get('hubGapM', 4.0)}m center spacing on raised pads."
        ),
        "tech_seams": (
            f"{'Floating tiles use per-tile transform Y offsets (+80u plateau / +12u platforms) — no full heightmap wipe at seams.' if floating else 'LiDAR heightmap preserved across tiles; seam stitch at neighbors, plateau offsets only on play disk markers.'}"
        ),
        "tech_navmesh": (
            "Bake navmesh after props: walkable hub maze floor + island landings; jump-only gaps excluded from walk mesh. "
            "AI patrols stay on green walkable areas only."
        ),
        "tech_spawn": (
            f"Spawn at play-center south hub; {tech.get('fallRespawn', 'respawn at hub after void fall')} with kill trigger below lowest island."
        ),
        "tech_seed": (
            "Random seed each build for variation in prop scatter; layout markers stay deterministic from planner brief."
        ),
        "tech_collision": (
            "Platform colliders on raised pads; trigger kill volume under void gaps; maze walls use terrain collision not invisible boxes."
        ),
        "tech_perf": (
            f"{'Demo perf budget: cap prop batches low, 9 play tiles dense, outer shell minimal.' if floating else f'Prop budget scales with {tiles} tiles — prioritize 65–90% per-tile coverage without exceeding editor RAM guardrails.'}"
        ),
    }
    reply = fallbacks.get(topic_id) or fallbacks["scope"]
    # Guarantee uniqueness vs prior replies by appending topic tag (invisible to checklist keywords)
    return reply


def _preset_or_raise(preset: str) -> dict[str, Any]:
    key = (preset or "").strip().lower()
    if key not in WORLD_PRESETS:
        raise RuntimeError(f"preset must be small, medium, or large (got {preset!r})")
    return WORLD_PRESETS[key]


def _apply_preset_bootstrap(doc: dict[str, Any], preset: str) -> None:
    """Seed sessionConfig + brief scaffold so builds and concept art match preset scope before Q&A ends."""
    p = _preset_or_raise(preset)
    doc["sessionConfig"] = _normalize_config(p.get("sessionConfig") or {})
    doc["autoRespondPreset"] = preset
    brief = dict(doc.get("brief") or {})
    brief.setdefault("title", p["title"])
    brief.setdefault("summary", p["description"])
    tc = int(doc["sessionConfig"].get("tileCount", 9))
    lp = dict(brief.get("layoutPlan") or {})
    lp.setdefault("technicalSpecs", dict(p.get("technicalSpecs") or {}))
    lp.setdefault(
        "gridNote",
        f"{tc}-tile scope — {p['title']} (fresh layout from your answers, not a catalog preset copy)",
    )
    brief["layoutPlan"] = lp
    doc["brief"] = brief


def _format_preset_config_lines(cfg: dict[str, Any]) -> str:
    lines = []
    for k, v in sorted(cfg.items()):
        lines.append(f"- {k}: {v}")
    return "\n".join(lines)


def _last_assistant_message(doc: dict[str, Any]) -> str:
    for m in reversed(doc.get("messages") or []):
        if m.get("role") == "assistant":
            return str(m.get("content") or "")
    return ""


def _generate_auto_user_reply(
    hub: Path,
    doc: dict[str, Any],
    preset: str,
    *,
    attempt: int = 0,
    blocked_replies: list[str] | None = None,
) -> str:
    spec = _preset_or_raise(preset)
    next_id = _next_pending_checklist_id(doc) or "scope"
    import world_state_snapshot as wss  # noqa: PLC0415

    world_state = {}
    try:
        world_state = wss.build_world_state(hub)
    except Exception:
        world_state = {}
    if world_state:
        doc["worldState"] = world_state
    world_block = wss.build_prompt_block(world_state) if world_state else ""
    if next_id == "scope":
        kw_hint = "Include scope/goals language and the tile footprint for this preset."
    else:
        kws = TOPIC_KEYWORDS.get(next_id, ())
        kw_hint = f"Include words like: {', '.join(kws)}" if kws else ""

    tech = spec.get("technicalSpecs") or {}
    tech_lines = "\n".join(f"- {k}: {v}" for k, v in tech.items()) or "(defaults)"

    retry_block = ""
    if blocked_replies:
        retry_block = (
            "\n## REJECTED — duplicate attempt(s); write a completely NEW answer:\n"
            + "\n".join(f"- REJECTED: {b.replace(chr(10), ' ')[:200]}" for b in blocked_replies[-3:])
            + f"\n\nRegeneration attempt {attempt + 1}: different wording, new numbers, answer ONLY [{next_id}]."
        )
    elif attempt > 0:
        retry_block = f"\nRegeneration attempt {attempt + 1}: vary wording and specifics.\n"

    system = SYSTEM_AUTO_USER.format(
        title=spec["title"],
        description=spec["description"],
        config_lines=_format_preset_config_lines(spec.get("sessionConfig") or {}),
        tech_lines=tech_lines,
        next_topic=next_id,
        next_label=_label_for_checklist_id(doc, next_id),
        keyword_hint=kw_hint,
        prior_block=_format_prior_replies_block(doc),
        retry_block=retry_block,
    )
    if world_block:
        system += f"\n\n{world_block}\nUse this matrix to keep placement/world claims scene-aware."
    planner_msg = _last_assistant_message(doc) or "Introduce the build and ask the first checklist question."
    planner_excerpt = planner_msg.replace("\n", " ").strip()[:600]
    user_prompt = (
        f"Planner's last message (answer THIS question only):\n{planner_msg}\n\n"
        f"Checklist topic: [{next_id}] — {_label_for_checklist_id(doc, next_id)}\n"
        f"Question focus: …{planner_excerpt[-280:]}\n\n"
        f"Write one unique user chat reply for topic [{next_id}]. "
        "Do not reuse sentences from prior user answers."
    )
    return _llm_messages(
        hub,
        system,
        [{"role": "user", "content": user_prompt}],
        stream_doc=doc,
        stream_role="user",
        json_response=False,
    ).strip()


def _generate_auto_user_reply_deduped(hub: Path, doc: dict[str, Any], preset: str) -> str:
    """Generate a user reply; retry on duplicate, then fall back to deterministic template."""
    blocked: list[str] = []
    next_id = _next_pending_checklist_id(doc) or "scope"

    for attempt in range(AUTO_REPLY_MAX_ATTEMPTS):
        reply = _generate_auto_user_reply(
            hub,
            doc,
            preset,
            attempt=attempt,
            blocked_replies=blocked or None,
        )
        is_dup, reason = _is_duplicate_auto_reply(reply, doc)
        if not is_dup:
            return reply
        blocked.append(reply)

    fallback = _fallback_auto_reply(preset, next_id)
    is_dup, _ = _is_duplicate_auto_reply(fallback, doc)
    if is_dup:
        fallback = (
            f"{fallback} (checklist topic {next_id}, attempt {len(blocked) + 1} — "
            f"unique decision for {_preset_or_raise(preset)['title']}.)"
        )
    return fallback


def _auto_respond_one_turn(hub: Path, preset: str) -> dict[str, Any]:
    doc = _read_session(hub) or {}
    phase = doc.get("phase")
    if phase == "awaiting_concept_approval":
        doc = _read_session(hub) or doc
        doc["autoRespondActive"] = False
        doc.pop("autoRespondTurn", None)
        doc.pop("autoRespondPreset", None)
        doc["cursorWorking"] = False
        _write_session(hub, doc)
        return {"done": True, "session": public_session(hub)}

    if phase != "qna":
        raise RuntimeError(f"Auto-respond stopped in phase {phase}")

    if doc.get("cursorWorking") or doc.get("streamingText"):
        return {"done": False, "waiting": True, "session": public_session(hub)}

    if _awaiting_assistant_reply(doc):
        resume_qna(hub)
        doc = _read_session(hub) or {}
        if doc.get("cursorWorking") or doc.get("streamingText"):
            return {"done": False, "waiting": True, "session": public_session(hub)}

    turn = int(doc.get("autoRespondTurn") or 0) + 1
    doc["autoRespondTurn"] = turn
    doc["autoRespondPreset"] = preset
    doc["autoRespondActive"] = True
    doc["cursorWorking"] = True
    _write_session(hub, doc)

    if _checklist_all_done(doc) and not _awaiting_assistant_reply(doc):
        chat_turn(hub, "move on to concept — generate the concept preview.")
    elif _awaiting_assistant_reply(doc):
        resume_qna(hub)
    else:
        reply = _generate_auto_user_reply_deduped(hub, doc, preset)
        if not reply:
            raise RuntimeError("AI responder returned empty text")
        chat_turn(hub, reply)

    doc = _read_session(hub) or {}
    done = doc.get("phase") == "awaiting_concept_approval"
    if done:
        doc["autoRespondActive"] = False
        doc.pop("autoRespondTurn", None)
    doc["cursorWorking"] = False
    _clear_streaming(doc)
    _write_session(hub, doc)
    return {"done": done, "session": public_session(hub)}


def auto_respond_step(
    hub: Path,
    preset: str,
    internet_research: bool = False,
) -> dict[str, Any]:
    """Run one planner ↔ auto-responder turn (for live UI streaming)."""
    load_dotenv(hub)
    _preset_or_raise(preset)

    doc = _read_session(hub)
    if not doc or doc.get("phase") == "cancelled":
        kickoff = _preset_or_raise(preset)["kickoff"]
        start_session(hub, kickoff, internet_research)
        doc = _read_session(hub) or {}
        _apply_preset_bootstrap(doc, preset)
        _write_session(hub, doc)
    elif internet_research and not doc.get("internetResearch"):
        # Respect late toggle: enable research for the active session.
        doc["internetResearch"] = True
        _write_session(hub, doc)

    if doc.get("phase") == "awaiting_concept_approval":
        doc["autoRespondActive"] = False
        doc.pop("autoRespondTurn", None)
        doc.pop("autoRespondPreset", None)
        doc["cursorWorking"] = False
        _write_session(hub, doc)
        return {"done": True, "session": public_session(hub)}

    if doc.get("phase") != "qna":
        raise RuntimeError(
            f"Auto-respond only runs during Q&A (current phase: {doc.get('phase')}). "
            "Reset the planner or approve/reject to return to Q&A."
        )

    return _auto_respond_one_turn(hub, preset)


def auto_respond_until_concept(
    hub: Path,
    preset: str,
    internet_research: bool = False,
) -> dict[str, Any]:
    """Run Cursor auto-user replies through Q&A until concept image awaits approval."""
    load_dotenv(hub)
    _preset_or_raise(preset)

    doc = _read_session(hub)
    if not doc or doc.get("phase") == "cancelled":
        kickoff = _preset_or_raise(preset)["kickoff"]
        start_session(hub, kickoff, internet_research)
        doc = _read_session(hub) or {}
        _apply_preset_bootstrap(doc, preset)
        _write_session(hub, doc)

    if doc.get("phase") == "awaiting_concept_approval":
        return public_session(hub)

    max_turns = 28
    turns = 0
    last: dict[str, Any] | None = None
    while turns < max_turns:
        last = auto_respond_step(hub, preset, internet_research)
        if last.get("done"):
            return last["session"]
        turns += 1

    doc = _read_session(hub) or {}
    pending = _pending_checklist_ids(doc)
    raise RuntimeError(
        f"Auto-respond finished after {turns} turn(s) but concept was not ready "
        f"(phase={doc.get('phase')}, pending={pending}). "
        "Try again or answer remaining checklist items manually."
    )


def _parse_llm_json(raw: str) -> dict[str, Any]:
    raw = raw.strip()
    if raw.startswith("```"):
        raw = re.sub(r"^```(?:json)?\s*", "", raw)
        raw = re.sub(r"\s*```$", "", raw)
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        match = re.search(r"\{[\s\S]*\}", raw)
        if match:
            return json.loads(match.group(0))
        raise


CANONICAL_CHECKLIST_IDS = (
    "play_disk",
    "scope",
    "props",
    "npcs",
    "enemies",
    "puzzles",
    "terrain",
    "speed",
    "tech_jump",
    "tech_seams",
    "tech_navmesh",
    "tech_spawn",
    "tech_seed",
    "tech_collision",
    "tech_perf",
)

CHECKLIST_ALIASES: dict[str, str] = {
    "world_layout": "scope",
    "tier": "speed",
    "character": "npcs",
    "content": "props",
    "labyrinth": "puzzles",
    "islands": "terrain",
    "play_disk_props": "props",
    "tech_jumps": "tech_jump",
    "tech_jump_physics": "tech_jump",
    "tech_terrain_seams": "tech_seams",
    "tech_respawn": "tech_spawn",
    "tech_procedural_seed": "tech_seed",
    "tech_lod": "tech_perf",
}

TOPIC_KEYWORDS: dict[str, tuple[str, ...]] = {
    "props": ("prop", "scatter", "bench", "tree", "decor", "vegetation"),
    "tech_jump": ("jump", "gap", "hop", "platform spacing", "apex", "coyote", "meter"),
    "tech_seams": ("seam", "heightmap", "plateau", "tile offset", "floating tile"),
    "tech_navmesh": ("navmesh", "walkable", "bake", "ai path"),
    "tech_spawn": ("spawn", "respawn", "fall", "kill volume", "checkpoint"),
    "tech_seed": ("seed", "random", "deterministic", "variation"),
    "tech_collision": ("collider", "trigger", "collision", "fall through"),
    "tech_perf": ("perf", "fps", "lod", "budget", "draw call", "dense"),
    "npcs": ("npc", "character", "villager", "host", "playable"),
    "enemies": ("enem", "combat", "monster", "fight"),
    "puzzles": ("collect", "pickup", "chest", "loot", "puzzle", "interact", "labyrinth", "maze"),
    "terrain": ("float", "island", "mountain", "water", "trail", "cave", "wilderness", "ring"),
    "speed": ("fast", "demo", "social", "quick", "aaa", "production", "video", "tier", "clip"),
}

VAGUE_DECISION_MARKERS = (
    "included",
    "not yet",
    "pending",
    "wanted but",
    "not yet placed",
    "not yet labeled",
    "tbd",
    "assumed",
    "pipeline-default",
    "default ",
)

GENERIC_DECISIONS = frozenset(
    {
        "always on",
        "npcs included",
        "enemies + combat",
        "collectibles / labyrinth",
        "play-disk props on",
        "fast social demo",
        "aaa / pro tier",
    }
)

DEFAULT_CHECKLIST: list[dict[str, Any]] = [
    {"id": "play_disk", "label": "9 play tiles centered (3×3)", "done": True, "locked": True, "decision": "always on"},
    {"id": "scope", "label": "Build scope & goals", "done": False, "locked": False, "decision": ""},
    {"id": "props", "label": "Props & scatter", "done": False, "locked": False, "decision": ""},
    {"id": "npcs", "label": "NPCs & characters", "done": False, "locked": False, "decision": ""},
    {"id": "enemies", "label": "Enemies & combat", "done": False, "locked": False, "decision": ""},
    {"id": "puzzles", "label": "Puzzles / interactables", "done": False, "locked": False, "decision": ""},
    {"id": "terrain", "label": "Mountains / water / labyrinth", "done": False, "locked": False, "decision": ""},
    {"id": "speed", "label": "Speed vs quality tier", "done": False, "locked": False, "decision": ""},
    {"id": "tech_jump", "label": "Learn: jump gaps & platform spacing (meters)", "done": False, "locked": False, "decision": ""},
    {"id": "tech_seams", "label": "Learn: tile height offsets & seams", "done": False, "locked": False, "decision": ""},
    {"id": "tech_navmesh", "label": "Learn: walkable vs jump-only routes", "done": False, "locked": False, "decision": ""},
    {"id": "tech_spawn", "label": "Learn: spawn point & fall respawn", "done": False, "locked": False, "decision": ""},
    {"id": "tech_seed", "label": "Learn: fixed vs random seed", "done": False, "locked": False, "decision": ""},
    {"id": "tech_collision", "label": "Learn: triggers, kill volumes, platform colliders", "done": False, "locked": False, "decision": ""},
    {"id": "tech_perf", "label": "Learn: demo perf / prop budget", "done": False, "locked": False, "decision": ""},
]

MOVE_ON_RE = re.compile(
    r"\b(move\s+on|skip\s+remaining|finish\s+q(?:&|and)?a|done\s+with\s+questions|proceed\s+to\s+concept)\b",
    re.IGNORECASE,
)
RETRY_CONCEPT_RE = re.compile(r"\bretry\s+concept\b", re.IGNORECASE)
CHECKLIST_OPTION_LIST_RE = re.compile(
    r"(?:change|revise|unlock|reopen|edit)\s+(?:checklist\s+)?options?\s+([\d\s,andor]+)",
    re.IGNORECASE,
)
CHECKLIST_OPTION_SINGLE_RE = re.compile(
    r"(?:change|revise|unlock|reopen|edit)\s+(?:checklist\s+)?(?:option|item|#)\s*(\d+)",
    re.IGNORECASE,
)


def _user_text(doc: dict[str, Any]) -> str:
    return "\n".join(m.get("content", "") for m in doc.get("messages", []) if m.get("role") == "user").lower()


def _snippet_for_topic(doc: dict[str, Any], topic_id: str) -> str:
    kws = TOPIC_KEYWORDS.get(topic_id, ())
    for m in doc.get("messages", []):
        if m.get("role") != "user":
            continue
        content = (m.get("content") or "").strip()
        low = content.lower()
        if any(kw in low for kw in kws):
            return content[:160]
    return ""


def _topic_mentioned_by_user(doc: dict[str, Any], topic_id: str) -> bool:
    if topic_id == "play_disk":
        return True
    if topic_id == "scope":
        for m in doc.get("messages", []):
            if m.get("role") == "user" and len((m.get("content") or "").strip()) > 12:
                return True
        return False
    text = _user_text(doc)
    return any(k in text for k in TOPIC_KEYWORDS.get(topic_id, ()))


def _decision_is_vague(decision: str, topic_id: str = "") -> bool:
    d = (decision or "").strip()
    low = d.lower()
    if not d:
        return True
    if topic_id == "scope" and len(d) > 45:
        return False
    if low in GENERIC_DECISIONS:
        return True
    if len(d) < 14:
        return True
    return any(marker in low for marker in VAGUE_DECISION_MARKERS)


def _canonical_id(item_id: str) -> str | None:
    if item_id in CANONICAL_CHECKLIST_IDS:
        return item_id
    return CHECKLIST_ALIASES.get(item_id)


def _checklist_index_for_id(item_id: str) -> int:
    cid = _canonical_id(item_id) or item_id
    try:
        return CANONICAL_CHECKLIST_IDS.index(cid) + 1
    except ValueError:
        return 0


def _id_for_checklist_index(index: int) -> str | None:
    if 1 <= index <= len(CANONICAL_CHECKLIST_IDS):
        return CANONICAL_CHECKLIST_IDS[index - 1]
    return None


def _ensure_checklist_shape(doc: dict[str, Any]) -> list[dict[str, Any]]:
    by_id: dict[str, dict[str, Any]] = {c["id"]: dict(c) for c in DEFAULT_CHECKLIST}
    for item in doc.get("checklist") or []:
        cid = _canonical_id(str(item.get("id", "")))
        if not cid:
            continue
        prev = by_id[cid]
        prev["label"] = item.get("label") or prev.get("label", cid)
        prev["decision"] = str(item.get("decision") or prev.get("decision") or "")
        prev["done"] = bool(item.get("done"))
        prev["locked"] = bool(item.get("locked"))
    doc["checklist"] = [by_id[cid] for cid in CANONICAL_CHECKLIST_IDS]
    return doc["checklist"]


def _latest_user_message(doc: dict[str, Any]) -> str:
    for m in reversed(doc.get("messages") or []):
        if m.get("role") == "user":
            return str(m.get("content") or "")
    return ""


def _parse_checklist_unlock_ids(text: str) -> list[str]:
    if not text:
        return []

    low = text.lower()
    if not any(w in low for w in ("change", "revise", "unlock", "reopen", "edit")):
        return []

    found: list[str] = []

    def _add_index(num: int) -> None:
        cid = _id_for_checklist_index(num)
        if cid and cid not in found:
            found.append(cid)

    for match in CHECKLIST_OPTION_LIST_RE.finditer(text):
        group = match.group(1) or ""
        for part in re.split(r"[\s,]+|and|or", group):
            part = part.strip()
            if part.isdigit():
                _add_index(int(part))

    for match in CHECKLIST_OPTION_SINGLE_RE.finditer(text):
        part = (match.group(1) or "").strip()
        if part.isdigit():
            _add_index(int(part))

    for cid in CANONICAL_CHECKLIST_IDS:
        if cid in low and cid not in found:
            found.append(cid)

    return found


def _apply_checklist_unlocks(doc: dict[str, Any], unlock_ids: list[str]) -> None:
    if not unlock_ids:
        return
    _ensure_checklist_shape(doc)
    for item in doc["checklist"]:
        if item.get("id") not in unlock_ids:
            continue
        if item.get("id") == "play_disk":
            continue
        item["done"] = False
        item["locked"] = False
        item["decision"] = ""


def _user_wants_move_on(doc: dict[str, Any]) -> bool:
    return bool(MOVE_ON_RE.search(_latest_user_message(doc)))


def _user_wants_retry_concept(doc: dict[str, Any]) -> bool:
    return bool(RETRY_CONCEPT_RE.search(_latest_user_message(doc)))


def _checklist_all_done(doc: dict[str, Any]) -> bool:
    _ensure_checklist_shape(doc)
    return all(bool(item.get("done")) for item in doc["checklist"])


def _pending_checklist_ids(doc: dict[str, Any]) -> list[str]:
    _ensure_checklist_shape(doc)
    return [str(item["id"]) for item in doc["checklist"] if not item.get("done")]


def _next_pending_checklist_id(doc: dict[str, Any]) -> str | None:
    pending = _pending_checklist_ids(doc)
    return pending[0] if pending else None


def _label_for_checklist_id(doc: dict[str, Any], item_id: str) -> str:
    _ensure_checklist_shape(doc)
    for item in doc["checklist"]:
        if item.get("id") == item_id:
            idx = _checklist_index_for_id(item_id)
            return f"Option {idx}: {item.get('label', item_id)}"
    return item_id


def _lock_completed_checklist(doc: dict[str, Any]) -> None:
    _ensure_checklist_shape(doc)
    for item in doc["checklist"]:
        if not item.get("done"):
            continue
        dec = str(item.get("decision") or "").strip()
        if item.get("id") == "play_disk" or (dec and not _decision_is_vague(dec, str(item.get("id")))):
            item["locked"] = True


def _format_checklist_for_llm(doc: dict[str, Any]) -> str:
    _ensure_checklist_shape(doc)
    lines = []
    for item in doc["checklist"]:
        idx = _checklist_index_for_id(str(item.get("id")))
        status = "LOCKED ✓" if item.get("locked") and item.get("done") else ("done ✓" if item.get("done") else "PENDING")
        dec = str(item.get("decision") or "").strip()
        suffix = f' — "{dec[:120]}"' if dec else ""
        lines.append(f"{idx}. [{item.get('id')}] {item.get('label')} — {status}{suffix}")
    pending = _pending_checklist_ids(doc)
    next_id = pending[0] if pending else None
    lines.append("")
    if next_id:
        lines.append(f"NEXT PENDING (ask about this id only): {next_id}")
    else:
        lines.append("ALL CHECKLIST ITEMS COMPLETE — you may set qnaComplete true (or wait for user move on).")
    if _user_wants_move_on(doc):
        lines.append("USER SAID MOVE ON — qnaComplete may be true even if items remain pending.")
    return "\n".join(lines)


def _build_qna_system(doc: dict[str, Any]) -> str:
    from wizard_tab_research import build_prompt_injection  # noqa: PLC0415
    import world_state_snapshot as wss  # noqa: PLC0415

    injection = build_prompt_injection("terrain", doc)
    world_state = {}
    try:
        hub_root = Path(os.environ.get("HUB_ROOT") or Path.cwd())
        world_state = wss.build_world_state(hub_root)
    except Exception:
        world_state = {}
    if world_state:
        doc["worldState"] = world_state
    world_block = wss.build_prompt_block(world_state) if world_state else ""
    return (
        f"{SYSTEM_QNA}\n\n"
        f"{injection}\n\n"
        f"{world_block}\n"
        "## Live checklist state (authoritative)\n"
        f"{_format_checklist_for_llm(doc)}\n"
    )


def _annotate_checklist_indices(checklist: list[dict[str, Any]]) -> list[dict[str, Any]]:
    out = []
    for item in checklist:
        row = dict(item)
        cid = str(row.get("id", ""))
        row["index"] = _checklist_index_for_id(cid)
        out.append(row)
    return out


def _note_blocks_done(note: str) -> bool:
    low = (note or "").lower()
    return any(m in low for m in ("pending", "not yet", "tbd", "not yet placed", "not yet labeled"))


def _sanitize_checklist(doc: dict[str, Any]) -> None:
    """Validate pending checklist items; never mutate locked completed decisions."""
    by_id: dict[str, dict[str, Any]] = {c["id"]: dict(c) for c in _ensure_checklist_shape(doc)}

    by_id["play_disk"]["done"] = True
    by_id["play_disk"]["locked"] = True
    by_id["play_disk"]["decision"] = by_id["play_disk"].get("decision") or "always on"

    for cid in CANONICAL_CHECKLIST_IDS:
        if cid == "play_disk":
            continue
        entry = by_id[cid]
        if entry.get("locked") and entry.get("done"):
            continue

        dec = str(entry.get("decision") or "").strip()
        mentioned = _topic_mentioned_by_user(doc, cid)

        if entry.get("done") and dec and not _decision_is_vague(dec, cid):
            entry["locked"] = True
            continue

        if entry.get("done") and (not dec or _decision_is_vague(dec, cid)):
            entry["done"] = False
            entry["locked"] = False
            continue

        if cid == "scope" and mentioned:
            brief = doc.get("brief") or {}
            for candidate in (dec, brief.get("summary"), _snippet_for_topic(doc, "scope")):
                c = (candidate or "").strip()
                if len(c) > 25 and not _decision_is_vague(c, cid):
                    entry["decision"] = c[:200]
                    entry["done"] = True
                    entry["locked"] = True
                    break
            continue

        if not mentioned:
            entry["done"] = False
            entry["locked"] = False
            if not dec or _decision_is_vague(dec, cid):
                entry["decision"] = ""
            continue

        if _decision_is_vague(dec, cid):
            snippet = _snippet_for_topic(doc, cid)
            if snippet and len(snippet) > 12:
                entry["decision"] = snippet[:200]
                entry["done"] = True
                entry["locked"] = True
            else:
                entry["done"] = False
                entry["locked"] = False
        elif dec:
            entry["done"] = True
            entry["locked"] = True

    doc["checklist"] = [by_id[cid] for cid in CANONICAL_CHECKLIST_IDS]


def _backfill_layout_plan(doc: dict[str, Any]) -> None:
    """Ensure brief.layoutPlan exists for concept rendering on older sessions."""
    brief = doc.setdefault("brief", {})
    if isinstance(brief.get("layoutPlan"), dict) and brief["layoutPlan"].get("markers"):
        return
    from planner_concept_render import derive_layout_plan

    brief["layoutPlan"] = derive_layout_plan(brief, doc.get("sessionConfig") or {})


def _merge_checklist(doc: dict[str, Any], incoming: list[dict[str, Any]] | None) -> None:
    if not incoming:
        return
    by_id = {c["id"]: dict(c) for c in _ensure_checklist_shape(doc)}
    for item in incoming:
        raw_id = item.get("id")
        if not raw_id:
            continue
        iid = _canonical_id(str(raw_id)) or str(raw_id)
        if iid not in by_id:
            continue
        prev = by_id[iid]
        if prev.get("locked") and prev.get("done"):
            continue
        prev["label"] = item.get("label") or prev.get("label", iid)
        if "done" in item:
            prev["done"] = bool(item["done"])
        if item.get("decision"):
            dec = str(item["decision"])[:200]
            if not _decision_is_vague(dec, iid):
                prev["decision"] = dec
        by_id[iid] = prev
    doc["checklist"] = [by_id[cid] for cid in CANONICAL_CHECKLIST_IDS]
    _sanitize_checklist(doc)
    _lock_completed_checklist(doc)


def _awaiting_assistant_reply(doc: dict[str, Any]) -> bool:
    if doc.get("phase") != "qna":
        return False
    if doc.get("cursorWorking"):
        return False
    msgs = doc.get("messages") or []
    return bool(msgs) and msgs[-1].get("role") == "user"


def _clear_stale_streaming(doc: dict[str, Any]) -> bool:
    """Drop streamingText left on disk after a turn finished or a worker crashed."""
    if doc.get("cursorWorking"):
        return False
    raw = doc.get("streamingText")
    if not raw:
        return False
    msgs = doc.get("messages") or []
    role = doc.get("streamingRole") or "assistant"
    display = (
        _planner_stream_display(str(raw))
        if role == "assistant"
        else str(raw)
    ).strip()
    if msgs and msgs[-1].get("role") == "assistant":
        committed = _sanitize_chat_content(str(msgs[-1].get("content") or "")).strip()
        if committed and display and (
            committed == display
            or committed.startswith(display[: min(len(display), 160)])
            or display.startswith(committed[: min(len(committed), 160)])
        ):
            _clear_streaming(doc)
            return True
    updated = doc.get("updatedUtc")
    if updated:
        try:
            t = datetime.fromisoformat(updated.replace("Z", "+00:00"))
            age = (datetime.now(timezone.utc) - t).total_seconds()
            if age > 30:
                _clear_streaming(doc)
                return True
        except Exception:
            _clear_streaming(doc)
            return True
    return False


def _clear_stale_auto_respond(doc: dict[str, Any]) -> bool:
    """Auto-responder is Q&A-only — clear flags once concept review starts."""
    if not doc.get("autoRespondActive") and "autoRespondTurn" not in doc:
        return False
    if doc.get("phase") in ("awaiting_concept_approval", "research", "awaiting_research_approval", "plan", "awaiting_plan_approval", "finalized"):
        doc["autoRespondActive"] = False
        doc.pop("autoRespondTurn", None)
        doc.pop("autoRespondPreset", None)
        return True
    return False


def _clear_stale_cursor_working(doc: dict[str, Any]) -> bool:
    """Drop cursorWorking if a prior request crashed mid-flight."""
    if not doc.get("cursorWorking"):
        return False
    updated = doc.get("updatedUtc")
    if not updated:
        doc["cursorWorking"] = False
        return True
    try:
        t = datetime.fromisoformat(updated.replace("Z", "+00:00"))
        age = (datetime.now(timezone.utc) - t).total_seconds()
        if age > 90:
            doc["cursorWorking"] = False
            return True
    except Exception:
        doc["cursorWorking"] = False
        return True
    return False


def start_session(hub: Path, user_message: str, internet_research: bool) -> dict[str, Any]:
    load_dotenv(hub)
    doc = {
        "version": 1,
        "phase": "qna",
        "internetResearch": bool(internet_research),
        "messages": [{"role": "user", "content": user_message}],
        "brief": None,
        "sessionConfig": None,
        "conceptImageRel": None,
        "researchBundle": None,
        "planSummary": None,
        "checklist": [dict(c) for c in DEFAULT_CHECKLIST],
        "cursorWorking": False,
        "createdUtc": _utc(),
    }
    return _qna_turn(hub, doc, bootstrap=True)


def resume_qna(hub: Path) -> dict[str, Any]:
    load_dotenv(hub)
    doc = _read_session(hub)
    if not doc:
        raise RuntimeError("No planner session")
    if doc.get("phase") == "cancelled":
        doc["phase"] = "qna"
    if not _awaiting_assistant_reply(doc):
        return public_session(hub)
    return _qna_turn(hub, doc, bootstrap=True)


def chat_turn(hub: Path, user_message: str | None = None, bootstrap: bool = False) -> dict[str, Any]:
    load_dotenv(hub)
    doc = _read_session(hub)
    if not doc:
        raise RuntimeError("No planner session — call /api/planner/start first")

    if doc.get("phase") != "qna":
        raise RuntimeError(f"Planner not in Q&A phase (current: {doc.get('phase')})")

    if user_message and not bootstrap:
        doc.setdefault("messages", []).append({"role": "user", "content": user_message})

    return _qna_turn(hub, doc, bootstrap=bootstrap)


def _qna_turn(hub: Path, doc: dict[str, Any], bootstrap: bool = False) -> dict[str, Any]:
    if "checklist" not in doc:
        doc["checklist"] = [dict(c) for c in DEFAULT_CHECKLIST]
    _ensure_checklist_shape(doc)
    unlock_ids = _parse_checklist_unlock_ids(_latest_user_message(doc))
    _apply_checklist_unlocks(doc, unlock_ids)

    if _user_wants_retry_concept(doc) and doc.get("brief"):
        doc["cursorWorking"] = True
        _write_session(hub, doc)
        try:
            _render_concept_image(hub, doc)
            doc["conceptImageRel"] = str(CONCEPT_REL)
            doc["phase"] = "awaiting_concept_approval"
            doc.setdefault("messages", []).append(
                {
                    "role": "assistant",
                    "content": "Concept preview regenerated — review the cards and Accept or Revise.",
                }
            )
        except Exception as ex:
            doc["phase"] = "qna"
            doc.setdefault("messages", []).append(
                {
                    "role": "assistant",
                    "content": (
                        f"Unity Scene preview render hit a snag ({ex}). Your plan is saved — "
                        "reply **retry concept** or send one small tweak and I'll regenerate the preview."
                    ),
                }
            )
        finally:
            doc["cursorWorking"] = False
            _write_session(hub, doc)
        return public_session(hub)

    doc["cursorWorking"] = True
    _write_session(hub, doc)
    try:
        llm_msgs = [{"role": m["role"], "content": m["content"]} for m in doc.get("messages", [])]
        raw = _llm_messages(
            hub,
            _build_qna_system(doc),
            llm_msgs,
            stream_doc=doc,
            stream_role="assistant",
        )
        parsed = _parse_llm_json(raw)
        assistant = _sanitize_chat_content(parsed.get("assistantMessage") or "OK.")
        doc.setdefault("messages", []).append({"role": "assistant", "content": assistant})
        _merge_checklist(doc, parsed.get("checklist"))

        wants_complete = bool(parsed.get("qnaComplete"))
        all_done = _checklist_all_done(doc)
        move_on = _user_wants_move_on(doc)
        if wants_complete and not all_done and not move_on:
            parsed["qnaComplete"] = False
            next_id = _next_pending_checklist_id(doc)
            if next_id:
                assistant = (
                    f"{assistant.rstrip()}\n\n"
                    f"**Still on the checklist:** {_label_for_checklist_id(doc, next_id)} — "
                    "answer this topic, or say **move on** to generate the concept with what we have."
                )
                doc["messages"][-1]["content"] = assistant

        if parsed.get("qnaComplete"):
            brief = parsed.get("brief") or {}
            base_cfg = doc.get("sessionConfig") or {}
            doc["sessionConfig"] = _normalize_config({**base_cfg, **(parsed.get("sessionConfig") or {})})
            from planner_concept_render import derive_layout_plan

            brief["layoutPlan"] = derive_layout_plan(brief, doc["sessionConfig"])
            doc["brief"] = brief
            try:
                _render_concept_image(hub, doc)
                doc["conceptImageRel"] = str(CONCEPT_REL)
                doc["phase"] = "awaiting_concept_approval"
            except Exception as ex:
                doc["phase"] = "qna"
                doc["messages"].append(
                    {
                        "role": "assistant",
                        "content": (
                            f"Unity Scene preview render hit a snag ({ex}). Your plan is saved — "
                            "reply **retry concept** or send one small tweak and I'll regenerate the preview."
                        ),
                    }
                )
        else:
            doc["phase"] = "qna"
    except Exception as ex:
        _clear_streaming(doc)
        err_msg = str(ex).strip() or "Planner request failed"
        msgs = doc.get("messages") or []
        if not msgs or msgs[-1].get("role") != "assistant":
            doc.setdefault("messages", []).append(
                {
                    "role": "assistant",
                    "content": (
                        f"**Planner could not reach the AI** (`{err_msg}`).\n\n"
                        "Click **Retry reply** below — your message is saved."
                    ),
                }
            )
        doc["lastPlannerError"] = err_msg
        doc["phase"] = "qna"
        return public_session(hub)
    finally:
        doc["cursorWorking"] = False
        _write_session(hub, doc)

    return public_session(hub)


def _map_revision_label(doc: dict[str, Any]) -> str:
    n = doc.get("mapReviseCount") or 0
    src = doc.get("lastMapRevisionSource") or "pending"
    summary = str(doc.get("lastMapRevisionSummary") or "").strip()
    if len(summary) > 72:
        summary = summary[:69] + "…"
    return f"Rev {n} · {src} · {summary}" if summary else f"Rev {n} · {src}"


def _set_concept_detail_rels(hub: Path, doc: dict[str, Any]) -> None:
    if (hub / CONCEPT_LAYOUT_DETAIL_REL).is_file():
        doc["conceptLayoutDetailRel"] = str(CONCEPT_LAYOUT_DETAIL_REL)
    if (hub / CONCEPT_DENSITY_DETAIL_REL).is_file():
        doc["conceptDensityDetailRel"] = str(CONCEPT_DENSITY_DETAIL_REL)


def _render_concept_image(hub: Path, doc: dict[str, Any]) -> None:
    try:
        from planner_concept_render import render_planner_concept
    except ImportError:
        return

    brief = doc.get("brief") or {}
    cfg = doc.get("sessionConfig") or {}
    out = hub / CONCEPT_REL
    out.parent.mkdir(parents=True, exist_ok=True)
    render_planner_concept(out, brief, cfg, doc.get("checklist"))
    doc["conceptImageRel"] = str(CONCEPT_REL)
    density = hub / CONCEPT_DENSITY_REL
    if density.is_file():
        doc["conceptDensityImageRel"] = str(CONCEPT_DENSITY_REL)
    _set_concept_detail_rels(hub, doc)
    doc["conceptImageTs"] = int(datetime.now(timezone.utc).timestamp())
    sync_concept_cards(hub, doc)
    refresh_concept_card_thumbnails(hub, doc, force=True)


def _render_concept_layout_only(hub: Path, doc: dict[str, Any]) -> None:
    from planner_concept_render import (
        derive_layout_plan,
        render_layout_detail_crop,
        render_planner_concept_layout_only,
    )

    brief = doc.get("brief") or {}
    cfg = doc.get("sessionConfig") or {}
    layout = derive_layout_plan(brief, cfg)
    rev = _map_revision_label(doc)
    out = hub / CONCEPT_REL
    out.parent.mkdir(parents=True, exist_ok=True)
    render_planner_concept_layout_only(out, brief, cfg, doc.get("checklist"), revision_label=rev)
    detail_out = hub / CONCEPT_LAYOUT_DETAIL_REL
    detail_out.parent.mkdir(parents=True, exist_ok=True)
    render_layout_detail_crop(
        detail_out, brief, cfg, layout, doc.get("checklist"), revision_label=rev
    )
    doc["conceptImageRel"] = str(CONCEPT_REL)
    _set_concept_detail_rels(hub, doc)
    doc["conceptImageTs"] = int(datetime.now(timezone.utc).timestamp())


def _render_concept_density_only(hub: Path, doc: dict[str, Any]) -> None:
    from planner_concept_render import (
        derive_layout_plan,
        render_density_detail_crop,
        render_planner_concept_density,
    )

    brief = doc.get("brief") or {}
    cfg = doc.get("sessionConfig") or {}
    layout = brief.get("layoutPlan")
    if not isinstance(layout, dict) or not layout.get("markers"):
        layout = derive_layout_plan(brief, cfg)
        brief["layoutPlan"] = layout
    rev = _map_revision_label(doc)
    out = hub / CONCEPT_REL
    render_planner_concept_density(
        out, brief, cfg, layout, doc.get("checklist"), revision_label=rev
    )
    detail_out = hub / CONCEPT_DENSITY_DETAIL_REL
    detail_out.parent.mkdir(parents=True, exist_ok=True)
    render_density_detail_crop(detail_out, brief, cfg, layout, revision_label=rev)
    doc["conceptDensityImageRel"] = str(CONCEPT_DENSITY_REL)
    _set_concept_detail_rels(hub, doc)
    doc["conceptImageTs"] = int(datetime.now(timezone.utc).timestamp())


def _concept_card_action_message(card: dict[str, Any]) -> dict[str, str]:
    cid = str(card.get("id") or "")
    if cid == "layout":
        return {"cardId": cid, "message": "Regenerated layout + legend (zoomed detail view)."}
    if cid == "density":
        return {"cardId": cid, "message": "Regenerated trail & prop density map."}
    if cid.startswith("asset:"):
        pool_size = int(card.get("poolSize") or 0)
        idx = int(card.get("candidateIndex") or 0)
        label = card.get("label") or cid
        if pool_size > 1:
            return {
                "cardId": cid,
                "message": f"Switched to {label} (option {(idx % pool_size) + 1} of {pool_size}).",
            }
        return {
            "cardId": cid,
            "message": (
                f"Refreshed {label}. For 3D previews export kit catalog in Unity "
                f"(Window → Environment Kit → Export planner kit catalog)."
            ),
        }
    return {"cardId": cid, "message": "Card updated."}


def _find_concept_card(doc: dict[str, Any], card_id: str) -> dict[str, Any] | None:
    for c in doc.get("conceptCards") or []:
        if c.get("id") == card_id:
            return c
    return None


def concept_card_revise(
    hub: Path, card_id: str, revision_note: str = ""
) -> dict[str, Any]:
    doc = _read_session(hub)
    if doc.get("phase") != "awaiting_concept_approval":
        raise RuntimeError("Not awaiting concept approval")

    card = _find_concept_card(doc, card_id)
    if card is None:
        raise RuntimeError(f"Unknown concept card: {card_id}")

    if card.get("status") == "accepted":
        card["status"] = "pending"

    cid = card.get("id")
    if cid in ("layout", "density"):
        from planner_card_job_queue import enqueue_card_job

        note = " ".join((revision_note or "").split()).strip()
        card["lastRevisionNote"] = note
        label = card.get("label") or str(cid)
        doc["lastConceptCardAction"] = {
            "cardId": str(cid),
            "message": (
                f"Cursor — revising {label}"
                + (f' — “{note[:72]}{"…" if len(note) > 72 else ""}”' if note else "…")
            ),
        }
        doc["updatedUtc"] = _utc()
        _write_session(hub, doc)
        enqueue_card_job(hub, str(cid), "revise_map", front=True, revision_note=note)
        kick_card_queue(hub)
        return _return_card_action_session(hub)
    elif str(cid).startswith("asset:"):
        if card_wants_ai_mesh(card):
            card["needsAiMesh"] = True
            slot = str(card.get("slotLabel") or "")
            if slot:
                card["label"] = slot.split()[-1] if slot.split() else slot
            card["prefabPath"] = None
            card["status"] = "pending"
            card["imageRel"] = _placeholder_thumb(
                hub,
                card_id,
                None,
                card.get("label") or slot or card_id,
                generated_prop=True,
            )
            card["imageTs"] = int(datetime.now(timezone.utc).timestamp())
            enqueue_card_job(hub, card_id, "generate_mesh", front=True)
            doc["lastConceptCardAction"] = {
                "cardId": card_id,
                "message": f"AI Generator — designing 3D mesh for {slot or card.get('label') or card_id}…",
            }
        else:
            _, needs_mesh = revise_asset_card(hub, card)
            card["imageTs"] = int(datetime.now(timezone.utc).timestamp())
            if needs_mesh:
                enqueue_card_job(hub, card_id, "generate_mesh", front=True)
                label = card.get("slotLabel") or card.get("label") or card_id
                doc["lastConceptCardAction"] = {
                    "cardId": card_id,
                    "message": (
                        f"No other kit variant for {label} — "
                        "generating a new mesh shape in Unity…"
                    ),
                }
            else:
                doc["lastConceptCardAction"] = _concept_card_action_message(card)
    else:
        raise RuntimeError(f"Cannot revise card type: {cid}")

    doc["updatedUtc"] = _utc()
    _write_session(hub, doc)
    kick_card_queue(hub)
    return _return_card_action_session(hub)


def concept_card_accept(hub: Path, card_id: str) -> dict[str, Any]:
    doc = _read_session(hub)
    if doc.get("phase") != "awaiting_concept_approval":
        raise RuntimeError("Not awaiting concept approval")

    card = _find_concept_card(doc, card_id)
    if card is None:
        raise RuntimeError(f"Unknown concept card: {card_id}")

    card["status"] = "accepted"
    doc["updatedUtc"] = _utc()
    _write_session(hub, doc)
    return _return_card_action_session(hub)


def concept_card_unaccept(hub: Path, card_id: str) -> dict[str, Any]:
    doc = _read_session(hub)
    if doc.get("phase") != "awaiting_concept_approval":
        raise RuntimeError("Not awaiting concept approval")

    card = _find_concept_card(doc, card_id)
    if card is None:
        raise RuntimeError(f"Unknown concept card: {card_id}")

    card["status"] = "pending"
    doc["updatedUtc"] = _utc()
    _write_session(hub, doc)
    return _return_card_action_session(hub)


def _cursor_character_sculpt_spec(
    hub: Path, card: dict[str, Any], regen_index: int, sculpt_hint: str
) -> dict[str, Any] | None:
    """Ask Cursor API for character morph parameters (requires CURSOR_API_KEY)."""
    load_dotenv(hub)
    if not os.environ.get("CURSOR_API_KEY", "").strip():
        return None

    script = TOOLS / "planner-character-sculpt.ts"
    if not script.is_file():
        return None

    doc = _read_session(hub) or {}
    brief = doc.get("brief") or {}
    brief_bits = [
        str(brief.get("title") or ""),
        str(brief.get("tone") or ""),
        str(brief.get("setting") or ""),
    ]
    brief_snippet = " · ".join(b for b in brief_bits if b)[:400]

    import tempfile

    payload = {
        "hubRoot": str(hub),
        "label": mesh_source_label(card),
        "cardType": str(card.get("cardType") or "npc"),
        "sculptHint": sculpt_hint,
        "regenIndex": regen_index,
        "briefSnippet": brief_snippet,
    }
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as tmp:
        json.dump(payload, tmp)
        req_path = tmp.name

    try:
        proc = subprocess.run(
            _tsx_argv(script, req_path),
            cwd=TOOLS,
            capture_output=True,
            text=True,
            timeout=120,
            env=_planner_subprocess_env(hub),
        )
        line = (proc.stdout or proc.stderr or "").strip().splitlines()[-1] if proc.stdout or proc.stderr else ""
        if not line:
            return None
        data = json.loads(line)
        if data.get("error") or not data.get("ok"):
            return None
        spec = data.get("spec")
        return spec if isinstance(spec, dict) else None
    except (json.JSONDecodeError, subprocess.TimeoutExpired, OSError):
        return None
    finally:
        try:
            os.unlink(req_path)
        except OSError:
            pass


def _cursor_prop_mesh_spec(hub: Path, card: dict[str, Any], regen_index: int) -> dict[str, Any] | None:
    """Ask Cursor API for a mesh design spec (requires CURSOR_API_KEY in cave-grader/.env)."""
    load_dotenv(hub)
    if not os.environ.get("CURSOR_API_KEY", "").strip():
        return None

    script = TOOLS / "planner-mesh-gen.ts"
    if not script.is_file():
        return None

    doc = _read_session(hub) or {}
    brief = doc.get("brief") or {}
    brief_bits = [
        str(brief.get("title") or ""),
        str(brief.get("tone") or ""),
        str(brief.get("setting") or ""),
    ]
    brief_snippet = " · ".join(b for b in brief_bits if b)[:400]

    import tempfile

    payload = {
        "hubRoot": str(hub),
        "label": mesh_source_label(card),
        "slotLabel": str(card.get("slotLabel") or ""),
        "kind": prop_kind_for_card(card),
        "categoryKey": str(card.get("categoryKey") or ""),
        "cardType": str(card.get("cardType") or ""),
        "regenIndex": regen_index,
        "briefSnippet": brief_snippet,
    }
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as tmp:
        json.dump(payload, tmp)
        req_path = tmp.name

    try:
        proc = subprocess.run(
            _tsx_argv(script, req_path),
            cwd=TOOLS,
            capture_output=True,
            text=True,
            timeout=120,
            env=_planner_subprocess_env(hub),
        )
        line = (proc.stdout or proc.stderr or "").strip().splitlines()[-1] if proc.stdout or proc.stderr else ""
        if not line:
            return None
        data = json.loads(line)
        if data.get("error") or not data.get("ok"):
            return None
        spec = data.get("spec")
        return spec if isinstance(spec, dict) else None
    except (json.JSONDecodeError, subprocess.TimeoutExpired, OSError):
        return None
    finally:
        try:
            os.unlink(req_path)
        except OSError:
            pass


def concept_card_sculpt_character(hub: Path, card_id: str, sculpt_hint: str) -> dict[str, Any]:
    from planner_asset_catalog import card_can_sculpt_character, normalize_sculpt_hint

    doc = _read_session(hub)
    if doc.get("phase") != "awaiting_concept_approval":
        raise RuntimeError("Not awaiting concept approval")

    card = _find_concept_card(doc, card_id)
    if card is None:
        raise RuntimeError(f"Unknown concept card: {card_id}")
    if not card_can_sculpt_character(hub, card):
        raise RuntimeError("Only pending NPC, enemy, and player cards support Resculpt.")

    hint = normalize_sculpt_hint(sculpt_hint)
    if not hint:
        raise RuntimeError("Enter 1–2 words describing the sculpt (e.g. tall lanky, stocky guard).")

    card["pendingSculptHint"] = hint
    doc["updatedUtc"] = _utc()
    _write_session(hub, doc)
    enqueue_card_job(hub, card_id, "sculpt_character", front=True, sculpt_hint=hint)
    return _return_card_action_session(hub)


def concept_card_generate_mesh(hub: Path, card_id: str) -> dict[str, Any]:
    doc = _read_session(hub)
    if doc.get("phase") != "awaiting_concept_approval":
        raise RuntimeError("Not awaiting concept approval")

    card = _find_concept_card(doc, card_id)
    if (card is None):
        raise RuntimeError(f"Unknown concept card: {card_id}")
    if not card_can_generate_mesh(hub, card):
        raise RuntimeError("Only pending prop/collectible cards support Generate mesh.")

    label = str(card.get("slotLabel") or card.get("label") or card_id)
    enqueue_card_job(hub, card_id, "generate_mesh", front=True)
    doc = _read_session(hub) or {}
    doc["lastConceptCardAction"] = {
        "cardId": card_id,
        "action": "generate_mesh",
        "message": f"AI Generator — building 3D mesh for {label}…",
    }
    doc["updatedUtc"] = _utc()
    _write_session(hub, doc)
    return _return_card_action_session(hub)


def concept_card_load_preview(hub: Path, card_id: str) -> dict[str, Any]:
    doc = _read_session(hub)
    if doc.get("phase") != "awaiting_concept_approval":
        raise RuntimeError("Not awaiting concept approval")

    card = _find_concept_card(doc, card_id)
    if card is None:
        raise RuntimeError(f"Unknown concept card: {card_id}")
    if not str(card_id).startswith("asset:"):
        raise RuntimeError("Only asset cards support preview loading")

    enqueue_card_job(hub, card_id, "load_preview", front=False)
    return _return_card_action_session(hub)


def concept_card_set_count(hub: Path, card_id: str, count: int) -> dict[str, Any]:
    doc = _read_session(hub)
    if doc.get("phase") != "awaiting_concept_approval":
        raise RuntimeError("Not awaiting concept approval")

    card = _find_concept_card(doc, card_id)
    if card is None:
        raise RuntimeError(f"Unknown concept card: {card_id}")
    if not str(card_id).startswith("asset:"):
        raise RuntimeError("Only kit asset cards support count adjustment")
    if card.get("cardType") == "npc":
        raise RuntimeError("Each NPC slot is one character — use Revise to pick a different prefab.")
    if card.get("cardType") not in COUNT_ADJUST_KINDS and not card.get("allowCountAdjust"):
        raise RuntimeError("This card type does not support instance count adjustment.")

    card["targetInstanceCount"] = max(0, min(500, int(count)))
    apply_card_counts_to_brief(doc)
    _render_concept_density_only(hub, doc)
    density = _find_concept_card(doc, "density")
    if density:
        density.update(build_density_card(doc, doc.get("conceptCards")))
        if density.get("status") == "accepted":
            density["status"] = "pending"
    doc["updatedUtc"] = _utc()
    _write_session(hub, doc)
    return _return_card_action_session(hub)


def concept_card_accept_all(hub: Path) -> dict[str, Any]:
    doc = _read_session(hub)
    if doc.get("phase") != "awaiting_concept_approval":
        raise RuntimeError("Not awaiting concept approval")

    for card in doc.get("conceptCards") or []:
        card["status"] = "accepted"
    doc["updatedUtc"] = _utc()
    _write_session(hub, doc)
    return _return_card_action_session(hub)


def approve_concept(hub: Path, approved: bool, feedback: str = "") -> dict[str, Any]:
    doc = _read_session(hub)
    if doc.get("phase") != "awaiting_concept_approval":
        raise RuntimeError("Not awaiting concept approval")

    if not approved:
        if feedback:
            doc.setdefault("messages", []).append({"role": "user", "content": f"Concept rejected: {feedback}"})
        for card in doc.get("conceptCards") or []:
            card["status"] = "pending"
        doc["phase"] = "qna"
        _write_session(hub, doc)
        return chat_turn(hub, feedback or "Please revise the concept.")

    apply_card_counts_to_brief(doc)
    for card in doc.get("conceptCards") or []:
        card["status"] = "accepted"
    doc["approvedConceptCards"] = approved_cards_manifest(doc.get("conceptCards") or [])
    doc["updatedUtc"] = _utc()
    _write_session(hub, doc)

    doc["phase"] = "research" if doc.get("internetResearch") else "plan"
    if doc.get("internetResearch"):
        run_research(hub)
        doc = _read_session(hub)
    else:
        doc["planSummary"] = _compile_plan_summary(doc)
        doc["phase"] = "awaiting_plan_approval"
        _write_session(hub, doc)
    return public_session(hub)


def _cursor_research(
    hub: Path,
    doc: dict[str, Any],
    *,
    tab_id: str | None = None,
) -> dict[str, Any]:
    """Run web research via Cursor Agent (web search tools), not raw URL scraping."""
    load_dotenv(hub)
    api_key = os.environ.get("CURSOR_API_KEY", "").strip()
    if not api_key:
        raise RuntimeError(
            "CURSOR_API_KEY missing — set it in Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/.env"
        )

    script = TOOLS / "planner-research.ts"
    if not script.is_file():
        raise RuntimeError(f"planner-research.ts not found at {script}")

    import tempfile

    from wizard_tab_research import (  # noqa: PLC0415
        TAB_RESEARCH_FOCUS,
        research_queries_for_tab,
    )

    resolved_tab = tab_id or str(doc.get("tabId") or "terrain")
    brief = doc.get("brief") or {}
    queries = brief.get("researchQueries") or research_queries_for_tab(
        resolved_tab,
        str((doc.get("messages") or [{}])[-1].get("content") or ""),
        brief,
    )
    research_focus = TAB_RESEARCH_FOCUS.get(resolved_tab, TAB_RESEARCH_FOCUS["terrain"])
    payload = {
        "hubRoot": str(hub),
        "brief": brief,
        "sessionConfig": doc.get("sessionConfig") or {},
        "queries": queries[:5],
        "tabId": resolved_tab,
        "researchFocus": research_focus,
    }
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as tmp:
        json.dump(payload, tmp)
        req_path = tmp.name

    try:
        env = _planner_subprocess_env(hub)
        proc = subprocess.run(
            _tsx_argv(script, req_path),
            cwd=TOOLS,
            capture_output=True,
            text=True,
            timeout=600,
            env=env,
        )
        raw_out = (proc.stdout or "").strip()
        if not raw_out:
            raise RuntimeError(proc.stderr or "AI research returned no output")
        data = None
        for candidate in reversed(raw_out.splitlines()):
            candidate = candidate.strip()
            if candidate.startswith("{"):
                try:
                    data = json.loads(candidate)
                    break
                except json.JSONDecodeError:
                    continue
        if data is None:
            match = re.search(r"\{[\s\S]*\}", raw_out)
            if match:
                data = json.loads(match.group(0))
        if data is None:
            raise RuntimeError("AI research returned non-JSON output")
        if data.get("error"):
            raise RuntimeError(str(data["error"]))
        items = data.get("items") or []
        if not items:
            raise RuntimeError("AI research returned no sources")
        return {
            "queries": data.get("queries") or queries[:5],
            "items": items,
            "provider": "cursor",
        }
    finally:
        try:
            os.unlink(req_path)
        except OSError:
            pass


def run_research(hub: Path) -> None:
    load_dotenv(hub)
    doc = _read_session(hub)
    doc["cursorWorking"] = True
    doc["phase"] = "research"
    _write_session(hub, doc)

    from wizard_tab_research import research_queries_for_tab  # noqa: PLC0415

    brief = doc.get("brief") or {}
    default_queries = brief.get("researchQueries") or research_queries_for_tab("terrain", "", brief)
    try:
        bundle = _cursor_research(hub, doc, tab_id="terrain")
        doc["researchBundle"] = bundle
        doc["researchError"] = None
    except Exception as exc:
        doc["researchBundle"] = {"queries": default_queries[:5], "items": [], "provider": "cursor"}
        doc["researchError"] = str(exc)
    finally:
        doc["cursorWorking"] = False
        doc["phase"] = "awaiting_research_approval"
        _write_session(hub, doc)


def approve_research(hub: Path, approved: bool, selected_ids: list[str] | None = None) -> dict[str, Any]:
    doc = _read_session(hub)
    if doc.get("phase") != "awaiting_research_approval":
        raise RuntimeError("Not awaiting research approval")

    if not approved:
        doc["phase"] = "research"
        run_research(hub)
        return public_session(hub)

    bundle = doc.get("researchBundle") or {}
    items = bundle.get("items") or []
    chosen = [i for i in items if not selected_ids or i.get("id") in selected_ids]
    if chosen:
        _save_research_entries(hub, chosen)

    doc["planSummary"] = _compile_plan_summary(doc)
    doc["phase"] = "awaiting_plan_approval"
    _write_session(hub, doc)
    return public_session(hub)


def _save_research_entries(hub: Path, items: list[dict[str, Any]]) -> None:
    script = TOOLS / "save-planner-research.ts"
    if not script.is_file():
        return
    payload = hub / "Assets/EnvironmentKit/Generated/_planner_research_bundle.json"
    payload.parent.mkdir(parents=True, exist_ok=True)
    payload.write_text(json.dumps({"items": items}, indent=2) + "\n", encoding="utf-8")
    subprocess.run(
        _tsx_argv(script, hub, payload),
        cwd=TOOLS,
        check=False,
        timeout=120,
        env=_planner_subprocess_env(hub),
    )


def _compile_plan_summary(doc: dict[str, Any]) -> str:
    brief = doc.get("brief") or {}
    cfg = doc.get("sessionConfig") or {}
    lines = [
        f"# {brief.get('title') or cfg.get('label', 'Build plan')}",
        "",
        brief.get("summary") or "",
        "",
        "## Session settings",
        f"- Tiles: {cfg.get('tileCount', 9)} (9 play tiles always centered)",
        f"- Props: {'on' if cfg.get('playDiskProps') else 'off'}",
        f"- Mountains/labyrinth: {cfg.get('outerRingMountains')}/{cfg.get('mountainLabyrinth')}",
        f"- Pro level: {'yes' if cfg.get('proLevelWorld') else 'no'}",
        f"- 3D objects: {'yes' if cfg.get('import3DObjects') else 'no'}",
        f"- Random land placement: {'yes' if cfg.get('randomLandPlacement') else 'no'}",
        "",
        "## Goals",
    ]
    for g in brief.get("userGoals") or []:
        lines.append(f"- {g}")
    lines.extend(["", "## Concept images (generation authority)"])
    if doc.get("conceptImageRel"):
        lines.append(
            f"- **Layout + legend:** `{doc['conceptImageRel']}` — terrain tiles, platforms, "
            "spawn, trails, prop kinds (colors match Unity sculpt sampling)."
        )
    if doc.get("conceptDensityImageRel"):
        lines.append(
            f"- **Trail & prop density:** `{doc['conceptDensityImageRel']}` — gold corridors = "
            "walk paths; amber dots = prop scatter; green heat = props per tile."
        )
    approved = doc.get("approvedConceptCards") or approved_cards_manifest(doc.get("conceptCards") or [])
    if approved:
        lines.extend(["", "## Approved concept cards (generation contract)"])
        for card in approved:
            prefab = card.get("prefabPath")
            extra = f" prefab `{prefab}`" if prefab else ""
            count = card.get("targetInstanceCount", card.get("instanceCount", 1))
            lines.append(
                f"- **{card.get('label', card.get('id'))}** (`{card.get('id')}`) ×{count}"
                f"{extra} — image `{card.get('imageRel')}`"
            )
    return "\n".join(lines)


def approve_plan(hub: Path, approved: bool) -> dict[str, Any]:
    doc = _read_session(hub)
    if doc.get("phase") != "awaiting_plan_approval":
        raise RuntimeError("Not awaiting plan approval")
    if not approved:
        doc["phase"] = "qna"
        _write_session(hub, doc)
        return public_session(hub)

    apply_card_counts_to_brief(doc)

    cfg = _normalize_config(doc.get("sessionConfig") or {})
    cfg["label"] = (doc.get("brief") or {}).get("title") or cfg.get("label")
    cfg["finalizedUtc"] = _utc()
    cfg["plannerSession"] = True
    cfg = _normalize_config(cfg)

    active = hub / ACTIVE_REL
    active.parent.mkdir(parents=True, exist_ok=True)
    active.write_text(json.dumps(cfg, indent=2) + "\n", encoding="utf-8")

    approved = doc.get("approvedConceptCards") or approved_cards_manifest(doc.get("conceptCards") or [])
    brief_out = {
        "planSummary": doc.get("planSummary"),
        "brief": doc.get("brief"),
        "conceptImageRel": doc.get("conceptImageRel"),
        "conceptDensityImageRel": doc.get("conceptDensityImageRel"),
        "approvedConceptCards": approved,
        "messages": doc.get("messages"),
        "compiledUtc": _utc(),
    }
    (hub / BRIEF_REL).write_text(json.dumps(brief_out, indent=2) + "\n", encoding="utf-8")

    if doc.get("conceptImageRel"):
        approved_concept = hub / Path(
            "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts/planner/concept.png"
        )
        approved_concept.parent.mkdir(parents=True, exist_ok=True)
        src = hub / doc["conceptImageRel"]
        if src.is_file():
            approved_concept.write_bytes(src.read_bytes())

    if doc.get("conceptDensityImageRel"):
        approved_density = hub / Path(
            "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts/planner/concept-density.png"
        )
        approved_density.parent.mkdir(parents=True, exist_ok=True)
        src_d = hub / doc["conceptDensityImageRel"]
        if src_d.is_file():
            approved_density.write_bytes(src_d.read_bytes())

    approved_assets_dir = hub / Path(
        "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts/planner/assets"
    )
    approved_assets_dir.mkdir(parents=True, exist_ok=True)
    for card in approved:
        rel = card.get("imageRel")
        if not rel:
            continue
        src = hub / rel
        if not src.is_file():
            continue
        safe = re.sub(r"[^a-zA-Z0-9_.-]+", "_", str(card.get("id") or "card"))[:80]
        dest = approved_assets_dir / f"{safe}.png"
        dest.write_bytes(src.read_bytes())

    state = {"phase": "finalized", "hubRoot": str(hub), "cancelled": False, "planner": True}
    (hub / STATE_REL).write_text(json.dumps(state, indent=2) + "\n", encoding="utf-8")

    doc["phase"] = "finalized"
    _write_session(hub, doc)
    try:
        from planner_recording import finalize_wizard_capture

        finalize_wizard_capture(hub)
    except Exception:
        pass
    _activate_unity_editor()
    try:
        from wizard_tab_apply import notify_tab_approved

        notify_tab_approved(hub, "terrain")
    except Exception:
        pass
    return public_session(hub)


def _tag_ai_mesh_cards(doc: dict[str, Any]) -> bool:
    """Mark prop/collectible slots that need Hub-owned AI meshes."""
    changed = False
    for card in doc.get("conceptCards") or []:
        if not card_wants_ai_mesh(card):
            continue
        if not card.get("needsAiMesh"):
            card["needsAiMesh"] = True
            changed = True
    return changed


_SIDE_EFFECTS_HUBS: set[str] = set()
_SIDE_EFFECTS_LOCK = threading.Lock()


def _apply_concept_session_side_effects(hub: Path, doc: dict[str, Any] | None = None) -> None:
    """Thumbnails, card sync, mesh/preview queue — run off the HTTP thread."""
    if doc is None:
        doc = _read_session(hub)
    if not doc or doc.get("phase") != "awaiting_concept_approval" or not doc.get("conceptImageRel"):
        return
    cards = doc.get("conceptCards") or []
    if concept_cards_schema_stale(doc, cards):
        sync_concept_cards(hub, doc)
        _write_session(hub, doc)
    elif not cards:
        sync_concept_cards(hub, doc)
        _write_session(hub, doc)
    elif not (hub / CONCEPT_LAYOUT_DETAIL_REL).is_file() or not (
        hub / CONCEPT_DENSITY_DETAIL_REL
    ).is_file():
        try:
            _render_concept_layout_only(hub, doc)
            _render_concept_density_only(hub, doc)
            cards = doc.get("conceptCards") or []
            for idx, c in enumerate(cards):
                if c.get("id") == "layout":
                    cards[idx] = build_layout_card(doc, cards)
                elif c.get("id") == "density":
                    cards[idx] = build_density_card(doc, cards)
            doc["conceptCards"] = cards
            _write_session(hub, doc)
        except Exception:
            pass
    else:
        _set_concept_detail_rels(hub, doc)
    thumb_changed = (
        normalize_generated_prop_cards(hub, doc)
        or repair_missing_card_thumbnails(hub, doc)
        or refresh_bad_card_thumbnails(hub, doc)
        or ensure_card_image_timestamps(doc)
        or refresh_ai_mesh_card_previews(hub, doc)
        or _tag_ai_mesh_cards(doc)
    )
    queue_changed = bootstrap_ai_mesh_jobs(hub, doc) or bootstrap_preview_jobs(hub, doc)
    if thumb_changed or queue_changed:
        _write_session(hub, doc)
    kick_card_queue(hub)


def _schedule_concept_session_side_effects(hub: Path) -> None:
    """Coalesce heavy concept-card maintenance so card actions return immediately."""
    key = str(hub.resolve())
    with _SIDE_EFFECTS_LOCK:
        if key in _SIDE_EFFECTS_HUBS:
            return
        _SIDE_EFFECTS_HUBS.add(key)

    def _worker() -> None:
        try:
            _apply_concept_session_side_effects(hub)
        finally:
            with _SIDE_EFFECTS_LOCK:
                _SIDE_EFFECTS_HUBS.discard(key)

    threading.Thread(
        target=_worker, daemon=True, name=f"planner-sidefx-{key[-8:]}"
    ).start()


def _return_card_action_session(hub: Path) -> dict[str, Any]:
    """Fast HTTP response for Revise/Accept/etc.; side effects run in background."""
    _schedule_concept_session_side_effects(hub)
    return public_session(hub, side_effects=False)


def public_session(hub: Path, *, side_effects: bool = True) -> dict[str, Any]:
    doc = _read_session(hub)
    if doc:
        stale = (
            _clear_stale_auto_respond(doc)
            or _clear_stale_cursor_working(doc)
            or _clear_stale_streaming(doc)
        )
        if stale:
            _write_session(hub, doc)
        _sanitize_checklist(doc)
    if doc.get("phase") not in (None, "qna", "cancelled") and doc.get("sessionConfig"):
        _backfill_layout_plan(doc)
    if side_effects and doc.get("phase") == "awaiting_concept_approval" and doc.get("conceptImageRel"):
        # Never block HTTP — card maintenance runs on a background thread.
        _schedule_concept_session_side_effects(hub)
        doc = _read_session(hub) or doc
    out = dict(doc)
    out["awaitingAssistantReply"] = _awaiting_assistant_reply(doc)
    out["llmProvider"] = "cursor"
    ts = doc.get("conceptImageTs") or doc.get("updatedUtc", "")
    hub_q = urllib.parse.quote(str(hub), safe="")
    ts_q = urllib.parse.quote(str(ts))
    if doc.get("conceptImageRel"):
        rel = doc["conceptImageRel"]
        out["conceptImageUrl"] = (
            f"/api/planner/asset?hub={hub_q}&rel={urllib.parse.quote(rel)}&t={ts_q}"
        )
    if doc.get("conceptDensityImageRel"):
        rel_d = doc["conceptDensityImageRel"]
        out["conceptDensityImageUrl"] = (
            f"/api/planner/asset?hub={hub_q}&rel={urllib.parse.quote(rel_d)}&t={ts_q}"
        )
    out["streamingText"], out["streamingRole"] = _public_streaming(doc)
    out["messages"] = [
        {**m, "content": _sanitize_chat_content(str(m.get("content") or ""))}
        for m in (doc.get("messages") or [])
    ]
    out["conceptCards"] = cards_for_public(hub, doc) if doc else []
    out["conceptCardsAccepted"] = sum(
        1 for c in (doc.get("conceptCards") or []) if c.get("status") == "accepted"
    )
    out["conceptCardsTotal"] = len(doc.get("conceptCards") or [])
    if doc.get("lastConceptCardAction"):
        out["lastConceptCardAction"] = doc["lastConceptCardAction"]
    if doc.get("lastMapRevisionSummary"):
        out["lastMapRevisionSummary"] = doc["lastMapRevisionSummary"]
    if doc.get("lastMapRevisionSource"):
        out["lastMapRevisionSource"] = doc["lastMapRevisionSource"]
    if doc.get("lastMapRevisionCard"):
        out["lastMapRevisionCard"] = doc["lastMapRevisionCard"]
    if doc.get("layoutReviseCount") is not None:
        out["layoutReviseCount"] = doc.get("layoutReviseCount")
    if doc.get("densityReviseCount") is not None:
        out["densityReviseCount"] = doc.get("densityReviseCount")
    out["kitCatalogExporting"] = bool(doc.get("kitCatalogExporting"))
    out["kitCatalogExportMessage"] = doc.get("kitCatalogExportMessage")
    out["kitCatalogExportError"] = doc.get("kitCatalogExportError")
    out["cardPacedQueue"] = list(doc.get("cardPacedQueue") or [])
    out["cardPacedActive"] = doc.get("cardPacedActive")
    out["cardPacedBusy"] = paced_queue_busy(doc) if doc else False
    out["kitCatalogThumbCount"] = kit_catalog_thumb_count(hub) if doc else 0
    out["cursorWorking"] = bool(doc.get("cursorWorking"))
    out["autoRespondActive"] = bool(doc.get("autoRespondActive"))
    out["autoRespondPreset"] = doc.get("autoRespondPreset")
    out["autoRespondTurn"] = doc.get("autoRespondTurn")
    if "checklist" not in out:
        out["checklist"] = [dict(c) for c in DEFAULT_CHECKLIST]
    out["checklist"] = _annotate_checklist_indices(out.get("checklist") or [])
    out["checklistComplete"] = _checklist_all_done(doc) if doc else False
    out["canMoveOn"] = True
    return out


def session_pulse(hub: Path) -> dict[str, Any]:
    """Lightweight poll payload for live streaming UI (no catalog/thumbnail side effects)."""
    doc = _read_session(hub)
    if not doc:
        return {"phase": None, "messages": []}
    if _clear_stale_cursor_working(doc):
        _write_session(hub, doc)
    stream_text, stream_role = _public_streaming(doc)
    return {
        "phase": doc.get("phase"),
        "messages": [
            {**m, "content": _sanitize_chat_content(str(m.get("content") or ""))}
            for m in (doc.get("messages") or [])
        ],
        "streamingText": stream_text,
        "streamingRole": stream_role,
        "cursorWorking": bool(doc.get("cursorWorking")),
        "awaitingAssistantReply": _awaiting_assistant_reply(doc),
        "autoRespondActive": bool(doc.get("autoRespondActive")),
        "autoRespondPreset": doc.get("autoRespondPreset"),
        "autoRespondTurn": doc.get("autoRespondTurn"),
        "kitCatalogExporting": bool(doc.get("kitCatalogExporting")),
        "kitCatalogExportMessage": doc.get("kitCatalogExportMessage"),
        "kitCatalogExportError": doc.get("kitCatalogExportError"),
    }


def cancel(hub: Path) -> None:
    state = {"phase": "cancelled", "hubRoot": str(hub), "cancelled": True}
    (hub / STATE_REL).parent.mkdir(parents=True, exist_ok=True)
    (hub / STATE_REL).write_text(json.dumps(state, indent=2) + "\n", encoding="utf-8")
    doc = _read_session(hub)
    if doc:
        doc["phase"] = "cancelled"
        _write_session(hub, doc)


def _unlink_if_exists(path: Path) -> None:
    if path.is_file():
        path.unlink()


def _write_wizard_state(hub: Path, phase: str, *, cancelled: bool = False) -> None:
    state = {"phase": phase, "hubRoot": str(hub), "cancelled": cancelled, "planner": True}
    (hub / STATE_REL).parent.mkdir(parents=True, exist_ok=True)
    (hub / STATE_REL).write_text(json.dumps(state, indent=2) + "\n", encoding="utf-8")


def _clear_unity_finalize(hub: Path) -> None:
    """Remove active build config and unwind wizard finalize so approve can run again."""
    _unlink_if_exists(hub / ACTIVE_REL)
    _write_wizard_state(hub, "awaiting_finalize", cancelled=False)


def reset_session(hub: Path, mode: str = "plan") -> dict[str, Any]:
    """
    Reset planner / Unity JSON for local testing.

    mode=plan — keep Q&A + concept + plan; rewind to the last approval step.
    mode=full — delete planner session and brief; return to the start screen.
    """
    mode = (mode or "plan").strip().lower()
    if mode not in ("plan", "full"):
        raise RuntimeError("mode must be 'plan' or 'full'")

    if mode == "full":
        _unlink_if_exists(planner_session_write_path(hub))
        _unlink_if_exists(hub / PLANNER_REL)
        _unlink_if_exists(hub / BRIEF_REL)
        _unlink_if_exists(hub / ACTIVE_REL)
        _write_wizard_state(hub, "cancelled", cancelled=True)
        return {}

    doc = _read_session(hub)
    if not doc:
        _clear_unity_finalize(hub)
        return {}

    _clear_unity_finalize(hub)

    if doc.get("planSummary"):
        doc["phase"] = "awaiting_plan_approval"
    elif doc.get("researchBundle") and doc.get("internetResearch"):
        doc["phase"] = "awaiting_research_approval"
    elif doc.get("conceptImageRel"):
        doc["phase"] = "awaiting_concept_approval"
    else:
        doc["phase"] = "qna"

    cfg = dict(doc.get("sessionConfig") or {})
    cfg.pop("finalizedUtc", None)
    cfg.pop("plannerSession", None)
    doc["sessionConfig"] = cfg
    doc["cursorWorking"] = False
    _write_session(hub, doc)
    return public_session(hub)
