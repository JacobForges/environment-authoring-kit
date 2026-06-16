#!/usr/bin/env python3
"""AI Director session — cinematic brief Q&A, voice persona, assistant hooks."""
from __future__ import annotations

import json
import re
import subprocess
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
from envkit_paths import approved_dir, atomic_write_text  # noqa: E402

SESSION_NAME = "DirectorSession.json"
CHECKLIST_VERSION = 2
CHAT_DEDUPE_SEC = 4.0
STALE_ASSISTANT_SEC = 180.0

_ACTION_LOCKS: dict[str, threading.Lock] = {}
_ACTION_LOCK_META = threading.Lock()
_LAST_USER_CHAT: dict[str, tuple[str, float]] = {}


def _action_lock(capture: Path) -> threading.Lock:
    key = str(capture.expanduser().resolve())
    with _ACTION_LOCK_META:
        if key not in _ACTION_LOCKS:
            _ACTION_LOCKS[key] = threading.Lock()
        return _ACTION_LOCKS[key]


def _normalize_chat(text: str) -> str:
    return " ".join((text or "").split())


def _recent_duplicate_chat(capture: Path, message: str) -> bool:
    key = str(capture.expanduser().resolve())
    norm = _normalize_chat(message)
    if not norm:
        return True
    now = time_mod.time()
    prev = _LAST_USER_CHAT.get(key)
    if prev and prev[0] == norm and (now - prev[1]) < CHAT_DEDUPE_SEC:
        return True
    _LAST_USER_CHAT[key] = (norm, now)
    return False


def _heal_stale_assistant(doc: dict[str, Any]) -> bool:
    healed = False
    if doc.get("scriptAssistantWorking"):
        updated = str(doc.get("updatedAt") or doc.get("createdAt") or "")
        try:
            ts = datetime.fromisoformat(updated.replace("Z", "+00:00"))
            age = (datetime.now(timezone.utc) - ts).total_seconds()
        except (TypeError, ValueError):
            age = 0.0
        if age > STALE_ASSISTANT_SEC:
            doc["scriptAssistantWorking"] = False
            healed = True
    if doc.get("assistantWorking"):
        updated = str(doc.get("updatedAt") or doc.get("createdAt") or "")
        try:
            ts = datetime.fromisoformat(updated.replace("Z", "+00:00"))
            age = (datetime.now(timezone.utc) - ts).total_seconds()
        except (TypeError, ValueError):
            age = 0.0
        if age > STALE_ASSISTANT_SEC:
            doc["assistantWorking"] = False
            doc.pop("streamingText", None)
            doc.pop("streamingRole", None)
            healed = True
    if doc.get("responderWorking"):
        updated = str(doc.get("updatedAt") or doc.get("createdAt") or "")
        try:
            ts = datetime.fromisoformat(updated.replace("Z", "+00:00"))
            age = (datetime.now(timezone.utc) - ts).total_seconds()
        except (TypeError, ValueError):
            age = 0.0
        if age > STALE_ASSISTANT_SEC:
            doc["responderWorking"] = False
            healed = True
    return healed


def _blocked_session(capture: Path, *, duplicate: bool = False, reason: str = "") -> dict[str, Any]:
    out = public_session(capture)
    out["inputBlocked"] = True
    out["duplicateRequest"] = duplicate
    if reason:
        out["blockReason"] = reason
    return out

