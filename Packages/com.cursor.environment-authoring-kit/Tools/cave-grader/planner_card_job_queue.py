#!/usr/bin/env python3
"""One-card-at-a-time paced queue for concept card 3D previews and mesh generation."""
from __future__ import annotations

import threading
import time
from pathlib import Path
from typing import Any, Callable

_HUB_WORKERS: dict[str, threading.Thread] = {}
_HUB_LOCKS: dict[str, threading.Lock] = {}

ProgressFn = Callable[[str, int, str], None]


def _hub_key(hub: Path) -> str:
    return str(hub.resolve())


def set_card_job_progress(
    doc: dict[str, Any], card_id: str, job_type: str, phase: str, progress: int, message: str
) -> None:
    payload = {
        "cardId": card_id,
        "jobType": job_type,
        "phase": phase,
        "progress": max(0, min(100, int(progress))),
        "message": message,
    }
    doc["cardPacedActive"] = payload
    for card in doc.get("conceptCards") or []:
        if card.get("id") == card_id:
            card["jobProgress"] = dict(payload)
            break


def clear_card_job_progress(doc: dict[str, Any], card_id: str) -> None:
    active = doc.get("cardPacedActive") or {}
    if active.get("cardId") == card_id:
        doc.pop("cardPacedActive", None)
    for card in doc.get("conceptCards") or []:
        if card.get("id") == card_id:
            card.pop("jobProgress", None)
            break


def enqueue_card_job(
    hub: Path,
    card_id: str,
    job_type: str,
    *,
    front: bool = False,
    sculpt_hint: str | None = None,
    revision_note: str | None = None,
) -> None:
    from build_planner import _read_session, _write_session

    doc = _read_session(hub) or {}
    if not doc.get("conceptCards"):
        return
    queue: list[dict[str, str]] = list(doc.get("cardPacedQueue") or [])
    entry: dict[str, str] = {"cardId": card_id, "jobType": job_type}
    if sculpt_hint:
        entry["sculptHint"] = sculpt_hint
    if revision_note is not None:
        entry["revisionNote"] = revision_note
    queue = [
        e
        for e in queue
        if not (e.get("cardId") == card_id and e.get("jobType") == job_type)
    ]
    if front:
        queue.insert(0, entry)
    else:
        queue.append(entry)
    doc["cardPacedQueue"] = queue
    set_card_job_progress(doc, card_id, job_type, "queued", 2, "Waiting in paced queue…")
    _write_session(hub, doc)
    _ensure_worker(hub)


def bootstrap_preview_jobs(hub: Path, doc: dict[str, Any]) -> bool:
    """Enqueue load_preview for kit NPC/enemy cards — not AI-mesh props."""
    from planner_asset_catalog import (
        _is_generated_prop_prefab,
        card_image_is_placeholder,
        card_wants_ai_mesh,
    )

    changed = False
    queue: list[dict[str, str]] = list(doc.get("cardPacedQueue") or [])
    active = doc.get("cardPacedActive") or {}
    active_id = active.get("cardId")
    for card in doc.get("conceptCards") or []:
        cid = str(card.get("id") or "")
        if not cid.startswith("asset:"):
            continue
        if not card.get("prefabPath"):
            continue
        if card_wants_ai_mesh(card) and not _is_generated_prop_prefab(str(card.get("prefabPath") or "")):
            continue
        if not card_image_is_placeholder(hub, card):
            continue
        if active_id == cid:
            continue
        entry = {"cardId": cid, "jobType": "load_preview"}
        if any(
            e.get("cardId") == cid and e.get("jobType") == "load_preview" for e in queue
        ):
            continue
        queue.append(entry)
        changed = True
    if changed:
        doc["cardPacedQueue"] = queue
    return changed


