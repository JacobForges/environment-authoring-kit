#!/usr/bin/env python3
"""Apply user revision notes to concept layout/density maps."""
from __future__ import annotations

import copy
import hashlib
import json
import random
import re
from typing import Any, Callable, Optional

ProgressFn = Optional[Callable[[str, int, str], None]]

LAYOUT_REVISE_SYSTEM = """You revise a Unity Environment Kit layoutPlan JSON for the LAYOUT + LEGEND card.

Change ONLY world geometry / routing structure:
- play disk tiles, maze/labyrinth plateaus, jump platforms, island platforms, spawn, cave mouth
- cardinal island links and traversable hop chains BETWEEN islands

Do NOT change unless the user explicitly asks:
- prop scatter positions/counts, trail styling, or density heatmap data
- technicalSpecs

Hard constraints:
- 9 play tiles centered (3×3 play disk on 81-tile / 9×9 shell when tileCount=81).
- Play-disk marker rows/cols: 0–2 (local 3×3) OR 3–5 (global 9×9).
- Return valid JSON only — no markdown fences.

Respond JSON:
{
  "layoutPlan": { ...full updated layoutPlan... },
  "changeSummary": "one sentence for the user"
}
"""

DENSITY_REVISE_SYSTEM = """You revise ONLY trail & prop density for an existing layoutPlan.

Change ONLY:
- prop markers (kind=prop, scatter labels, slots, rows/cols)
- trails array (routes, emphasis, count)

Do NOT change:
- playDisk / labyrinth / maze wall geometry
- spawn, NPC, enemy, collectible, cave mouth markers
- island platform structure or jump-pad chains
- technicalSpecs

Hard constraints:
- Keep all non-prop, non-trail markers identical unless removing duplicate scatter props.
- Return valid JSON only — no markdown fences.

Respond JSON:
{
  "layoutPlan": { ...full updated layoutPlan... },
  "changeSummary": "one sentence for the user"
}
"""


def _play_rng(seed: str, idx: int) -> random.Random:
    digest = hashlib.md5(f"{seed}:{idx}".encode()).hexdigest()
    return random.Random(int(digest[:8], 16))


def _heuristic_revise_layout_geom(
    layout: dict[str, Any],
    note: str,
    revise_idx: int,
) -> tuple[dict[str, Any], str]:
    """Geometry-only fallback — platforms, maze, spawn; not prop scatter."""
    out = copy.deepcopy(layout)
    markers: list[dict[str, Any]] = list(out.get("markers") or [])
    text = (note or "").lower()
    rng = _play_rng(note or "layout-geom", revise_idx)

    spawn = next((m for m in markers if str(m.get("kind")) == "spawn"), None)
    if spawn and any(k in text for k in ("southwest", "south-west", "sw corner", "bottom-left")):
        spawn["row"], spawn["col"] = 2, 0
    elif spawn and any(k in text for k in ("north", "top")):
        spawn["row"], spawn["col"] = 0, 1
    elif spawn and any(k in text for k in ("south", "bottom")):
        spawn["row"], spawn["col"] = 2, 1

    pd = dict(out.get("playDisk") or {})
    if any(k in text for k in ("maze", "labyrinth", "corner", "wall")):
        if revise_idx % 2 == 0:
            pd["labyrinth"] = True
            pd["labyrinthNote"] = "corner maze plateaus — half walls opened (revise)"
        else:
            pd["labyrinth"] = True
            pd["labyrinthNote"] = "rows 1–2 maze · row 0 north overlook"
    if any(k in text for k in ("open", "center", "hub")):
        pd["labyrinthNote"] = "opened center hub — reduced maze ring"
    out["playDisk"] = pd

    if any(k in text for k in ("jump", "pad", "hop", "island", "tradable")):
        trails: list[dict[str, Any]] = list(out.get("trails") or [])
        for d in ("N", "E", "S", "W"):
            trails.append(
                {
                    "from": f"play-hop-{d.lower()}",
                    "to": f"island-{d}",
                    "label": f"tradable hop pad → {d} (rev {revise_idx})",
                }
            )
        out["trails"] = trails

    out["markers"] = markers
    snippet = (note or "geometry tweak").strip()[:72]
    return out, f"Rev {revise_idx} (layout): {snippet}"


