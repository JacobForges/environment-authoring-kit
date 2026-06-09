#!/usr/bin/env python3
"""AI build planner — Q&A, concept image, optional web research, session compile."""
from __future__ import annotations

import json
import os
import platform
import re
import shutil
import subprocess
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

TOOLS = Path(__file__).resolve().parent
PLANNER_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildPlannerSession.json")
BRIEF_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildPlannerBrief.json")
ACTIVE_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildActiveSessionConfig.json")
STATE_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildWizardState.json")
CONCEPT_REL = Path(
    "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_pending-review/planner-session/concept.png"
)


def _utc() -> str:
    return datetime.now(timezone.utc).isoformat()


def _activate_unity_editor() -> None:
    """Bring Unity Editor to the foreground so finalize polling can start the build immediately."""
    if platform.system() != "Darwin":
        return

    unity_path = os.environ.get("UNITY_PATH", "").strip()
    if unity_path:
        app = Path(unity_path)
        bundle = app if app.suffix == ".app" else app.parent.parent if app.name == "Unity" else None
        if bundle and bundle.suffix == ".app" and bundle.is_dir():
            subprocess.run(["open", "-a", str(bundle)], check=False, timeout=5)
            return

    subprocess.run(
        ["osascript", "-e", 'tell application "Unity" to activate'],
        check=False,
        timeout=5,
    )


def load_dotenv(hub: Path) -> None:
    p = TOOLS / ".env"
    if not p.is_file():
        return
    for line in p.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        k, _, v = line.partition("=")
        k, v = k.strip(), v.strip().strip('"').strip("'")
        if k and k not in os.environ:
            os.environ[k] = v
    if hub and not os.environ.get("HUB_ROOT"):
        os.environ["HUB_ROOT"] = str(hub)


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
        "so the AI planner can call Cursor."
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
    return hub / PLANNER_REL


def _read_session(hub: Path) -> dict[str, Any]:
    p = _session_path(hub)
    if not p.is_file():
        return {}
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except Exception:
        return {}


def _write_session(hub: Path, doc: dict[str, Any]) -> None:
    p = _session_path(hub)
    p.parent.mkdir(parents=True, exist_ok=True)
    doc["updatedUtc"] = _utc()
    p.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")


def _defaults_config() -> dict[str, Any]:
    return {
        "version": 2,
        "label": "AI planned build",
        "tileCount": 81,
        "randomSeedEachBuild": True,
        "seed": 0,
        "playDiskProps": True,
        "hollowTitan": False,
        "enhancementPhases": False,
        "prePlacementResearch": False,
        "sequentialTerrain": False,
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


def _normalize_config(cfg: dict[str, Any]) -> dict[str, Any]:
    out = {**_defaults_config(), **(cfg or {})}
    out["tileCount"] = 289 if int(out.get("tileCount", 81)) > 81 else 81
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

    # Floating-islands social demo — strip AAA bloat the LLM sometimes leaves on.
    if out.get("floatingTiles") and out.get("tileCount", 81) <= 81 and not out.get("proLevelWorld"):
        out["enhancementPhases"] = False
        out["prePlacementResearch"] = False
        out["sequentialTerrain"] = False
        out["preBuildReloop"] = False
        out["outerRingMountains"] = False
        out["mountainLabyrinth"] = False
        out["agentInvokes"] = False
        out["runPostBuildResearch"] = False
        out["hollowTitan"] = False
        out["surfaceTerrainPasses"] = min(4, int(out.get("surfaceTerrainPasses", 4)))
        out["use3DCaveSystem"] = bool(out.get("use3DCaveSystem"))

    return out


def _llm_messages(hub: Path, system: str, messages: list[dict[str, str]]) -> str:
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

    payload = {
        "hubRoot": str(hub),
        "system": system,
        "messages": messages,
        "mode": "prompt",
    }
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as tmp:
        json.dump(payload, tmp)
        req_path = tmp.name

    try:
        env = _augment_path_env()
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
            raise RuntimeError(proc.stderr or "Cursor planner returned no output")
        data = json.loads(line)
        if data.get("error"):
            raise RuntimeError(str(data["error"]))
        text = data.get("text", "")
        if not text:
            raise RuntimeError("Cursor planner returned empty text")
        return text
    finally:
        try:
            os.unlink(req_path)
        except OSError:
            pass


SYSTEM_QNA = """You are the Environment Kit AI Build Planner for a Unity procedural cave/surface world pipeline.

ALWAYS preserve: 9 play tiles centered (3×3 flat hometown disk) — never relocate the play disk.

Interview the user until you have enough to compile a build session. Ask ONE focused follow-up at a time when qnaComplete is false.

When ready, set qnaComplete true and include:
- brief: {
    title, summary, userGoals[], researchQueries[] (3-5 web search strings if research useful),
    layoutPlan: {
      gridNote: "short grid description",
      playDisk: { labyrinth: bool, labyrinthNote: "which rows are maze vs flat spawn" },
      markers: [
        { kind: "spawn|prop|npc|enemy|collectible", zone: "play|island", row, col, dir, slot, label }
      ],
      trails: [ { from: "play-south|play-center", to: "island-N|island-E|...", label } ],
      islands: [ { dir: "N|E|S|W", label, enemies, npcs, props, collectibles } ],
      caveMouths: [ { pos: "play-north-center", label } ]
    }
  }
  layoutPlan must list EVERY prop, NPC, enemy, collectible, trail, and spawn with labels so the concept map is unambiguous.
- sessionConfig: object matching these keys:
  label, tileCount (81 or 289), randomSeedEachBuild, playDiskProps, hollowTitan, enhancementPhases,
  prePlacementResearch, sequentialTerrain, surfaceTerrainPasses, outerRingMountains, mountainLabyrinth,
  surfaceTrails, surfaceWater, agentInvokes, runPostBuildResearch, preBuildReloop,
  randomLandPlacement, floatingTiles, import3DObjects, proLevelWorld, use3DCaveSystem

Also maintain checklist — use ONLY these ids: play_disk, scope, props, npcs, enemies, puzzles, terrain, speed.
Mark done:true ONLY when the user explicitly answered that topic in chat (not pipeline defaults).
Use done:false and a short decision note when still open, e.g. "pending: labyrinth A vs B".
Never mark all items done at once unless each was truly answered. Generic filler like "NPCs included" is NOT a decision.
"checklist": [
  { "id": "play_disk", "label": "9 play tiles centered", "done": true, "decision": "always on" },
  { "id": "props", "label": "Props & scatter", "done": false, "decision": "" }
]

Respond JSON only:
{
  "assistantMessage": "markdown string shown in chat",
  "qnaComplete": false,
  "brief": null,
  "sessionConfig": null,
  "checklist": []
}
"""


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
)