def bootstrap_ai_mesh_jobs(hub: Path, doc: dict[str, Any]) -> bool:
    """Queue generate_mesh for props/collectibles that still use kit placeholders."""
    from planner_asset_catalog import (
        _is_generated_prop_prefab,
        card_can_generate_mesh,
        card_wants_ai_mesh,
    )

    changed = False
    queue: list[dict[str, str]] = list(doc.get("cardPacedQueue") or [])
    active = doc.get("cardPacedActive") or {}
    active_id = active.get("cardId")
    active_job = str(active.get("jobType") or "")
    for card in doc.get("conceptCards") or []:
        cid = str(card.get("id") or "")
        if not cid.startswith("asset:"):
            continue
        if not card_can_generate_mesh(hub, card):
            continue
        if not card_wants_ai_mesh(card):
            continue
        if _is_generated_prop_prefab(str(card.get("prefabPath") or "")):
            continue
        if any(e.get("cardId") == cid and e.get("jobType") == "generate_mesh" for e in queue):
            continue
        if active_id == cid and active_job == "generate_mesh":
            continue
        queue.append({"cardId": cid, "jobType": "generate_mesh"})
        changed = True
    if changed:
        doc["cardPacedQueue"] = queue
    return changed


def paced_queue_busy(doc: dict[str, Any]) -> bool:
    return bool(doc.get("cardPacedQueue")) or bool(doc.get("cardPacedActive"))


def _ensure_worker(hub: Path) -> None:
    key = _hub_key(hub)
    lock = _HUB_LOCKS.setdefault(key, threading.Lock())
    with lock:
        thread = _HUB_WORKERS.get(key)
        if thread and thread.is_alive():
            return
        thread = threading.Thread(
            target=_worker_loop, args=(hub,), daemon=True, name=f"card-queue-{key[-8:]}"
        )
        _HUB_WORKERS[key] = thread
        thread.start()


def kick_card_queue(hub: Path) -> None:
    """Restart paced worker when the queue has jobs but no live thread."""
    from build_planner import _read_session

    doc = _read_session(hub) or {}
    if doc.get("cardPacedQueue") or doc.get("cardPacedActive"):
        _ensure_worker(hub)


def _worker_loop(hub: Path) -> None:
    from build_planner import _find_concept_card, _read_session, _utc, _write_session

    while True:
        doc = _read_session(hub) or {}
        if not doc.get("conceptCards"):
            return
        queue = list(doc.get("cardPacedQueue") or [])
        if not queue:
            if doc.get("cardPacedActive"):
                active = doc.get("cardPacedActive") or {}
                if active.get("phase") in ("done", "error"):
                    doc.pop("cardPacedActive", None)
                    _write_session(hub, doc)
            return

        job = queue[0]
        card_id = str(job.get("cardId") or "")
        job_type = str(job.get("jobType") or "load_preview")
        card = _find_concept_card(doc, card_id)
        if card is None:
            queue = queue[1:]
            doc["cardPacedQueue"] = queue
            _write_session(hub, doc)
            continue

        def progress(phase: str, pct: int, message: str) -> None:
            live = _read_session(hub) or doc
            set_card_job_progress(live, card_id, job_type, phase, pct, message)
            live["cardPacedQueue"] = list(live.get("cardPacedQueue") or queue)
            _write_session(hub, live)

        job_error: str | None = None
        try:
            if job_type == "generate_mesh":
                _run_generate_mesh(hub, doc, card, card_id, progress)
            elif job_type == "sculpt_character":
                hint = str(job.get("sculptHint") or card.get("pendingSculptHint") or "")
                _run_sculpt_character(hub, doc, card, card_id, hint, progress)
            elif job_type == "revise_map":
                _run_revise_map(hub, doc, card, card_id, job, progress)
            else:
                _run_load_preview(hub, doc, card, card_id, progress)
        except Exception as ex:
            job_error = str(ex)[:240]
            progress("error", 0, job_error)

        live = _read_session(hub) or doc
        queue = list(live.get("cardPacedQueue") or [])
        if queue and queue[0].get("cardId") == card_id and queue[0].get("jobType") == job_type:
            queue = queue[1:]
        live["cardPacedQueue"] = queue
        if job_error:
            err_payload = {
                "cardId": card_id,
                "jobType": job_type,
                "phase": "error",
                "progress": 0,
                "message": job_error,
            }
            for c in live.get("conceptCards") or []:
                if c.get("id") == card_id:
                    c["jobProgress"] = dict(err_payload)
                    break
            live.pop("cardPacedActive", None)
        else:
            clear_card_job_progress(live, card_id)
        live["updatedUtc"] = _utc()
        _write_session(hub, live)
        time.sleep(0.35)