# Full AAA / broadcast-grade pre-production questionnaire (Environment Kit recap pipeline).
DEFAULT_CHECKLIST: list[dict[str, Any]] = [
    # —— Strategy & distribution ——
    {
        "id": "platform",
        "category": "Strategy",
        "label": "Primary platform",
        "done": False,
        "value": "",
        "teach": "Platform sets aspect ratio, length, and retention rules. YouTube favors depth; Shorts/TikTok need frame-one hooks; portfolio reels prioritize clarity over length.",
    },
    {
        "id": "aspect_ratio",
        "category": "Strategy",
        "label": "Aspect ratio & framing",
        "done": False,
        "value": "",
        "teach": "16:9 for YouTube/desktop; 9:16 for vertical social. Letterboxing in compose can add cinematic bars — decide before encode.",
    },
    {
        "id": "length",
        "category": "Strategy",
        "label": "Target runtime",
        "done": False,
        "value": "",
        "teach": "Runtime drives word count, hold durations, and act count. An 8-minute piece needs three acts; a 45-second Short is one beat.",
    },
    {
        "id": "audience",
        "category": "Strategy",
        "label": "Primary audience",
        "done": False,
        "value": "",
        "teach": "Studios scan for pipeline craft. Recruiters want a clear role story. Peers want honesty. Tailor vocabulary and pacing to who must be impressed.",
    },
    {
        "id": "success_metric",
        "category": "Strategy",
        "label": "Success metric",
        "done": False,
        "value": "",
        "teach": "One north star: hire-me reel, 1k subscribers, portfolio piece, or client pitch. Every creative choice should serve that metric.",
    },
    {
        "id": "working_title",
        "category": "Strategy",
        "label": "Working title & logline",
        "done": False,
        "value": "",
        "teach": "Title + one-sentence logline anchor script, cards, and YouTube metadata. Great titles promise transformation, not features.",
    },
    {
        "id": "cta",
        "category": "Strategy",
        "label": "Call to action",
        "done": False,
        "value": "",
        "teach": "One explicit ask at the end: follow, repo, hire, course link. Outro card and final narration line must match.",
    },
    {
        "id": "chapters_endscreen",
        "category": "Strategy",
        "label": "Chapters & end-screen plan",
        "done": False,
        "value": "",
        "teach": "YouTube chapters boost retention. Plan act breaks at milestone boundaries; end-screen points to subscribe or next video.",
    },
    # —— Story & structure ——
    {
        "id": "hook",
        "category": "Story",
        "label": "Opening hook (0–3 sec)",
        "done": False,
        "value": "",
        "teach": "Award-grade opens with motion + promise: cave reveal, grade jump, or a spoken line that creates curiosity. No logos first.",
    },
    {
        "id": "narrative_arc",
        "category": "Story",
        "label": "Three-act structure",
        "done": False,
        "value": "",
        "teach": "Act I: world promise. Act II: struggle/build. Act III: payoff. Map acts to surface → underground → finale in your capture.",
    },
    {
        "id": "emotional_peak",
        "category": "Story",
        "label": "Emotional peak / hero moment",
        "done": False,
        "value": "",
        "teach": "One shot the viewer remembers — longest hold, loudest line, or biggest visual change. Usually grading pass or cave mouth.",
    },
    {
        "id": "payoff",
        "category": "Story",
        "label": "Payoff & takeaway",
        "done": False,
        "value": "",
        "teach": "What the viewer believes after the last frame: 'I could build this', 'this world is playable', 'this person is hireable'.",
    },
    {
        "id": "stakes",
        "category": "Story",
        "label": "Stakes & tension",
        "done": False,
        "value": "",
        "teach": "Even devlogs need stakes: broken seams, tight deadline, first playable demo. Tension keeps mid-section from feeling like a slideshow.",
    },
    {
        "id": "wizard_chapter",
        "category": "Story",
        "label": "Wizard / planning footage role",
        "done": False,
        "value": "",
        "teach": "In-wizard chapters show intent before the build. Use for 'how I designed this' — bookend or Act I only, not every scene.",
    },
    # —— Visual & cinematic ——
    {
        "id": "visual_mood",
        "category": "Visual",
        "label": "Visual mood & color story",
        "done": False,
        "value": "",
        "teach": "Karst emerald, lava orange, fog blue — pick a palette and grade toward it. Consistency reads as AAA; random saturation reads amateur.",
    },
    {
        "id": "grade_intent",
        "category": "Visual",
        "label": "Grade & contrast intent",
        "done": False,
        "value": "",
        "teach": "Crushed blacks vs lifted documentary shadows. videoEnhance and master grade in compose — decide cinematic dark vs bright reel.",
    },
    {
        "id": "hero_shot",
        "category": "Visual",
        "label": "Money shot (hero frame)",
        "done": False,
        "value": "",
        "teach": "The thumbnail frame: which milestone PNG becomes the poster image. Often cave mouth, vista, or final graded overview.",
    },
    {
        "id": "annotation_style",
        "category": "Visual",
        "label": "On-screen annotations",
        "done": False,
        "value": "",
        "teach": "OpenCV boxes teach; too many look like a tutorial dump. Fixed vs vision-driven — match audience (hire-me vs teach-me).",
    },
    {
        "id": "intro_card",
        "category": "Visual",
        "label": "Intro card tone",
        "done": False,
        "value": "",
        "teach": "Title card sets genre: broadcast documentary, indie devlog, or cinematic trailer. Must match ApprovedIntro assets.",
    },
    {
        "id": "outro_card",
        "category": "Visual",
        "label": "Outro card tone",
        "done": False,
        "value": "",
        "teach": "Outro is the brand stamp + CTA. Signature, portrait, and social handles — verify in Cards tab before compose.",
    },
    # —— Editorial & pacing ——
    {
        "id": "pacing_overall",
        "category": "Editorial",
        "label": "Overall pacing feel",
        "done": False,
        "value": "",
        "teach": "Cinematic = longer milestone holds; urgent = short subbeats. Maps to milestoneHoldSec and subbeatHoldSec sliders.",
    },
    {
        "id": "milestone_priority",
        "category": "Editorial",
        "label": "Which phases get longest holds",
        "done": False,
        "value": "",
        "teach": "Not every build step deserves 20 seconds. Prioritize mouth, route, grading — trim setup and compile unless the story needs them.",
    },
    {
        "id": "crossfade_style",
        "category": "Editorial",
        "label": "Transitions & crossfades",
        "done": False,
        "value": "",
        "teach": "segmentXfadeSec: hard cuts feel modern; long dissolves feel prestige TV. Match platform (Shorts = harder cuts).",
    },
    {
        "id": "timelapse_rhythm",
        "category": "Editorial",
        "label": "Timelapse rhythm",
        "done": False,
        "value": "",
        "teach": "timelapseSecPerFrame: fast = energy; slow = craftsmanship. Sync with narration — don't outrun the voice.",
    },
    {
        "id": "caption_pacing",
        "category": "Editorial",
        "label": "Caption read pacing",
        "done": False,
        "value": "",
        "teach": "captionReadPauseSec gives viewers time to read bullets without pausing the film feel. Educational captions need air.",
    },
    # —— Voice & narration ——
    {
        "id": "tone",
        "category": "Voice",
        "label": "Narrator tone & persona",
        "done": False,
        "value": "",
        "teach": "Personal Voice is your timbre; persona is performance — mentor, host, peer. Sets sliders in Voice tab.",
    },
    {
        "id": "narration_arc",
        "category": "Voice",
        "label": "Narration emotional arc",
        "done": False,
        "value": "",
        "teach": "Voice should lift at the peak and land softly at CTA. Plan where energy rises — usually Act II struggle.",
    },
    {
        "id": "script_density",
        "category": "Voice",
        "label": "Words-per-minute target",
        "done": False,
        "value": "",
        "teach": "Documentary ~140–180 wpm; hype trailer higher. sayRate slider + script length must fit target runtime.",
    },
    {
        "id": "emphasis_moments",
        "category": "Voice",
        "label": "Emphasis & punch words",
        "done": False,
        "value": "",
        "teach": "3–5 words to hit harder: first cave light, 'playable', your name. narrationEmphasisCapture in pipeline.",
    },
    {
        "id": "breath_pauses",
        "category": "Voice",
        "label": "Breath & dramatic pauses",
        "done": False,
        "value": "",
        "teach": "Silence after a big visual sells scale. [[slnc]] markers and natural pauses — Grammy mixes leave room to breathe.",
    },
    {
        "id": "bot_presence",
        "category": "Voice",
        "label": "Bot avatar / host presence",
        "done": False,
        "value": "",
        "teach": "Lip-sync avatar adds personality but can distract. Use for intro once or key beats — not wall-to-wall unless that's the brand.",
    },
    # —— Sound & music ——
    {
        "id": "music_bed",
        "category": "Sound",
        "label": "Music bed intent",
        "done": False,
        "value": "",
        "teach": "Current pipeline is voice-forward. Plan if you'll add music in post — leaves headroom in narration mix (-3 to -6 dB peaks).",
    },
    {
        "id": "sfx_intent",
        "category": "Sound",
        "label": "Sound design / SFX",
        "done": False,
        "value": "",
        "teach": "Subtle whooshes on transitions are optional in future passes. Note intent now so export doesn't fight later stems.",
    },
    {
        "id": "loudness_target",
        "category": "Sound",
        "label": "Loudness & streaming target",
        "done": False,
        "value": "",
        "teach": "YouTube ~ -14 LUFS integrated. Personal Voice ladder masters toward consistent level — avoid harsh sibilance on earbuds.",
    },
    # —— Graphics & accessibility ——
    {
        "id": "caption_style",
        "category": "Graphics",
        "label": "Caption & lower-third style",
        "done": False,
        "value": "",
        "teach": "Educational captions: key point + bullets + NEXT line. 32pt minimum. forceLocalCaptions — readable on phone.",
    },
    {
        "id": "on_screen_text",
        "category": "Graphics",
        "label": "On-screen text minimalism",
        "done": False,
        "value": "",
        "teach": "AAA = restraint. One idea per hold. If narration says it, captions don't repeat verbatim — they reinforce.",
    },
    {
        "id": "accessibility",
        "category": "Graphics",
        "label": "Accessibility plan",
        "done": False,
        "value": "",
        "teach": "Captions for deaf/HoH viewers; high contrast text; avoid red-green-only meaning. YouTube auto-captions are backup, not master.",
    },
    # —— Technical master ——
    {
        "id": "resolution",
        "category": "Technical",
        "label": "Master resolution",
        "done": False,
        "value": "",
        "teach": "Record at 1920×1080 for final sharpness; 720p capture upscales with videoEnhance. Plan re-capture if quality matters.",
    },
    {
        "id": "sharpness_enhance",
        "category": "Technical",
        "label": "Enhance & upscale intent",
        "done": False,
        "value": "",
        "teach": "videoEnhance in ApprovedCards — upscale + grade on final mux. Use for portfolio masters; skip for fast drafts.",
    },
    {
        "id": "draft_vs_master",
        "category": "Technical",
        "label": "Draft vs master pass",
        "done": False,
        "value": "",
        "teach": "Silent shell = director's cut for approval. Final Personal Voice pass = master. Don't skip script review between them.",
    },
    # —— Social & discoverability ——
    {
        "id": "thumbnail_moment",
        "category": "Social",
        "label": "Thumbnail hero moment",
        "done": False,
        "value": "",
        "teach": "Pick the frame you'd click on: high contrast face/world, readable at 120px wide. Often matches hero_shot milestone.",
    },
    {
        "id": "clip_derivatives",
        "category": "Social",
        "label": "Short clips & derivatives",
        "done": False,
        "value": "",
        "teach": "Plan a 30–60s vertical cut from Act III for Shorts. One long master, many crops — decide which beats export separately.",
    },
    {
        "id": "seo_metadata",
        "category": "Social",
        "label": "SEO title, description, tags",
        "done": False,
        "value": "",
        "teach": "Description first 2 lines matter. Tags: gamedev, procedural, Unity, world building. Brief feeds YouTube export button.",
    },
    # —— Approval ——
    {
        "id": "ship_criteria",
        "category": "Approval",
        "label": "Ship criteria (definition of done)",
        "done": False,
        "value": "",
        "teach": "Write what 'AAA enough' means for YOU: narrated master, no audio clicks, cards correct, one friend watch-through without confusion.",
    },
]

CHECKLIST_IDS: tuple[str, ...] = tuple(c["id"] for c in DEFAULT_CHECKLIST)
CHECKLIST_CATEGORIES: tuple[str, ...] = tuple(
    dict.fromkeys(c.get("category", "General") for c in DEFAULT_CHECKLIST)
)

BRIEFING_PHASES = frozenset({"qna", "briefing_ready"})
EDITOR_PHASES = frozenset(
    {"editor", "awaiting_cards", "awaiting_compose", "awaiting_script", "awaiting_final", "complete"}
)

VOICE_PERSONA_DEFAULTS: dict[str, Any] = {
    "tone": "warm-documentary",
    "character": "mentor-host",
    "energy": 62,
    "formality": 45,
    "sayRate": 186,
    "delivery": "ladderDocumentary",
    "personaNotes": "",
    "warmth": True,
    "humanize": True,
}

DELIVERY_PRESETS = ("ladderDocumentary", "raw", "tvhost")
VOICE_TEST_WAV = "DirectorVoiceTest.wav"
DEFAULT_TEST_PHRASE = (
    "Hello — this is my Personal Voice. [[slnc 300]] "
    "I'm shaping a procedural world recap for YouTube and social."
)

_narr_mod: Any = None


def _narrator():
    global _narr_mod
    if _narr_mod is None:
        import importlib.util

        spec = importlib.util.spec_from_file_location("narr", TOOLS / "demo-recap-narrator.py")
        mod = importlib.util.module_from_spec(spec)
        assert spec.loader
        spec.loader.exec_module(mod)
        _narr_mod = mod
    return _narr_mod


def _load_approved_cards() -> dict[str, Any]:
    path = approved_dir() / "ApprovedCards.json"
    if not path.is_file():
        return {}
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return {}


def voice_test_wav_path(capture: Path) -> Path:
    return capture / VOICE_TEST_WAV


