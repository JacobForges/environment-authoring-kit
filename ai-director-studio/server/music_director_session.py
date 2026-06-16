#!/usr/bin/env python3
"""Music Director session — pop/trap track Q&A before production pipeline."""
from __future__ import annotations

import json
import re
import sys
import threading
import time as time_mod
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

TOOLS = Path(__file__).resolve().parent
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

import build_planner as planner  # noqa: E402
import music_project  # noqa: E402
from director_paths import atomic_write_text, studio_repo_root  # noqa: E402

SESSION_NAME = "MusicDirectorSession.json"
CHECKLIST_VERSION = 1
CHAT_DEDUPE_SEC = 4.0
STALE_ASSISTANT_SEC = 180.0

_ACTION_LOCKS: dict[str, threading.Lock] = {}
_ACTION_LOCK_META = threading.Lock()
_LAST_USER_CHAT: dict[str, tuple[str, float]] = {}

DEFAULT_CHECKLIST: list[dict[str, Any]] = [
    {
        "id": "genre",
        "category": "Concept",
        "label": "Genre & subgenre",
        "done": False,
        "value": "",
        "teach": "Pop/trap hybrids need a clear lane — dark trap, melodic pop, hyperpop. Genre drives BPM range and instrumental prompt.",
    },
    {
        "id": "mood",
        "category": "Concept",
        "label": "Mood & emotional tone",
        "done": False,
        "value": "",
        "teach": "Mood is the listener's feeling: melancholic, hype, romantic, angry. It colors lyrics, vocal delivery, and mix darkness.",
    },
    {
        "id": "working_title",
        "category": "Concept",
        "label": "Working title",
        "done": False,
        "value": "",
        "teach": "A working title anchors the hook and export filename — even 'untitled trap demo' helps the pipeline stay organized.",
    },
    {
        "id": "bpm",
        "category": "Beat",
        "label": "Target BPM",
        "done": False,
        "value": "",
        "teach": "Trap lives 130–150; pop ballads 80–110. BPM locks karaoke timing and autotune feel.",
    },
    {
        "id": "key",
        "category": "Beat",
        "label": "Musical key",
        "done": False,
        "value": "",
        "teach": "Key + scale guide pitch correction and instrumental generation. Use 'auto' if unsure — we detect from the beat later.",
    },
    {
        "id": "scale",
        "category": "Beat",
        "label": "Major or minor",
        "done": False,
        "value": "",
        "teach": "Minor = darker trap/pop; major = brighter hooks. Matches mood and vocal melody choices.",
    },
    {
        "id": "instrumental_style",
        "category": "Beat",
        "label": "Instrumental sonic palette",
        "done": False,
        "value": "",
        "teach": "808 slide, pluck arps, ambient pads, guitar loops — describe the beat texture for AI generation or upload matching.",
    },
    {
        "id": "instrumental_prompt",
        "category": "Beat",
        "label": "AI instrumental prompt",
        "done": False,
        "value": "",
        "teach": "Full Suno-style prompt: genre, BPM, instruments, reference artists. This string generates the backing track.",
    },
    {
        "id": "lyrics_text",
        "category": "Vocals",
        "label": "Lyrics (full or hook outline)",
        "done": False,
        "value": "",
        "teach": "Paste verses or describe the hook — karaoke needs line breaks. You can refine during record.",
    },
    {
        "id": "vocal_style",
        "category": "Vocals",
        "label": "Vocal delivery style",
        "done": False,
        "value": "",
        "teach": "Melodic sing, half-sung rap, whisper, belting — sets autotune amount and mix compression.",
    },
    {
        "id": "performance_vibe",
        "category": "Vocals",
        "label": "Performance energy",
        "done": False,
        "value": "",
        "teach": "Bedroom intimate vs stadium hype — affects reverb, doubles, and ad-lib layers in produce.",
    },
    {
        "id": "autotune_intent",
        "category": "Production",
        "label": "Autotune & polish level",
        "done": False,
        "value": "",
        "teach": "Transparent pitch fix vs obvious T-Pain effect. 'Sing bad, sound pro' still needs a target polish.",
    },
    {
        "id": "mix_vibe",
        "category": "Production",
        "label": "Mix reference & sonic goal",
        "done": False,
        "value": "",
        "teach": "Reference track or adjectives: wide, punchy vocal, crunchy 808, streaming loudness — guides the produce chain.",
    },
    {
        "id": "music_video_intent",
        "category": "Video",
        "label": "Music video handoff",
        "done": False,
        "value": "",
        "teach": "Yes/no plus concept: performance clip, lyric video, AI Director recap from webcam — unlocks optional video step after master.",
    },
]

