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


def fill_milestone_captions(milestone: dict, *, force: bool = False) -> dict:
    if not force and milestone.get("line1") and milestone.get("line2"):
        if not milestone.get("line3"):
            milestone["line3"] = _phase_line3(milestone)
        return milestone
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


# Bot voice — TV adventure show for younger viewers. Zero lecture; captions carry facts.
NARRATOR_CHAPTER: dict[str, str] = {
    "session bootstrap": (
        "Lights on! A brand-new world is waking up in Unity — and we are here for ALL of it. "
        "Look at that land stirring. Something big is about to happen."
    ),
    "grid contract": (
        "Whoa — the ground is snapping into a giant puzzle. "
        "Every piece has to match or the whole island goes bouncy-bouncy. "
        "Watch those corners — that is where the magic starts."
    ),
    "seam invariants": (
        "Okay okay — the edges are getting welded so nobody trips on invisible walls. "
        "Not flashy, but if this goes wrong you yeet off a cliff. "
        "The ground is finally acting like one piece."
    ),
    "play disk": (
        "Flat zone time! The middle is getting smoothed out so heroes actually have room to run. "
        "The wild stuff can stay wild around the edges — the center is home base. "
        "That flat spot is gonna be where the action lives."
    ),
    "terrain meat": (
        "HERE comes the big sculpt — hills and rolls crashing in like a slow-motion wave. "
        "If it looks frozen, the backstage crew is just busy — the world is still cooking. "
        "Every second the land gets more personality."
    ),
    "foothills": (
        "The flat land is shaking hands with the wild hills — like a ramp into adventure. "
        "We want cool slopes, not a ski jump into the sky. "
        "This is the handshake between safe zone and explore zone."
    ),
    "mountain ring": (
        "Mountains rising on the horizon — main-character energy for the whole map! "
        "Pretty is great but you gotta be able to walk there. "
        "Those peaks are framing the whole kingdom."
    ),
    "labyrinth annex": (
        "Secret corner alert — a twisty pocket is carving in, like a mini maze. "
        "Branches, not spaghetti — you want paths that feel like a real hideout. "
        "Squint at that corner — maze energy loading."
    ),
    "trail bench": (
        "Path time! Somebody is carving a road from spawn to the good stuff. "
        "Every slice of trail is another step you will actually run in the game. "
        "This is the hallway to the adventure."
    ),
    "surface phase": (
        "Still surface season — underground stuff is waiting backstage. "
        "The top of the world runs the show until the big lock clicks. "
        "What you see up here is the truth for now."
    ),
    "surface lock": (
        "Surface lock incoming — tuck the top world in before the underground party starts. "
        "Once this freezes, the caves get their turn. "
        "Screenshot this in your brain — it is the before picture."
    ),
    "recording tail": (
        "Last look before the director yells cut — freeze this frame in your head. "
        "This is the save point before the next wild upgrade. "
        "The map right here is the receipt for everything we just watched."
    ),
    "final capture": (
        "Curtain call on this episode — from empty land to THIS. "
        "The world looks totally different from minute one. "
        "Same show, bigger map, next episode is gonna hit harder."
    ),
    "start": (
        "Episode start — real timelapse, real world, zero fake tricks. "
        "I call the awesome moments; the captions handle the nerdy subtitles. "
        "Eyes on the land — here we go!"
    ),
    "complete": (
        "Episode over — what a ride from first dirt to this vista. "
        "The patch notes get their turn next, but YOU just watched the origin story. "
        "Thanks for hanging till the credits."
    ),
}

NARRATOR_SUBBEAT: dict[str, str] = {
    "queue drain": "Backstage hustle — pieces finishing one after another. Still moving!",
    "tile flatten": "Neighbors learning to agree on height — teamwork makes the dream work.",
    "seam weld": "Quick weld at the border — no more ground gaslighting us.",
    "disk grade": "Center zone getting friendlier for feet — walk mode unlocked.",
    "noise sculpt": "Personality incoming — bumps and rolls without earthquake cosplay.",
    "foothill blend": "Handshake between safe land and wild land — love to see it.",
    "mountain lift": "Outer ring going UP — horizon getting dramatic.",
    "annex carve": "Little pocket carved for secrets — branch vibes only.",
    "trail stamp": "Another slice of path — they add up to a real adventure route.",
    "surface lock": "Almost locked on top — underground queue is warming up.",
}


def _match_key(haystack: str, table: dict) -> str | None:
    h = (haystack or "").lower()
    for key in sorted(table.keys(), key=len, reverse=True):
        if key in h:
            return key
    return None


def _phase_context(milestone: dict) -> str:
    parts = [
        milestone.get("phase") or "",
        milestone.get("sub") or "",
        milestone.get("subAction") or "",
        milestone.get("chapter") or "",
    ]
    return " ".join(str(p).strip() for p in parts if p)


def fill_milestone_narrator_script(milestone: dict, *, force: bool = False) -> dict:
    """Spoken script for Personal Voice — TV host; never copies caption bullets."""
    if not force and (milestone.get("narratorScript") or "").strip():
        return milestone

    ctx = _phase_context(milestone)

    if milestone.get("beatKind") == "subbeat":
        sk = _match_key(ctx, NARRATOR_SUBBEAT)
        if sk:
            milestone["narratorScript"] = NARRATOR_SUBBEAT[sk]
            return milestone

    ck = _match_key(ctx, NARRATOR_CHAPTER)
    if ck:
        milestone["narratorScript"] = NARRATOR_CHAPTER[ck]
        return milestone

    phase = (milestone.get("phase") or "").lower()
    if phase in NARRATOR_CHAPTER:
        milestone["narratorScript"] = NARRATOR_CHAPTER[phase]
        return milestone

    milestone["narratorScript"] = (
        "Whoa — something just changed on screen! "
        "Pause it in your head — the world is telling a story right now."
    )
    return milestone