def build_narrator_spec(capture: Path, persona: dict[str, Any] | None = None) -> dict[str, Any]:
    narr = _narrator()
    cards = _load_approved_cards()
    approved_path = approved_dir() / "ApprovedCards.json"
    spec = narr.apply_recap_narrator_defaults(
        dict(cards),
        run_dir=capture,
        approved_path=approved_path if approved_path.is_file() else None,
    )
    p = {**VOICE_PERSONA_DEFAULTS, **(persona or read_voice_persona(capture))}
    spec["narratorPersonalSayRate"] = int(p.get("sayRate", 186))
    spec["narratorRate"] = spec["narratorPersonalSayRate"]
    spec["narratorPersonalDelivery"] = str(p.get("delivery", "ladderDocumentary"))
    spec["narratorPersonalHumanize"] = bool(p.get("humanize", True))
    spec["narrationDeliveryStyle"] = str(p.get("tone", "warm-documentary"))
    vh = dict(spec.get("voiceHelpers") or {})
    vh["sayRate"] = spec["narratorPersonalSayRate"]
    vh["preset"] = spec["narratorPersonalDelivery"]
    spec["voiceHelpers"] = vh
    if p.get("personaNotes"):
        spec["narratorPersonaNotes"] = str(p["personaNotes"])
    return narr.flatten_narrator_personal_settings(spec)


def sync_persona_from_approved(capture: Path) -> dict[str, Any]:
    """Pull ApprovedCards + Personal Voice name into Director session (your saved studio setup)."""
    cards = _load_approved_cards()
    narr = _narrator()
    nested = cards.get("narratorPersonalSettings") or {}
    vp = cards.get("voicePersona") or {}
    persona = {**VOICE_PERSONA_DEFAULTS}
    if nested.get("sayRate") is not None:
        persona["sayRate"] = int(nested["sayRate"])
    if nested.get("delivery"):
        persona["delivery"] = str(nested["delivery"])
    if nested.get("personalHumanize") is not None:
        persona["humanize"] = bool(nested["personalHumanize"])
    if cards.get("narrationDeliveryStyle"):
        persona["tone"] = str(cards["narrationDeliveryStyle"])
    if isinstance(vp, dict):
        for key in ("tone", "character", "energy", "formality"):
            if vp.get(key) is not None:
                persona[key] = vp[key]
        if vp.get("notes"):
            persona["personaNotes"] = str(vp["notes"])
    vh = cards.get("voiceHelpers") or {}
    if isinstance(vh, dict) and vh.get("sayRate") is not None:
        persona["sayRate"] = int(vh["sayRate"])
    if isinstance(vh, dict) and vh.get("preset"):
        persona["delivery"] = str(vh["preset"])
    doc = _read_session(capture)
    if not doc:
        start_session(capture)
        doc = _read_session(capture)
    assert doc is not None
    doc["voicePersona"] = {**persona, **(doc.get("voicePersona") or {})}
    doc["voiceName"] = narr.resolve_personal_voice_name(build_narrator_spec(capture, doc["voicePersona"]), capture)
    doc["testPhrase"] = str(cards.get("narratorTestPhrase") or DEFAULT_TEST_PHRASE)
    _write_session(capture, doc)
    return read_voice_profile(capture)


def _utc() -> str:
    return datetime.now(timezone.utc).isoformat()


def session_path(capture: Path) -> Path:
    return capture / SESSION_NAME


def _stream_persist(capture: Path):
    def persist(doc: dict[str, Any]) -> None:
        doc.pop("cursorWorking", None)
        _write_session(capture, doc)

    return persist