CHECKLIST_IDS: tuple[str, ...] = tuple(c["id"] for c in DEFAULT_CHECKLIST)
CHECKLIST_CATEGORIES: tuple[str, ...] = tuple(
    dict.fromkeys(c.get("category", "General") for c in DEFAULT_CHECKLIST)
)

BRIEFING_PHASES = frozenset({"qna", "briefing_ready"})
PRODUCTION_PHASES = frozenset({"instrumental", "record", "produce", "complete"})

MUSIC_RESPONDER_FALLBACKS: dict[str, str] = {
    "genre": "Dark pop trap — melodic hooks over 808s, Drake-adjacent but more underground.",
    "mood": "Moody late-night, confident but vulnerable — not fully sad, not fully hype.",
    "working_title": "Midnight Loop (working title).",
    "bpm": "140 BPM — classic trap pocket with room for half-time sections.",
    "key": "F minor (or auto-detect from instrumental).",
    "scale": "Minor — darker trap feel.",
    "instrumental_style": "Sliding 808, sparse piano plucks, airy pad, tight hi-hats.",
    "instrumental_prompt": "dark pop trap 140 bpm, moody 808s, minor key, catchy hook space, no vocals",
    "lyrics_text": "Hook outline: late nights on the loop / say you care but never prove / I'm done with empty promises",
    "vocal_style": "Melodic rap-sing — half spoken verses, sung hook.",
    "performance_vibe": "Intimate bedroom take, confident delivery, not shouting.",
    "autotune_intent": "Obvious but musical autotune on hook; transparent fix on verses.",
    "mix_vibe": "Wide vocal, punchy 808, streaming-ready (-14 LUFS), reference: early Juice WRLD demos.",
    "music_video_intent": "Yes — performance clip from webcam karaoke session; optional handoff to AI Director for recap edit.",
}


def _action_lock(project: Path) -> threading.Lock:
    key = str(project.expanduser().resolve())
    with _ACTION_LOCK_META:
        if key not in _ACTION_LOCKS:
            _ACTION_LOCKS[key] = threading.Lock()
        return _ACTION_LOCKS[key]


def _normalize_chat(text: str) -> str:
    return " ".join((text or "").split())


def _recent_duplicate_chat(project: Path, message: str) -> bool:
    key = str(project.expanduser().resolve())
    norm = _normalize_chat(message)
    if not norm:
        return True
    now = time_mod.time()
    prev = _LAST_USER_CHAT.get(key)
    if prev and prev[0] == norm and (now - prev[1]) < CHAT_DEDUPE_SEC:
        return True
    _LAST_USER_CHAT[key] = (norm, now)
    return False


def _utc() -> str:
    return datetime.now(timezone.utc).isoformat()


def _cave_grader_tools(hub: Path) -> Path:
    return hub.expanduser().resolve() / "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader"


def _import_wizard_tab_research(hub: Path):
    tools = _cave_grader_tools(hub)
    if str(tools) not in sys.path and tools.is_dir():
        sys.path.insert(0, str(tools))
    import wizard_tab_research as wtr  # noqa: PLC0415

    return wtr


def _run_music_research(hub: Path, doc: dict[str, Any], user_message: str = "") -> None:
    try:
        wtr = _import_wizard_tab_research(hub)
        wtr.maybe_run_session_research(hub, "music", doc, user_message)
    except Exception as exc:
        doc["researchBundle"] = {"tabId": "music", "items": [], "error": str(exc)}
        doc["researchError"] = str(exc)


def session_path(project: Path) -> Path:
    return project / SESSION_NAME


def _hub_for_project(project: Path) -> Path:
    env = __import__("os").environ.get("STUDIO_ROOT", "").strip()
    if env:
        return Path(env).expanduser()
    return studio_repo_root()