def _run_revise_map(
    hub: Path,
    doc: dict[str, Any],
    card: dict[str, Any],
    card_id: str,
    job: dict[str, Any],
    progress: ProgressFn,
) -> None:
    from datetime import datetime, timezone

    from build_planner import (
        _llm_messages,
        _render_concept_density_only,
        _render_concept_layout_only,
        _read_session,
        _write_session,
        build_density_card,
        build_layout_card,
    )
    from planner_layout_revise import (
        apply_density_card_revision,
        apply_layout_card_revision,
    )

    cid = str(card.get("id") or card_id)
    if cid not in ("layout", "density"):
        raise RuntimeError(f"revise_map only supports layout/density, got {cid}")

    note = " ".join(str(job.get("revisionNote") or card.get("lastRevisionNote") or "").split())
    progress("cursor", 6, f"Cursor API — queued {cid} revision…")

    import threading

    heartbeat_stop = threading.Event()

    def llm_call(h: Path, system: str, messages: list[dict[str, str]]) -> str:
        label = "layout geometry" if cid == "layout" else "trails & props"
        progress("cursor", 28, f"Cursor API — revising {label}…")

        def _heartbeat() -> None:
            pct = 28
            notes = [
                f"Cursor API — revising {label}…",
                f"Cursor API — still working ({label} can take 1–2 min)…",
                f"Cursor API — designing changes from your note…",
            ]
            step = 0
            while not heartbeat_stop.wait(14):
                pct = min(74, pct + 5)
                progress("cursor", pct, notes[step % len(notes)])
                step += 1

        beat = threading.Thread(target=_heartbeat, daemon=True)
        beat.start()
        try:
            return _llm_messages(h, system, messages, json_response=True)
        finally:
            heartbeat_stop.set()

    live = _read_session(hub) or doc
    if cid == "layout":
        summary = apply_layout_card_revision(
            hub, live, note, llm_call=llm_call, progress=progress
        )
    else:
        summary = apply_density_card_revision(
            hub, live, note, llm_call=llm_call, progress=progress
        )
    _write_session(hub, live)

    ts = int(datetime.now(timezone.utc).timestamp())
    if cid == "layout":
        progress("render", 84, "Rendering layout + legend…")
        _render_concept_layout_only(hub, live)
        progress("render", 92, "Refreshing trail & prop density view…")
        _render_concept_density_only(hub, live)
        live["layoutImageTs"] = ts
        live["densityImageTs"] = ts
        live["conceptImageTs"] = ts
    else:
        progress("render", 84, "Rendering trail & prop density only…")
        _render_concept_density_only(hub, live)
        live["densityImageTs"] = ts

    cards = live.setdefault("conceptCards", [])
    for idx, c in enumerate(cards):
        if c.get("id") == "layout":
            updated = build_layout_card(live, cards)
            if cid == "layout":
                updated["lastRevisionNote"] = note
                updated["reviseCount"] = live.get("layoutReviseCount")
                updated["imageTs"] = live.get("layoutImageTs") or ts
            cards[idx] = updated
        elif c.get("id") == "density":
            updated = build_density_card(live, cards)
            if cid == "density":
                updated["lastRevisionNote"] = note
                updated["reviseCount"] = live.get("densityReviseCount")
                updated["imageTs"] = live.get("densityImageTs") or ts
            elif cid == "layout":
                updated["imageTs"] = live.get("densityImageTs") or ts
            cards[idx] = updated

    src = live.get("lastMapRevisionSource") or "cursor"
    live["lastConceptCardAction"] = {
        "cardId": cid,
        "message": f"[{src}] {summary or 'Map revised.'}",
    }
    live["updatedUtc"] = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    _write_session(hub, live)
    progress("done", 100, live["lastConceptCardAction"]["message"])


def _run_load_preview(
    hub: Path,
    doc: dict[str, Any],
    card: dict[str, Any],
    card_id: str,
    progress: ProgressFn,
) -> None:
    from build_planner import _find_concept_card, _read_session, _utc, _write_session
    from planner_asset_catalog import ensure_card_thumbnail

    progress("unity_preview", 12, "Requesting Unity 3D preview…")

    def thumb_progress(phase: str, pct: int, msg: str) -> None:
        progress(phase, pct, msg)

    from planner_asset_catalog import thumb_is_real_preview

    new_rel = ensure_card_thumbnail(hub, card, wait_unity=True, on_progress=thumb_progress)
    if not thumb_is_real_preview(hub, new_rel):
        raise RuntimeError(
            "Unity did not produce a 3D preview — keep Hub open in the Editor and retry."
        )

    from datetime import datetime, timezone

    live = _read_session(hub) or doc
    live_card = _find_concept_card(live, card_id)
    ts = int(datetime.now(timezone.utc).timestamp())
    if live_card is not None and new_rel:
        live_card["imageRel"] = new_rel
        live_card["imageTs"] = ts
    live["conceptImageTs"] = ts
    live["updatedUtc"] = _utc()
    _write_session(hub, live)
    progress("done", 100, "3D preview ready")