def _heuristic_revise_density(
    layout: dict[str, Any],
    note: str,
    revise_idx: int,
) -> tuple[dict[str, Any], str]:
    """Trails + prop scatter only."""
    out = copy.deepcopy(layout)
    markers: list[dict[str, Any]] = list(out.get("markers") or [])
    text = (note or "").lower()
    rng = _play_rng(note or "density", revise_idx)

    play_props = [
        m
        for m in markers
        if str(m.get("zone") or "play") == "play"
        and str(m.get("kind") or "") == "prop"
        and "hub" not in str(m.get("label") or "").lower()
        and "entrance" not in str(m.get("label") or "").lower()
    ]
    for m in play_props:
        m["slot"] = rng.randint(0, 8)
        m["row"] = rng.randint(3, 5)
        m["col"] = rng.randint(3, 5)

    if any(k in text for k in ("more", "dens", "heavier", "extra", "increase")):
        templates = [m for m in play_props if "scatter" in str(m.get("label") or "").lower()]
        if not templates:
            templates = play_props[:2]
        for src in templates[:3]:
            clone = dict(src)
            clone["label"] = f"{src.get('label', 'scatter')}-rev{revise_idx}"
            clone["slot"] = rng.randint(0, 8)
            clone["row"] = rng.randint(3, 5)
            clone["col"] = rng.randint(3, 5)
            markers.append(clone)
    elif any(k in text for k in ("less", "fewer", "sparse", "reduce", "lighter", "half")):
        scatter = [
            m
            for m in markers
            if str(m.get("kind") or "") == "prop"
            and "scatter" in str(m.get("label") or "").lower()
        ]
        remove_n = max(1, len(scatter) // 2)
        for m in scatter[:remove_n]:
            markers.remove(m)

    if any(k in text for k in ("west", "left")):
        for m in play_props:
            m["col"] = 3
    elif any(k in text for k in ("east", "right")):
        for m in play_props:
            m["col"] = 5
    elif any(k in text for k in ("north", "top")):
        for m in play_props:
            m["row"] = 3
    elif any(k in text for k in ("south", "bottom")):
        for m in play_props:
            m["row"] = 5

    trails: list[dict[str, Any]] = list(out.get("trails") or [])
    if "trail" in text:
        if any(k in text for k in ("wider", "stronger", "bold", "thicker")):
            trails.append(
                {"from": "play-center", "to": "island-E", "label": f"bold trail (rev {revise_idx})"}
            )
        elif any(k in text for k in ("fewer", "less")) and trails:
            trails = trails[:-1]
        else:
            d = ["N", "E", "S", "W"][revise_idx % 4]
            trails.append({"from": "play-center", "to": f"island-{d}", "label": f"density trail → {d}"})
    out["trails"] = trails
    out["markers"] = markers

    snippet = (note or "trail/prop density tweak").strip()[:72]
    return out, f"Rev {revise_idx} (density): {snippet}"


def _extract_json_object(text: str) -> dict[str, Any] | None:
    raw = (text or "").strip()
    if not raw:
        return None
    if raw.startswith("```"):
        raw = re.sub(r"^```(?:json)?\s*", "", raw)
        raw = re.sub(r"\s*```$", "", raw)
    try:
        data = json.loads(raw)
        return data if isinstance(data, dict) else None
    except json.JSONDecodeError:
        pass
    start = raw.find("{")
    end = raw.rfind("}")
    if start >= 0 and end > start:
        try:
            data = json.loads(raw[start : end + 1])
            return data if isinstance(data, dict) else None
        except json.JSONDecodeError:
            return None
    return None


def _parse_llm_layout_response(raw: str) -> tuple[dict[str, Any] | None, str, str]:
    data = _extract_json_object(raw)
    if not data:
        return None, "", "Cursor response was not valid JSON"
    plan = data.get("layoutPlan")
    if not isinstance(plan, dict) and isinstance(data.get("markers"), list):
        plan = data
    summary = str(
        data.get("changeSummary") or data.get("summary") or data.get("message") or ""
    ).strip()
    if isinstance(plan, dict) and plan.get("markers"):
        return plan, summary, ""
    return None, summary, "Cursor JSON missing layoutPlan.markers"


def llm_revise_layout_plan(
    hub,
    layout: dict[str, Any],
    note: str,
    system: str,
    focus: str,
    *,
    llm_call,
    progress: ProgressFn = None,
) -> tuple[dict[str, Any] | None, str, str]:
    cfg_snippet = json.dumps(
        {"focus": focus, "revisionNote": note, "currentMarkerCount": len(layout.get("markers") or [])},
        indent=0,
    )
    messages = [
        {
            "role": "user",
            "content": (
                f"Focus: {focus}\nUser revision request:\n{note}\n\n"
                f"Context:\n{cfg_snippet}\n\n"
                f"Current layoutPlan:\n{json.dumps(layout, indent=2)}"
            ),
        }
    ]
    if progress:
        progress("cursor", 22, "Cursor API — sending revision…")
    try:
        raw = llm_call(hub, system, messages)
        if progress:
            progress("cursor", 68, "Cursor API — parsing revised layout…")
        return _parse_llm_layout_response(raw)
    except Exception as ex:
        return None, "", str(ex)[:240]


def _apply_revision(
    hub,
    doc: dict[str, Any],
    card_id: str,
    revision_note: str,
    *,
    system: str,
    focus: str,
    heuristic_fn,
    llm_call=None,
    progress: ProgressFn = None,
) -> str:
    from build_planner import cursor_api_configured
    from planner_concept_render import _sanitize_layout_plan, derive_layout_plan

    brief = doc.setdefault("brief", {})
    cfg = doc.get("sessionConfig") or {}
    layout = brief.get("layoutPlan")
    if not isinstance(layout, dict) or not layout.get("markers"):
        layout = derive_layout_plan(brief, cfg)

    note = " ".join((revision_note or "").split()).strip()
    count_key = "layoutReviseCount" if card_id == "layout" else "densityReviseCount"
    revise_idx = int(doc.get(count_key) or 0) + 1
    doc[count_key] = revise_idx
    doc["mapReviseCount"] = int(doc.get("mapReviseCount") or 0) + 1

    summary = ""
    source = "heuristic"
    llm_error = ""
    new_plan: dict[str, Any] | None = None

    use_cursor = bool(note) and llm_call is not None and cursor_api_configured(hub)
    if use_cursor:
        new_plan, summary, llm_error = llm_revise_layout_plan(
            hub, layout, note, system, focus, llm_call=llm_call, progress=progress
        )
        if new_plan is not None:
            source = "cursor"
        elif llm_error:
            doc["lastMapRevisionError"] = llm_error

    if new_plan is None:
        new_plan, summary = heuristic_fn(layout, note, revise_idx)
        if use_cursor and llm_error:
            summary = f"Cursor: {llm_error} — local fallback: {summary}"
            source = "heuristic-fallback"

    brief["layoutPlan"] = _sanitize_layout_plan(new_plan)
    doc["lastMapRevisionSummary"] = summary
    doc["lastMapRevisionSource"] = source
    doc["lastMapRevisionCard"] = card_id

    for card in doc.get("conceptCards") or []:
        if card.get("id") == card_id:
            card["lastRevisionNote"] = note
            card["reviseCount"] = revise_idx

    return summary


def apply_layout_card_revision(
    hub,
    doc: dict[str, Any],
    revision_note: str,
    *,
    llm_call=None,
    progress: ProgressFn = None,
) -> str:
    return _apply_revision(
        hub,
        doc,
        "layout",
        revision_note,
        system=LAYOUT_REVISE_SYSTEM,
        focus="layout + legend — play disk, platforms, maze, islands, jump pads",
        heuristic_fn=_heuristic_revise_layout_geom,
        llm_call=llm_call,
        progress=progress,
    )


def apply_density_card_revision(
    hub,
    doc: dict[str, Any],
    revision_note: str,
    *,
    llm_call=None,
    progress: ProgressFn = None,
) -> str:
    return _apply_revision(
        hub,
        doc,
        "density",
        revision_note,
        system=DENSITY_REVISE_SYSTEM,
        focus="trail & prop density — scatter props and trail links only",
        heuristic_fn=_heuristic_revise_density,
        llm_call=llm_call,
        progress=progress,
    )


def apply_map_card_revision(
    hub,
    doc: dict[str, Any],
    card_id: str,
    revision_note: str,
    *,
    llm_call=None,
    progress: ProgressFn = None,
) -> str:
    """Route to layout-only or density-only revision."""
    if card_id == "density":
        return apply_density_card_revision(
            hub, doc, revision_note, llm_call=llm_call, progress=progress
        )
    return apply_layout_card_revision(
        hub, doc, revision_note, llm_call=llm_call, progress=progress
    )
