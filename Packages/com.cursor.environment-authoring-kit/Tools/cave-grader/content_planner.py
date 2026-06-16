#!/usr/bin/env python3
"""AI Content Layout Planner — NPC / enemy / props Q&A for MainScene."""
from __future__ import annotations

import json
import os
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import build_planner as bp
import wizard_checklist as wc
import wizard_session_public as wsp
import wizard_tab_research as wtr
from envkit_paths import atomic_write_text
from world_tab_brains import evaluate_tab_brain
import world_state_snapshot as wss

TOOLS = Path(__file__).resolve().parent
SESSION_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildContentLayoutSession.json")
BRIEF_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildContentLayoutBrief.json")
WORLD_SNAPSHOT_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildContentLayoutWorldSnapshot.json")
WORLD_ANCHORS_REL = Path("Assets/EnvironmentKit/ContentLayout/ContentLayoutWorldAnchors.json")

DEFAULT_CHECKLIST: list[dict[str, Any]] = [
    {"id": "scene_scope", "label": "MainScene scope", "done": False, "decision": "", "category": "Scene"},
    {"id": "zones", "label": "Named zones (town, arena, trails)", "done": False, "decision": "", "category": "Scene"},
    {"id": "quest_npcs", "label": "Quest giver NPCs + objectives", "done": False, "decision": "", "category": "NPCs"},
    {"id": "trainer_npcs", "label": "Trainer / coach NPCs", "done": False, "decision": "", "category": "NPCs"},
    {"id": "ambient_npcs", "label": "Ambient flavor NPCs", "done": False, "decision": "", "category": "NPCs"},
    {"id": "blocker_gates", "label": "Gate NPCs / blocked areas", "done": False, "decision": "", "category": "NPCs"},
    {"id": "enemy_patrols", "label": "Enemy spawners + patrol routes", "done": False, "decision": "", "category": "Combat"},
    {"id": "props_layout", "label": "Props, signs, interactables", "done": False, "decision": "", "category": "Props"},
    {"id": "textures", "label": "Texture / material look per prop cluster", "done": False, "decision": "", "category": "Props"},
    {"id": "dialog_hybrid", "label": "Hybrid dialog (Talk branches + Shop tab)", "done": False, "decision": "", "category": "Dialog"},
    {"id": "blockers_training", "label": "Agent training level blockers", "done": False, "decision": "", "category": "Blockers"},
    {"id": "blockers_quest", "label": "Quest prerequisite blockers", "done": False, "decision": "", "category": "Blockers"},
    {"id": "placement", "label": "World positions & facing", "done": False, "decision": "", "category": "Layout"},
    {"id": "playmode_smoke", "label": "Play Mode smoke test plan", "done": False, "decision": "", "category": "Verify"},
]