def _read_session(project: Path) -> dict[str, Any] | None:
    p = session_path(project)
    if not p.is_file():
        return None
    for _ in range(3):
        try:
            return json.loads(p.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            time_mod.sleep(0.05)
    return None


def _write_session(project: Path, doc: dict[str, Any]) -> None:
    doc["updatedAt"] = _utc()
    atomic_write_text(session_path(project), json.dumps(doc, indent=2))


def _stream_persist(project: Path):
    def persist(doc: dict[str, Any]) -> None:
        doc.pop("cursorWorking", None)
        _write_session(project, doc)

    return persist


def _template_by_id() -> dict[str, dict[str, Any]]:
    return {str(c["id"]): c for c in DEFAULT_CHECKLIST}


def _ensure_checklist(doc: dict[str, Any]) -> bool:
    templates = _template_by_id()
    changed = False
    if "checklist" not in doc:
        doc["checklist"] = [dict(c) for c in DEFAULT_CHECKLIST]
        changed = True
    existing_ids = {str(c.get("id")) for c in doc["checklist"] if c.get("id")}
    for item in doc["checklist"]:
        iid = str(item.get("id") or "")
        tpl = templates.get(iid)
        if not tpl:
            continue
        for key in ("teach", "label", "category"):
            if not item.get(key) and tpl.get(key):
                item[key] = tpl[key]
                changed = True
    for tid, tpl in templates.items():
        if tid not in existing_ids:
            doc["checklist"].append(dict(tpl))
            changed = True
    order = {str(c["id"]): i for i, c in enumerate(DEFAULT_CHECKLIST)}
    doc["checklist"].sort(key=lambda x: order.get(str(x.get("id")), 999))
    if int(doc.get("checklistVersion") or 0) < CHECKLIST_VERSION:
        doc["checklistVersion"] = CHECKLIST_VERSION
        changed = True
    return changed


def _checklist_all_done(doc: dict[str, Any]) -> bool:
    _ensure_checklist(doc)
    return all(bool(c.get("done")) for c in doc["checklist"])


def _merge_checklist(doc: dict[str, Any], items: list | None) -> None:
    if not items:
        return
    by_id = {c["id"]: c for c in doc.get("checklist", [])}
    for item in items:
        if not isinstance(item, dict) or not item.get("id"):
            continue
        cid = str(item["id"])
        if cid in by_id:
            if "done" in item:
                by_id[cid]["done"] = bool(item["done"])
            if item.get("value") is not None:
                by_id[cid]["value"] = str(item["value"])


def _next_pending_checklist_id(doc: dict[str, Any]) -> str | None:
    _ensure_checklist(doc)
    for item in doc["checklist"]:
        if not item.get("done"):
            return str(item.get("id") or "")
    return None


def _label_for_checklist_id(doc: dict[str, Any], checklist_id: str) -> str:
    for item in doc.get("checklist") or []:
        if str(item.get("id")) == checklist_id:
            return str(item.get("label") or checklist_id)
    for item in DEFAULT_CHECKLIST:
        if str(item.get("id")) == checklist_id:
            return str(item.get("label") or checklist_id)
    return checklist_id


def _heal_stale_assistant(doc: dict[str, Any]) -> bool:
    healed = False
    for flag in ("assistantWorking", "responderWorking"):
        if doc.get(flag):
            updated = str(doc.get("updatedAt") or doc.get("createdAt") or "")
            try:
                ts = datetime.fromisoformat(updated.replace("Z", "+00:00"))
                age = (datetime.now(timezone.utc) - ts).total_seconds()
            except (TypeError, ValueError):
                age = 0.0
            if age > STALE_ASSISTANT_SEC:
                doc[flag] = False
                if flag == "assistantWorking":
                    doc.pop("streamingText", None)
                    doc.pop("streamingRole", None)
                healed = True
    return healed


def _blocked_session(project: Path, *, duplicate: bool = False, reason: str = "") -> dict[str, Any]:
    out = public_session(project)
    out["inputBlocked"] = True
    out["duplicateRequest"] = duplicate
    if reason:
        out["blockReason"] = reason
    return out


def _parse_llm_json(raw: str) -> dict[str, Any]:
    text = raw.strip()
    if text.startswith("```"):
        text = re.sub(r"^```(?:json)?\s*", "", text)
        text = re.sub(r"\s*```$", "", text)
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return {"assistantMessage": text}


def _checklist_value(doc: dict[str, Any], item_id: str) -> str:
    for item in doc.get("checklist") or []:
        if str(item.get("id")) == item_id:
            return str(item.get("value") or "").strip()
    return ""


def _parse_bpm(text: str) -> int | None:
    m = re.search(r"\b(\d{2,3})\b", text)
    if m:
        val = int(m.group(1))
        if 60 <= val <= 220:
            return val
    return None


def _parse_key_scale(key_text: str, scale_text: str) -> tuple[str, str]:
    key = (key_text or "auto").strip()
    scale = (scale_text or "minor").strip().lower()
    if scale not in ("major", "minor"):
        scale = "minor"
    key_upper = key.upper().replace("♯", "#").replace("♭", "b")
    valid = {"AUTO", "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"}
    if key_upper == "AUTO" or not key:
        return "auto", scale
    if key_upper in valid:
        return key_upper if "#" in key_upper else key_upper[0], scale
    return "auto", scale


def _ensure_music_project_on_disk(project: Path, name: str | None = None) -> None:
    if music_project.read_music_project(project):
        return
    music_project.music_dir(project)
    music_project.write_music_project(
        project,
        {
            "projectKind": "music",
            "name": name or project.name,
            "key": "auto",
            "scale": "minor",
            "bpm": None,
            "instrumentalPrompt": "",
            "lyrics": "",
            "lyricsFormat": "lines",
            "createdAt": _utc(),
            "status": music_project.default_status(),
        },
    )


def apply_brief_to_music_project(project: Path, doc: dict[str, Any] | None = None) -> dict[str, Any]:
    """Sync checklist answers into MusicProject.json for production steps."""
    doc = doc or _read_session(project) or {}
    _ensure_music_project_on_disk(project, doc.get("brief", {}).get("title") or project.name)
    brief = doc.get("brief") or {}
    key, scale = _parse_key_scale(_checklist_value(doc, "key"), _checklist_value(doc, "scale"))
    bpm = _parse_bpm(_checklist_value(doc, "bpm")) or brief.get("bpm")
    lyrics = _checklist_value(doc, "lyrics_text") or brief.get("lyrics") or ""
    prompt = (
        _checklist_value(doc, "instrumental_prompt")
        or _checklist_value(doc, "instrumental_style")
        or brief.get("instrumentalPrompt")
        or ""
    )
    title = _checklist_value(doc, "working_title") or brief.get("title") or project.name
    patch: dict[str, Any] = {
        "name": title,
        "key": key,
        "scale": scale,
        "bpm": bpm,
        "instrumentalPrompt": prompt,
        "lyrics": lyrics,
        "lyricsFormat": "lines",
        "brief": {
            "genre": _checklist_value(doc, "genre"),
            "mood": _checklist_value(doc, "mood"),
            "vocalStyle": _checklist_value(doc, "vocal_style"),
            "performanceVibe": _checklist_value(doc, "performance_vibe"),
            "autotuneIntent": _checklist_value(doc, "autotune_intent"),
            "mixVibe": _checklist_value(doc, "mix_vibe"),
            "musicVideoIntent": _checklist_value(doc, "music_video_intent"),
        },
    }
    return music_project.update_music_project(project, patch)


def _build_qna_system(hub: Path, doc: dict[str, Any]) -> str:
    next_id = _next_pending_checklist_id(doc) or "none"
    next_item = next(
        (c for c in doc.get("checklist", []) if str(c.get("id")) == next_id),
        None,
    )
    by_category: dict[str, list[str]] = {}
    for c in DEFAULT_CHECKLIST:
        cat = str(c.get("category") or "General")
        by_category.setdefault(cat, []).append(str(c["id"]))

    injection = ""
    try:
        wtr = _import_wizard_tab_research(hub)
        injection = wtr.build_prompt_injection("music", doc)
    except Exception:
        injection = (
            "## Tab scope guard (strict)\n"
            "**Scope:** Music production ONLY — genre, BPM, instrumental prompt, vocals, mix.\n"
            "**Do NOT plan:** terrain tiles, cave structure, NPC placement, video terrain content.\n"
        )

    return (
        "You are the **Music Director** — Grammy-caliber vocal producer mentor for pop/trap bedroom artists.\n"
        "Guide the user through track decisions before instrumental generation, karaoke record, and autotune/mix.\n"
        "Every reply MUST teach something real (2–4 sentences) before the next question.\n\n"
        f"{injection}\n\n"
        "## Interview rules (strict while musicQnaComplete is false)\n"
        "- Work through the checklist IN ORDER — one id per turn (next pending below).\n"
        "- Ask ONE focused question tied to that id's label and teaching hint.\n"
        "- When the user answers clearly, mark ONLY that id done:true with a specific value.\n"
        "- For lyrics_text: capture hook or full lyrics with line breaks when provided.\n"
        "- For instrumental_prompt: synthesize genre, BPM, mood, and sonic palette into one Suno-ready string.\n"
        "- Set musicQnaComplete:true ONLY when every checklist item is done OR user says start production.\n"
        "- Never mark items done without a real user answer.\n"
        "- Redirect terrain/world/NPC/video requests to Environment Kit wizard tabs — music only here.\n\n"
        f"Checklist categories: {json.dumps(by_category)}\n"
        f"All checklist ids ({len(CHECKLIST_IDS)}): {', '.join(CHECKLIST_IDS)}\n"
        f"Next pending id: {next_id}"
        + (f" ({next_item.get('label')})" if next_item else "")
        + "\n"
        f"Current checklist: {json.dumps(doc.get('checklist', []))}\n\n"
        "Respond JSON only:\n"
        '{"assistantMessage":"markdown for chat","teachingMoment":"optional aside",'
        '"checklist":[{"id":"<id>","done":true,"value":"specific answer"}],'
        '"musicQnaComplete":false,'
        '"brief":{"genre":"","mood":"","bpm":0,"key":"","scale":"","instrumentalPrompt":"",'
        '"lyrics":"","vocalStyle":"","musicVideoIntent":""}}\n'
        "Never mention vendor AI product names. Say Assistant or Music Director."
    )


def public_session(project: Path) -> dict[str, Any]:
    doc = _read_session(project)
    if not doc:
        return {"ok": False, "error": "no session", "project": str(project)}
    if _heal_stale_assistant(doc):
        _write_session(project, doc)
    if _ensure_checklist(doc):
        _write_session(project, doc)
    phase = str(doc.get("phase", "qna"))
    checklist_done = _checklist_all_done(doc)
    stream_text, stream_role = planner._public_streaming(doc)  # noqa: SLF001
    messages_out: list[dict[str, Any]] = []
    for m in doc.get("messages", []):
        entry = dict(m)
        content = str(entry.get("content") or "")
        if content.strip().startswith("{"):
            entry["content"] = planner._sanitize_chat_content(content)  # noqa: SLF001
        messages_out.append(entry)
    return {
        "ok": True,
        "project": str(project),
        "projectKind": "music",
        "phase": phase,
        "messages": messages_out,
        "checklist": doc.get("checklist", []),
        "brief": doc.get("brief", {}),
        "streamingText": stream_text or "",
        "streamingRole": stream_role,
        "assistantWorking": bool(doc.get("assistantWorking")),
        "responderWorking": bool(doc.get("responderWorking")),
        "inputBlocked": bool(doc.get("assistantWorking") or doc.get("responderWorking")),
        "duplicateRequest": False,
        "checklistComplete": checklist_done,
        "musicQnaComplete": bool(doc.get("musicQnaComplete")) or checklist_done,
        "checklistTotal": len(doc.get("checklist") or []),
        "checklistDone": sum(1 for c in (doc.get("checklist") or []) if c.get("done")),
        "checklistVersion": int(doc.get("checklistVersion") or CHECKLIST_VERSION),
        "internetResearch": bool(doc.get("internetResearch")),
        "categories": list(CHECKLIST_CATEGORIES),
        "inBriefing": phase in BRIEFING_PHASES,
        "inProduction": phase in PRODUCTION_PHASES,
        "productionStep": doc.get("productionStep") or "instrumental",
    }


def get_session(project: Path) -> dict[str, Any]:
    project = project.expanduser().resolve()
    if not project.is_dir():
        raise FileNotFoundError(f"project folder not found: {project}")
    if not _read_session(project):
        return {
            "ok": False,
            "noSession": True,
            "project": str(project),
            "checklistTotal": len(DEFAULT_CHECKLIST),
            "checklistDone": 0,
        }
    return public_session(project)


def start_session(
    project: Path | None = None,
    *,
    name: str | None = None,
    internet_research: bool = True,
) -> dict[str, Any]:
    if project is None:
        project, _meta = music_project.create_music_project(name or "track")
    else:
        project = project.expanduser().resolve()
        if not project.is_dir():
            raise FileNotFoundError(f"project folder not found: {project}")
        _ensure_music_project_on_disk(project, name or project.name)

    existing = _read_session(project)
    if existing:
        return public_session(project)

    n_items = len(DEFAULT_CHECKLIST)
    doc: dict[str, Any] = {
        "version": 1,
        "checklistVersion": CHECKLIST_VERSION,
        "projectKind": "music",
        "project": str(project),
        "phase": "qna",
        "productionStep": "instrumental",
        "internetResearch": bool(internet_research),
        "researchBundle": None,
        "createdAt": _utc(),
        "messages": [
            {
                "role": "assistant",
                "content": (
                    f"Welcome to **music pre-production** — {n_items} decisions before we generate your beat, "
                    "run karaoke, and polish vocals.\n\n"
                    "I'll teach one pro concept per question. Work the checklist on the right — "
                    "Concept → Beat → Vocals → Production → Video.\n\n"
                    "When everything is checked, we auto-advance through **instrumental → record → produce** "
                    "(and optional music video).\n\n"
                    "**First question (Concept — genre):** what genre and subgenre are we aiming for — "
                    "dark pop trap, melodic rap, hyperpop, R&B trap, or something else?"
                ),
            }
        ],
        "checklist": [dict(c) for c in DEFAULT_CHECKLIST],
        "brief": {},
        "musicQnaComplete": False,
    }
    _write_session(project, doc)
    return public_session(project)


def chat_turn(
    project: Path,
    user_message: str | None = None,
    bootstrap: bool = False,
    *,
    _skip_action_lock: bool = False,
    user_from_responder: bool = False,
) -> dict[str, Any]:
    hub = _hub_for_project(project)
    planner.load_dotenv(hub)
    lock = _action_lock(project)
    acquired = _skip_action_lock or lock.acquire(blocking=False)
    if not _skip_action_lock and not acquired:
        return _blocked_session(
            project,
            duplicate=True,
            reason="Music Director is processing your last message — please wait.",
        )
    try:
        doc = _read_session(project)
        if not doc:
            start_session(project)
            doc = _read_session(project)
        assert doc is not None
        if _heal_stale_assistant(doc):
            _write_session(project, doc)
        if user_message and not bootstrap:
            if doc.get("assistantWorking"):
                return _blocked_session(
                    project,
                    duplicate=True,
                    reason="Assistant is still working on your last message.",
                )
            if _recent_duplicate_chat(project, user_message):
                return _blocked_session(
                    project,
                    duplicate=True,
                    reason="Duplicate message ignored — wait a moment before resending.",
                )
        return _chat_turn_locked(
            project, hub, doc, user_message, bootstrap, user_from_responder=user_from_responder
        )
    finally:
        if not _skip_action_lock and acquired:
            lock.release()


def _chat_turn_locked(
    project: Path,
    hub: Path,
    doc: dict[str, Any],
    user_message: str | None,
    bootstrap: bool,
    *,
    user_from_responder: bool = False,
) -> dict[str, Any]:
    if doc.get("phase") not in BRIEFING_PHASES:
        if user_message:
            doc.setdefault("messages", []).append({"role": "user", "content": user_message})
            doc.setdefault("messages", []).append(
                {
                    "role": "assistant",
                    "content": (
                        f"We're in **{doc.get('phase')}** — use the production steps to finish your track."
                    ),
                }
            )
            _write_session(project, doc)
        return public_session(project)

    if user_message and not bootstrap:
        entry: dict[str, Any] = {"role": "user", "content": user_message}
        if user_from_responder:
            entry["fromResponder"] = True
        doc.setdefault("messages", []).append(entry)

    if doc.get("internetResearch") and not doc.get("researchBundle"):
        doc["assistantWorking"] = True
        _write_session(project, doc)
        try:
            user_msg = str(user_message or "")
            if not user_msg:
                for m in reversed(doc.get("messages") or []):
                    if m.get("role") == "user":
                        user_msg = str(m.get("content") or "")
                        break
            _run_music_research(hub, doc, user_msg)
        finally:
            doc["assistantWorking"] = False

    doc["assistantWorking"] = True
    _write_session(project, doc)
    try:
        llm_msgs = [{"role": m["role"], "content": m["content"]} for m in doc.get("messages", [])]
        raw = planner._llm_messages(  # noqa: SLF001
            hub,
            _build_qna_system(hub, doc),
            llm_msgs,
            stream_doc=doc,
            stream_role="assistant",
            stream_persist=_stream_persist(project),
        )
        parsed = _parse_llm_json(raw)
        assistant = planner._sanitize_chat_content(str(parsed.get("assistantMessage") or "Got it."))  # noqa: SLF001
        doc.setdefault("messages", []).append({"role": "assistant", "content": assistant})
        _merge_checklist(doc, parsed.get("checklist"))
        brief = parsed.get("brief")
        if isinstance(brief, dict):
            doc["brief"] = {**doc.get("brief", {}), **{k: v for k, v in brief.items() if v}}

        qna_done = bool(parsed.get("musicQnaComplete")) and _checklist_all_done(doc)
        if qna_done:
            doc["musicQnaComplete"] = True
            doc["phase"] = "briefing_ready"
            apply_brief_to_music_project(project, doc)
            doc.setdefault("messages", []).append(
                {
                    "role": "assistant",
                    "content": (
                        "Track brief is **complete** — config saved to your music project. "
                        "Click **Start production** to generate the instrumental and move into karaoke record."
                    ),
                }
            )
    except Exception as exc:
        doc.setdefault("messages", []).append(
            {
                "role": "assistant",
                "content": (
                    f"Assistant unavailable ({exc}). "
                    f"Set CURSOR_API_KEY in {planner.studio_env_path().resolve()} and restart."
                ),
            }
        )
    finally:
        doc["assistantWorking"] = False
        doc.pop("streamingText", None)
        doc.pop("streamingRole", None)
        _write_session(project, doc)
    return public_session(project)


def _generate_music_responder_reply(
    project: Path,
    hub: Path,
    doc: dict[str, Any],
    *,
    attempt: int = 0,
    blocked_replies: list[str] | None = None,
) -> str:
    next_id = _next_pending_checklist_id(doc) or "genre"
    retry_block = ""
    if blocked_replies:
        retry_block = (
            "\n## REJECTED — write a completely NEW answer:\n"
            + "\n".join(f"- REJECTED: {b.replace(chr(10), ' ')[:200]}" for b in blocked_replies[-3:])
            + f"\n\nRegeneration attempt {attempt + 1}: answer ONLY [{next_id}]."
        )
    elif attempt > 0:
        retry_block = f"\nRegeneration attempt {attempt + 1}: vary wording.\n"

    system = (
        "You simulate the USER answering the Music Director's pop/trap track checklist.\n"
        "Output **plain text only** — no JSON, no role labels. Write 2–5 sentences as if the user typed in chat.\n\n"
        f"## Rules\n- Answer ONLY the Director's last message.\n"
        f"- Address checklist topic **[{next_id}]** ({_label_for_checklist_id(doc, next_id)}).\n"
        f"{planner._format_prior_replies_block(doc)}\n"  # noqa: SLF001
        f"{retry_block}"
    )
    director_msg = planner._last_assistant_message(doc) or "Ask the first checklist question."  # noqa: SLF001
    user_prompt = (
        f"Director's last message (answer THIS only):\n{director_msg}\n\n"
        f"Checklist topic: [{next_id}] — {_label_for_checklist_id(doc, next_id)}\n\n"
        "Write one unique user reply for a dark pop trap bedroom track."
    )
    return planner._llm_messages(hub, system, [{"role": "user", "content": user_prompt}], json_response=False).strip()  # noqa: SLF001


def _generate_music_responder_reply_deduped(project: Path, hub: Path, doc: dict[str, Any]) -> str:
    blocked: list[str] = []
    next_id = _next_pending_checklist_id(doc) or "genre"
    for attempt in range(planner.AUTO_REPLY_MAX_ATTEMPTS):  # noqa: SLF001
        reply = _generate_music_responder_reply(
            project, hub, doc, attempt=attempt, blocked_replies=blocked or None
        )
        is_dup, _ = planner._is_duplicate_auto_reply(reply, doc)  # noqa: SLF001
        if not is_dup and len(reply.strip()) >= 8:
            return reply
        blocked.append(reply)
    fallback = MUSIC_RESPONDER_FALLBACKS.get(
        next_id,
        f"Production decision for {_label_for_checklist_id(doc, next_id)}.",
    )
    is_dup, _ = planner._is_duplicate_auto_reply(fallback, doc)  # noqa: SLF001
    if is_dup:
        fallback = f"{fallback} (topic {next_id}, take {len(blocked) + 1})."
    return fallback


def responder_step(
    project: Path,
    preset: str = "bedroom-trap",
    *,
    max_turns: int = 1,
) -> dict[str, Any]:
    lock = _action_lock(project)
    if not lock.acquire(blocking=False):
        return {
            "ok": False,
            "error": "Music Director busy — wait for the current action to finish.",
            "session": _blocked_session(project, duplicate=True),
        }
    try:
        doc = _read_session(project)
        if not doc:
            start_session(project)
            doc = _read_session(project)
        assert doc is not None
        if doc.get("phase") not in BRIEFING_PHASES:
            return {"ok": True, "done": True, "session": public_session(project)}

        hub = _hub_for_project(project)
        planner.load_dotenv(hub)
        doc["responderWorking"] = True
        _write_session(project, doc)
        session: dict[str, Any] = public_session(project)
        turns_run = 0
        try:
            for _ in range(max_turns):
                doc = _read_session(project)
                if not doc or doc.get("phase") not in BRIEFING_PHASES:
                    break
                if doc.get("assistantWorking"):
                    for _wait in range(40):
                        time_mod.sleep(0.05)
                        doc = _read_session(project)
                        if not doc or not doc.get("assistantWorking"):
                            break
                    if doc and doc.get("assistantWorking"):
                        break
                reply = _generate_music_responder_reply_deduped(project, hub, doc)
                if not reply.strip():
                    break
                session = chat_turn(
                    project, reply, _skip_action_lock=True, user_from_responder=True
                )
                turns_run += 1
                if session.get("phase") != "qna" or session.get("checklistComplete"):
                    break
        finally:
            doc = _read_session(project) or {}
            doc["responderWorking"] = False
            _write_session(project, doc)
        done = session.get("phase") != "qna" or bool(session.get("checklistComplete"))
        return {"ok": True, "done": done, "turns": turns_run, "session": public_session(project)}
    finally:
        lock.release()


def responder_run_until_done(project: Path, preset: str = "bedroom-trap") -> dict[str, Any]:
    return responder_step(project, preset, max_turns=999)


def enter_production(project: Path, step: str = "instrumental") -> dict[str, Any]:
    doc = _read_session(project)
    if not doc:
        start_session(project)
        doc = _read_session(project)
    assert doc is not None
    if doc.get("phase") == "qna" and not _checklist_all_done(doc):
        raise RuntimeError("Complete the music brief checklist before starting production.")
    apply_brief_to_music_project(project, doc)
    doc["phase"] = "instrumental"
    doc["productionStep"] = step
    doc["musicQnaComplete"] = True
    _write_session(project, doc)
    return public_session(project)


def set_production_step(project: Path, step: str) -> dict[str, Any]:
    doc = _read_session(project)
    if not doc:
        raise FileNotFoundError("no music director session")
    doc["productionStep"] = step
    doc["phase"] = step if step in PRODUCTION_PHASES else doc.get("phase", "instrumental")
    _write_session(project, doc)
    return public_session(project)