CHECKLIST_ALIASES: dict[str, str] = {
    "world_layout": "scope",
    "tier": "speed",
    "character": "npcs",
    "content": "props",
    "labyrinth": "puzzles",
    "islands": "terrain",
    "play_disk_props": "props",
}

TOPIC_KEYWORDS: dict[str, tuple[str, ...]] = {
    "props": ("prop", "scatter", "bench", "tree", "decor", "vegetation"),
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
    {"id": "play_disk", "label": "9 play tiles centered (3×3)", "done": True, "decision": "always on"},
    {"id": "scope", "label": "Build scope & goals", "done": False, "decision": ""},
    {"id": "props", "label": "Props & scatter", "done": False, "decision": ""},
    {"id": "npcs", "label": "NPCs & characters", "done": False, "decision": ""},
    {"id": "enemies", "label": "Enemies & combat", "done": False, "decision": ""},
    {"id": "puzzles", "label": "Puzzles / interactables", "done": False, "decision": ""},
    {"id": "terrain", "label": "Mountains / water / labyrinth", "done": False, "decision": ""},
    {"id": "speed", "label": "Speed vs quality tier", "done": False, "decision": ""},
]


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


def _note_blocks_done(note: str) -> bool:
    low = (note or "").lower()
    return any(m in low for m in ("pending", "not yet", "tbd", "not yet placed", "not yet labeled"))