SYSTEM_QNA = """You are the Environment Kit **Content Layout Planner** for Deep Train Academy MainScene.

**Surface content tab ONLY.** Never discuss tileCount, terrain passes, cave pipeline, procedural grid scope, music, or video — redirect those to the correct wizard tab.

The user describes NPCs, enemies, props, dialog, shops, and blockers. You run a patient checklist interview like the world build planner.

## Target scene
- **MainScene** as it exists when Play starts — use world positions (x,y,z), zone labels, and notes referencing visible landmarks (town, arena, cave mouth, etc.).
- Do NOT assume planner grid markers unless the user mentions them.

## Placement rules (mandatory — enforce in every brief)
- **NPCs** → Polyvania **town_center** plaza sidewalks only (`plazaSidewalks` / town_center slots). Never wild zones.
- **Enemies** → **wild_east / wild_west** spawners outside measured map bounds. Never town blocks or plaza.
- **Props** → worldwide by zone: east_trail, cave_gate, battle_arena; shop props beside Mara at town_center only.
- Export scene snapshot first when possible: **Game → Export Content Layout World Snapshot**.

## NPC roles (v1)
- **quest_giver** — branching Talk dialog + objectives + rewards
- **trainer** — links to agent behavior-cloning / training milestones
- **ambient** — flavor dialog only
- **blocker_gate** — blocks passage or options until prerequisites met
- Merchants use **hybrid dialog**: Talk tab (branching chat) + **Shop** tab (buy items) + Leave

## Blockers (record in contentPlan)
- `agent_training_level` — min grade 0–100 or milestone id
- `quest_completed` — questId must be done

Blocked dialog options and shop items include a `blockers[]` array. Show `{ "type", "questId" | "minTrainingGrade", "hint" }`.

## Visual catalog (CC0 models)
Stable brief ids map to CC0 prefabs in `Assets/EnvironmentKit/ContentLayout/ContentLayoutVisualCatalog.json`.
Reuse ids like `npc_mara_dispatch`, `npc_sera_scout`, `enemy_spawner_east_trail`, `prop_depot_sign` when possible.

## World-aware placement (mandatory)
1. **Export scene snapshot first** in Unity: **Game → Export Content Layout World Snapshot** (open MainScene with PolyvaniaTownMap placed).
2. Read `CaveBuildContentLayoutWorldSnapshot.json` — **version 4** includes scene-measured zone centers, map bounds, plaza sidewalk samples.
3. Fallback: `ContentLayoutWorldAnchors.json` slot offsets (static hubOffset guesses).
**town_center = Polyvania four-way intersection** — all NPCs on plaza sidewalks from `plazaSidewalks` or town_center slots.
Enemy spawners use **wild_east / wild_west** centers (outside measured map bounds), never town blocks.
Props use east_trail / cave_gate / battle_arena zone centers; shop props only beside Mara at town_center.
Use slot offsets from snapshot; Y=0 for feet (Unity snaps). Elevated props use `height` meters above ground.

## Interview rules (while qnaComplete is false)
- Ask **ONE** question per turn tied to the **next pending** checklist id (including any **follow-up** items appended below).
- Include **teachingMoment** (2–3 sentences): why placement, dialog ids, or blockers matter for Unity runtime.
- Mark checklist items `done:true` only when the user clearly answered **that specific topic** with implementable detail.
- If an answer is vague, off-topic, or missing sub-parts, either leave the item pending or add **`checklistAdditions`**: `[{ "id": "followup_<topic>", "label": "...", "category": "..." }]`.
- Do **NOT** mark `playmode_smoke` done unless the decision lists **requiredNpcIds** and ordered **steps[]**.
- Do **NOT** mark `ambient_npcs` done until each ambient NPC has **2–3 dialog node ids**.
- Set `qnaComplete:true` only when **every** checklist row (base + follow-ups) is `done:true` with concrete decisions **and** `brief.contentPlan` is complete enough to apply in Unity.

## Dynamic checklist (session stays open until quality bar met)
- When base items are done but answers are thin, add focused follow-up rows via `checklistAdditions` instead of forcing `qnaComplete`.
- Re-open a topic only if the user explicitly asks to revise it.
- Example additions: `followup_ambient_dialog`, `followup_playmode_smoke`, `followup_enemy_patrols`, `followup_mara_dialog`.

## Output brief (when qnaComplete true)
Include `brief.contentPlan`:
{
  "sceneName": "MainScene",
  "zones": [{ "id", "label", "notes" }],
  "npcs": [{
    "id", "role", "displayName",
    "worldPosition": { "x", "y", "z" }, "rotationY",
    "zoneId",
    "dialog": {
      "greeting": "string",
      "nodes": [{ "id", "text", "options": [{ "label", "nextId", "blockers": [], "opensShop": false }] }],
      "shop": { "currency": "gold|gems", "items": [{ "id", "name", "price", "blockers": [] }] }
    },
    "gateBlockers": []
  }],
  "enemies": [{
    "id", "displayName",
    "worldPosition": { "x", "y", "z" },
    "patrolWaypoints": [{ "x", "y", "z" }],
    "blockers": []
  }],
  "props": [{
    "id", "label", "worldPosition": { "x", "y", "z" },
    "textureHint", "blockers": []
  }],
  "playmodeSmokeTest": {
    "requiredNpcIds": ["npc_id"],
    "steps": ["Walk to NPC", "Open dialog", "Verify shop tab"]
  }
}

Respond JSON only:
{
  "assistantMessage": "markdown for chat",
  "teachingMoment": "optional",
  "qnaComplete": false,
  "brief": null,
  "checklist": [],
  "checklistAdditions": []
}
"""


def _utc() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _session_path(hub: Path) -> Path:
    return hub.expanduser().resolve() / SESSION_REL


def _brief_path(hub: Path) -> Path:
    return hub.expanduser().resolve() / BRIEF_REL


def _read_session(hub: Path) -> dict[str, Any]:
    p = _session_path(hub)
    if not p.is_file():
        return {}
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return {}


def _write_session(hub: Path, doc: dict[str, Any]) -> None:
    p = _session_path(hub)
    p.parent.mkdir(parents=True, exist_ok=True)
    doc["updatedUtc"] = _utc()
    atomic_write_text(p, json.dumps(doc, indent=2) + "\n")