def _run_generate_mesh(
    hub: Path,
    doc: dict[str, Any],
    card: dict[str, Any],
    card_id: str,
    progress: ProgressFn,
) -> None:
    from datetime import datetime, timezone

    from build_planner import _cursor_prop_mesh_spec, _find_concept_card
    from planner_asset_catalog import (
        _is_bad_saved_thumb,
        _is_placeholder_thumb,
        card_can_generate_mesh,
        generate_mesh_for_concept_card,
        mesh_source_label,
        thumbnail_after_mesh_generation,
    )

    if not card_can_generate_mesh(hub, card):
        raise RuntimeError("Only pending prop/collectible cards support mesh generation.")

    regen_index = int(card.get("meshRegenCount") or 0) + 1
    progress("ai_design", 15, "AI Generator — photorealistic mesh design…")
    ai_spec = _cursor_prop_mesh_spec(hub, card, regen_index)
    progress("ai_design", 38, "AI design complete — building mesh in Unity…")

    def unity_progress(phase: str, pct: int, msg: str) -> None:
        progress(phase, pct, msg)

    ok, msg, prefab, unity_thumb = generate_mesh_for_concept_card(
        hub, card, ai_spec=ai_spec, on_progress=unity_progress
    )
    if not ok or not prefab:
        raise RuntimeError(msg or "Mesh generation failed.")

    progress("thumbnail", 92, "Rendering 3D card preview…")
    work = dict(card)
    work["meshRegenCount"] = regen_index
    work["prefabPath"] = prefab
    work["status"] = "pending"
    work["meshSourceLabel"] = mesh_source_label(card)
    work["label"] = mesh_source_label(card)
    new_rel = thumbnail_after_mesh_generation(
        hub, card_id, work, prefab, unity_thumb_rel=unity_thumb
    )
    if new_rel:
        new_path = hub / new_rel
        if new_path.is_file() and not _is_bad_saved_thumb(new_path):
            old_rel = card.get("imageRel")
            if old_rel and old_rel != new_rel:
                old_path = hub / old_rel
                if old_path.is_file() and (
                    _is_placeholder_thumb(old_path) or _is_bad_saved_thumb(old_path)
                ):
                    try:
                        old_path.unlink()
                    except OSError:
                        pass
            work["imageRel"] = new_rel
            work["imageTs"] = int(datetime.now(timezone.utc).timestamp())

    from build_planner import _read_session, _write_session, _utc

    live = _read_session(hub) or doc
    live_card = _find_concept_card(live, card_id)
    if live_card is not None:
        live_card.update(
            {
                "meshRegenCount": regen_index,
                "prefabPath": prefab,
                "status": "pending",
                "meshSourceLabel": mesh_source_label(card),
                "label": mesh_source_label(card),
            }
        )
        if work.get("imageRel"):
            live_card["imageRel"] = work["imageRel"]
            live_card["imageTs"] = work.get("imageTs")
    live["lastConceptCardAction"] = {
        "cardId": card_id,
        "action": "generate_mesh",
        "message": msg,
    }
    from planner_asset_catalog import thumb_is_real_preview

    if not thumb_is_real_preview(hub, work.get("imageRel")):
        from planner_asset_catalog import write_orb_sphere_preview_thumb

        orb_rel = write_orb_sphere_preview_thumb(hub, card_id, prefab, work)
        if orb_rel and thumb_is_real_preview(hub, orb_rel):
            work["imageRel"] = orb_rel
            if live_card is not None:
                live_card["imageRel"] = orb_rel
        else:
            raise RuntimeError(
                "Mesh built but card preview failed — check UNITY_PATH and "
                "Assets/EnvironmentKit/Generated/planner-concept-card-mesh.log"
            )

    ts = int(datetime.now(timezone.utc).timestamp())
    live["conceptImageTs"] = ts
    live["updatedUtc"] = _utc()
    _write_session(hub, live)
    progress("done", 100, msg or "AI mesh ready")