def _sanitize_checklist(doc: dict[str, Any]) -> None:
    """Only mark topics done when the user actually addressed them in chat."""
    by_id: dict[str, dict[str, Any]] = {c["id"]: dict(c) for c in DEFAULT_CHECKLIST}
    pending_notes: dict[str, str] = {}
    blocked: dict[str, str] = {}

    for item in doc.get("checklist") or []:
        cid = _canonical_id(str(item.get("id", "")))
        if cid and item.get("done") is False:
            dec = str(item.get("decision") or "").strip()
            if dec:
                blocked[cid] = dec[:200]

    for item in doc.get("checklist") or []:
        cid = _canonical_id(str(item.get("id", "")))
        if not cid:
            continue
        prev = by_id[cid]
        dec = str(item.get("decision") or "").strip()
        vague = _decision_is_vague(dec, cid)
        if dec and not vague and len(dec) >= len(str(prev.get("decision") or "")):
            prev["decision"] = dec[:200]
            if item.get("done"):
                prev["done"] = True
        elif dec and vague:
            if not prev.get("decision") or _decision_is_vague(str(prev.get("decision")), cid):
                prev["decision"] = dec[:200]
            prev["done"] = False
            pending_notes[cid] = dec[:200]
        elif item.get("done") is False:
            prev["done"] = False
            if dec:
                prev["decision"] = dec[:200]
                pending_notes[cid] = dec[:200]

    by_id["play_disk"]["done"] = True
    by_id["play_disk"]["decision"] = by_id["play_disk"].get("decision") or "always on"

    for cid in CANONICAL_CHECKLIST_IDS:
        if cid == "play_disk":
            continue
        entry = by_id[cid]
        dec = str(entry.get("decision") or "").strip()
        mentioned = _topic_mentioned_by_user(doc, cid)

        if cid in blocked:
            entry["done"] = False
            entry["decision"] = blocked[cid]
            continue

        if cid in pending_notes:
            note = pending_notes[cid]
            entry["decision"] = note
            note_low = note.lower()
            if _note_blocks_done(note):
                entry["done"] = False
            elif mentioned and _decision_is_vague(note, cid):
                snippet = _snippet_for_topic(doc, cid)
                if snippet:
                    entry["decision"] = snippet[:200]
                    entry["done"] = True
                else:
                    entry["done"] = False
            else:
                entry["done"] = False
            continue

        if cid == "scope" and mentioned and (not dec or _decision_is_vague(dec, cid)):
            brief = doc.get("brief") or {}
            for candidate in (brief.get("summary"), _snippet_for_topic(doc, "scope") or _user_text(doc)):
                c = (candidate or "").strip()
                if len(c) > 25:
                    entry["decision"] = c[:200]
                    entry["done"] = True
                    break
            continue

        if not mentioned:
            entry["done"] = False
            if not dec or _decision_is_vague(dec, cid):
                entry["decision"] = ""
            continue

        if _decision_is_vague(dec, cid):
            snippet = _snippet_for_topic(doc, cid)
            if snippet:
                entry["decision"] = snippet[:200]
                entry["done"] = True
            else:
                entry["done"] = False
        else:
            entry["done"] = True

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
    by_id = {c["id"]: dict(c) for c in doc.get("checklist") or DEFAULT_CHECKLIST}
    for item in incoming:
        raw_id = item.get("id")
        if not raw_id:
            continue
        iid = _canonical_id(str(raw_id)) or str(raw_id)
        if iid not in by_id:
            continue
        prev = by_id[iid]
        prev["label"] = item.get("label") or prev.get("label", iid)
        if "done" in item:
            prev["done"] = bool(item["done"])
        if item.get("decision"):
            prev["decision"] = str(item["decision"])[:200]
        by_id[iid] = prev
    doc["checklist"] = list(by_id.values())
    _sanitize_checklist(doc)


def _awaiting_assistant_reply(doc: dict[str, Any]) -> bool:
    if doc.get("phase") != "qna":
        return False
    if doc.get("cursorWorking"):
        return False
    msgs = doc.get("messages") or []
    return bool(msgs) and msgs[-1].get("role") == "user"


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
    doc["cursorWorking"] = True
    _write_session(hub, doc)
    try:
        llm_msgs = [{"role": m["role"], "content": m["content"]} for m in doc.get("messages", [])]
        raw = _llm_messages(hub, SYSTEM_QNA, llm_msgs)
        parsed = _parse_llm_json(raw)
        assistant = parsed.get("assistantMessage") or "OK."
        doc.setdefault("messages", []).append({"role": "assistant", "content": assistant})
        _merge_checklist(doc, parsed.get("checklist"))

        if parsed.get("qnaComplete"):
            brief = parsed.get("brief") or {}
            if not brief.get("layoutPlan"):
                brief["layoutPlan"] = None  # derived at render time
            doc["brief"] = brief
            doc["sessionConfig"] = _normalize_config(parsed.get("sessionConfig") or {})
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
                            f"Concept map render hit a snag ({ex}). Your plan is saved — "
                            "reply **retry concept** or send one small tweak and I'll regenerate the map."
                        ),
                    }
                )
        else:
            doc["phase"] = "qna"
    finally:
        doc["cursorWorking"] = False
        _write_session(hub, doc)

    return public_session(hub)


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
    doc["conceptImageTs"] = int(datetime.now(timezone.utc).timestamp())