def _session_still_current(hub: Path, doc: dict[str, Any]) -> bool:
    current = _read_session(hub)
    if not current:
        return False
    return str(current.get("createdUtc") or "") == str(doc.get("createdUtc") or "")


def _ensure_checklist_shape(doc: dict[str, Any]) -> None:
    wc.ensure_checklist_shape(doc, DEFAULT_CHECKLIST)


def _apply_content_quality_pass(doc: dict[str, Any], parsed: dict[str, Any]) -> None:
    """Re-open weak decisions and append follow-up checklist rows until answers are right."""
    wc.merge_additions(doc, parsed.get("checklistAdditions"), DEFAULT_CHECKLIST)

    reopen: list[str] = []
    for item in doc.get("checklist") or []:
        iid = str(item.get("id") or "")
        if not item.get("done") or not iid:
            continue
        if iid.startswith("followup_"):
            continue
        if wc.decision_looks_weak(iid, str(item.get("decision") or "")):
            reopen.append(iid)

    if reopen:
        wc.reopen_items(doc, reopen, DEFAULT_CHECKLIST)


def _build_qna_system(doc: dict[str, Any]) -> str:
    next_id = wc.next_pending_id(doc, DEFAULT_CHECKLIST) or "none"
    injection = wtr.build_prompt_injection("surface-content", doc)
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
        SYSTEM_QNA
        + "\n\n"
        + injection
        + "\n\n"
        + world_block
        + "\n\n## Checklist rules (critical)\n"
        "- Include `\"checklist\"` every turn; mark answered items `done:true` with concrete `decision`.\n"
        "- Add `\"checklistAdditions\"` when new sub-topics need their own row.\n"
        "- `assistantMessage` is user-facing only — never raw JSON.\n"
        + f"\nNext pending checklist id: {next_id}\n"
        + f"Current checklist: {json.dumps(wc.ensure_checklist_shape(doc, DEFAULT_CHECKLIST))}\n"
    )


def public_session(hub: Path) -> dict[str, Any]:
    doc = _read_session(hub)
    if doc and bp._clear_stale_streaming(doc):
        _write_session(hub, doc)
    intro = (
        "## Surface content\n\n"
        "Plan MainScene / surface NPCs, monsters, items, quests, dialog, shops.\n\n"
        "**Not here:** terrain tiles (Terrain tab), cave structure (Caves tab)."
    )
    return wsp.public_tab_session(
        doc,
        tab_id="surface-content",
        help_intro=intro,
        default_checklist=DEFAULT_CHECKLIST,
        brief_rel=str(BRIEF_REL),
    )


def session_pulse(hub: Path) -> dict[str, Any]:
    doc = _read_session(hub)
    if doc and bp._clear_stale_streaming(doc):
        _write_session(hub, doc)
    return wsp.session_pulse(doc, DEFAULT_CHECKLIST)


def start_session(
    hub: Path,
    user_message: str,
    *,
    internet_research: bool = True,
) -> dict[str, Any]:
    bp.load_dotenv(hub)
    doc = {
        "version": 1,
        "kind": "content_layout",
        "tabId": "surface-content",
        "phase": "qna",
        "internetResearch": bool(internet_research),
        "sceneName": "MainScene",
        "messages": [{"role": "user", "content": user_message}],
        "brief": None,
        "researchBundle": None,
        "checklist": [dict(c) for c in DEFAULT_CHECKLIST],
        "cursorWorking": False,
        "createdUtc": _utc(),
    }
    return _qna_turn(hub, doc, bootstrap=True)


def chat_turn(hub: Path, user_message: str) -> dict[str, Any]:
    bp.load_dotenv(hub)
    doc = _read_session(hub)
    if not doc:
        raise RuntimeError("No content session — call /api/content/start first")
    if doc.get("phase") not in ("qna",):
        raise RuntimeError(f"Content planner not in Q&A (phase={doc.get('phase')})")
    if wsp.session_busy(doc):
        raise RuntimeError("Wait for the current reply to finish")
    doc.setdefault("messages", []).append({"role": "user", "content": user_message})
    return _qna_turn(hub, doc, bootstrap=False)


def resume_qna(hub: Path) -> dict[str, Any]:
    bp.load_dotenv(hub)
    doc = _read_session(hub)
    if not doc:
        raise RuntimeError("No content session")
    if doc.get("phase") == "cancelled":
        doc["phase"] = "qna"
    return _qna_turn(hub, doc, bootstrap=True)