def _read_session(capture: Path) -> dict[str, Any] | None:
    p = session_path(capture)
    if not p.is_file():
        return None
    for _ in range(3):
        try:
            return json.loads(p.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            import time

            time.sleep(0.05)
    return None


def _write_session(capture: Path, doc: dict[str, Any]) -> None:
    doc["updatedAt"] = _utc()
    atomic_write_text(session_path(capture), json.dumps(doc, indent=2))


def _hub_for_capture(capture: Path) -> Path:
    from director_paths import studio_repo_root

    env = __import__("os").environ.get("STUDIO_ROOT", "").strip()
    if env:
        return Path(env).expanduser()
    return studio_repo_root()


def _template_by_id() -> dict[str, dict[str, Any]]:
    return {str(c["id"]): c for c in DEFAULT_CHECKLIST}


def _upgrade_checklist(doc: dict[str, Any]) -> bool:
    """Merge new checklist items into older sessions; preserve done/value answers."""
    changed = False
    if int(doc.get("checklistVersion") or 0) >= CHECKLIST_VERSION:
        return _ensure_checklist(doc)
    old_by_id = {str(c.get("id")): c for c in doc.get("checklist") or [] if c.get("id")}
    templates = _template_by_id()
    merged: list[dict[str, Any]] = []
    for template in DEFAULT_CHECKLIST:
        tid = str(template["id"])
        item = dict(template)
        if tid in old_by_id:
            prev = old_by_id[tid]
            item["done"] = bool(prev.get("done"))
            item["value"] = str(prev.get("value") or "")
        merged.append(item)
    doc["checklist"] = merged
    doc["checklistVersion"] = CHECKLIST_VERSION
    changed = True
    _ensure_checklist(doc)
    return changed


def _ensure_checklist(doc: dict[str, Any]) -> bool:
    """Fill missing checklist fields; append any new template ids. Returns True if mutated."""
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


def _latest_user_message(doc: dict[str, Any]) -> str:
    for m in reversed(doc.get("messages") or []):
        if m.get("role") == "user":
            return str(m.get("content") or "")
    return ""


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


def media_ingested(capture: Path) -> bool:
    """True when user uploaded media and frames exist on disk."""
    cap = capture.expanduser().resolve()
    try:
        import ingest_media as im

        meta = im.read_media_ingest(cap)
        if meta and int(meta.get("frameCount") or 0) > 0:
            return True
        return im.frame_count(cap) > 0
    except Exception:
        tl_dir = cap / "timelapse"
        return tl_dir.is_dir() and any(tl_dir.glob("tl_*.png"))


def _read_media_summary(capture: Path) -> dict[str, Any] | None:
    try:
        import ingest_media as im

        meta = im.read_media_ingest(capture)
        if meta:
            return meta
    except Exception:
        pass
    tl_path = capture / "DemoRecapTimeline.json"
    if tl_path.is_file():
        try:
            spec = json.loads(tl_path.read_text(encoding="utf-8"))
            if spec.get("source") == "user_media" or spec.get("mediaIngest"):
                return spec.get("mediaIngest") or {
                    "source": "user_media",
                    "frameCount": spec.get("frameCount", 0),
                }
        except json.JSONDecodeError:
            pass
    return None


def probe_capture_footage(capture: Path) -> dict[str, Any]:
    """What recap footage exists on disk — drives world-aware Q&A."""
    cap = capture.expanduser().resolve()
    wizard_dir = cap / "wizard" / "frames"
    wizard = len(list(wizard_dir.glob("wf_*.png"))) if wizard_dir.is_dir() else 0
    tl_dir = cap / "timelapse"
    timelapse = len(list(tl_dir.glob("tl_*.png"))) if tl_dir.is_dir() else 0
    scene_dir = cap / "frames"
    scene_frames = len(list(scene_dir.glob("*.png"))) if scene_dir.is_dir() else 0
    presentation = (cap / "DemoRecapPresentation.mp4").is_file()
    silent = any(
        p.is_file()
        for p in (
            cap / "_presentation_compose" / "_final_video.mp4",
            cap / "_presentation_compose_preview120" / "_final_video.mp4",
        )
    )
    media = _read_media_summary(cap)
    user_media = bool(
        (media and media.get("source") == "user_media")
        or (timelapse >= 1 and wizard < 2 and scene_frames < 3)
    )
    if presentation or silent:
        phase = "post_build"
    elif user_media and timelapse >= 1:
        phase = "user_media"
    elif timelapse >= 4 or scene_frames >= 3:
        phase = "world_building"
    elif wizard >= 2:
        phase = "planner_only"
    else:
        phase = "pre_capture"
    out: dict[str, Any] = {
        "phase": phase,
        "wizardFrames": wizard,
        "timelapseFrames": timelapse,
        "sceneFrames": scene_frames,
        "hasPresentation": presentation,
        "hasSilentVideo": silent,
        "source": media.get("source") if media else ("user_media" if user_media else "capture"),
        "hasVideo": bool(media.get("hasVideo")) if media else False,
        "hasImages": bool(media.get("hasImages")) if media else False,
        "durationSec": media.get("durationSec") or media.get("estimatedDurationSec") if media else None,
        "uploadPaths": media.get("uploadPaths") or [] if media else [],
        "mediaIngested": media_ingested(cap),
    }
    return out


def _footage_awareness_block(capture: Path) -> str:
    foot = probe_capture_footage(capture)
    phase = foot["phase"]
    media = _read_media_summary(capture)
    lines = [
        "## Capture footage reality (strict — do not hallucinate world content)",
        f"- Footage phase: **{phase}**",
        f"- Source: **{foot.get('source', 'unknown')}**",
        f"- Wizard UI frames: {foot['wizardFrames']}",
        f"- Timelapse frames: {foot['timelapseFrames']}",
        f"- Scene milestone frames: {foot['sceneFrames']}",
        f"- Silent video ready: {foot['hasSilentVideo']}",
        f"- Final presentation: {foot['hasPresentation']}",
    ]
    if media:
        kind = []
        if foot.get("hasVideo"):
            kind.append("video")
        if foot.get("hasImages"):
            kind.append("images")
        kind_label = " + ".join(kind) if kind else "media"
        dur = foot.get("durationSec")
        dur_line = f"{dur:.1f}s" if isinstance(dur, (int, float)) else "unknown"
        lines += [
            f"- **User-uploaded {kind_label}** — {foot['timelapseFrames']} extracted frames, ~{dur_line} source duration.",
            "- Q&A must reference THIS footage: ask about hooks, hero moments, and story beats visible in their upload.",
            "- Do NOT assume Unity world-build, planner UI, or Environment Kit milestones unless they appear in the upload.",
        ]
        if foot.get("uploadPaths"):
            lines.append(f"- Upload paths on disk: {len(foot['uploadPaths'])} file(s).")
    if phase == "user_media":
        lines += [
            "- Treat uploaded clips/stills as the **primary visual source** for the recap film.",
            "- Story, pacing, and hero-shot answers should tie to content the user actually provided.",
        ]
    elif phase in ("planner_only", "pre_capture"):
        lines += [
            "- **No footage ingested yet** — if timelapse frames are zero, ask the user to upload media first.",
            "- **Planner segment only** — no Unity world generation is in this footage yet.",
            "- Frame visual/story answers as **production intent** (plan, target, when-built): "
            "'we will open on…', 'hero frame once terrain generates', 'planner chapter leads'.",
            "- Do NOT write present-tense facts about cave mouth, karst terrain, milestones, props, "
            "or underground routes as if they already appear on screen.",
            "- Strategy, audience, runtime, voice, cards, and export decisions are fine in present tense.",
        ]
    elif phase == "world_building":
        lines += [
            "- World build timelapse is in progress or partial — reference only generic 'generation' beats, "
            "not specific landmarks unless the user confirms they are visible.",
        ]
    else:
        lines += [
            "- Post-build footage exists — you may reference completed milestones when grounded in the brief.",
        ]
    return "\n".join(lines)


SYSTEM_DIRECTOR_AUTO_USER = """You simulate the USER answering the AI Director's pre-production checklist.
Output **plain text only** — no JSON, no role labels. Write 2–5 sentences as if the user typed in chat.

{footage_block}

## Rules
- Answer ONLY the Director's last message — do not ask questions back.
- Address checklist topic **[{next_topic}]** ({next_label}).
- Match the footage phase above: never claim terrain, cave, milestones, or props exist on screen
  during planner_only / pre_capture phases.
- Use future/planning tense for visuals until timelapse footage exists.
{prior_block}
{retry_block}
"""

DIRECTOR_RESPONDER_FALLBACKS: dict[str, str] = {
    "platform": "YouTube long-form 16:9 primary (~8 min); portfolio cut for recruiters second.",
    "aspect_ratio": "16:9 landscape master at 1920×1080; safe title-safe for Shorts crops later.",
    "length": "Target 8 minutes — enough for planner chapter plus timelapse acts once captured.",
    "audience": "Indie hiring managers and CS peers evaluating world-building craft.",
    "success_metric": "Success = interview conversations citing the playable procedural loop.",
    "working_title": "Working title: Procedural Florida Karst — Playable World (subject to post-gen hero).",
    "cta": "Subscribe + link to playable build after the underground payoff lands in footage.",
    "chapters_endscreen": "YouTube chapters: Planning, Surface build, Underground route, Playable proof.",
    "hook": "Open on the planner question 'what world are we building?' — save terrain hook for post-generation timelapse.",
    "narrative_arc": "Act 1: planner briefing (current footage). Acts 2–3: surface then cave once Unity capture exists.",
    "emotional_peak": "Peak emotion when the first playable loop is proven — not during planner Q&A.",
    "payoff": "Payoff is 'this world is playable' — deliver after build footage, not in planner segment.",
    "stakes": "Stakes: credibility as a hireable world builder, not just a timelapse artist.",
    "wizard_chapter": "Lead recap with the full planner web UI chapter — matches recorded footage today.",
    "visual_mood": "Plan cinematic documentary grade: cool shadows, readable mids — apply when terrain shots exist.",
    "grade_intent": "Crushed blacks with lifted mids once karst terrain is on screen; planner stays neutral UI.",
    "hero_shot": "Hero frame on the main environment reveal after world generation — not in planner-only footage.",
    "annotation_style": "Minimal on-screen labels during planner; milestone callouts only on Unity timelapse.",
    "intro_card": "Intro card after planner chapter — title + Environment Kit branding.",
    "outro_card": "Outro with portrait and social handles once master is approved.",
    "pacing_overall": "Slow planner teaching pace, then accelerate timelapse once generation footage lands.",
    "milestone_priority": "Planner Q&A first, then surface layout beats, then cave route when timelapse exists.",
    "crossfade_style": "Gentle dissolves between planner and timelapse; match cut on first terrain tile.",
    "timelapse_rhythm": "Hold wide on coverage during generation; no tight tile jitter.",
    "caption_pacing": "Caption pauses for teaching beats in planner; shorter in timelapse montage.",
    "tone": "Warm documentary mentor — peer devlog energy, not hype trailer.",
    "narration_arc": "Narration introduces plan in Act 1, narrates build in Act 2, celebrates playability in Act 3.",
    "script_density": "~150 wpm average; denser during planner teaching, lighter over fast timelapse.",
    "emphasis_moments": "Emphasize checklist decisions in planner; save terrain callouts for post-generation shots.",
    "breath_pauses": "Half-second breath after each planner teaching beat.",
    "bot_presence": "Bot avatar lower-right during timelapse only — not over planner web UI.",
    "music_bed": "Subtle documentary bed under timelapse; planner chapter mostly voice-forward.",
    "sfx_intent": "Light UI whoosh on chapter transitions; no terrain SFX until world footage exists.",
    "loudness_target": "-14 LUFS integrated for YouTube delivery.",
    "caption_style": "High-contrast captions on timelapse; optional off during planner screen recording.",
    "on_screen_text": "Chapter titles only — no spoiler labels for terrain not yet generated.",
    "accessibility": "Burned-in captions on final master; chapter markers in description.",
    "resolution": "1080p master; 4K upscale optional after ship criteria met.",
    "sharpness_enhance": "Light sharpen on timelapse export only.",
    "draft_vs_master": "Silent shell for script approval; Personal Voice on master only.",
    "thumbnail_moment": "Thumbnail from first dramatic terrain frame post-generation — not planner screenshot.",
    "clip_derivatives": "45s Short from cave-route payoff once that footage exists.",
    "seo_metadata": "Tags: procedural world, Unity, environment art, playable demo.",
    "ship_criteria": "Ship when narrated master has no clicks, cards verified, friend watch-through passes.",
}


def _fallback_director_responder_reply(
    doc: dict[str, Any],
    checklist_id: str,
    footage: dict[str, Any],
) -> str:
    base = DIRECTOR_RESPONDER_FALLBACKS.get(
        checklist_id,
        f"Production decision for {_label_for_checklist_id(doc, checklist_id)} — aligned with recap intent.",
    )
    if footage.get("phase") in ("planner_only", "pre_capture") and checklist_id in {
        "hero_shot",
        "hook",
        "visual_mood",
        "milestone_priority",
        "emphasis_moments",
        "thumbnail_moment",
        "narrative_arc",
        "payoff",
    }:
        return base
    return base


def _generate_director_responder_reply(
    capture: Path,
    hub: Path,
    doc: dict[str, Any],
    *,
    attempt: int = 0,
    blocked_replies: list[str] | None = None,
) -> str:
    next_id = _next_pending_checklist_id(doc) or "platform"
    footage_block = _footage_awareness_block(capture)
    retry_block = ""
    if blocked_replies:
        retry_block = (
            "\n## REJECTED — duplicate attempt(s); write a completely NEW answer:\n"
            + "\n".join(f"- REJECTED: {b.replace(chr(10), ' ')[:200]}" for b in blocked_replies[-3:])
            + f"\n\nRegeneration attempt {attempt + 1}: answer ONLY [{next_id}]."
        )
    elif attempt > 0:
        retry_block = f"\nRegeneration attempt {attempt + 1}: vary wording.\n"

    system = SYSTEM_DIRECTOR_AUTO_USER.format(
        footage_block=footage_block,
        next_topic=next_id,
        next_label=_label_for_checklist_id(doc, next_id),
        prior_block=planner._format_prior_replies_block(doc),  # noqa: SLF001
        retry_block=retry_block,
    )
    director_msg = planner._last_assistant_message(doc) or "Ask the first checklist question."  # noqa: SLF001
    user_prompt = (
        f"Director's last message (answer THIS only):\n{director_msg}\n\n"
        f"Checklist topic: [{next_id}] — {_label_for_checklist_id(doc, next_id)}\n\n"
        f"Write one unique user reply. Respect footage phase — no claims about unbuilt world content."
    )
    return planner._llm_messages(  # noqa: SLF001
        hub,
        system,
        [{"role": "user", "content": user_prompt}],
        json_response=False,
    ).strip()


def _generate_director_responder_reply_deduped(capture: Path, hub: Path, doc: dict[str, Any]) -> str:
    blocked: list[str] = []
    next_id = _next_pending_checklist_id(doc) or "platform"
    footage = probe_capture_footage(capture)

    for attempt in range(planner.AUTO_REPLY_MAX_ATTEMPTS):
        reply = _generate_director_responder_reply(
            capture,
            hub,
            doc,
            attempt=attempt,
            blocked_replies=blocked or None,
        )
        is_dup, _reason = planner._is_duplicate_auto_reply(reply, doc)  # noqa: SLF001
        if not is_dup and len(reply.strip()) >= 8:
            return reply
        blocked.append(reply)

    fallback = _fallback_director_responder_reply(doc, next_id, footage)
    is_dup, _ = planner._is_duplicate_auto_reply(fallback, doc)  # noqa: SLF001
    if is_dup:
        fallback = (
            f"{fallback} (topic {next_id}, take {len(blocked) + 1} — "
            f"footage phase {footage.get('phase')})."
        )
    return fallback


def _build_qna_system(doc: dict[str, Any], capture: Path) -> str:
    next_id = _next_pending_checklist_id(doc) or "none"
    next_item = next(
        (c for c in doc.get("checklist", []) if str(c.get("id")) == next_id),
        None,
    )
    by_category: dict[str, list[str]] = {}
    for c in DEFAULT_CHECKLIST:
        cat = str(c.get("category") or "General")
        by_category.setdefault(cat, []).append(str(c["id"]))
    return (
        "You are the AI Director — Emmy/Grammy-caliber film mentor for Environment Kit recap films "
        "(cinematic cuts from Unity world-build timelapse + wizard chapters + Personal Voice).\n\n"
        "The user is learning broadcast-grade production for YouTube, social, portfolio, and hire-me reels.\n"
        "Every reply MUST teach something real (2–4 sentences) before the next question.\n\n"
        "## Interview rules (strict while qnaComplete is false)\n"
        "- Work through the checklist IN ORDER — one id per turn (next pending below).\n"
        "- Ask ONE focused question tied to that id's label and teaching hint.\n"
        "- Include teachingMoment: why this decision matters for compose, holds, narration, cards, or export.\n"
        "- When the user answers clearly, mark ONLY that id done:true with a specific value (not vague).\n"
        "- You may mark multiple ids done in one turn ONLY if the user's message clearly answered each.\n"
        "- Set qnaComplete:true ONLY when every checklist item is done OR user says move on / enter studio.\n"
        "- Never mark items done without a real user answer.\n\n"
        f"{_footage_awareness_block(capture)}\n\n"
        f"Checklist categories: {json.dumps(by_category)}\n"
        f"All checklist ids ({len(CHECKLIST_IDS)}): {', '.join(CHECKLIST_IDS)}\n"
        f"Next pending id: {next_id}"
        + (f" ({next_item.get('label')})" if next_item else "")
        + "\n"
        f"Current checklist: {json.dumps(doc.get('checklist', []))}\n\n"
        "Respond JSON only:\n"
        '{"assistantMessage":"markdown for chat","teachingMoment":"optional aside",'
        '"checklist":[{"id":"<id>","done":true,"value":"specific answer"}],'
        '"qnaComplete":false,'
        '"brief":{"platform":"","targetLengthSec":0,"hook":"","audience":"","tone":"","pacing":"","cta":"",'
        '"visualMood":"","heroShot":"","shipCriteria":""}}\n'
        "Never mention vendor AI product names. Say Assistant or Director."
    )


def _parse_llm_json(raw: str) -> dict[str, Any]:
    text = raw.strip()
    if text.startswith("```"):
        text = re.sub(r"^```(?:json)?\s*", "", text)
        text = re.sub(r"\s*```$", "", text)
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return {"assistantMessage": text}


def public_session(capture: Path) -> dict[str, Any]:
    doc = _read_session(capture)
    if not doc:
        return {"ok": False, "error": "no session", "capture": str(capture)}
    if _heal_stale_assistant(doc):
        _write_session(capture, doc)
    if _upgrade_checklist(doc) or _ensure_checklist(doc):
        _write_session(capture, doc)
    phase = str(doc.get("phase", "qna"))
    if phase == "awaiting_cards":
        phase = "briefing_ready"
    checklist_done = _checklist_all_done(doc)
    stream_text, stream_role = planner._public_streaming(doc)
    messages_out: list[dict[str, Any]] = []
    for m in doc.get("messages", []):
        entry = dict(m)
        content = str(entry.get("content") or "")
        if content.strip().startswith("{"):
            entry["content"] = planner._sanitize_chat_content(content)
        messages_out.append(entry)
    out = {
        "ok": True,
        "capture": str(capture),
        "phase": phase,
        "messages": messages_out,
        "checklist": doc.get("checklist", []),
        "brief": doc.get("brief", {}),
        "voicePersona": doc.get("voicePersona", VOICE_PERSONA_DEFAULTS),
        "streamingText": stream_text or "",
        "streamingRole": stream_role,
        "assistantWorking": bool(doc.get("assistantWorking")),
        "responderWorking": bool(doc.get("responderWorking")),
        "inputBlocked": bool(doc.get("assistantWorking") or doc.get("responderWorking")),
        "duplicateRequest": False,
        "exportReady": bool(doc.get("exportApproved")),
        "checklistComplete": checklist_done,
        "checklistTotal": len(doc.get("checklist") or []),
        "checklistDone": sum(1 for c in (doc.get("checklist") or []) if c.get("done")),
        "checklistVersion": int(doc.get("checklistVersion") or CHECKLIST_VERSION),
        "categories": list(CHECKLIST_CATEGORIES),
        "inBriefing": phase in BRIEFING_PHASES,
        "inEditor": phase in EDITOR_PHASES,
        "footage": probe_capture_footage(capture),
        "scriptMessages": doc.get("scriptMessages") or [],
        "scriptAssistantWorking": bool(doc.get("scriptAssistantWorking")),
    }
    return out


def get_session(capture: Path) -> dict[str, Any]:
    """Return session if started; do not auto-create (landing gate)."""
    capture = capture.expanduser().resolve()
    if not capture.is_dir():
        raise FileNotFoundError(f"capture folder not found: {capture}")
    if not _read_session(capture):
        foot = probe_capture_footage(capture)
        return {
            "ok": False,
            "noSession": True,
            "capture": str(capture),
            "mediaIngested": media_ingested(capture),
            "footage": foot,
            "checklistTotal": 0,
            "checklistDone": 0,
        }
    return public_session(capture)


def start_session(capture: Path) -> dict[str, Any]:
    capture = capture.expanduser().resolve()
    if not capture.is_dir():
        raise FileNotFoundError(f"capture folder not found: {capture}")
    if not media_ingested(capture):
        raise ValueError("Upload video or images before starting the Director briefing.")
    existing = _read_session(capture)
    if existing:
        return public_session(capture)
    n_items = len(DEFAULT_CHECKLIST)
    foot = probe_capture_footage(capture)
    media = _read_media_summary(capture)
    media_intro = ""
    if foot.get("phase") == "user_media" and media:
        kind = []
        if foot.get("hasVideo"):
            kind.append("video")
        if foot.get("hasImages"):
            kind.append("images")
        kind_label = " and ".join(kind) if kind else "media"
        frames = foot.get("timelapseFrames", 0)
        dur = foot.get("durationSec")
        dur_note = f", ~{dur:.0f}s source duration" if isinstance(dur, (int, float)) else ""
        media_intro = (
            f"\n\nI've ingested your **{kind_label}** — **{frames} frames** extracted{dur_note}. "
            "I'll tailor every question to what you actually shot, not generic world-build assumptions.\n\n"
        )
    doc: dict[str, Any] = {
        "version": 2,
        "checklistVersion": CHECKLIST_VERSION,
        "capture": str(capture),
        "phase": "qna",
        "createdAt": _utc(),
        "messages": [
            {
                "role": "assistant",
                "content": (
                    f"Welcome to **broadcast pre-production** — {n_items} decisions across strategy, story, "
                    "visual design, editorial, voice, sound, graphics, technical master, social, and ship criteria."
                    f"{media_intro}\n"
                    "I'll teach one pro concept per question (like the build planner). "
                    "Work the checklist on the right — Strategy first, then Story, Visual, Editorial, Voice, and so on. "
                    "When **every** item is checked, you enter the **Studio Editor** to preview, test your voice, "
                    "and approve the production-quality render.\n\n"
                    "This is the difference between a timelapse dump and a hire-me reel.\n\n"
                    "**First question (Strategy — platform):** where will this film live primarily — "
                    "YouTube long-form, Shorts/TikTok, Instagram, or a portfolio reel — and why that platform?"
                ),
            }
        ],
        "checklist": [dict(c) for c in DEFAULT_CHECKLIST],
        "brief": {},
        "voicePersona": dict(VOICE_PERSONA_DEFAULTS),
        "exportApproved": False,
        "footageSummary": {
            "source": foot.get("source") or "user_media",
            "frameCount": foot.get("timelapseFrames", 0),
            "hasVideo": foot.get("hasVideo", False),
            "hasImages": foot.get("hasImages", False),
            "durationSec": foot.get("durationSec"),
            "phase": foot.get("phase"),
        },
    }
    if media:
        doc["mediaIngest"] = media
    _write_session(capture, doc)
    sync_persona_from_approved(capture)
    return public_session(capture)


def chat_turn(
    capture: Path,
    user_message: str | None = None,
    bootstrap: bool = False,
    *,
    _skip_action_lock: bool = False,
    user_from_responder: bool = False,
) -> dict[str, Any]:
    hub = _hub_for_capture(capture)
    planner.load_dotenv(hub)
    lock = _action_lock(capture)
    acquired = _skip_action_lock or lock.acquire(blocking=False)
    if not _skip_action_lock and not acquired:
        return _blocked_session(
            capture,
            duplicate=True,
            reason="Director is processing your last message — please wait.",
        )

    try:
        doc = _read_session(capture)
        if not doc:
            start_session(capture)
            doc = _read_session(capture)
        assert doc is not None
        if _heal_stale_assistant(doc):
            _write_session(capture, doc)

        if user_message and not bootstrap:
            if doc.get("assistantWorking"):
                return _blocked_session(
                    capture,
                    duplicate=True,
                    reason="Assistant is still working on your last message.",
                )
            if _recent_duplicate_chat(capture, user_message):
                return _blocked_session(
                    capture,
                    duplicate=True,
                    reason="Duplicate message ignored — wait a moment before resending.",
                )

        return _chat_turn_locked(
            capture, hub, doc, user_message, bootstrap, user_from_responder=user_from_responder
        )
    finally:
        if not _skip_action_lock and acquired:
            lock.release()


def _chat_turn_locked(
    capture: Path,
    hub: Path,
    doc: dict[str, Any],
    user_message: str | None,
    bootstrap: bool,
    *,
    user_from_responder: bool = False,
) -> dict[str, Any]:
    if doc.get("phase") != "qna":
        if user_message:
            doc.setdefault("messages", []).append({"role": "user", "content": user_message})
            doc.setdefault("messages", []).append(
                {
                    "role": "assistant",
                    "content": (
                        f"We're in **{doc.get('phase')}** — use the studio panels to review and approve. "
                        "Ask me to adjust tone, script, or pacing anytime."
                    ),
                }
            )
            _write_session(capture, doc)
        return public_session(capture)

    if user_message and not bootstrap:
        entry: dict[str, Any] = {"role": "user", "content": user_message}
        if user_from_responder:
            entry["fromResponder"] = True
        doc.setdefault("messages", []).append(entry)

    doc["assistantWorking"] = True
    _write_session(capture, doc)
    try:
        llm_msgs = [{"role": m["role"], "content": m["content"]} for m in doc.get("messages", [])]
        raw = planner._llm_messages(  # noqa: SLF001
            hub,
            _build_qna_system(doc, capture),
            llm_msgs,
            stream_doc=doc,
            stream_role="assistant",
            stream_persist=_stream_persist(capture),
        )
        parsed = _parse_llm_json(raw)
        assistant = planner._sanitize_chat_content(str(parsed.get("assistantMessage") or "Got it."))
        doc.setdefault("messages", []).append({"role": "assistant", "content": assistant})
        _merge_checklist(doc, parsed.get("checklist"))
        brief = parsed.get("brief")
        if isinstance(brief, dict):
            doc["brief"] = {**doc.get("brief", {}), **{k: v for k, v in brief.items() if v}}

        if parsed.get("qnaComplete") and _checklist_all_done(doc):
            doc["phase"] = "briefing_ready"
            doc.setdefault("messages", []).append(
                {
                    "role": "assistant",
                    "content": (
                        "Pre-production brief is **complete**. Click **Enter Studio Editor** to preview your capture, "
                        "test Personal Voice, tune pacing, and approve production-quality generation."
                    ),
                }
            )
    except Exception as exc:
        doc.setdefault("messages", []).append(
            {"role": "assistant", "content": f"Assistant unavailable ({exc}). Check .env API key and retry."}
        )
    finally:
        doc["assistantWorking"] = False
        doc.pop("streamingText", None)
        doc.pop("streamingRole", None)
        _write_session(capture, doc)
    return public_session(capture)


def responder_run_until_done(
    capture: Path,
    preset: str = "student-portfolio",
) -> dict[str, Any]:
    """Run AI Responder until Q&A completes or no pending checklist items remain."""
    return responder_step(capture, preset, max_turns=999)


def responder_step(
    capture: Path,
    preset: str = "student-portfolio",
    *,
    max_turns: int = 1,
) -> dict[str, Any]:
    """Simulated user turns for hands-off Q&A (AI Responder) — advances several checklist items."""
    lock = _action_lock(capture)
    if not lock.acquire(blocking=False):
        return {
            "ok": False,
            "error": "Director busy — wait for the current action to finish.",
            "session": _blocked_session(capture, duplicate=True),
        }

    try:
        doc = _read_session(capture)
        if not doc:
            start_session(capture)
            doc = _read_session(capture)
        assert doc is not None
        if doc.get("phase") != "qna":
            return {"ok": True, "done": True, "session": public_session(capture)}

        hub = _hub_for_capture(capture)
        planner.load_dotenv(hub)
        doc["responderWorking"] = True
        _write_session(capture, doc)
        session: dict[str, Any] = public_session(capture)
        turns_run = 0
        try:
            for _ in range(max_turns):
                doc = _read_session(capture)
                if not doc or doc.get("phase") != "qna":
                    break
                if doc.get("assistantWorking"):
                    for _wait in range(40):
                        time_mod.sleep(0.05)
                        doc = _read_session(capture)
                        if not doc or not doc.get("assistantWorking"):
                            break
                    if doc and doc.get("assistantWorking"):
                        break
                reply = _generate_director_responder_reply_deduped(capture, hub, doc)
                if not reply.strip():
                    break
                session = chat_turn(
                    capture, reply, _skip_action_lock=True, user_from_responder=True
                )
                turns_run += 1
                if session.get("phase") != "qna" or session.get("checklistComplete"):
                    break
        finally:
            doc = _read_session(capture) or {}
            doc["responderWorking"] = False
            _write_session(capture, doc)
        done = session.get("phase") != "qna" or bool(session.get("checklistComplete"))
        return {"ok": True, "done": done, "turns": turns_run, "session": public_session(capture)}
    finally:
        lock.release()


def read_voice_persona(capture: Path) -> dict[str, Any]:
    doc = _read_session(capture) or {}
    persona = {**VOICE_PERSONA_DEFAULTS, **(doc.get("voicePersona") or {})}
    cards = _load_approved_cards()
    nested = cards.get("narratorPersonalSettings") or {}
    vp = cards.get("voicePersona") or {}
    if nested.get("sayRate") is not None:
        persona["sayRate"] = int(nested["sayRate"])
    if nested.get("delivery"):
        persona["delivery"] = str(nested["delivery"])
    if isinstance(vp, dict):
        for key in ("tone", "character", "energy", "formality"):
            if vp.get(key) is not None:
                persona[key] = vp[key]
        if vp.get("notes"):
            persona["personaNotes"] = str(vp["notes"])
    vh = cards.get("voiceHelpers") or {}
    if isinstance(vh, dict) and vh.get("sayRate") is not None:
        persona["sayRate"] = int(vh["sayRate"])
    return persona


def read_voice_profile(capture: Path) -> dict[str, Any]:
    narr = _narrator()
    persona = read_voice_persona(capture)
    doc = _read_session(capture) or {}
    cards = _load_approved_cards()
    spec = build_narrator_spec(capture, persona)
    voice_name = narr.resolve_personal_voice_name(spec, capture) or "Jacob Adkins"
    test_phrase = str(
        doc.get("testPhrase") or cards.get("narratorTestPhrase") or DEFAULT_TEST_PHRASE
    )
    ready, ready_reason = narr.personal_voice_capture_ready(spec, capture)
    test_wav = voice_test_wav_path(capture)
    say_rate = narr.personal_say_rate(spec)
    return {
        "persona": persona,
        "voiceName": voice_name,
        "testPhrase": test_phrase,
        "sayRate": say_rate,
        "delivery": persona.get("delivery", "ladderDocumentary"),
        "deliveryPresets": list(DELIVERY_PRESETS),
        "personalVoiceReady": ready,
        "readyReason": ready_reason,
        "hasLastTest": test_wav.is_file() and test_wav.stat().st_size > 2048,
        "lastTestBytes": test_wav.stat().st_size if test_wav.is_file() else 0,
        "approvedDir": str(approved_dir()),
        "capture": str(capture),
    }


def write_voice_persona(capture: Path, patch: dict[str, Any]) -> dict[str, Any]:
    doc = _read_session(capture)
    if not doc:
        start_session(capture)
        doc = _read_session(capture)
    assert doc is not None
    persona = {**VOICE_PERSONA_DEFAULTS, **(doc.get("voicePersona") or {}), **patch}
    doc["voicePersona"] = persona
    _write_session(capture, doc)

    approved = approved_dir() / "ApprovedCards.json"
    cards: dict[str, Any] = {}
    if approved.is_file():
        try:
            cards = json.loads(approved.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            cards = {}
    nested = dict(cards.get("narratorPersonalSettings") or {})
    nested["sayRate"] = int(persona.get("sayRate", 186))
    nested["delivery"] = str(persona.get("delivery", "ladderDocumentary"))
    nested["personalHumanize"] = bool(persona.get("humanize", True))
    cards["narratorPersonalSettings"] = nested
    cards["narrationDeliveryStyle"] = str(persona.get("tone", "warm-documentary"))
    cards["voicePersona"] = {
        "tone": persona.get("tone"),
        "character": persona.get("character"),
        "energy": persona.get("energy"),
        "formality": persona.get("formality"),
        "notes": persona.get("personaNotes", ""),
    }
    if patch.get("testPhrase"):
        cards["narratorTestPhrase"] = str(patch["testPhrase"])
        doc["testPhrase"] = cards["narratorTestPhrase"]
        _write_session(capture, doc)
    vh = dict(cards.get("voiceHelpers") or {})
    vh["sayRate"] = int(persona.get("sayRate", 186))
    vh["preset"] = str(persona.get("delivery", "ladderDocumentary"))
    cards["voiceHelpers"] = vh
    atomic_write_text(approved, json.dumps(cards, indent=2))
    return persona


def run_voice_test(
    capture: Path,
    text: str | None = None,
    *,
    open_player: bool = True,
) -> dict[str, Any]:
    """Synthesize a short Personal Voice sample with current studio persona (pre–E2E test)."""
    lock = _action_lock(capture)
    if not lock.acquire(blocking=False):
        return {
            "ok": False,
            "error": "Voice test already running — wait for it to finish.",
            "duplicateRequest": True,
        }

    try:
        return _run_voice_test_locked(capture, text, open_player=open_player)
    finally:
        lock.release()


def _run_voice_test_locked(
    capture: Path,
    text: str | None,
    *,
    open_player: bool,
) -> dict[str, Any]:
    narr = _narrator()
    profile = read_voice_profile(capture)
    line = (text or profile["testPhrase"] or DEFAULT_TEST_PHRASE).strip()
    if not line:
        return {"ok": False, "error": "Test phrase is empty."}
    spec = build_narrator_spec(capture, profile["persona"])
    voice_name = profile["voiceName"]
    out = voice_test_wav_path(capture)
    try:
        out.unlink(missing_ok=True)
    except OSError:
        pass
    ok = narr.speech_to_wav(
        line,
        out,
        voice=voice_name,
        engine="personal",
        spec={**spec, "narratorEngine": "personal", "narratorRequirePersonal": True},
        run_dir=capture,
    )
    audible = ok and out.is_file() and out.stat().st_size > 2048 and narr.wav_is_audible(out)
    if not audible:
        try:
            out.unlink(missing_ok=True)
        except OSError:
            pass
        return {
            "ok": False,
            "error": (
                "Personal Voice test failed — no audible audio. "
                "Run authorize in Terminal if needed, then retry."
            ),
            "voiceName": voice_name,
            "sayRate": profile["sayRate"],
            "testPhrase": line,
        }
    if open_player and sys.platform == "darwin":
        subprocess.run(["afplay", str(out)], check=False)
    doc = _read_session(capture) or {}
    if doc:
        doc["testPhrase"] = line
        _write_session(capture, doc)
    write_voice_persona(capture, {"testPhrase": line})
    return {
        "ok": True,
        "voiceName": voice_name,
        "sayRate": profile["sayRate"],
        "testPhrase": line,
        "wavPath": str(out),
        "wavBytes": out.stat().st_size,
        "audioUrl": f"/api/director/voice-test/audio?path={capture}",
        "message": f"Personal Voice test ready ({voice_name}, {profile['sayRate']} wpm).",
    }


def save_narration_script(capture: Path, markdown: str) -> dict[str, Any]:
    guide = capture / "NarrationVoiceGuide.md"
    atomic_write_text(guide, markdown)
    txt = capture / "DemoRecapFullNarration.txt"
    # Keep spoken plain text in sync when user edits markdown manually.
    plain = re.sub(r"^#+ .+$", "", markdown, flags=re.MULTILINE)
    plain = re.sub(r"\*\*|__|\*|_", "", plain)
    atomic_write_text(txt, plain.strip() + "\n")
    return {"ok": True, "path": str(guide), "length": len(markdown)}


def assistant_adjust_voice(capture: Path, instruction: str = "") -> dict[str, Any]:
    hub = _hub_for_capture(capture)
    planner.load_dotenv(hub)
    persona = read_voice_persona(capture)
    guide_path = capture / "NarrationVoiceGuide.md"
    guide_excerpt = ""
    if guide_path.is_file():
        guide_excerpt = guide_path.read_text(encoding="utf-8", errors="replace")[:4000]
    system = (
        "You are a voice director for Personal Voice narration (user's own voice, not TTS). "
        "Return JSON only: "
        '{"assistantMessage":"…","persona":{"tone":"","character":"","energy":0-100,"formality":0-100,'
        '"sayRate":150-220,"delivery":"ladderDocumentary|raw|tvhost","personaNotes":""}}'
    )
    user = instruction or "Suggest studio-grade persona sliders for an educational game-world devlog."
    raw = planner._llm_messages(  # noqa: SLF001
        hub,
        system,
        [{"role": "user", "content": f"Current persona: {json.dumps(persona)}\n\nScript excerpt:\n{guide_excerpt}\n\n{user}"}],
    )
    parsed = _parse_llm_json(raw)
    patch = parsed.get("persona") if isinstance(parsed.get("persona"), dict) else {}
    if patch:
        persona = write_voice_persona(capture, patch)
    return {"ok": True, "assistantMessage": parsed.get("assistantMessage", "Voice persona updated."), "persona": persona}


def _timeline_script_context(capture: Path) -> str:
    tl = capture / "DemoRecapTimeline.json"
    if not tl.is_file():
        return "No timeline yet — run silent compose to lock picture beats."
    try:
        spec = json.loads(tl.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return "Timeline present but unreadable."
    miles = list(spec.get("milestones") or [])[:14]
    if not miles:
        return "Timeline loaded — milestone captions will appear after compose."
    lines = ["Picture beats (write narration to match these on-screen moments):"]
    for m in miles:
        phase = str(m.get("phase") or m.get("chapter") or "")
        sub = str(m.get("sub") or "")
        l1 = str(m.get("line1") or m.get("title") or "").strip()
        l2 = str(m.get("line2") or "").strip()
        chunk = f"- [{phase}/{sub}] {l1}"
        if l2:
            chunk += f" — {l2[:120]}"
        lines.append(chunk)
    hold = spec.get("milestoneHoldSec")
    if hold:
        lines.append(f"- Pacing: milestone holds ~{hold}s")
    return "\n".join(lines)


def _build_script_chat_system(capture: Path, doc: dict[str, Any]) -> str:
    brief = doc.get("brief") or {}
    persona = doc.get("voicePersona") or VOICE_PERSONA_DEFAULTS
    guide_path = capture / "NarrationVoiceGuide.md"
    script_excerpt = ""
    if guide_path.is_file():
        script_excerpt = guide_path.read_text(encoding="utf-8", errors="replace")[:6000]
    return (
        "You are the **Script Director** — write Personal Voice narration **to picture** for an Environment Kit recap.\n"
        "The user watches the silent video preview while you collaborate in chat.\n\n"
        f"{_footage_awareness_block(capture)}\n\n"
        f"## Pre-production brief\n{json.dumps(brief, indent=0)[:2000]}\n\n"
        f"## Voice persona\n{json.dumps(persona, indent=0)}\n\n"
        f"{_timeline_script_context(capture)}\n\n"
        f"## Current script (NarrationVoiceGuide.md)\n{script_excerpt or '(empty — offer to draft from timeline)'}\n\n"
        "## Rules\n"
        "- Teach briefly, then ask ONE script question OR propose concrete lines.\n"
        "- Time copy to holds and milestones — do not narrate past unbuilt footage.\n"
        "- Match brief tone, WPM target, emphasis words from pre-production.\n"
        "- When the user approves a rewrite, set applyScript:true and include full scriptMarkdown.\n"
        "- For discussion only, applyScript:false and keep scriptMarkdown empty.\n"
        "- Opening line, act transitions, and CTA wording are high priority.\n\n"
        "Respond JSON only:\n"
        '{"assistantMessage":"markdown for chat","applyScript":false,'
        '"scriptMarkdown":"full updated markdown when applying changes"}\n'
        "Never mention vendor AI product names."
    )


def script_chat_turn(
    capture: Path,
    user_message: str | None = None,
    *,
    bootstrap: bool = False,
) -> dict[str, Any]:
    """Conversational script development with silent video on screen."""
    hub = _hub_for_capture(capture)
    planner.load_dotenv(hub)
    lock = _action_lock(capture)
    if not lock.acquire(blocking=False):
        return {
            "ok": False,
            "error": "Script Director busy — wait for the current turn.",
            "session": public_session(capture),
        }
    try:
        doc = _read_session(capture)
        if not doc:
            start_session(capture)
            doc = _read_session(capture)
        assert doc is not None
        if _heal_stale_assistant(doc):
            _write_session(capture, doc)
        if doc.get("scriptAssistantWorking"):
            return {
                "ok": False,
                "duplicateRequest": True,
                "session": public_session(capture),
            }

        doc.setdefault("scriptMessages", [])
        if bootstrap and not doc["scriptMessages"]:
            silent = probe_capture_footage(capture)
            has_video = silent.get("hasSilentVideo") or silent.get("timelapseFrames", 0) > 0
            intro = (
                "**Script session** — watch the silent preview on the left while we write narration to picture.\n\n"
                if has_video
                else "**Script session** — run silent compose first so we can time lines to picture. "
                "Until then, we can draft intent from your brief.\n\n"
            )
            doc["scriptMessages"].append(
                {
                    "role": "assistant",
                    "content": (
                        intro
                        + "I'll help with opening hook, act transitions, emphasis words, and the final CTA. "
                        "What's the first line you want viewers to hear?"
                    ),
                }
            )
            _write_session(capture, doc)
            if not user_message:
                return {"ok": True, "session": public_session(capture)}

        if user_message and not bootstrap:
            norm = _normalize_chat(user_message)
            for m in reversed(doc["scriptMessages"]):
                if m.get("role") == "user" and _normalize_chat(str(m.get("content") or "")) == norm:
                    return {"ok": True, "duplicateRequest": True, "session": public_session(capture)}
            doc["scriptMessages"].append({"role": "user", "content": user_message})

        doc["scriptAssistantWorking"] = True
        _write_session(capture, doc)
        try:
            llm_msgs = [{"role": m["role"], "content": m["content"]} for m in doc["scriptMessages"]]
            raw = planner._llm_messages(  # noqa: SLF001
                hub,
                _build_script_chat_system(capture, doc),
                llm_msgs,
                stream_doc=doc,
                stream_role="script",
                stream_persist=_stream_persist(capture),
            )
            parsed = _parse_llm_json(raw)
            assistant = planner._sanitize_chat_content(str(parsed.get("assistantMessage") or "Got it."))
            doc["scriptMessages"].append({"role": "assistant", "content": assistant})
            applied = False
            script_md = parsed.get("scriptMarkdown")
            if parsed.get("applyScript") and isinstance(script_md, str) and len(script_md.strip()) > 80:
                save_narration_script(capture, script_md.strip())
                applied = True
        except Exception as exc:
            doc["scriptMessages"].append(
                {"role": "assistant", "content": f"Script Director unavailable ({exc}). Check .env API key."}
            )
            applied = False
            script_md = None
        finally:
            doc["scriptAssistantWorking"] = False
            doc.pop("streamingText", None)
            doc.pop("streamingRole", None)
            _write_session(capture, doc)

        out: dict[str, Any] = {"ok": True, "session": public_session(capture)}
        if applied and isinstance(script_md, str):
            out["scriptMarkdown"] = script_md
            out["scriptApplied"] = True
        return out
    finally:
        lock.release()


def assistant_adjust_script(capture: Path, instruction: str) -> dict[str, Any]:
    hub = _hub_for_capture(capture)
    planner.load_dotenv(hub)
    guide_path = capture / "NarrationVoiceGuide.md"
    if not guide_path.is_file():
        return {"ok": False, "error": "Narration script not generated yet — run silent compose first."}
    text = guide_path.read_text(encoding="utf-8", errors="replace")
    system = (
        "You edit narration scripts for Personal Voice. Return JSON: "
        '{"assistantMessage":"…","scriptMarkdown":"full updated markdown"}'
    )
    raw = planner._llm_messages(  # noqa: SLF001
        hub,
        system,
        [{"role": "user", "content": f"Instruction: {instruction}\n\nCurrent script:\n{text}"}],
    )
    parsed = _parse_llm_json(raw)
    updated = parsed.get("scriptMarkdown")
    if isinstance(updated, str) and len(updated) > 100:
        atomic_write_text(guide_path, updated)
    return {
        "ok": True,
        "assistantMessage": parsed.get("assistantMessage", "Script updated."),
        "scriptLength": len(updated) if isinstance(updated, str) else len(text),
    }


def enter_editor(capture: Path) -> dict[str, Any]:
    doc = _read_session(capture)
    if not doc:
        start_session(capture)
        doc = _read_session(capture)
    assert doc is not None
    if doc.get("phase") in EDITOR_PHASES:
        return public_session(capture)
    if doc.get("phase") not in BRIEFING_PHASES and doc.get("phase") not in EDITOR_PHASES:
        doc["phase"] = "briefing_ready" if _checklist_all_done(doc) else "qna"
    if doc.get("phase") == "qna" and not _checklist_all_done(doc):
        raise RuntimeError("Complete the pre-production checklist before entering the studio editor.")
    doc["phase"] = "editor"
    _write_session(capture, doc)
    return public_session(capture)


def return_to_briefing(capture: Path) -> dict[str, Any]:
    doc = _read_session(capture)
    if not doc:
        return start_session(capture)
    doc["phase"] = "briefing_ready" if _checklist_all_done(doc) else "qna"
    _write_session(capture, doc)
    return public_session(capture)


def set_phase(capture: Path, phase: str) -> dict[str, Any]:
    doc = _read_session(capture)
    if not doc:
        start_session(capture)
        doc = _read_session(capture)
    assert doc is not None
    doc["phase"] = phase
    if phase == "complete":
        doc["exportApproved"] = True
    _write_session(capture, doc)
    return public_session(capture)


def apply_brief_to_timeline(capture: Path, timeline: dict[str, Any]) -> dict[str, Any]:
    doc = _read_session(capture) or {}
    brief = doc.get("brief") or {}
    out = dict(timeline)
    length = brief.get("targetLengthSec") or brief.get("target_length_sec")
    if isinstance(length, (int, float)) and length > 0:
        persona = read_voice_persona(capture)
        # Scale holds toward target (rough heuristic)
        factor = max(0.6, min(1.4, float(length) / 480.0))
        for key in ("milestoneHoldSec", "subbeatHoldSec"):
            if key in out:
                out[key] = round(float(out[key]) * factor, 2)
    pacing = str(brief.get("pacing", "")).lower()
    if "fast" in pacing:
        out["milestoneHoldSec"] = min(float(out.get("milestoneHoldSec", 14)), 10.0)
        out["subbeatHoldSec"] = min(float(out.get("subbeatHoldSec", 8)), 5.0)
    elif "slow" in pacing or "cinematic" in pacing:
        out["milestoneHoldSec"] = max(float(out.get("milestoneHoldSec", 14)), 16.0)
    return out


def youtube_export_payload(capture: Path) -> dict[str, Any]:
    doc = _read_session(capture) or {}
    brief = doc.get("brief") or {}
    mp4 = capture / "DemoRecapPresentation.mp4"
    if not mp4.is_file() or mp4.stat().st_size < 50_000:
        return {"ok": False, "error": "Final narrated video not ready — approve Personal Voice first."}
    title = brief.get("title") or brief.get("hook") or f"Environment Kit — {capture.name}"
    platform = brief.get("platform") or "YouTube"
    description = (
        f"{brief.get('audience', 'Game development & procedural worlds')}\n\n"
        f"{brief.get('cta', 'Built with Environment Authoring Kit')}\n\n"
        f"#gamedev #procedural #unity"
    )
    return {
        "ok": True,
        "videoPath": str(mp4),
        "title": str(title)[:100],
        "description": description[:5000],
        "platform": platform,
        "studioUrl": "https://studio.youtube.com/",
    }


def run_youtube_export(capture: Path) -> dict[str, Any]:
    payload = youtube_export_payload(capture)
    if not payload.get("ok"):
        return payload
    meta = f"{payload['title']}\n\n{payload['description']}"
    try:
        subprocess.run(["pbcopy"], input=meta.encode("utf-8"), check=False, timeout=5)
    except (FileNotFoundError, subprocess.TimeoutExpired):
        pass
    subprocess.run(["open", payload["videoPath"]], check=False)
    subprocess.run(["open", payload["studioUrl"]], check=False)
    set_phase(capture, "complete")
    return {**payload, "clipboard": True, "message": "Title & description copied — video opened. Upload in YouTube Studio."}


def list_segments(capture: Path) -> list[dict[str, Any]]:
    items: list[dict[str, Any]] = []
    for work_name in ("_presentation_compose", "_presentation_compose_preview120"):
        seg_dir = capture / work_name / "segments"
        if not seg_dir.is_dir():
            continue
        for mp4 in sorted(seg_dir.glob("*.mp4")):
            items.append(
                {
                    "name": mp4.name,
                    "workDir": work_name,
                    "path": str(mp4),
                    "sizeBytes": mp4.stat().st_size,
                }
            )
    return items