def approve_concept(hub: Path, approved: bool, feedback: str = "") -> dict[str, Any]:
    doc = _read_session(hub)
    if doc.get("phase") != "awaiting_concept_approval":
        raise RuntimeError("Not awaiting concept approval")

    if not approved:
        if feedback:
            doc.setdefault("messages", []).append({"role": "user", "content": f"Concept rejected: {feedback}"})
        doc["phase"] = "qna"
        _write_session(hub, doc)
        return chat_turn(hub, feedback or "Please revise the concept.")

    doc["phase"] = "research" if doc.get("internetResearch") else "plan"
    if doc.get("internetResearch"):
        run_research(hub)
        doc = _read_session(hub)
    else:
        doc["planSummary"] = _compile_plan_summary(doc)
        doc["phase"] = "awaiting_plan_approval"
        _write_session(hub, doc)
    return public_session(hub)


def _cursor_research(hub: Path, doc: dict[str, Any]) -> dict[str, Any]:
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

    brief = doc.get("brief") or {}
    queries = brief.get("researchQueries") or [
        "procedural terrain game design playable demo",
        "unity terrain tile grid open world best practices",
    ]
    payload = {
        "hubRoot": str(hub),
        "brief": brief,
        "sessionConfig": doc.get("sessionConfig") or {},
        "queries": queries[:5],
    }
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as tmp:
        json.dump(payload, tmp)
        req_path = tmp.name

    try:
        env = _augment_path_env()
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
            raise RuntimeError(proc.stderr or "Cursor research returned no output")
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
            raise RuntimeError("Cursor research returned non-JSON output")
        if data.get("error"):
            raise RuntimeError(str(data["error"]))
        items = data.get("items") or []
        if not items:
            raise RuntimeError("Cursor research returned no sources")
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

    brief = doc.get("brief") or {}
    default_queries = brief.get("researchQueries") or [
        "procedural terrain game design playable demo",
        "unity terrain tile grid open world best practices",
    ]
    try:
        bundle = _cursor_research(hub, doc)
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
        env=_augment_path_env(),
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
        f"- Tiles: {cfg.get('tileCount', 81)} (9 play tiles always centered)",
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
    return "\n".join(lines)


def approve_plan(hub: Path, approved: bool) -> dict[str, Any]:
    doc = _read_session(hub)
    if doc.get("phase") != "awaiting_plan_approval":
        raise RuntimeError("Not awaiting plan approval")
    if not approved:
        doc["phase"] = "qna"
        _write_session(hub, doc)
        return public_session(hub)

    cfg = _normalize_config(doc.get("sessionConfig") or {})
    cfg["label"] = (doc.get("brief") or {}).get("title") or cfg.get("label")
    cfg["finalizedUtc"] = _utc()
    cfg["plannerSession"] = True
    cfg = _normalize_config(cfg)

    active = hub / ACTIVE_REL
    active.parent.mkdir(parents=True, exist_ok=True)
    active.write_text(json.dumps(cfg, indent=2) + "\n", encoding="utf-8")

    brief_out = {
        "planSummary": doc.get("planSummary"),
        "brief": doc.get("brief"),
        "conceptImageRel": doc.get("conceptImageRel"),
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

    state = {"phase": "finalized", "hubRoot": str(hub), "cancelled": False, "planner": True}
    (hub / STATE_REL).write_text(json.dumps(state, indent=2) + "\n", encoding="utf-8")

    doc["phase"] = "finalized"
    _write_session(hub, doc)
    _activate_unity_editor()
    return public_session(hub)


def public_session(hub: Path) -> dict[str, Any]:
    doc = _read_session(hub)
    if doc:
        if _clear_stale_cursor_working(doc):
            _write_session(hub, doc)
        _sanitize_checklist(doc)
    if doc.get("phase") not in (None, "qna", "cancelled") and doc.get("sessionConfig"):
        _backfill_layout_plan(doc)
    out = dict(doc)
    out["awaitingAssistantReply"] = _awaiting_assistant_reply(doc)
    out["llmProvider"] = "cursor"
    if doc.get("conceptImageRel"):
        rel = doc["conceptImageRel"]
        ts = doc.get("conceptImageTs") or doc.get("updatedUtc", "")
        out["conceptImageUrl"] = (
            f"/api/planner/asset?hub={urllib.parse.quote(str(hub))}"
            f"&rel={urllib.parse.quote(rel)}&t={urllib.parse.quote(str(ts))}"
        )
    out["cursorWorking"] = bool(doc.get("cursorWorking"))
    if "checklist" not in out:
        out["checklist"] = [dict(c) for c in DEFAULT_CHECKLIST]
    return out


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