def _qna_turn(hub: Path, doc: dict[str, Any], *, bootstrap: bool) -> dict[str, Any]:
    wc.ensure_checklist_shape(doc, DEFAULT_CHECKLIST)
    user_before = wsp.latest_user_message(doc)

    if bootstrap and doc.get("internetResearch") and not doc.get("researchBundle"):
        doc["cursorWorking"] = True
        doc["phase"] = "research"
        _write_session(hub, doc)
        try:
            wtr.maybe_run_session_research(hub, "surface-content", doc, user_before)
        finally:
            doc["phase"] = "qna"
            doc["cursorWorking"] = False

    pending_before = wc.next_pending_id(doc, DEFAULT_CHECKLIST)

    doc["cursorWorking"] = True
    _write_session(hub, doc)
    try:
        llm_msgs = [{"role": m["role"], "content": m["content"]} for m in doc.get("messages", [])]
        raw = bp._llm_messages(hub, _build_qna_system(doc), llm_msgs, stream_doc=doc, stream_role="assistant")
        parsed = bp._parse_llm_json(raw)
        assistant = bp._sanitize_chat_content(parsed.get("assistantMessage") or "OK.")
        if not assistant.strip():
            assistant = bp._sanitize_chat_content(raw) or "OK."
        doc.setdefault("messages", []).append({"role": "assistant", "content": assistant})
        wc.merge_checklist(
            doc,
            parsed.get("checklist"),
            DEFAULT_CHECKLIST,
            latest_user=user_before,
            allow_auto_fallback=False,
            expected_pending_id=pending_before,
        )
        _apply_content_quality_pass(doc, parsed)
        # Run rule-first tab brain for audit + deterministic reopen decisions.
        brain_result = evaluate_tab_brain("surface-content", doc)
        if brain_result.reopen_checklist_ids:
            wc.reopen_items(doc, brain_result.reopen_checklist_ids, DEFAULT_CHECKLIST)
        if brain_result.followup_additions:
            wc.merge_additions(
                doc,
                [
                    {"id": a.id, "label": a.label, "category": a.category}
                    for a in brain_result.followup_additions
                ],
                DEFAULT_CHECKLIST,
            )

        move_on = bp._user_wants_move_on(doc)
        all_done = wc.all_done(doc, DEFAULT_CHECKLIST)
        wants_complete = bool(parsed.get("qnaComplete"))

        if parsed.get("brief") and isinstance(parsed["brief"], dict):
            doc["brief"] = {**(doc.get("brief") or {}), **parsed["brief"]}

        if wants_complete:
            if not all_done and not move_on:
                next_id = wc.next_pending_id(doc, DEFAULT_CHECKLIST)
                if next_id:
                    doc.setdefault("messages", []).append(
                        {
                            "role": "assistant",
                            "content": (
                                f"**Still on the checklist:** {wc.label_for_id(doc, next_id, DEFAULT_CHECKLIST)} — "
                                "answer that topic before finalizing."
                            ),
                        }
                    )
            elif all_done or move_on:
                if not doc.get("brief"):
                    doc["brief"] = parsed.get("brief") or {"contentPlan": {"sceneName": "MainScene"}}
                doc["phase"] = "awaiting_approval"
                doc.setdefault("messages", []).append(
                    {
                        "role": "assistant",
                        "content": (
                            "Content layout brief is **ready for review**. Check NPC dialog, shop items, "
                            "blockers, and patrol paths — then **Approve & apply to MainScene**."
                        ),
                    }
                )
    except Exception as exc:
        doc.setdefault("messages", []).append(
            {"role": "assistant", "content": f"Assistant unavailable ({exc}). Check CURSOR_API_KEY in cave-grader/.env"}
        )
    finally:
        if not _session_still_current(hub, doc):
            return public_session(hub)
        doc["cursorWorking"] = False
        doc.pop("streamingText", None)
        doc.pop("streamingRole", None)
        _write_session(hub, doc)
    return public_session(hub)


