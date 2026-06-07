"""Professor caption drafts when AI director is unavailable."""
from __future__ import annotations

CHAPTER_COPY: dict[str, tuple[str, str, str]] = {
    "session bootstrap": (
        "Watch the Scene view as the build session comes online.",
        "We establish capture cadence first so the timelapse reflects real editor pacing—not a slideshow of random frames.",
        "Look for steady terrain changes rather than queue stalls.",
    ),
    "grid contract": (
        "The fullworld grid is the layout contract for every later sculpt pass.",
        "Tiles must agree on edge heights before meat loops amplify small seams into visible faults.",
        "Scan tile corners; flicker between holds means seam work is not finished.",
    ),
    "seam invariants": (
        "Seam passes keep height continuous across tile borders.",
        "Players feel invisible walls when seams drift—fix flatten invariants before foothills.",
        "Compare border vertices between this hold and the previous one.",
    ),
    "play disk": (
        "The play disk is the gameplay-first height target in the center.",
        "Outer noise can rise, but the disk must stay readable for locomotion and combat.",
        "Notice whether the central flat reads cleaner than the wilderness ring.",
    ),
    "terrain meat": (
        "Terrain meat is paced heightfield work across the disk and approaches.",
        "Long plateaus in the recording usually mean queue depth, not a failed phase.",
        "Relief should evolve gradually frame to frame.",
    ),
    "foothills": (
        "Foothills translate disk logic into wilderness without breaking approach corridors.",
        "This is where annex silhouettes begin to compete for editor time.",
        "Check that slopes read at gameplay scale, not only aerial beauty.",
    ),
    "mountain ring": (
        "Mountain massing frames the world and constrains sightlines to the cave mouth later.",
        "Relief that fights collision is a grading failure even if the overview looks dramatic.",
        "Watch the outer ring height against the south annex.",
    ),
    "labyrinth annex": (
        "The south annex labyrinth should read as branches—not a hub star carved into the surface.",
        "Annex-local carve scope is an invariant; global stars are a common regression.",
        "Verify maze readability in the lower-right of the scene.",
    ),
    "trail bench": (
        "Radial trail benches often dominate queue depth during additive surface builds.",
        "When holds look identical for minutes, one bench label may be spamming the editor queue.",
        "Focus on the approach path being cut into the disk.",
    ),
    "surface phase": (
        "Surface authority remains with the heightfield until surface lock fires.",
        "Cave geometry must not mutate the authoritative surface prematurely.",
        "Additive sessions can remain on surface step 1 for a long time—read the timeline accordingly.",
    ),
    "surface lock pending": (
        "Surface lock is still pending—treat height here as source of truth.",
        "Downstream cave phases assume a locked, audited surface.",
        "Compare against layout audit before a non-additive AAA rebuild.",
    ),
    "pipeline step 1/122": (
        "The pipeline is still on early surface work—later cave phases have not started.",
        "Captions should not imply cave meat or props that never ran in this recording.",
        "Use this hold as forensic evidence of where the session stopped.",
    ),
    "recording tail": (
        "Final Scene view before the editor session ended.",
        "Preserve captures after disconnect—they outrank logs that were overwritten on restart.",
        "Note relief, queue pressure, and annex readability at the stop point.",
    ),
    "final capture": (
        "Final Scene view before the editor session ended.",
        "Preserve captures after disconnect—they outrank logs that were overwritten on restart.",
        "Note relief, queue pressure, and annex readability at the stop point.",
    ),
}


PHASE_LINE3: dict[str, str] = {
    "editor queue": "Editor queue · bench drain",
    "nine-tile grid": "3×3 tile grid · height flatten",
    "tile seams": "Tile seams · border weld",
    "play disk grading": "Play disk · locomotion grade",
    "terrain meat": "Terrain meat · macro sculpt",
    "foothills": "Foothills · disk-to-wild blend",
    "mountains": "Mountain ring · outer massing",
    "south annex": "South annex · labyrinth carve",
    "trail queue": "Trail bench · radial stamp",
    "surface lock": "Surface lock · pre-cave freeze",
    "surface_build": "Additive surface_build",
}