def _run_sculpt_character(
    hub: Path,
    doc: dict[str, Any],
    card: dict[str, Any],
    card_id: str,
    sculpt_hint: str,
    progress: ProgressFn,
) -> None:
    from datetime import datetime, timezone

    from build_planner import _cursor_character_sculpt_spec, _find_concept_card
    from planner_asset_catalog import (
        _is_bad_saved_thumb,
        _is_placeholder_thumb,
        card_can_sculpt_character,
        character_sculpt_source_prefab,
        ensure_card_thumbnail,
        generate_sculpt_for_concept_card,
        mesh_source_label,
        normalize_sculpt_hint,
        thumb_is_real_preview,
    )

    hint = normalize_sculpt_hint(sculpt_hint)
    if not hint:
        raise RuntimeError("Enter 1–2 words for the sculpt hint.")

    if not card_can_sculpt_character(hub, card):
        raise RuntimeError("Only pending NPC, enemy, and player cards support Resculpt.")

    regen_index = int(card.get("sculptRegenCount") or 0) + 1
    progress("ai_design", 15, f"AI Generator — morphing (“{hint}”)…")
    ai_spec = _cursor_character_sculpt_spec(hub, card, regen_index, hint)
    progress("ai_design", 38, "AI morph complete — sculpting in Unity…")

    def unity_progress(phase: str, pct: int, msg: str) -> None:
        progress(phase, pct, msg)

    ok, msg, prefab = generate_sculpt_for_concept_card(
        hub, card, sculpt_hint=hint, ai_spec=ai_spec, on_progress=unity_progress
    )
    if not ok or not prefab:
        raise RuntimeError(msg or "Character sculpt failed.")

    progress("thumbnail", 92, "Rendering 3D card preview…")
    source_prefab = character_sculpt_source_prefab(card)
    work = dict(card)
    work["sculptRegenCount"] = regen_index
    work["lastSculptHint"] = hint
    work.pop("pendingSculptHint", None)
    if source_prefab and not work.get("sourcePrefabPath"):
        work["sourcePrefabPath"] = source_prefab
    work["prefabPath"] = prefab
    work["status"] = "pending"
    work["label"] = mesh_source_label(card)

    def thumb_progress(phase: str, pct: int, thumb_msg: str) -> None:
        progress(phase, pct, thumb_msg)

    new_rel = ensure_card_thumbnail(hub, work, wait_unity=True, on_progress=thumb_progress)
    if new_rel:
        new_path = hub / new_rel
        if new_path.is_file() and not _is_bad_saved_thumb(new_path):
            old_rel = card.get("imageRel")
            if old_rel and old_rel != new_rel:
                old_path = hub / old_rel
                if old_path.is_file() and (
                    _is_placeholder_thumb(old_path) or _is_bad_saved_thumb(old_path)
                ):
                    try:
                        old_path.unlink()
                    except OSError:
                        pass
            work["imageRel"] = new_rel
            work["imageTs"] = int(datetime.now(timezone.utc).timestamp())

    from build_planner import _read_session, _write_session, _utc

    live = _read_session(hub) or doc
    live_card = _find_concept_card(live, card_id)
    if live_card is not None:
        live_card.update(
            {
                "sculptRegenCount": regen_index,
                "lastSculptHint": hint,
                "prefabPath": prefab,
                "status": "pending",
                "label": mesh_source_label(card),
            }
        )
        if work.get("sourcePrefabPath"):
            live_card["sourcePrefabPath"] = work["sourcePrefabPath"]
        live_card.pop("pendingSculptHint", None)
        if work.get("imageRel"):
            live_card["imageRel"] = work["imageRel"]
            live_card["imageTs"] = work.get("imageTs")
    live["lastConceptCardAction"] = {
        "cardId": card_id,
        "action": "sculpt_character",
        "message": msg,
    }

    if not thumb_is_real_preview(hub, work.get("imageRel")):
        raise RuntimeError(
            "Sculpt succeeded but 3D card preview failed — keep Hub open in the Editor and retry."
        )

    ts = int(datetime.now(timezone.utc).timestamp())
    live["conceptImageTs"] = ts
    live["updatedUtc"] = _utc()
    _write_session(hub, live)
    progress("done", 100, msg or "Character resculpt ready")