def _normalize_brief_for_unity(brief: dict[str, Any]) -> dict[str, Any]:
    """Ensure contentPlan arrays exist for Unity JsonUtility."""
    plan = brief.setdefault("contentPlan", {})
    plan.setdefault("sceneName", brief.get("sceneName") or "MainScene")
    plan.setdefault("zones", plan.get("zones") or [])
    plan.setdefault("npcs", plan.get("npcs") or [])
    plan.setdefault("enemies", plan.get("enemies") or [])
    plan.setdefault("props", plan.get("props") or [])
    plan.setdefault(
        "playmodeSmokeTest",
        plan.get("playmodeSmokeTest")
        or {"requiredNpcIds": [], "steps": []},
    )
    for npc in plan.get("npcs") or []:
        if not isinstance(npc, dict):
            continue
        wp = npc.get("worldPosition")
        if isinstance(wp, dict):
            npc["worldPosition"] = {
                "x": float(wp.get("x", 0)),
                "y": float(wp.get("y", 0)),
                "z": float(wp.get("z", 0)),
            }
        dialog = npc.get("dialog")
        if isinstance(dialog, dict):
            if dialog.get("shop") is None:
                dialog.pop("shop", None)
            for node in dialog.get("nodes") or []:
                if not isinstance(node, dict):
                    continue
                for opt in node.get("options") or []:
                    if isinstance(opt, dict) and opt.get("nextId") is None:
                        opt["nextId"] = ""
    for enemy in plan.get("enemies") or []:
        if isinstance(enemy, dict) and isinstance(enemy.get("worldPosition"), dict):
            wp = enemy["worldPosition"]
            enemy["worldPosition"] = {
                "x": float(wp.get("x", 0)),
                "y": float(wp.get("y", 0)),
                "z": float(wp.get("z", 0)),
            }
    for prop in plan.get("props") or []:
        if isinstance(prop, dict) and isinstance(prop.get("worldPosition"), dict):
            wp = prop["worldPosition"]
            prop["worldPosition"] = {
                "x": float(wp.get("x", 0)),
                "y": float(wp.get("y", 0)),
                "z": float(wp.get("z", 0)),
            }
    return brief


def _read_json_file(path: Path) -> dict[str, Any]:
    if not path.is_file():
        return {}
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        return data if isinstance(data, dict) else {}
    except (json.JSONDecodeError, OSError):
        return {}


def _load_world_snapshot(hub: Path) -> dict[str, Any]:
    hub = hub.expanduser().resolve()
    snapshot = _read_json_file(hub / WORLD_SNAPSHOT_REL)
    anchors = _read_json_file(hub / WORLD_ANCHORS_REL)

    if anchors.get("version", 0) >= 2:
        for key in ("npcSlots", "enemySlots", "propSlots", "landmark"):
            if key in anchors:
                snapshot[key] = anchors[key]
        if anchors.get("version", 0) >= 3 and anchors.get("zones"):
            snap_zones = snapshot.setdefault("zones", {})
            for zid, zdef in anchors["zones"].items():
                if zid not in snap_zones:
                    snap_zones[zid] = zdef

    return snapshot or anchors


def _zone_center(snapshot: dict[str, Any], zone_id: str) -> dict[str, float] | None:
    zones = snapshot.get("zones") or {}
    zone = zones.get(zone_id) if isinstance(zones, dict) else None
    if not isinstance(zone, dict):
        return None
    center = zone.get("center")
    if isinstance(center, dict):
        return {
            "x": float(center.get("x", 0)),
            "y": float(center.get("y", 0)),
            "z": float(center.get("z", 0)),
        }
    hub_offset = zone.get("hubOffset")
    if isinstance(hub_offset, dict):
        off_x = float(hub_offset.get("x", 0))
        off_z = float(hub_offset.get("z", 0))
    elif "hubOffsetX" in zone or "hubOffsetZ" in zone:
        off_x = float(zone.get("hubOffsetX", 0))
        off_z = float(zone.get("hubOffsetZ", 0))
    else:
        return None
    if zone_id == "town_center":
        base = {"x": 0.0, "y": 0.0, "z": 0.0}
        spawn = snapshot.get("playerSpawn")
        if isinstance(spawn, dict):
            base = {
                "x": float(spawn.get("x", 0)),
                "y": float(spawn.get("y", 0)),
                "z": float(spawn.get("z", 0)),
            }
        return {
            "x": base["x"] + off_x,
            "y": base["y"],
            "z": base["z"] + off_z,
        }
    town = _zone_center(snapshot, "town_center")
    if town is None:
        town = {"x": 0.0, "y": 0.0, "z": 0.0}
    return {
        "x": town["x"] + off_x,
        "y": town["y"],
        "z": town["z"] + off_z,
    }