SUBBEAT_COPY: dict[str, tuple[str, str]] = {
    "queue drain": (
        "The editor queue is draining—benches are finishing in order.",
        "Clearing backlog keeps the timelapse honest; stalled queues look like frozen terrain.",
    ),
    "tile flatten": (
        "Tiles are flattening to a shared height contract.",
        "Without flatten, later sculpt passes amplify seams into visible cliffs.",
    ),
    "seam weld": (
        "Seam work is welding border vertices between tiles.",
        "A single bad seam propagates through every later mountain and annex pass.",
    ),
    "disk grade": (
        "The play disk is being graded for locomotion readability.",
        "Gameplay reads the center first—outer noise must not steal the flat.",
    ),
    "noise sculpt": (
        "Macro noise is sculpting relief across the disk and approaches.",
        "This pass should evolve gradually; sudden spikes mean a bench restarted.",
    ),
    "foothill blend": (
        "Foothills are blending disk logic into wilderness approaches.",
        "Transitions here set whether annex carving has clean approach corridors.",
    ),
    "mountain lift": (
        "Mountain ring massing is lifting on the outer frame.",
        "Sightlines to the cave mouth depend on disciplined outer-ring height.",
    ),
    "annex carve": (
        "The south annex pocket is being carved for labyrinth branches.",
        "Annex-local scope must stay local—a hub star here is a regression.",
    ),
    "trail stamp": (
        "Trail benches are stamping the radial approach path.",
        "Trail work often dominates queue depth during additive surface builds.",
    ),
    "surface lock": (
        "Surface authority is nearing lock—heightfield remains source of truth.",
        "Cave phases assume this surface is audited before non-additive rebuilds.",
    ),
}


def _phase_line3(milestone: dict) -> str:
    phase = (milestone.get("phase") or "").lower()
    for key, line in PHASE_LINE3.items():
        if key in phase:
            return line
    sub = (milestone.get("sub") or milestone.get("subAction") or "").strip()
    if sub:
        return sub.title()
    return ""


def fill_milestone_captions(milestone: dict) -> dict:
    if milestone.get("beatKind") == "subbeat":
        action = (milestone.get("subAction") or milestone.get("sub") or "").lower()
        for key, (l1, l2) in SUBBEAT_COPY.items():
            if key in action or key in (milestone.get("chapter") or "").lower():
                milestone["line1"] = l1
                milestone["line2"] = l2
                sub_label = (milestone.get("subAction") or milestone.get("sub") or key).strip()
                milestone["line3"] = sub_label.title() if sub_label else _phase_line3(milestone)
                return milestone
        if not milestone.get("line1"):
            milestone.setdefault("line1", "A sub-step is running between major checkpoints.")
            milestone.setdefault("line2", "Watch what changes before the next full hold.")
            sub_label = (milestone.get("subAction") or milestone.get("sub") or "").strip()
            milestone.setdefault("line3", sub_label.title() if sub_label else "Sub-step")
        return milestone
    if milestone.get("line1") and milestone.get("line2"):
        if not milestone.get("line3"):
            milestone["line3"] = _phase_line3(milestone)
        return milestone
    chapter = (milestone.get("chapter") or "").lower()
    for key, (l1, l2, l3) in CHAPTER_COPY.items():
        if key in chapter:
            milestone.setdefault("line1", l1)
            milestone.setdefault("line2", l2)
            milestone.setdefault("line3", l3 or _phase_line3(milestone))
            return milestone
    milestone.setdefault("line1", "Study this Scene view checkpoint.")
    milestone.setdefault("line2", "Compare to the prior hold to see what the pipeline changed.")
    milestone.setdefault("line3", "")
    return milestone
