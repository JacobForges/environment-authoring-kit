"""
Narration script grade → meat-loop → dedupe before Personal Voice / video mux.

Mirrors world meat-loop: grade, fix one issue per pass, re-grade until acceptable
or max passes — never drop unique milestone context; only remove true duplicates
and forbidden live-stream filler.
"""
from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any

# Live-stream / chat filler — recap is edited devlog, not fake LIVE.
FORBIDDEN_PHRASES: tuple[tuple[str, str], ...] = (
    (r"\bwe(?:'re| are) live\b", "we're rolling"),
    (r"\byo[ —,-]", ""),
    (r"\bchat\b", "folks"),
    (r"\bdrop a (?:hi|hello)\b", "stick around"),
    (r"\bif you(?:'re| are) (?:still )?here\b", ""),
    (r"\bnext stream\b", "next recap"),
    (r"\bhit follow\b", "come back for the next pass"),
    (r"\bsnacks optional\b", ""),
    (r"\bhype mandatory\b", ""),
    (r"\bno cap\b", ""),
    (r"\bwe(?:'re| are) so back\b", "we're moving again"),
    (r"\blet(?:'s| us) ride\b", "let's keep going"),
    (r"\blive build stream\b", "build recap"),
    (r"\bdev-?stream host\b", "recap host"),
    (r"\bfake montage\b", "montage"),
)

def _load_captions_module():
    import importlib.util

    path = Path(__file__).resolve().parent / "demo-recap-captions.py"
    spec = importlib.util.spec_from_file_location("demo_recap_captions", path)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(mod)
    return mod