def _apply_slot_position(
    snapshot: dict[str, Any],
    entity: dict[str, Any],
    slot_key: str,
    slots: dict[str, Any],
) -> None:
    slot = slots.get(slot_key) if isinstance(slots, dict) else None
    if not isinstance(slot, dict):
        return
    zone_id = slot.get("zoneId") or entity.get("zoneId")
    center = _zone_center(snapshot, str(zone_id or ""))
    if center is None:
        return
    offset = slot.get("offset") if isinstance(slot.get("offset"), dict) else {}
    off_x = float(slot.get("offsetX", offset.get("x", 0)))
    off_z = float(slot.get("offsetZ", offset.get("z", 0)))
    height = float(slot.get("height", 0))
    entity["zoneId"] = zone_id
    entity["worldPosition"] = {
        "x": center["x"] + off_x,
        "y": height if height >= 1 else float(offset.get("y", 0.2)),
        "z": center["z"] + off_z,
    }
    if slot.get("rotationY") is not None and str(entity.get("id", "")).startswith("npc_"):
        entity["rotationY"] = float(slot["rotationY"])


def _apply_world_snapshot_to_brief(brief: dict[str, Any], hub: Path) -> dict[str, Any]:
    snapshot = _load_world_snapshot(hub)
    if not snapshot:
        return brief
    plan = brief.setdefault("contentPlan", {})
    for npc in plan.get("npcs") or []:
        if isinstance(npc, dict) and npc.get("id"):
            _apply_slot_position(snapshot, npc, str(npc["id"]), snapshot.get("npcSlots") or {})
    for enemy in plan.get("enemies") or []:
        if isinstance(enemy, dict) and enemy.get("id"):
            _apply_slot_position(snapshot, enemy, str(enemy["id"]), snapshot.get("enemySlots") or {})
    for prop in plan.get("props") or []:
        if isinstance(prop, dict) and prop.get("id"):
            _apply_slot_position(snapshot, prop, str(prop["id"]), snapshot.get("propSlots") or {})
    brief["worldSnapshotAppliedUtc"] = _utc()
    return brief


def approve_layout(hub: Path, approved: bool, feedback: str = "") -> dict[str, Any]:
    doc = _read_session(hub)
    if not doc:
        raise RuntimeError("No content session")
    if not approved:
        doc["phase"] = "qna"
        doc.setdefault("messages", []).append(
            {"role": "user", "content": feedback or "Please revise the content layout."}
        )
        _write_session(hub, doc)
        return _qna_turn(hub, doc, bootstrap=False)

    brief = _normalize_brief_for_unity(doc.get("brief") or {})
    brief = _apply_world_snapshot_to_brief(brief, hub)
    brief["version"] = 1
    brief["sceneName"] = brief.get("sceneName") or doc.get("sceneName") or "MainScene"
    brief["finalizedUtc"] = _utc()
    if "contentPlan" not in brief:
        brief["contentPlan"] = {}

    out_path = _brief_path(hub)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    atomic_write_text(out_path, json.dumps(brief, indent=2) + "\n")

    doc["phase"] = "finalized"
    doc["gradePassed"] = True
    doc.setdefault("messages", []).append(
        {
            "role": "assistant",
            "content": (
                f"Layout brief written to `{BRIEF_REL}`. "
                "Unity will auto-apply surface content and save MainScene — keep the Editor open."
            ),
        }
    )
    _write_session(hub, doc)
    try:
        from wizard_tab_apply import notify_tab_approved

        notify_tab_approved(hub, "surface-content")
    except Exception:
        pass
    return public_session(hub)


def reset_session(hub: Path) -> dict[str, Any]:
    fresh = {
        "version": 1,
        "kind": "content_layout",
        "tabId": "surface-content",
        "phase": "idle",
        "internetResearch": True,
        "sceneName": "MainScene",
        "messages": [],
        "brief": None,
        "researchBundle": None,
        "checklist": [dict(c) for c in DEFAULT_CHECKLIST],
        "cursorWorking": False,
        "autoRespondActive": False,
        "autoRespondPreset": None,
        "helpSeen": False,
        "createdUtc": _utc(),
    }
    _write_session(hub, fresh)
    return public_session(hub)


# --- AI Responder (auto-fill Q&A for content layout) ---

CONTENT_PRESETS: dict[str, dict[str, Any]] = {
    "polyvania_social_hub": {
        "title": "Polyvania social hub (recommended)",
        "kickoff": (
            "MainScene Polyvania map: **all NPCs on the four-way plaza** (town_center sidewalks). "
            "Mara shop west + depot sign/crate/pennant beside her only. Sigrid east, Sera south, Lumen north, Jonas bench NE, Ren coach NW. "
            "Props spread to zone arms: fork signs on east road, cave arch north, arena banners west — not stacked on NPCs. "
            "Enemy spawners only in wild_east / wild_west outside the city, never in plaza or road arms."
        ),
        "description": "Plaza NPC cluster, world-scattered props, wild-only combat.",
    },
    "town_hub": {
        "title": "Town hub",
        "kickoff": (
            "MainScene Polyvania town hub: quest giver at the gate, ambient villagers, "
            "one trainer near the rail depot, blocker NPC until intro quest completes, "
            "minimal enemy patrols outside town."
        ),
        "description": "Town-focused — NPC dialog heavy, light combat, shop after first quest.",
    },
    "arena_trainers": {
        "title": "Arena & trainers",
        "kickoff": (
            "MainScene battle arena zone: trainer NPC coaches behavior cloning, "
            "shop unlocks after agent training grade 70%, patrol enemies on arena perimeter, "
            "quest NPC links competition milestones."
        ),
        "description": "Training/competition focus — hybrid Talk+Shop, training-level blockers.",
    },
    "full_mainscene": {
        "title": "Full MainScene",
        "kickoff": (
            "Full MainScene as currently built: town NPCs, arena trainers, cave gate blocker, "
            "east-path enemy patrols, props/signs with texture notes, playmode smoke on 3 key NPCs."
        ),
        "description": "Complete pass — all checklist topics for the live MainScene layout.",
    },
}

CONTENT_TOPIC_HINTS: dict[str, str] = {
    "scene_scope": "Use the existing MainScene geometry — town, arena, cave mouth — not planner grid tiles.",
    "zones": "Name zones: town_center, battle_arena, east_trail, cave_gate.",
    "quest_npcs": "At least one quest giver with branching dialog and quest_completed rewards.",
    "trainer_npcs": "Trainer explains BC; shop blocked until agent_training_level >= 70.",
    "ambient_npcs": "2–3 flavor NPCs with short Talk-only lines.",
    "blocker_gates": "Gate NPC or collider zone blocked until prerequisite quest.",
    "enemy_patrols": "1–2 patrol spawners with 3–4 waypoints each.",
    "props_layout": "Signs, crates, lanterns at zone entrances.",
    "textures": "Weathered wood signs, slate stone props, cyan accent banners.",
    "dialog_hybrid": "Every merchant/quest NPC: Talk tab + Shop tab + Leave.",
    "blockers_training": "Document minTrainingGrade per shop item or dialog option.",
    "blockers_quest": "Document questId prerequisites.",
    "placement": "ContentLayoutWorldAnchors v3: ALL npcSlots use town_center; enemySlots use wild_east/wild_west only; propSlots at east_trail/cave_gate/battle_arena except shop props beside npc_mara_dispatch.",
    "playmode_smoke": "List requiredNpcIds to interact with in Play Mode.",
    "followup_ambient_dialog": "Author dlg_* nodes for npc_ambient_sigrid and npc_ambient_jonas — flavor only, no shop.",
    "followup_enemy_patrols": "3–4 patrol waypoints per wild-zone spawner with world offsets.",
    "followup_playmode_smoke": "requiredNpcIds[] plus ordered steps[] with expected outcomes (Mara shop, Lumen seal, Sera fork, Ren grade gate).",
}

SYSTEM_AUTO_USER = """You simulate the human author answering the Content Layout Planner interview.

Preset: {title} — {description}

Answer ONLY the checklist topic [{next_topic}] ({next_label}).
One short paragraph with concrete names, positions, dialog beats, blocker ids, and grades.
{topic_hint}

Do NOT answer other checklist items. No markdown JSON. Plain chat text only.
{prior_block}
{retry_block}
"""


def _preset_or_raise(preset: str) -> dict[str, Any]:
    if preset not in CONTENT_PRESETS:
        raise RuntimeError(f"Unknown content preset: {preset} (use town_hub, arena_trainers, full_mainscene)")
    return CONTENT_PRESETS[preset]


def _last_assistant_message(doc: dict[str, Any]) -> str:
    for m in reversed(doc.get("messages") or []):
        if m.get("role") == "assistant":
            return str(m.get("content") or "")
    return ""