def _milestones_with_narrator_voice(
    milestones: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    """Ensure every beat has narratorScript before meat-loop expand."""
    caps = _load_captions_module()
    out: list[dict[str, Any]] = []
    for m in milestones:
        row = dict(m)
        caps.fill_milestone_narrator_script(row, force=True)
        out.append(row)
    return out


CAPTION_LECTURE_PATTERNS: tuple[str, ...] = (
    r"on screen you will see",
    r"the caption says",
    r"as the caption",
    r"read the (?:bullet|caption)",
    r"line one",
    r"step[- ]by[- ]step",
    r"here is why this matters",
    r"in this lesson",
    r"notice (?:how|whether)",
    r"compare (?:this|the)",
    r"source of truth",
    r"heightfield",
    r"downstream",
    r"forensic",
    r"caption",
    r"grading json",
    r"pipeline",
    r"seam pass",
    r"play disk",
    r"tile",
)

# Per-scene TV craft: wide (Attenborough open) → setup → punchline (comedic button) → out.
# Irwin energy: direct address, plain wonder. Never pipeline jargon.
HOST_SCENES: dict[str, tuple[str, str, str, str]] = {
    "session bootstrap": (
        "Somewhere in Florida, on an empty screen, a world is trying to be born.",
        "Have a look — the land is stirring like it just heard the starting gun.",
        "How neat is that? Empty dirt already acting like it has plans.",
        "Stick with me. This is where the adventure starts.",
    ),
    "grid contract": (
        "From up here, the ground is snapping into a giant puzzle.",
        "Every piece has to match, or the whole island turns into a trampoline.",
        "One wrong corner and your hero is bouncing into the sky. Comedy, but also chaos.",
        "Watch those corners — that is where the magic locks in.",
    ),
    "seam invariants": (
        "Now the edges are getting welded, so nobody face-plants on an invisible wall.",
        "It is not flashy work, but it is the difference between walking and launching into a cliff.",
        "The ground finally agrees it is one piece. Thank you, ground.",
        "Onward — the fun sculpt is coming.",
    ),
    "play disk": (
        "Dead center, the land is flattening out — home base energy.",
        "Mountains can be dramatic later. Right now we need a floor you can actually run on.",
        "That flat spot is where the story lives. Everything else is decoration.",
        "Safe zone secured. Let the wild stuff creep in.",
    ),
    "terrain meat": (
        "And here come the big rolls — hills crashing in like a slow-motion wave.",
        "If it looks frozen, the crew behind the curtain is still cooking. Trust the process.",
        "Every second the land picks up personality, like cookie dough getting chunky.",
        "This is the part kids point at the screen for.",
    ),
    "foothills": (
        "The flat land is shaking hands with the wild hills — ramp into adventure mode.",
        "We want cool slopes, not a ski jump to the moon.",
        "That handshake is the border between safe and explore. I love a good border.",
        "Cross it mentally. We are going in.",
    ),
    "mountain ring": (
        "On the horizon, mountains are lifting up — main-character skyline energy.",
        "Pretty is great, but you still gotta walk there without exploding.",
        "Those peaks are framing the whole kingdom like a movie poster.",
        "Poster looks good. Let us see what is hiding in the corners.",
    ),
    "labyrinth annex": (
        "Tucked in the corner, a twisty pocket is carving out — mini maze vibes.",
        "Branches, not spaghetti. Paths you would actually sneak through.",
        "Squint at that corner. Hide-and-seek champions would lose their minds.",
        "Secret zone unlocked. Do not tell the boss.",
    ),
    "trail bench": (
        "Somebody is carving a road from here to the good stuff.",
        "Every slice of path is another step you will sprint down later.",
        "Hallway to the adventure, one stamp at a time.",
        "Follow the trail with your eyes. It only gets better.",
    ),
    "surface phase": (
        "We are still topside — the underground party is waiting backstage.",
        "What you see on top is the truth for now. No guessing games.",
        "The surface runs the show until the big lock clicks.",
        "Patience. The cave stuff gets its turn soon.",
    ),
    "surface lock": (
        "Surface lock is closing in — tuck the top world in before the underworld wakes up.",
        "Screenshot this in your brain. Before picture, museum quality.",
        "Once this freezes, everything below has to behave.",
        "Lock it. Next chapter loads underground.",
    ),
    "recording tail": (
        "Last look before the director yells cut.",
        "Freeze this frame — receipt for everything we just watched.",
        "The map at this second is the evidence locker.",
        "And that brings us to the end of this ride.",
    ),
    "final capture": (
        "Curtain on this episode — from bare land to this vista.",
        "The world looks nothing like minute one. That is the whole point.",
        "Same show, bigger map, harder boss next time.",
        "Roll credits in your head. We earned it.",
    ),
    "start": (
        "Tonight's episode: a world builds itself while we watch.",
        "No tricks, no fake montage — just land waking up in real time.",
        "Eyes on the screen. Here we go.",
        "Three, two — look at that.",
    ),
    "complete": (
        "That is the arc — dirt to kingdom in one sitting.",
        "You watched the origin story happen live.",
        "Next episode loads more mountains, more secrets, more chaos.",
        "Thanks for riding shotgun the whole way.",
    ),
}

HOST_SUBBEAT_SCENES: dict[str, tuple[str, str]] = {
    "queue drain": (
        "Pieces are falling into place one after another — like dominoes with better scenery.",
        "Still moving. The land does not nap on us.",
    ),
    "tile flatten": (
        "Neighbors are agreeing on height — group project that actually worked.",
        "Teamwork makes the dream work, even for dirt.",
    ),
    "seam weld": (
        "Quick weld at the border — ground stops pranking us.",
        "Solid. Next.",
    ),
    "disk grade": (
        "Center zone getting friendlier for feet — walk mode vibes.",
        "Shoes approve.",
    ),
    "noise sculpt": (
        "Bumps and rolls rolling in — personality without earthquake cosplay.",
        "Land with attitude. Love it.",
    ),
    "foothill blend": (
        "Safe land meets wild land — handshake complete.",
        "Border patrol passed.",
    ),
    "mountain lift": (
        "Outer ring going up — horizon getting dramatic.",
        "Skyline just flexed.",
    ),
    "annex carve": (
        "Little pocket for secrets — branch energy only.",
        "Hideout loading.",
    ),
    "trail stamp": (
        "Another slice of path — they add up to a real route.",
        "Keep stamping. I am following.",
    ),
    "surface lock": (
        "Almost locked on top — underground queue warming up.",
        "Hold the door — cave season soon.",
    ),
}

HOST_ENCORE_PUNCH: dict[str, tuple[str, ...]] = {
    "terrain meat": (
        "The hills look like someone turned up the volume on planet Earth.",
        "I would sled down that if my insurance allowed it.",
    ),
    "mountain ring": (
        "Those peaks are showing off and I am here for it.",
    ),
    "labyrinth annex": (
        "If that maze were a cereal box, kids would fight over the toy inside.",
    ),
    "trail bench": (
        "That path has main-quest written all over it.",
    ),
}

HOST_TRANSITIONS: tuple[str, ...] = (
    "But wait — there is more.",
    "Okay, cut to the next shot.",
    "Hang on, watch this part.",
    "Now here is where it gets good.",
)

HOST_WONDER_FILLERS: tuple[str, ...] = (
    "Every pass on this map feels like turning another page in a field guide to a world that does not exist yet.",
    "That is the magic of procedural land — boring from far away, absolutely wild up close.",
    "If you squint, you can almost hear the dirt settling into its final shape.",
    "Adventure hosts live for moments like this — quiet on screen, chaos behind the curtain.",
    "The horizon keeps scooting back, which means the explore zone keeps getting bigger.",
    "Kids would absolutely boot this world just to sled down that slope — I would too, honestly.",
    "Somewhere under all that green is a cave waiting to steal the spotlight next episode.",
    "Florida karst energy is weird and wonderful — sinkholes, springs, and secrets underfoot.",
    "This is the part where the map stops being a blueprint and starts being a place.",
    "Stick with the recap — the best corners always show up after the obvious ones.",
    "You can feel the momentum building, like the world knows the boss fight is coming.",
    "Every seam that locks is one less face-plant for whoever runs this route first.",
    "The land has personality now — lumpy, dramatic, and ready for a hero with muddy boots.",
    "I am narrating like this is prime-time because for us builders, it kind of is.",
    "Same adventure energy as a nature documentary — except we built the nature.",
    "Roll the mental credits, then roll them back — we are not done yet.",
)


def _sentence_split(text: str) -> list[str]:
    parts = re.split(r"(?<=[.!?])\s+", (text or "").strip())
    return [p.strip() for p in parts if p.strip()]


def _sentence_key(sentence: str) -> str:
    return re.sub(r"\s+", " ", sentence.lower().strip())


def _word_overlap(a: str, b: str) -> float:
    wa = set(_sentence_key(a).split())
    wb = set(_sentence_key(b).split())
    if not wa or not wb:
        return 0.0
    return len(wa & wb) / min(len(wa), len(wb))


def _match_host_key(haystack: str, table: dict) -> str | None:
    h = (haystack or "").lower()
    for key in sorted(table.keys(), key=len, reverse=True):
        if key in h:
            return key
    return None


def _milestone_context(milestone: dict[str, Any]) -> str:
    parts = [
        milestone.get("phase") or "",
        milestone.get("sub") or "",
        milestone.get("subAction") or "",
        milestone.get("chapter") or "",
    ]
    return " ".join(str(p).strip() for p in parts if p)


def _host_scene_for_milestone(milestone: dict[str, Any], *, variant: int = 0) -> str:
    """One TV scene: wide → setup → punchline → out (documentary comedy craft)."""
    ctx = _milestone_context(milestone)
    if milestone.get("beatKind") == "subbeat":
        sk = _match_host_key(ctx, HOST_SUBBEAT_SCENES)
        if sk:
            a, b = HOST_SUBBEAT_SCENES[sk]
            return f"{a} {b}"
    ck = _match_host_key(ctx, HOST_SCENES)
    if ck:
        wide, setup, punch, out = HOST_SCENES[ck]
        return f"{wide} {setup} {punch} {out}"
    phase = (milestone.get("phase") or "").lower()
    if phase in HOST_SCENES:
        wide, setup, punch, out = HOST_SCENES[phase]
        return f"{wide} {setup} {punch} {out}"
    fallback = (
        "Have a look at this shot — something just changed on screen.",
        "The land is telling a story and we are front row.",
        "How neat is that?",
        "On to the next moment.",
    )
    return " ".join(fallback)


def _host_encore_line(milestone: dict[str, Any], variant: int) -> str:
    ctx = _milestone_context(milestone)
    ck = _match_host_key(ctx, HOST_ENCORE_PUNCH) or _match_host_key(ctx, HOST_SCENES)
    if ck and ck in HOST_ENCORE_PUNCH:
        lines = HOST_ENCORE_PUNCH[ck]
        return lines[variant % len(lines)]
    return HOST_TRANSITIONS[variant % len(HOST_TRANSITIONS)]


def _host_scene_alt_line(milestone: dict[str, Any], variant: int) -> str:
    """One extra beat from a milestone scene — used when encore lines run out."""
    ctx = _milestone_context(milestone)
    ck = _match_host_key(ctx, HOST_SCENES)
    if not ck:
        phase = (milestone.get("phase") or "").lower()
        ck = phase if phase in HOST_SCENES else None
    if not ck:
        alts = (
            "Picture the map breathing — every pass adds another layer of story.",
            "This is the kind of moment where you lean in and go, wait, did that just happen?",
            "Adventure builds in slices, and we are collecting every good one.",
            "Keep your eyes on the horizon — the wild bits are still waking up.",
        )
        return alts[variant % len(alts)]
    wide, setup, punch, out = HOST_SCENES[ck]
    return (wide, setup, punch, out)[variant % 4]


def ensure_outro_last(text: str, outro: str) -> str:
    """Outro card copy must be the final spoken beat — never mid-episode filler."""
    outro = (outro or "").strip()
    if not outro:
        return polish_host_script(text)
    outro_sents = _sentence_split(outro)
    outro_keys = {_sentence_key(s) for s in outro_sents}
    kept: list[str] = []
    for s in _sentence_split(polish_host_script(text)):
        if _sentence_key(s) not in outro_keys:
            kept.append(s)
    transition_keys = {_sentence_key(t) for t in HOST_TRANSITIONS}
    while kept and _sentence_key(kept[-1]) in transition_keys:
        kept.pop()
    return polish_host_script(f"{' '.join(kept + outro_sents)}".strip())


def compose_tv_host_script(
    milestones: list[dict[str, Any]],
    spec: dict[str, Any],
    target_sec: float,
    *,
    narr_mod: Any,
    intro: str,
    outro: str,
) -> str:
    """Build one continuous TV episode script — scene per milestone, outro last."""
    milestones = _milestones_with_narrator_voice(milestones)
    parts: list[str] = [intro.strip()]
    active = [m for m in milestones if not m.get("_skipNarration")]
    for i, m in enumerate(active):
        parts.append(_host_scene_for_milestone(m))
        if i > 0 and i % 4 == 0:
            parts.append(HOST_TRANSITIONS[i % len(HOST_TRANSITIONS)])
    target_words = narr_mod.narration_target_word_count(spec, target_sec)
    body_words = len(" ".join(parts).split())
    enc_i = 0
    while body_words < int(target_words * 0.92) and enc_i < max(len(active) * 8, 48):
        m = active[enc_i % len(active)]
        parts.append(_host_encore_line(m, enc_i))
        if enc_i >= len(active) * 2:
            parts.append(HOST_WONDER_FILLERS[enc_i % len(HOST_WONDER_FILLERS)])
        body_words = len(" ".join(parts).split())
        enc_i += 1
    parts.append(outro.strip())
    text = " ".join(p for p in parts if p)
    text = narr_mod.humanize_speech_text(narr_mod.sanitize_narration_text(text), spec)
    text = ensure_outro_last(text, outro)
    return polish_host_script(text)


def polish_host_script(text: str) -> str:
    """Dedupe, kill phrase spam, strip lecture jargon."""
    out, _ = strip_forbidden_narration_phrases(text)
    out, _ = dedupe_narration_sentences(out, overlap_threshold=0.9)
    out = collapse_repeated_short_phrases(out)
    out = re.sub(r"\s{2,}", " ", out).strip()
    return out


def collapse_repeated_short_phrases(text: str, *, max_repeat: int = 1) -> str:
    """Collapse 'No way.'-style spam the sentence deduper misses."""
    sents = _sentence_split(text)
    if not sents:
        return text
    kept: list[str] = []
    prev_key = ""
    streak = 0
    for s in sents:
        key = _sentence_key(s)
        if key == prev_key or (len(key.split()) <= 3 and key in prev_key):
            streak += 1
            if streak > max_repeat:
                continue
        else:
            streak = 0
        kept.append(s)
        prev_key = key
    out = " ".join(kept)
    out = re.sub(r"(\bNo way\.\s*){2,}", "No way. ", out, flags=re.I)
    out = re.sub(r"(\bWhoa[—,-]?\s*){2,}", "Whoa — ", out, flags=re.I)
    return out.strip()


def script_has_spam_patterns(text: str) -> bool:
    if re.search(r"(\bNo way\.\s*){3,}", text, flags=re.I):
        return True
    if re.search(r"(\bWhoa\b[.!]?\s*){5,}", text, flags=re.I):
        return True
    sents = _sentence_split(text)
    if len(sents) < 8:
        return False
    keys = [_sentence_key(s) for s in sents]
    unique = len(set(keys))
    return unique / max(len(keys), 1) < 0.55


def dedupe_narration_sentences(text: str, *, overlap_threshold: float = 0.92) -> tuple[str, int]:
    """
    Remove repeated/near-duplicate sentences; keep first occurrence (preserves order + context).
    Returns (cleaned text, removed count).
    """
    sents = _sentence_split(text)
    if not sents:
        return text, 0
    kept: list[str] = []
    keys: list[str] = []
    removed = 0
    for s in sents:
        key = _sentence_key(s)
        if len(key.split()) < 4:
            if key in keys:
                removed += 1
                continue
            kept.append(s)
            keys.append(key)
            continue
        dup = False
        for prev in keys:
            if key == prev or _word_overlap(s, prev) >= overlap_threshold:
                dup = True
                break
        if dup:
            removed += 1
            continue
        kept.append(s)
        keys.append(key)
    return " ".join(kept), removed


def strip_forbidden_narration_phrases(text: str) -> tuple[str, int]:
    """Replace live-stream/chat jargon; returns (text, fix count)."""
    out = text or ""
    fixes = 0
    for pat, repl in FORBIDDEN_PHRASES:
        new, n = re.subn(pat, repl, out, flags=re.IGNORECASE)
        if n:
            fixes += n
            out = new
    out = re.sub(r"\s{2,}", " ", out)
    out = re.sub(r"\s+([,.!?])", r"\1", out)
    return out.strip(), fixes


def _hold_seconds(milestone: dict[str, Any], spec: dict[str, Any]) -> float:
    if milestone.get("beatKind") == "subbeat":
        return float(
            milestone.get("holdSec")
            or spec.get("subbeatHoldSec", spec.get("milestoneHoldSec", 12.0) * 0.45)
        )
    return float(milestone.get("holdSec") or spec.get("milestoneHoldSec", 12.0))


def expand_narration_from_milestones(
    script: str,
    milestones: list[dict[str, Any]],
    spec: dict[str, Any],
    target_sec: float,
    *,
    narr_mod: Any,
) -> str:
    """Add encore punchlines tied to milestones — never generic pad spam."""
    milestones = _milestones_with_narrator_voice(milestones)
    target_words = narr_mod.narration_target_word_count(spec, target_sec)
    beats = [m for m in milestones if not m.get("_skipNarration")]
    if not beats:
        return script

    out = polish_host_script(script)
    outro_key = _sentence_key(str(spec.get("_tvHostOutro") or ""))
    tail = ""
    if outro_key:
        sents = _sentence_split(out)
        split_at = None
        for i, s in enumerate(sents):
            if outro_key in _sentence_key(s) or any(
                m.lower() in _sentence_key(s)
                for m in (
                    "And that is our episode",
                    "Thanks for riding shotgun",
                    "You made it to the end",
                )
            ):
                if i >= max(2, len(sents) // 3):
                    split_at = i
                    break
        if split_at is not None:
            tail = " ".join(sents[split_at:]).strip()
            out = " ".join(sents[:split_at]).strip()
    for variant in range(max(48, len(beats) * 8)):
        if len(out.split()) >= int(target_words * 0.92):
            break
        prev = len(out.split())
        for m in beats:
            if len(out.split()) >= int(target_words * 0.92):
                break
            line = _host_encore_line(m, variant)
            key = _sentence_key(line)
            if key and key not in {_sentence_key(s) for s in _sentence_split(out)}:
                out = f"{out} {line}"
        if len(out.split()) <= prev:
            for m in beats:
                if len(out.split()) >= int(target_words * 0.92):
                    break
                line = _host_scene_alt_line(m, variant)
                key = _sentence_key(line)
                if key and key not in {_sentence_key(s) for s in _sentence_split(out)}:
                    out = f"{out} {line}"
        if len(out.split()) <= prev:
            line = HOST_WONDER_FILLERS[variant % len(HOST_WONDER_FILLERS)]
            key = _sentence_key(line)
            if key and key not in {_sentence_key(s) for s in _sentence_split(out)}:
                out = f"{out} {line}"
    out = polish_host_script(out)
    if tail:
        out = f"{out} {tail}".strip()
    return out


def trim_narration_to_target(script: str, target_words: int, *, max_ratio: float = 1.06) -> str:
    """Trim from the middle outward when script runs long — keep intro + outro sentences."""
    words = script.split()
    cap = int(target_words * max_ratio)
    if len(words) <= cap:
        return script
    sents = _sentence_split(script)
    if len(sents) <= 3:
        return " ".join(words[:cap])
    keep_head = 2
    keep_tail = 2
    body = sents[keep_head:-keep_tail]
    need = cap - sum(len(sents[i].split()) for i in range(keep_head))
    need -= sum(len(sents[-i - 1].split()) for i in range(keep_tail))
    trimmed_body: list[str] = []
    for s in body:
        if sum(len(x.split()) for x in trimmed_body) + len(s.split()) <= max(need, 0):
            trimmed_body.append(s)
    out = sents[:keep_head] + trimmed_body + sents[-keep_tail:]
    return " ".join(out)


def grade_narration_script(
    script: str,
    milestones: list[dict[str, Any]],
    spec: dict[str, Any],
    target_sec: float,
    *,
    narr_mod: Any,
) -> dict[str, Any]:
    """Grade script vs graded video runtime and recap voice rules."""
    words = len((script or "").split())
    target_words = narr_mod.narration_target_word_count(spec, target_sec)
    fill = words / max(target_words, 1)
    issues: list[dict[str, Any]] = []

    if fill < 0.92:
        issues.append(
            {
                "id": "too_short",
                "severity": "error",
                "message": f"Script {words} words < target {target_words} ({fill:.0%} fill)",
            }
        )
    elif fill > 1.08:
        issues.append(
            {
                "id": "too_long",
                "severity": "warn",
                "message": f"Script {words} words > target {target_words} ({fill:.0%} fill)",
            }
        )

    blob = (script or "").lower()
    for pat, _ in FORBIDDEN_PHRASES:
        if re.search(pat, blob, flags=re.IGNORECASE):
            issues.append({"id": "forbidden_phrase", "severity": "error", "message": f"Matches {pat}"})
            break

    for pat in CAPTION_LECTURE_PATTERNS:
        if re.search(pat, blob, flags=re.IGNORECASE):
            issues.append({"id": "caption_lecture", "severity": "warn", "message": f"Matches {pat}"})
            break

    _, dup_removed = dedupe_narration_sentences(script or "")
    if dup_removed >= 2:
        issues.append(
            {
                "id": "duplicate_sentences",
                "severity": "warn",
                "message": f"{dup_removed} near-duplicate sentences detected",
            }
        )

    errors = [i for i in issues if i["severity"] == "error"]
    score = 100
    score -= max(0, int((0.92 - fill) * 120)) if fill < 0.92 else 0
    score -= max(0, int((fill - 1.08) * 40)) if fill > 1.08 else 0
    score -= 8 * len([i for i in issues if i["id"] == "forbidden_phrase"])
    score -= 4 * len([i for i in issues if i["id"] == "caption_lecture"])
    score -= min(12, dup_removed * 2)
    score = max(0, min(100, score))

    acceptable = score >= 85 and not errors and fill >= 0.88 and fill <= 1.12

    if fill < 0.92:
        action = "expand_milestones"
    elif any(i["id"] == "forbidden_phrase" for i in issues):
        action = "strip_forbidden"
    elif dup_removed >= 1:
        action = "dedupe"
    elif fill > 1.08:
        action = "trim"
    else:
        action = "accept"

    return {
        "acceptable": acceptable,
        "score": score,
        "wordCount": words,
        "targetWordCount": target_words,
        "targetDurationSec": float(target_sec),
        "fillRatio": round(fill, 3),
        "issues": issues,
        "recommendedAction": action,
    }


def narration_script_needs_finalize(
    payload: dict[str, Any],
    spec: dict[str, Any],
    target_sec: float,
    *,
    narr_mod: Any,
) -> bool:
    """True when cached script must re-run meat-loop (duration drift or failed grade)."""
    script = str(payload.get("script") or "").strip()
    if not script:
        return True
    if payload.get("scriptFinalized") is not True:
        return True
    cached_dur = float(payload.get("targetDurationSec") or 0)
    if abs(cached_dur - float(target_sec)) > 6.0:
        return True
    grade = grade_narration_script(script, [], spec, target_sec, narr_mod=narr_mod)
    return not grade.get("acceptable")


def finalize_narration_script(
    script: str,
    milestones: list[dict[str, Any]],
    spec: dict[str, Any],
    target_sec: float,
    *,
    narr_mod: Any,
    run_dir: Path | None = None,
    source: str = "unknown",
) -> tuple[str, dict[str, Any]]:
    """
    Grade → fix → re-grade meat loop. Expands from milestones when short, strips junk,
    dedupes only near-exact repeats (keeps unique context). Writes grade + history JSON.
    """
    max_expand = int(spec.get("narrationScriptMeatLoopMax", 20))
    min_score = int(spec.get("narrationScriptMinScore", 85))
    history: list[dict[str, Any]] = []
    milestones = _milestones_with_narrator_voice(milestones)
    out = narr_mod.humanize_speech_text(narr_mod.sanitize_narration_text((script or "").strip()), spec)
    out, strip_fixes = strip_forbidden_narration_phrases(out)

    target_words = narr_mod.narration_target_word_count(spec, target_sec)
    for pass_i in range(max_expand):
        wc = len(out.split())
        grade = grade_narration_script(out, milestones, spec, target_sec, narr_mod=narr_mod)
        grade["pass"] = pass_i
        grade["stripFixes"] = strip_fixes
        history.append(grade)
        if wc >= int(target_words * 0.92):
            break
        out = expand_narration_from_milestones(
            out, milestones, {**spec, "_tvHostOutro": spec.get("_tvHostOutro")}, target_sec, narr_mod=narr_mod
        )

    out = polish_host_script(out)
    removed = 0
    if len(out.split()) > int(target_words * 1.08):
        out = trim_narration_to_target(out, target_words)

    outro_line = str(spec.get("_tvHostOutro") or "").strip()

    if script_has_spam_patterns(out):
        out = collapse_repeated_short_phrases(out, max_repeat=0)
        out, removed = dedupe_narration_sentences(out, overlap_threshold=0.88)

    if outro_line:
        out = ensure_outro_last(out, outro_line)

    final_grade = grade_narration_script(out, milestones, spec, target_sec, narr_mod=narr_mod)
    if script_has_spam_patterns(out):
        final_grade["acceptable"] = False
        final_grade["issues"].append(
            {
                "id": "phrase_spam",
                "severity": "error",
                "message": "Repeated hype phrases — script blocked from finalize",
            }
        )
    final_grade["dedupedSentences"] = removed
    final_grade["passes"] = len(history)
    final_grade["source"] = source

    if run_dir:
        run_dir.mkdir(parents=True, exist_ok=True)
        (run_dir / "DemoRecapNarrationScriptGrade.json").write_text(
            json.dumps(final_grade, indent=2) + "\n", encoding="utf-8"
        )
        (run_dir / "DemoRecapNarrationMeatLoopHistory.json").write_text(
            json.dumps({"passes": history, "final": final_grade}, indent=2) + "\n",
            encoding="utf-8",
        )

    status = "OK" if final_grade.get("acceptable") else "WARN"
    print(
        f"Narration script meat-loop {status}: {len(out.split())} words "
        f"(target {final_grade['targetWordCount']}, fill {final_grade['fillRatio']:.0%}, "
        f"score {final_grade['score']}, {len(history)} passes)",
        flush=True,
    )
    if not final_grade.get("acceptable"):
        for issue in final_grade.get("issues", [])[:4]:
            print(f"  script grade: [{issue.get('severity')}] {issue.get('message')}", flush=True)

    return out, final_grade


def save_finalized_narration_script(
    run_dir: Path,
    script: str,
    grade: dict[str, Any],
    spec: dict[str, Any],
    *,
    narr_mod: Any,
    source: str,
) -> str:
    """Write DemoRecapFullNarration.json after meat-loop finalize."""
    say_r = narr_mod.personal_say_rate(spec)
    acceptable = bool(grade.get("acceptable")) and not script_has_spam_patterns(script)
    payload = {
        "script": script,
        "wordCount": len(script.split()),
        "targetDurationSec": grade.get("targetDurationSec"),
        "targetWordCount": grade.get("targetWordCount"),
        "sayRateWpm": say_r,
        "source": source,
        "scriptFinalized": acceptable,
        "scriptGradeScore": grade.get("score"),
        "scriptFillRatio": grade.get("fillRatio"),
        "scriptGradeAcceptable": acceptable,
    }
    payload.update(narr_mod.narration_voice_metadata(spec, run_dir))
    path = run_dir / "DemoRecapFullNarration.json"
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    (run_dir / "DemoRecapFullNarration.txt").write_text(script + "\n", encoding="utf-8")
    return script