def _generate_content_auto_reply(hub: Path, doc: dict[str, Any], preset: str) -> str:
    spec = _preset_or_raise(preset)
    next_id = wc.next_pending_id(doc, DEFAULT_CHECKLIST) or "scene_scope"
    hint = CONTENT_TOPIC_HINTS.get(next_id, "")
    world_state = {}
    try:
        world_state = wss.build_world_state(hub)
    except Exception:
        world_state = {}
    if world_state:
        doc["worldState"] = world_state
    world_block = wss.build_prompt_block(world_state) if world_state else ""
    system = SYSTEM_AUTO_USER.format(
        title=spec["title"],
        description=spec["description"],
        next_topic=next_id,
        next_label=wc.label_for_id(doc, next_id, DEFAULT_CHECKLIST),
        topic_hint=hint,
        prior_block=bp._format_prior_replies_block(doc) if hasattr(bp, "_format_prior_replies_block") else "",
        retry_block="",
    )
    if world_block:
        system += f"\n\n{world_block}\nUse this world-state matrix when choosing ids/zones/positions."
    planner_msg = ""
    for m in reversed(doc.get("messages") or []):
        if m.get("role") == "assistant":
            planner_msg = bp._sanitize_chat_content(str(m.get("content") or ""))
            break
    user_prompt = (
        f"Planner asked:\n{planner_msg or 'Ask the first content layout question.'}\n\n"
        f"Write one user reply for checklist [{next_id}] only."
    )
    doc["cursorWorking"] = True
    doc["streamingRole"] = "user"
    _write_session(hub, doc)
    try:
        return bp._llm_messages(
            hub,
            system,
            [{"role": "user", "content": user_prompt}],
            stream_doc=doc,
            stream_role="user",
            json_response=False,
        ).strip()
    finally:
        doc = _read_session(hub) or doc
        doc["cursorWorking"] = False
        doc.pop("streamingText", None)
        doc.pop("streamingRole", None)
        _write_session(hub, doc)


def _auto_respond_one_turn(hub: Path, preset: str) -> dict[str, Any]:
    doc = _read_session(hub) or {}
    phase = doc.get("phase")

    if phase == "awaiting_approval":
        doc["autoRespondActive"] = False
        doc["cursorWorking"] = False
        _write_session(hub, doc)
        return {"done": True, "session": public_session(hub)}

    if phase != "qna":
        raise RuntimeError(f"Content auto-respond stopped in phase {phase}")

    if wsp.session_busy(doc):
        return {"done": False, "waiting": True, "session": public_session(hub)}

    doc["autoRespondActive"] = True
    doc["autoRespondPreset"] = preset
    doc["cursorWorking"] = True
    _write_session(hub, doc)

    try:
        if wc.all_done(doc, DEFAULT_CHECKLIST):
            chat_turn(hub, "move on — finalize the content layout brief for review.")
        else:
            reply = _generate_content_auto_reply(hub, doc, preset)
            if not reply:
                raise RuntimeError("AI responder returned empty text")
            chat_turn(hub, reply)
    finally:
        doc = _read_session(hub) or {}
        done = doc.get("phase") == "awaiting_approval"
        if done:
            doc["autoRespondActive"] = False
        doc["cursorWorking"] = False
        doc.pop("streamingText", None)
        _write_session(hub, doc)

    return {"done": done, "session": public_session(hub)}


def auto_respond_step(hub: Path, preset: str, *, internet_research: bool = True) -> dict[str, Any]:
    bp.load_dotenv(hub)
    _preset_or_raise(preset)
    doc = _read_session(hub)
    if not doc or doc.get("phase") == "cancelled":
        start_session(hub, _preset_or_raise(preset)["kickoff"], internet_research=internet_research)
    else:
        # If research was toggled on after session creation, apply it to this session.
        if internet_research and not doc.get("internetResearch"):
            doc["internetResearch"] = True
        if internet_research and not doc.get("researchBundle"):
            user_before = wsp.latest_user_message(doc)
            doc["cursorWorking"] = True
            doc["phase"] = "research"
            _write_session(hub, doc)
            try:
                wtr.maybe_run_session_research(hub, "surface-content", doc, user_before)
            finally:
                doc["phase"] = "qna"
                doc["cursorWorking"] = False
            _write_session(hub, doc)
        else:
            _write_session(hub, doc)
    return _auto_respond_one_turn(hub, preset)


def auto_respond_until_approval(hub: Path, preset: str, *, internet_research: bool = True) -> dict[str, Any]:
    bp.load_dotenv(hub)
    _preset_or_raise(preset)
    doc = _read_session(hub)
    if not doc or doc.get("phase") == "cancelled":
        start_session(hub, _preset_or_raise(preset)["kickoff"], internet_research=internet_research)

    for _ in range(24):
        result = _auto_respond_one_turn(hub, preset)
        if result.get("done"):
            return result.get("session") or public_session(hub)
        if result.get("waiting"):
            continue
    return public_session(hub)

