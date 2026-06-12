#!/usr/bin/env python3
"""Kit prefab catalog + per-card thumbnails for planner concept approval."""
from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import subprocess
import threading
from pathlib import Path
from typing import Any, Callable

from PIL import Image, ImageDraw, ImageFont

CATALOG_MANIFEST_REL = Path(
    "Assets/EnvironmentKit/ResearchCache/planner-kit-catalog/manifest.json"
)
CATALOG_THUMBS_REL = Path("Assets/EnvironmentKit/ResearchCache/planner-kit-catalog/thumbs")
CONCEPT_ASSETS_REL = Path(
    "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_pending-review/planner-session/assets"
)
CONCEPT_LAYOUT_DETAIL_REL = Path(
    "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_pending-review/planner-session/concept-layout-detail.png"
)
CONCEPT_DENSITY_DETAIL_REL = Path(
    "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_pending-review/planner-session/concept-density-detail.png"
)

GENERATED_PROPS_ROOT = Path("Assets/EnvironmentKit/Generated/PlannerProps/Prefabs")
GENERATED_CHARACTERS_ROOT = Path("Assets/EnvironmentKit/Generated/PlannerCharacters/Prefabs")
PLAYER_RESOURCES = Path("Assets/Resources/Player_KenneyVisual.prefab")
CHARACTER_SCULPT_KINDS = frozenset({"npc", "enemy", "player"})
LP_ROOT = Path("Assets/PolitePenguin/LPMagicalForest/Prefabs")
CC0_CHARS = Path("Assets/EnvironmentKit/CC0Imports/Prefabs/Characters")
CC0_ITEMS = Path("Assets/EnvironmentKit/CC0Imports/Prefabs/Items")
GAME_ENEMY = Path("Assets/GameData/Prefabs/CaveEnemy.prefab")

GUID_RE = re.compile(r"guid:\s*([a-f0-9]{32})", re.I)
SOURCE_PREFAB_RE = re.compile(
    r"m_SourcePrefab:\s*\{fileID:\s*\d+,\s*guid:\s*([a-f0-9]{32})",
    re.I,
)
_HUB_GUID_INDEX: dict[str, dict[str, Path]] = {}
PLACEHOLDER_THUMB_SIZE = (256, 256)
PLACEHOLDER_MARKER_RGB = (88, 140, 210)


def _load_font(size: int) -> ImageFont.ImageFont:
    for path in (
        "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/Library/Fonts/Arial.ttf",
    ):
        try:
            return ImageFont.truetype(path, size)
        except OSError:
            continue
    return ImageFont.load_default()


def _read_meta_guid(meta_path: Path) -> str | None:
    if not meta_path.is_file():
        return None
    text = meta_path.read_text(encoding="utf-8", errors="ignore")
    m = GUID_RE.search(text)
    return m.group(1).lower() if m else None


def _guid_index(hub: Path) -> dict[str, Path]:
    key = str(hub.resolve())
    cached = _HUB_GUID_INDEX.get(key)
    if cached is not None:
        return cached
    idx: dict[str, Path] = {}
    assets = hub / "Assets"
    if assets.is_dir():
        for meta in assets.rglob("*.meta"):
            guid = _read_meta_guid(meta)
            if not guid or guid in idx:
                continue
            asset = Path(str(meta)[:-5])
            if asset.is_file():
                idx[guid] = asset
    _HUB_GUID_INDEX[key] = idx
    return idx


def _guid_to_path(hub: Path, guid: str) -> Path | None:
    if not guid:
        return None
    return _guid_index(hub).get(guid.lower())


def _is_texture_candidate(path: Path) -> bool:
    low = path.name.lower()
    skip = ("normal", "norm", "mask", "rough", "metallic", "ao", "height", "icon", "legend")
    return path.suffix.lower() in {".png", ".jpg", ".jpeg", ".webp", ".tga"} and not any(s in low for s in skip)


_BAD_PREVIEW_NAME_TOKENS = (
    "atlas",
    "coloratlas",
    "palette",
    "ramp",
    "gradient",
    "lut",
    "lookup",
    "spectrum",
    "swatch",
    "colormap",
    "color_chart",
    "colourchart",
)


def _is_bad_preview_texture(path: Path) -> bool:
    """Reject shared color atlases / ramps — useless as a single-asset card preview."""
    if not path.is_file():
        return True
    low = path.name.lower()
    if any(tok in low for tok in _BAD_PREVIEW_NAME_TOKENS):
        return True
    try:
        with Image.open(path) as im:
            w, h = im.size
            if w < 16 or h < 16:
                return True
            short, long = min(w, h), max(w, h)
            if long / max(1, short) > 2.0:
                return True
    except OSError:
        return True
    return False


def _is_good_preview_texture(path: Path) -> bool:
    return _is_texture_candidate(path) and not _is_bad_preview_texture(path)


def _search_texture_by_stem(hub: Path, stem: str) -> Path | None:
    assets = hub / "Assets"
    if not assets.is_dir():
        return None
    tokens = {stem}
    if "_" in stem:
        tokens.add(stem.rsplit("_", 1)[0])
        tokens.add(stem.replace("_01", "").replace("_02", ""))
    roots = [
        assets / "PolitePenguin",
        assets / "EnvironmentKit",
        assets,
    ]
    best: Path | None = None
    best_score = -1
    for root in roots:
        if not root.is_dir():
            continue
        for tok in tokens:
            if len(tok) < 4:
                continue
            try:
                for hit in root.rglob(f"*{tok}*"):
                    if not hit.is_file() or not _is_good_preview_texture(hit):
                        continue
                    score = 0
                    if tok.lower() in hit.stem.lower():
                        score += 10
                    if "albedo" in hit.name.lower() or "diffuse" in hit.name.lower() or "basecolor" in hit.name.lower():
                        score += 8
                    if "texture" in str(hit.parent).lower():
                        score += 4
                    if hit.suffix.lower() == ".png":
                        score += 2
                    if score > best_score:
                        best_score = score
                        best = hit
            except OSError:
                continue
    return best


def _textures_from_yaml_guids(hub: Path, text: str, *, limit: int = 80) -> Path | None:
    for guid in GUID_RE.findall(text)[:limit]:
        resolved = _guid_to_path(hub, guid)
        if resolved and _is_good_preview_texture(resolved):
            return resolved
        if resolved and resolved.suffix.lower() == ".mat":
            mat_text = resolved.read_text(encoding="utf-8", errors="ignore")
            for g2 in GUID_RE.findall(mat_text)[:20]:
                tex = _guid_to_path(hub, g2)
                if tex and _is_good_preview_texture(tex):
                    return tex
        if resolved and resolved.suffix.lower() == ".prefab":
            nested = resolved.read_text(encoding="utf-8", errors="ignore")
            tex = _textures_from_yaml_guids(hub, nested, limit=40)
            if tex:
                return tex
    return None


def _is_generated_prop_prefab(prefab_rel: str | None) -> bool:
    return bool(prefab_rel) and "/Generated/PlannerProps/" in prefab_rel.replace("\\", "/")


def mesh_source_label(card: dict[str, Any]) -> str:
    """Stable kit-style label for mesh generation (never stacks Gen_ prefixes)."""
    if card.get("meshSourceLabel"):
        raw = re.sub(r"^(Gen_)+", "", str(card["meshSourceLabel"]))
        card["meshSourceLabel"] = raw
        return raw
    raw = str(card.get("slotLabel") or card.get("label") or "")
    raw = re.sub(r"^(Gen_)+", "", raw)
    if not raw or raw.startswith("asset:"):
        stem = Path(str(card.get("prefabPath") or "")).stem
        raw = re.sub(r"^(Gen_)+", "", stem) or str(card.get("id") or "prop")
    card["meshSourceLabel"] = raw
    return raw


AI_MESH_LABEL_KEYWORDS = (
    "orb",
    "crystal",
    "gem",
    "shard",
    "pickup",
    "loot",
    "relic",
    "token",
    "key",
    "powerup",
    "collectible",
)


def _slot_wants_ai_mesh(slot_label: str, kind: str) -> bool:
    lab = (slot_label or "").lower()
    return any(k in lab for k in AI_MESH_LABEL_KEYWORDS)


def card_wants_ai_mesh(card: dict[str, Any]) -> bool:
    """Props/collectibles use Cursor→Unity generated meshes, not third-party kit prefab icons."""
    if card.get("needsAiMesh"):
        return True
    kind = str(card.get("cardType") or "").lower()
    if kind in ("prop", "collectible"):
        if _is_generated_prop_prefab(str(card.get("prefabPath") or "")):
            return False
        return True
    kind = kind or "prop"
    slot = str(card.get("slotLabel") or "")
    label = str(card.get("label") or "")
    return _slot_wants_ai_mesh(slot, kind) or _slot_wants_ai_mesh(label, kind)


def card_uses_kit_pool(card: dict[str, Any]) -> bool:
    if card_wants_ai_mesh(card):
        return False
    return True


def prop_kind_for_card(card: dict[str, Any]) -> str:
    cat = str(card.get("categoryKey") or "").lower()
    lab = mesh_source_label(card).lower()
    if "grass" in cat or "grass" in lab:
        return "grass"
    if "rock" in cat or "rock" in lab or "stone" in lab:
        return "rock"
    if "tree" in cat or "tree" in lab:
        return "tree"
    if any(k in lab for k in ("orb", "crystal", "gem", "shard", "pickup", "loot", "relic")):
        return "orb"
    if "collectible" in cat or str(card.get("cardType") or "").lower() == "collectible":
        return "orb"
    return "bush"


def stable_generated_prefab_rel(card: dict[str, Any]) -> str:
    core = re.sub(r"[^a-zA-Z0-9]+", "_", mesh_source_label(card)).strip("_")[:48] or "prop"
    slug = core if core.startswith("Gen_") else f"Gen_{core}"
    kind = prop_kind_for_card(card)
    return f"Assets/EnvironmentKit/Generated/PlannerProps/Prefabs/{kind}/{slug}.prefab"


def _texture_for_prefab(hub: Path, prefab_rel: str) -> Path | None:
    prefab = hub / prefab_rel
    if not prefab.is_file():
        return None
    norm = prefab_rel.replace("\\", "/")
    if "/Generated/PlannerProps/" in norm:
        tex_root = hub / "Assets/EnvironmentKit/Generated/PlannerProps/Textures"
        stem = prefab.stem
        for name in (
            f"{stem}_albedo.png",
            f"{stem}_leaf.png",
            f"{stem}_trunk.png",
        ):
            candidate = tex_root / name
            if candidate.is_file() and _is_good_preview_texture(candidate):
                return candidate
        stripped = re.sub(r"^(Gen_)+", "", stem)
        for name in (
            f"Gen_{stripped}_albedo.png",
            f"Gen_{stripped}_leaf.png",
            f"Gen_{stripped}_trunk.png",
        ):
            candidate = tex_root / name
            if candidate.is_file() and _is_good_preview_texture(candidate):
                return candidate
        for candidate in sorted(tex_root.glob(f"{stem}*.png")):
            if _is_good_preview_texture(candidate):
                return candidate
    folder = prefab.parent
    for ext in (".png", ".jpg", ".jpeg", ".webp"):
        candidate = folder / f"{prefab.stem}{ext}"
        if candidate.is_file():
            return candidate
    for sibling in folder.glob(f"{prefab.stem}*.png"):
        if _is_good_preview_texture(sibling):
            return sibling
    textures_dir = folder.parent / "Textures"
    if textures_dir.is_dir():
        for sibling in textures_dir.glob(f"*{prefab.stem.split('_')[0]}*.png"):
            if _is_good_preview_texture(sibling):
                return sibling
    text = prefab.read_text(encoding="utf-8", errors="ignore")
    source_match = SOURCE_PREFAB_RE.search(text)
    if source_match:
        source = _guid_to_path(hub, source_match.group(1))
        if source and source.is_file():
            tex = _textures_from_yaml_guids(hub, source.read_text(encoding="utf-8", errors="ignore"))
            if tex:
                return tex
    tex = _textures_from_yaml_guids(hub, text)
    if tex:
        return tex
    return _search_texture_by_stem(hub, prefab.stem)


def _classify_prefab_path(rel: str) -> str:
    low = rel.lower().replace("\\", "/")
    name = Path(rel).stem.lower()
    if "/plannercharacters/" in low:
        if "/player/" in low:
            return "player"
        if "/enemy/" in low:
            return "enemy"
        return "npc"
    if "/characters/" in low or name.startswith("npc_"):
        if name.startswith("enemy_") or name.startswith("boss_"):
            return "enemy"
        return "npc"
    if "/items/" in low or name.startswith(("k", "w", "b", "a", "c", "l", "p", "g-")):
        return "collectible"
    if any(k in name for k in ("tree", "bush", "grass", "flower", "plant", "fern")):
        if "tree" in name:
            return "prop_tree"
        if "grass" in name or "flower" in name or "plant" in name:
            return "prop_grass"
        return "prop_bush"
    if any(k in name for k in ("rock", "stone", "boulder", "cliff")):
        return "prop_rock"
    return "prop"


def _scan_prefab_pool(hub: Path, roots: list[Path]) -> dict[str, list[str]]:
    pools: dict[str, list[str]] = {
        "prop_tree": [],
        "prop_grass": [],
        "prop_bush": [],
        "prop_rock": [],
        "prop": [],
        "npc": [],
        "enemy": [],
        "player": [],
        "collectible": [],
    }
    seen: set[str] = set()
    for root in roots:
        abs_root = hub / root
        if not abs_root.is_dir():
            continue
        for prefab in sorted(abs_root.rglob("*.prefab")):
            try:
                rel = prefab.relative_to(hub).as_posix()
            except ValueError:
                continue
            if rel in seen:
                continue
            seen.add(rel)
            bucket = _classify_prefab_path(rel)
            pools.setdefault(bucket, []).append(rel)
            if bucket.startswith("prop_") and bucket != "prop":
                pools["prop"].append(rel)
    return pools


def _ingest_unity_thumbs(hub: Path, catalog: dict[str, Any]) -> None:
    """Map on-disk Unity AssetPreview PNGs into prefabThumbs when manifest is stale."""
    thumbs_dir = hub / CATALOG_THUMBS_REL
    if not thumbs_dir.is_dir():
        return
    thumbs = catalog.setdefault("prefabThumbs", {})
    pools = catalog.get("pools") or {}
    for pool in pools.values():
        for prefab in pool or []:
            if prefab in thumbs:
                continue
            safe = prefab.replace("/", "_").replace("\\", "_")
            candidate = thumbs_dir / f"{safe}.png"
            if candidate.is_file():
                thumbs[prefab] = candidate.relative_to(hub).as_posix()


def _prepend_generated_characters(hub: Path, pools: dict[str, list[str]]) -> dict[str, list[str]]:
    """Sculpted Hub characters win over kit CC0 in NPC/enemy/player pools."""
    if not (hub / GENERATED_CHARACTERS_ROOT).is_dir():
        return pools
    generated = _scan_prefab_pool(hub, [GENERATED_CHARACTERS_ROOT])
    out = dict(pools)
    for key in ("npc", "enemy", "player"):
        gen_items = list(generated.get(key) or [])
        if not gen_items and key == "player":
            gen_items = list(generated.get("npc") or [])
        if not gen_items:
            continue
        merged: list[str] = []
        for rel in gen_items:
            if rel not in merged:
                merged.append(rel)
        for rel in out.get(key) or []:
            if rel not in merged:
                merged.append(rel)
        out[key] = merged
    return out


def _prepend_generated_pools(hub: Path, pools: dict[str, list[str]]) -> dict[str, list[str]]:
    """Hub-owned generated props win over third-party kit in revise/accept pools."""
    if not (hub / GENERATED_PROPS_ROOT).is_dir():
        return pools
    generated = _scan_prefab_pool(hub, [GENERATED_PROPS_ROOT])
    out = dict(pools)
    for key, gen_items in generated.items():
        if not gen_items:
            continue
        merged: list[str] = []
        for rel in gen_items:
            if rel not in merged:
                merged.append(rel)
        for rel in out.get(key) or []:
            if rel not in merged:
                merged.append(rel)
        out[key] = merged
    prop_union = list(out.get("prop") or [])
    for rel in generated.get("prop") or []:
        if rel not in prop_union:
            prop_union.insert(0, rel)
    out["prop"] = prop_union
    return out


def load_or_build_catalog(hub: Path) -> dict[str, Any]:
    manifest_path = hub / CATALOG_MANIFEST_REL
    if manifest_path.is_file():
        try:
            data = json.loads(manifest_path.read_text(encoding="utf-8"))
            if data.get("pools") and data.get("version", 0) >= 1:
                if not data.get("prefabThumbs"):
                    data["prefabThumbs"] = {}
                data["pools"] = _prepend_generated_pools(hub, data.get("pools") or {})
                _ingest_unity_thumbs(hub, data)
                return data
        except json.JSONDecodeError:
            pass

    roots = [GENERATED_PROPS_ROOT, LP_ROOT, CC0_CHARS, CC0_ITEMS]
    if (hub / GAME_ENEMY).is_file():
        roots.append(GAME_ENEMY.parent)

    pools = _scan_prefab_pool(hub, roots)
    pools = _prepend_generated_pools(hub, pools)
    pools = _prepend_generated_characters(hub, pools)
    if (hub / PLAYER_RESOURCES).is_file():
        player_rel = PLAYER_RESOURCES.as_posix()
        player_pool = [player_rel]
        for rel in pools.get("npc") or []:
            if rel not in player_pool:
                player_pool.append(rel)
        pools["player"] = player_pool
    pools["prop"] = sorted(set(pools.get("prop", [])))

    manifest = {
        "version": 1,
        "pools": pools,
        "prefabThumbs": {},
    }
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest


def _pool_for_marker_kind(kind: str, label: str, pools: dict[str, list[str]]) -> list[str]:
    k = (kind or "").lower()
    lab = (label or "").lower()
    if k == "npc":
        return pools.get("npc") or []
    if k == "enemy":
        return pools.get("enemy") or []
    if k == "collectible":
        return pools.get("collectible") or []
    if "tree" in lab:
        return pools.get("prop_tree") or pools.get("prop") or []
    if "grass" in lab:
        return pools.get("prop_grass") or pools.get("prop") or []
    if "rock" in lab:
        return pools.get("prop_rock") or pools.get("prop") or []
    if k == "prop":
        return pools.get("prop_bush") or pools.get("prop") or []
    return pools.get("prop") or []


def _category_key(kind: str, label: str) -> str:
    k = (kind or "prop").lower()
    lab = (label or "").lower()
    if k == "prop":
        if "tree" in lab:
            return "prop:tree"
        if "grass" in lab:
            return "prop:grass"
        if "rock" in lab:
            return "prop:rock"
        if "plat" in lab:
            return "prop:platform"
        return "prop:scatter"
    if k in ("npc", "enemy", "collectible"):
        return f"{k}:default"
    return f"{k}:other"


def _pick_from_pool(pool: list[str], index: int) -> str | None:
    if not pool:
        return None
    return pool[index % len(pool)]


def _unique_prefab_paths(pool: list[str]) -> list[str]:
    seen: set[str] = set()
    out: list[str] = []
    for prefab in pool:
        if not prefab or prefab in seen:
            continue
        seen.add(prefab)
        out.append(prefab)
    return out


def _pick_revise_prefab(
    raw_pool: list[str],
    current_prefab: str | None,
    candidate_index: int,
) -> tuple[str | None, int, list[str]]:
    """Advance to the next distinct prefab path (Revise must change the asset, not just re-index)."""
    by_path = _unique_prefab_paths(raw_pool)
    if not by_path:
        return None, candidate_index, []
    start = int(candidate_index) + 1
    for offset in range(len(by_path)):
        idx = start + offset
        pick = by_path[idx % len(by_path)]
        if pick != current_prefab:
            return pick, idx, by_path
    idx = start
    pick = by_path[idx % len(by_path)]
    return pick, idx, by_path


def _unity_thumb_rel(catalog: dict[str, Any], prefab_rel: str) -> str | None:
    rel = (catalog.get("prefabThumbs") or {}).get(prefab_rel)
    return rel if rel else None


def _preview_identity(hub: Path, catalog: dict[str, Any], prefab_rel: str) -> tuple[str, str]:
    thumb = _unity_thumb_rel(catalog, prefab_rel)
    if thumb and (hub / thumb).is_file():
        return ("unity", thumb)
    tex = _texture_for_prefab(hub, prefab_rel)
    if tex:
        return ("tex", str(tex.resolve()))
    return ("none", prefab_rel)


def _prefab_has_distinct_preview(hub: Path, catalog: dict[str, Any], prefab_rel: str) -> bool:
    kind, _ = _preview_identity(hub, catalog, prefab_rel)
    return kind in ("unity", "tex")


def _revise_pool(hub: Path, catalog: dict[str, Any], pool: list[str]) -> list[str]:
    """Drop atlas-only duplicates so Revise cycles visually distinct kit options."""
    if not pool:
        return []
    with_preview: list[str] = []
    seen: set[tuple[str, str]] = set()
    for prefab in pool:
        kind, key = _preview_identity(hub, catalog, prefab)
        if kind == "none":
            continue
        ident = (kind, key)
        if ident in seen:
            continue
        seen.add(ident)
        with_preview.append(prefab)
    if with_preview:
        return with_preview
    # No Unity/texture preview — one representative per kit family (not 12× the same atlas strip).
    fallback: list[str] = []
    seen_families: set[str] = set()

    def _family(prefab_rel: str) -> str:
        stem = Path(prefab_rel).stem
        parts = stem.split("_")
        if len(parts) >= 2:
            return "_".join(parts[:2])
        return stem

    for prefab in pool:
        fam = _family(prefab)
        if fam in seen_families:
            continue
        seen_families.add(fam)
        fallback.append(prefab)
    return fallback or list(pool[:1])


def _thumb_output_path(hub: Path, prefab_rel: str | None, card_id: str) -> Path:
    out_dir = hub / CONCEPT_ASSETS_REL
    out_dir.mkdir(parents=True, exist_ok=True)
    if prefab_rel:
        digest = hashlib.md5(prefab_rel.encode("utf-8")).hexdigest()[:16]
        return out_dir / f"prefab_{digest}.png"
    safe = re.sub(r"[^a-zA-Z0-9_-]+", "_", card_id)[:80]
    return out_dir / f"{safe}.png"


def _is_placeholder_thumb(path: Path) -> bool:
    if not path.is_file():
        return True
    try:
        with Image.open(path) as im:
            if im.size == PLACEHOLDER_THUMB_SIZE:
                return True
            # New labeled kit placeholder (320×320; marker pixel top-left).
            if im.size == (320, 320):
                im = im.convert("RGB")
                if im.load()[16, 16] == PLACEHOLDER_MARKER_RGB:
                    return True
    except OSError:
        return True
    return False


def _thumb_content_stats(im: Image.Image) -> tuple[float, float, int, int]:
    """Return (mean luma, content aspect, content_w, content_h) for a 320px thumb."""
    im = im.convert("RGB")
    w, h = im.size
    pixels = im.load()
    bg = (24, 28, 38)
    min_x, min_y, max_x, max_y = w, h, 0, 0
    found = False
    luma_sum = 0.0
    luma_n = 0
    for y in range(h):
        for x in range(w):
            px = pixels[x, y]
            if px != bg:
                found = True
                min_x = min(min_x, x)
                max_x = max(max_x, x)
                min_y = min(min_y, y)
                max_y = max(max_y, y)
                luma_sum += 0.299 * px[0] + 0.587 * px[1] + 0.114 * px[2]
                luma_n += 1
    if not found:
        return 0.0, 1.0, 0, 0
    bw = max_x - min_x + 1
    bh = max_y - min_y + 1
    aspect = max(bw, bh) / max(1, min(bw, bh))
    mean_luma = luma_sum / max(1, luma_n)
    return mean_luma, aspect, bw, bh


def _is_unity_default_prefab_icon(path: Path) -> bool:
    """Reject Unity's 128×128 package icon — not a real AssetPreview render."""
    try:
        with Image.open(path) as im:
            if im.size != (128, 128):
                return False
            mean_luma, _aspect, cw, ch = _thumb_content_stats(im.convert("RGB"))
            # Real AssetPreview thumbnails fill most of the 128×128 canvas.
            if cw >= 96 and ch >= 96 and mean_luma >= 22:
                return False
            return True
    except OSError:
        return False


def _is_flat_texture_card_thumb(path: Path) -> bool:
    """Reject centered albedo/texture swatches — not upscaled Unity AssetPreview renders."""
    try:
        with Image.open(path) as im:
            if im.size != (320, 320):
                return False
            im = im.convert("RGB")
            pixels = im.load()
            bg = (24, 28, 38)
            corners = (pixels[0, 0], pixels[319, 0], pixels[0, 319], pixels[319, 319])
            if not all(px == bg for px in corners):
                return False
            mean_luma, aspect, cw, ch = _thumb_content_stats(im)
            if cw < 40 or ch < 40:
                return False
            # Upscaled 128px AssetPreview / PreviewRenderUtility on our 320 canvas.
            if cw >= 200 and ch >= 200:
                return False
            if cw >= 180 and ch >= 180 and mean_luma > 45:
                return False
            if not (0.65 <= aspect <= 1.55):
                return False
            margin_x = (320 - cw) // 2
            if margin_x < 6:
                return False
            if mean_luma > 120:
                return False
            left_bg = sum(1 for y in range(320) for x in range(margin_x) if pixels[x, y] == bg)
            return left_bg > margin_x * 320 * 0.8
    except OSError:
        pass
    return False


def _is_bad_saved_thumb(path: Path) -> bool:
    """Reject atlas strips, blank exports, and near-black useless previews."""
    if not path.is_file():
        return True
    if _is_unity_default_prefab_icon(path):
        return True
    if _is_flat_texture_card_thumb(path):
        return True
    if _is_placeholder_thumb(path):
        try:
            with Image.open(path) as im:
                # New labeled 320px placeholder is an acceptable fallback preview.
                if im.size == (320, 320) and im.convert("RGB").load()[16, 16] == PLACEHOLDER_MARKER_RGB:
                    return False
        except OSError:
            pass
        return True
    try:
        with Image.open(path) as im:
            mean_luma, aspect, cw, ch = _thumb_content_stats(im)
            if cw == 0:
                return True
            # Extreme thin atlas strips only — tall NPC silhouettes are valid.
            if aspect > 6.0 and min(cw, ch) < 80:
                return True
            # Mostly dark canvas with a thin strip reads as a black box in the UI.
            if mean_luma < 48 and max(cw, ch) < 180:
                return True
            if mean_luma < 22:
                return True
    except OSError:
        return True
    return False


def _copy_thumb_to_card(src: Path, dest: Path) -> bool:
    """Copy or upscale a catalog / Unity preview onto the 320px concept card canvas."""
    try:
        im = Image.open(src).convert("RGB")
        w, h = im.size
        dest.parent.mkdir(parents=True, exist_ok=True)
        if (w, h) == (320, 320):
            shutil.copy2(src, dest)
            return True
        canvas = Image.new("RGB", (320, 320), (24, 28, 38))
        if w == h == 128:
            up = im.resize((256, 256), Image.Resampling.LANCZOS)
            canvas.paste(up, (32, 32))
        else:
            im.thumbnail((300, 300), Image.Resampling.LANCZOS)
            ox = (320 - im.width) // 2
            oy = (320 - im.height) // 2
            canvas.paste(im, (ox, oy))
        canvas.save(dest, format="PNG", optimize=True)
        return dest.is_file()
    except OSError:
        return False


def _write_texture_thumb(dest: Path, tex: Path) -> bool:
    if _is_bad_preview_texture(tex):
        return False
    try:
        im = Image.open(tex).convert("RGB")
        canvas = Image.new("RGB", (320, 320), (24, 28, 38))
        im.thumbnail((300, 300), Image.Resampling.LANCZOS)
        ox = (320 - im.width) // 2
        oy = (320 - im.height) // 2
        canvas.paste(im, (ox, oy))
        canvas.save(dest, format="PNG", optimize=True)
        return True
    except OSError:
        return False


def _placeholder_thumb(
    hub: Path,
    card_id: str,
    prefab_rel: str | None,
    label: str,
    *,
    generated_prop: bool = False,
) -> str:
    out = _thumb_output_path(hub, prefab_rel, card_id)
    size = 320
    img = Image.new("RGB", (size, size), (54, 62, 78))
    draw = ImageDraw.Draw(img)
    font = _load_font(15)
    small = _load_font(11)
    title_font = _load_font(22)
    title = (label or Path(prefab_rel).stem if prefab_rel else card_id)[:32]
    initial = (title[:1] or "?").upper()
    draw.point((16, 16), PLACEHOLDER_MARKER_RGB)
    draw.rounded_rectangle([10, 10, size - 10, size - 10], radius=16, outline=(120, 145, 185), width=2)
    draw.rounded_rectangle([18, 18, size - 18, size - 18], radius=12, fill=(68, 78, 98))
    draw.ellipse([108, 46, 212, 150], fill=(88, 108, 138), outline=(150, 175, 210))
    draw.text((160, 98), initial, fill=(235, 242, 255), font=title_font, anchor="mm")
    draw.text((size // 2, 178), title, fill=(245, 248, 255), font=font, anchor="mm")
    hint = (
        "3D preview loading…"
        if not generated_prop
        else "3D preview — click Regenerate mesh"
    )
    draw.text((size // 2, 210), hint, fill=(185, 198, 220), font=small, anchor="mm")
    img.save(out, format="PNG", optimize=True)
    return out.relative_to(hub).as_posix()


def _wrap_prefab_path(rel: str) -> list[str]:
    parts: list[str] = []
    cur = rel
    while len(cur) > 34:
        parts.append(cur[:34])
        cur = cur[34:]
    if cur:
        parts.append(cur)
    return parts[:6]


def _catalog_thumb_file(hub: Path, prefab_rel: str) -> Path:
    safe = prefab_rel.replace("\\", "/").replace("/", "_")
    return hub / CATALOG_THUMBS_REL / f"{safe}.png"


def write_generated_prop_mesh_thumb(
    hub: Path,
    card_id: str,
    prefab_rel: str | None,
    catalog: dict[str, Any] | None = None,
) -> str | None:
    """Copy a Unity 3D preview PNG when it is not the 128px package icon."""
    if not _is_generated_prop_prefab(prefab_rel):
        return None
    dest = _thumb_output_path(hub, prefab_rel, card_id)
    dest.parent.mkdir(parents=True, exist_ok=True)
    if dest.is_file() and _is_bad_saved_thumb(dest):
        try:
            dest.unlink()
        except OSError:
            pass
    sources: list[Path] = []
    if catalog:
        rel_thumb = (catalog.get("prefabThumbs") or {}).get(prefab_rel or "")
        if rel_thumb:
            sources.append(hub / rel_thumb)
    sources.append(_catalog_thumb_file(hub, prefab_rel or ""))
    for src in sources:
        if not src.is_file() or _is_bad_saved_thumb(src):
            continue
        if _copy_thumb_to_card(src, dest) and not _is_bad_saved_thumb(dest):
            return dest.relative_to(hub).as_posix()
    return None


def write_tree_composite_thumb(
    hub: Path, card_id: str, prefab_rel: str | None
) -> str | None:
    """Stack trunk + canopy textures so tree cards don't look like flat grass."""
    if not prefab_rel:
        return None
    stem = Path(prefab_rel).stem
    tex_root = hub / "Assets/EnvironmentKit/Generated/PlannerProps/Textures"
    trunk = tex_root / f"{stem}_trunk.png"
    leaf = tex_root / f"{stem}_leaf.png"
    if not leaf.is_file():
        return None
    dest = _thumb_output_path(hub, prefab_rel, card_id)
    dest.parent.mkdir(parents=True, exist_ok=True)
    try:
        canvas = Image.new("RGB", (320, 320), (24, 28, 38))
        if trunk.is_file():
            tr = Image.open(trunk).convert("RGB")
            tr = tr.resize((72, 120), Image.Resampling.LANCZOS)
            canvas.paste(tr, ((320 - 72) // 2, 188))
        lf = Image.open(leaf).convert("RGB")
        lf.thumbnail((220, 180), Image.Resampling.LANCZOS)
        canvas.paste(lf, ((320 - lf.width) // 2, 24))
        canvas.save(dest, format="PNG", optimize=True)
        return dest.relative_to(hub).as_posix() if dest.is_file() else None
    except OSError:
        return None


def write_generated_prop_albedo_thumb(
    hub: Path, card_id: str, prefab_rel: str | None, card: dict[str, Any] | None = None
) -> str | None:
    """Write a 320px albedo swatch for Hub-generated props (reliable vs Unity package icon)."""
    if not _is_generated_prop_prefab(prefab_rel):
        return None
    if card and prop_kind_for_card(card) == "tree":
        tree_rel = write_tree_composite_thumb(hub, card_id, prefab_rel)
        if tree_rel:
            return tree_rel
        return None
    dest = _thumb_output_path(hub, prefab_rel, card_id)
    dest.parent.mkdir(parents=True, exist_ok=True)
    if dest.is_file() and _is_bad_saved_thumb(dest):
        try:
            dest.unlink()
        except OSError:
            pass
    tex = _texture_for_prefab(hub, prefab_rel or "")
    if tex and tex.is_file() and _write_texture_thumb(dest, tex) and not _is_bad_saved_thumb(dest):
        return dest.relative_to(hub).as_posix()
    return None


def resolve_card_thumbnail(hub: Path, catalog: dict[str, Any], card_id: str, prefab_rel: str | None, label: str) -> str:
    dest = _thumb_output_path(hub, prefab_rel, card_id)
    if dest.is_file() and _is_bad_saved_thumb(dest):
        try:
            dest.unlink()
        except OSError:
            pass

    if prefab_rel:
        # Generated props: 3D mesh preview only (never flat albedo swatches).
        if _is_generated_prop_prefab(prefab_rel):
            mesh_rel = write_generated_prop_mesh_thumb(hub, card_id, prefab_rel, catalog)
            if mesh_rel:
                return mesh_rel
            stem = Path(prefab_rel).stem.lower()
            if "tree" in stem:
                tree_rel = write_tree_composite_thumb(hub, card_id, prefab_rel)
                if tree_rel:
                    tree_path = hub / tree_rel
                    if tree_path.is_file() and not _is_bad_saved_thumb(tree_path):
                        return tree_rel

        thumbs = catalog.get("prefabThumbs") or {}
        rel_thumb = thumbs.get(prefab_rel)
        if rel_thumb:
            src = hub / rel_thumb
            if src.is_file() and not _is_bad_saved_thumb(src):
                if _copy_thumb_to_card(src, dest) and not _is_bad_saved_thumb(dest):
                    return dest.relative_to(hub).as_posix()
        cat_src = _catalog_thumb_file(hub, prefab_rel)
        if cat_src.is_file() and not _is_bad_saved_thumb(cat_src):
            if _copy_thumb_to_card(cat_src, dest) and not _is_bad_saved_thumb(dest):
                return dest.relative_to(hub).as_posix()

        if not _is_generated_prop_prefab(prefab_rel):
            tex = _texture_for_prefab(hub, prefab_rel)
            if tex and tex.is_file() and _write_texture_thumb(dest, tex) and not _is_bad_saved_thumb(dest):
                return dest.relative_to(hub).as_posix()

    if dest.is_file() and not _is_bad_saved_thumb(dest) and not _is_placeholder_thumb(dest):
        return dest.relative_to(hub).as_posix()

    return _placeholder_thumb(
        hub,
        card_id,
        prefab_rel,
        label,
        generated_prop=_is_generated_prop_prefab(prefab_rel),
    )


def write_orb_sphere_preview_thumb(
    hub: Path, card_id: str, prefab_rel: str | None, card: dict[str, Any] | None = None
) -> str | None:
    """Glowing sphere card preview for generated orbs (works without Unity AssetPreview)."""
    if not _is_generated_prop_prefab(prefab_rel):
        return None
    if card and prop_kind_for_card(card) != "orb":
        lab = mesh_source_label(card).lower()
        if "orb" not in lab and "crystal" not in lab and "gem" not in lab:
            return None
    dest = _thumb_output_path(hub, prefab_rel, card_id)
    dest.parent.mkdir(parents=True, exist_ok=True)
    tex = _texture_for_prefab(hub, prefab_rel or "")
    r, g, b = 80, 180, 240
    if tex and tex.is_file():
        try:
            with Image.open(tex) as im:
                im = im.convert("RGB").resize((12, 12))
                px = list(im.getdata())
                r = sum(p[0] for p in px) // len(px)
                g = sum(p[1] for p in px) // len(px)
                b = sum(p[2] for p in px) // len(px)
        except OSError:
            pass
    try:
        size = 320
        canvas = Image.new("RGB", (size, size), (24, 28, 38))
        draw = ImageDraw.Draw(canvas)
        cx, cy = size // 2, size // 2 - 6
        for radius, alpha in ((118, 28), (96, 55), (74, 120)):
            glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
            gdraw = ImageDraw.Draw(glow)
            gdraw.ellipse(
                [cx - radius, cy - radius, cx + radius, cy + radius],
                fill=(r, g, b, alpha),
            )
            canvas = Image.alpha_composite(canvas.convert("RGBA"), glow).convert("RGB")
            draw = ImageDraw.Draw(canvas)
        draw.ellipse([cx - 62, cy - 62, cx + 62, cy + 62], fill=(r, g, b), outline=(220, 240, 255))
        draw.ellipse([cx - 28, cy - 40, cx - 6, cy - 18], fill=(min(255, r + 70), min(255, g + 70), min(255, b + 70)))
        canvas.save(dest, format="PNG", optimize=True)
        return dest.relative_to(hub).as_posix() if dest.is_file() else None
    except OSError:
        return None


def _copy_unity_thumb_to_card(hub: Path, card_id: str, prefab_rel: str, unity_thumb_rel: str) -> str | None:
    src = hub / unity_thumb_rel
    if not src.is_file() or _is_bad_saved_thumb(src):
        return None
    dest = _thumb_output_path(hub, prefab_rel, card_id)
    dest.parent.mkdir(parents=True, exist_ok=True)
    if _copy_thumb_to_card(src, dest) and not _is_bad_saved_thumb(dest):
        return dest.relative_to(hub).as_posix()
    return None


def thumbnail_after_mesh_generation(
    hub: Path,
    card_id: str,
    card: dict[str, Any],
    prefab_rel: str,
    *,
    unity_thumb_rel: str | None = None,
) -> str | None:
    """Pick a card preview after mesh regen — never downgrade to Unity's package icon."""
    label = mesh_source_label(card)
    catalog = load_or_build_catalog(hub)

    if unity_thumb_rel:
        copied = _copy_unity_thumb_to_card(hub, card_id, prefab_rel, unity_thumb_rel)
        if copied and thumb_is_real_preview(hub, copied):
            return copied

    mesh_rel = write_generated_prop_mesh_thumb(hub, card_id, prefab_rel, catalog)
    if mesh_rel and thumb_is_real_preview(hub, mesh_rel):
        return mesh_rel

    orb_rel = write_orb_sphere_preview_thumb(hub, card_id, prefab_rel, card)
    if orb_rel and thumb_is_real_preview(hub, orb_rel):
        return orb_rel

    ok, _msg = export_prefab_thumbnail_sync(hub, prefab_rel, timeout_sec=90)
    if ok:
        mesh_rel = write_generated_prop_mesh_thumb(hub, card_id, prefab_rel, catalog)
        if mesh_rel and thumb_is_real_preview(hub, mesh_rel):
            return mesh_rel
        orb_rel = write_orb_sphere_preview_thumb(hub, card_id, prefab_rel, card)
        if orb_rel and thumb_is_real_preview(hub, orb_rel):
            return orb_rel

    rel = resolve_card_thumbnail(hub, catalog, card_id, prefab_rel, label)
    path = hub / rel if rel else None
    if (
        path is not None
        and path.is_file()
        and not _is_bad_saved_thumb(path)
        and not _is_placeholder_thumb(path)
    ):
        return rel

    old_rel = card.get("imageRel")
    if old_rel:
        old_path = hub / old_rel
        if old_path.is_file() and not _is_bad_saved_thumb(old_path):
            return old_rel
    return rel


def refresh_concept_card_thumbnails(hub: Path, doc: dict[str, Any], *, force: bool = False) -> bool:
    """Replace stale text-placeholder thumbs with kit textures / Unity previews."""
    cards = doc.get("conceptCards") or []
    if not cards:
        return False
    catalog = load_or_build_catalog(hub)
    changed = False
    for card in cards:
        cid = str(card.get("id") or "")
        if not cid.startswith("asset:"):
            continue
        prefab = card.get("prefabPath")
        old_rel = card.get("imageRel")
        old_path = hub / old_rel if old_rel else None
        canonical = (
            _thumb_output_path(hub, prefab, cid).relative_to(hub).as_posix() if prefab else old_rel
        )
        stale_path = bool(prefab and old_rel and old_rel != canonical)
        stale_image = _is_bad_saved_thumb(old_path or Path())
        if not force and not stale_path and not stale_image and old_path and old_path.is_file():
            continue
        if force and old_path and old_path.is_file():
            try:
                old_path.unlink()
            except OSError:
                pass
        new_rel = resolve_card_thumbnail(
            hub,
            catalog,
            cid,
            prefab,
            card.get("label") or cid,
        )
        new_path = hub / new_rel
        upgraded = new_path.is_file() and not _is_placeholder_thumb(new_path) and stale_image
        if new_rel != old_rel or upgraded or stale_path:
            card["imageRel"] = new_rel
            changed = True
    return changed


CARD_SCHEMA_VERSION = 2
INDIVIDUAL_CARD_KINDS = frozenset({"npc", "enemy", "collectible"})
COUNT_ADJUST_KINDS = frozenset({"prop", "enemy"})


def _marker_slot_key(m: dict[str, Any]) -> str:
    from planner_concept_render import (
        _coerce_island_slot,
        _coerce_marker_slot,
        _coerce_play_row_col,
    )

    kind = str(m.get("kind") or "prop").lower()
    label = str(m.get("label") or kind)
    slug = re.sub(r"[^a-zA-Z0-9]+", "-", label.lower()).strip("-")[:40] or kind
    zone = str(m.get("zone") or "play")
    if zone == "island":
        d = str(m.get("dir") or "x")
        slot = _coerce_island_slot(m.get("slot"))
        return f"{kind}:{zone}:{d}:s{slot}:{slug}"
    row, col = _coerce_play_row_col(m.get("row"), m.get("col"))
    slot = _coerce_marker_slot(m.get("slot"))
    return f"{kind}:{zone}:r{row}c{col}:s{slot}:{slug}"


def _ensure_character_markers(layout: dict[str, Any], brief: dict[str, Any]) -> None:
    """Add NPC/enemy markers implied by islands + checklist when the LLM brief omitted slots."""
    markers: list[dict[str, Any]] = list(layout.get("markers") or [])

    from planner_concept_render import _coerce_island_slot

    def _has_npc_island(d: str, slot: int) -> bool:
        return any(
            m.get("kind") == "npc"
            and str(m.get("zone")) == "island"
            and str(m.get("dir")) == d
            and _coerce_island_slot(m.get("slot")) == slot
            for m in markers
        )

    def _has_enemy_island(d: str, slot: int) -> bool:
        return any(
            m.get("kind") == "enemy"
            and str(m.get("zone")) == "island"
            and str(m.get("dir")) == d
            and _coerce_island_slot(m.get("slot")) == slot
            for m in markers
        )

    for isl in layout.get("islands") or []:
        d = str(isl.get("dir") or "")
        for slot in range(int(isl.get("npcs") or 0)):
            if not _has_npc_island(d, slot):
                markers.append(
                    {
                        "kind": "npc",
                        "zone": "island",
                        "dir": d,
                        "slot": slot,
                        "label": f"island NPC {d}",
                    }
                )
        for slot in range(int(isl.get("enemies") or 0)):
            if not _has_enemy_island(d, slot):
                markers.append(
                    {
                        "kind": "enemy",
                        "zone": "island",
                        "dir": d,
                        "slot": slot,
                        "label": f"enemy {d}{slot + 1}",
                    }
                )

    blob = json.dumps(brief, default=str).lower()
    npc_count = sum(1 for m in markers if m.get("kind") == "npc")
    wants_two_npcs = any(
        tok in blob
        for tok in (
            "ambient",
            "two npc",
            "2 npc",
            "second npc",
            "another npc",
            "one ambient",
        )
    )
    if wants_two_npcs and npc_count < 2:
        if not any("ambient" in str(m.get("label", "")).lower() for m in markers if m.get("kind") == "npc"):
            markers.append(
                {
                    "kind": "npc",
                    "zone": "play",
                    "row": 1,
                    "col": 2,
                    "label": "ambient NPC",
                }
            )

    enemy_count = sum(1 for m in markers if m.get("kind") == "enemy")
    wants_enemies = any(tok in blob for tok in ("enemy", "combat", "monster", "patrol"))
    if wants_enemies and enemy_count == 0:
        markers.append(
            {
                "kind": "enemy",
                "zone": "play",
                "row": 2,
                "col": 0,
                "label": "patrol enemy",
            }
        )

    layout["markers"] = markers


def _stamp_marker_slot_keys(markers: list[dict[str, Any]]) -> None:
    for m in markers:
        m["markerSlotKey"] = _marker_slot_key(m)


def _marker_groups(layout: dict[str, Any]) -> dict[str, dict[str, Any]]:
    groups: dict[str, dict[str, Any]] = {}
    for m in layout.get("markers") or []:
        kind = str(m.get("kind") or "prop")
        if kind.lower() in ("spawn", *INDIVIDUAL_CARD_KINDS):
            continue
        label = str(m.get("label") or kind)
        key = _category_key(kind, label)
        g = groups.setdefault(
            key,
            {"kind": kind, "label": label, "categoryKey": key, "count": 0},
        )
        g["count"] += 1
    return groups


def _apply_prev_card_state(entry: dict[str, Any], prev: dict[str, Any] | None) -> None:
    if not prev:
        return
    wants_ai = entry.get("needsAiMesh") or card_wants_ai_mesh(entry)
    if wants_ai:
        entry["needsAiMesh"] = True
        if prev.get("meshRegenCount") is not None:
            entry["meshRegenCount"] = prev["meshRegenCount"]
        prev_prefab = str(prev.get("prefabPath") or "")
        if _is_generated_prop_prefab(prev_prefab):
            entry["prefabPath"] = prev_prefab
            entry["imageRel"] = prev.get("imageRel") or entry.get("imageRel")
            entry["imageTs"] = prev.get("imageTs")
            entry["label"] = prev.get("label") or entry.get("label")
        if prev.get("status") == "accepted" and _is_generated_prop_prefab(prev_prefab):
            entry["status"] = "accepted"
        if prev.get("targetInstanceCount") is not None:
            entry["targetInstanceCount"] = int(prev["targetInstanceCount"])
        return
    if prev.get("status") == "accepted" and prev.get("prefabPath"):
        entry["status"] = "accepted"
        entry["prefabPath"] = prev["prefabPath"]
        entry["candidateIndex"] = prev.get("candidateIndex", entry.get("candidateIndex", 0))
        entry["label"] = prev.get("label") or entry.get("label")
    elif prev.get("candidateIndex") is not None and entry.get("status") != "accepted":
        entry["candidateIndex"] = prev["candidateIndex"]
        entry["prefabPath"] = prev.get("prefabPath") or entry.get("prefabPath")
        if entry.get("prefabPath"):
            entry["label"] = Path(entry["prefabPath"]).stem
    if prev.get("targetInstanceCount") is not None:
        entry["targetInstanceCount"] = int(prev["targetInstanceCount"])


def build_asset_cards(hub: Path, doc: dict[str, Any], existing: list[dict[str, Any]] | None = None) -> list[dict[str, Any]]:
    from planner_concept_render import derive_layout_plan

    cfg = doc.get("sessionConfig") or {}
    brief = doc.setdefault("brief", {})
    layout = brief.get("layoutPlan")
    if not isinstance(layout, dict) or not layout.get("markers"):
        layout = derive_layout_plan(brief, cfg)
    _ensure_character_markers(layout, brief)
    _stamp_marker_slot_keys(list(layout.get("markers") or []))
    brief["layoutPlan"] = layout

    catalog = load_or_build_catalog(hub)
    pools = catalog.get("pools") or {}
    existing_by_id = {c["id"]: c for c in (existing or []) if c.get("id")}

    cards: list[dict[str, Any]] = []

    # —— Props: grouped by scatter category, merged by prefab (instance counter) ——
    category_assign: dict[str, dict[str, Any]] = {}
    for key, meta in _marker_groups(layout).items():
        raw_pool = _pool_for_marker_kind(meta["kind"], meta["label"], pools)
        pool = _revise_pool(hub, catalog, raw_pool)
        prev = existing_by_id.get(f"asset:{key}")
        idx = int(prev.get("candidateIndex", 0)) if prev else 0
        prefab = _pick_from_pool(pool, idx)
        category_assign[key] = {
            **meta,
            "candidateIndex": idx,
            "prefabPath": prefab,
            "poolSize": len(pool),
            "rawPoolSize": len(raw_pool),
        }

    by_slot: dict[str, dict[str, Any]] = {}
    for key, meta in category_assign.items():
        prefab = meta.get("prefabPath")
        card_id = f"asset:{key}"
        entry = by_slot.setdefault(
            card_id,
            {
                "id": card_id,
                "cardType": meta["kind"],
                "label": Path(prefab).stem if prefab else meta["label"],
                "categoryKey": key,
                "prefabPath": prefab,
                "instanceCount": 0,
                "candidateIndex": meta["candidateIndex"],
                "poolSize": meta["poolSize"],
                "status": "pending",
                "allowCountAdjust": True,
                "cardSchemaVersion": CARD_SCHEMA_VERSION,
            },
        )
        entry["instanceCount"] += meta["count"]

    merged_by_prefab: dict[str, dict[str, Any]] = {}
    for card_id, entry in by_slot.items():
        prefab = entry.get("prefabPath") or f"__missing__:{card_id}"
        if prefab not in merged_by_prefab:
            merged = dict(entry)
            merged["id"] = card_id
            merged["mergedCategoryKeys"] = [entry.get("categoryKey")]
            merged_by_prefab[prefab] = merged
        else:
            merged = merged_by_prefab[prefab]
            merged["instanceCount"] += entry["instanceCount"]
            merged["mergedCategoryKeys"].append(entry.get("categoryKey"))

    for entry in sorted(merged_by_prefab.values(), key=lambda x: x["label"]):
        stable_id = entry["id"]
        prev = existing_by_id.get(stable_id)
        _apply_prev_card_state(entry, prev)
        if entry.get("cardType") not in COUNT_ADJUST_KINDS:
            entry["targetInstanceCount"] = 1
        elif prev and prev.get("targetInstanceCount") is not None:
            entry["targetInstanceCount"] = int(prev["targetInstanceCount"])
        else:
            entry["targetInstanceCount"] = int(entry.get("instanceCount") or 1)
        entry["imageRel"] = resolve_card_thumbnail(
            hub,
            catalog,
            stable_id,
            entry.get("prefabPath"),
            entry.get("label") or stable_id,
        )
        if str(entry.get("cardType") or "").lower() in ("prop", "collectible"):
            if not _is_generated_prop_prefab(str(entry.get("prefabPath") or "")):
                entry["needsAiMesh"] = True
                entry["meshSourceLabel"] = mesh_source_label(entry)
        cards.append(entry)

    # —— NPC / enemy / collectible: one approval card per layout marker slot ——
    for m in layout.get("markers") or []:
        kind = str(m.get("kind") or "").lower()
        if kind not in INDIVIDUAL_CARD_KINDS:
            continue
        slot_key = str(m.get("markerSlotKey") or _marker_slot_key(m))
        card_id = f"asset:{slot_key}"
        slot_label = str(m.get("label") or kind)
        prev = existing_by_id.get(card_id)
        wants_mesh = _slot_wants_ai_mesh(slot_label, kind)
        raw_pool = _pool_for_marker_kind(kind, slot_label, pools)
        pool = _revise_pool(hub, catalog, raw_pool)
        default_idx = int(hashlib.md5(slot_key.encode()).hexdigest()[:6], 16)
        idx = int(prev.get("candidateIndex", default_idx)) if prev else default_idx
        if wants_mesh:
            stub = {"slotLabel": slot_label, "categoryKey": _category_key(kind, slot_label), "id": card_id}
            stable = stable_generated_prefab_rel(stub)
            prefab = stable if (hub / stable).is_file() else None
        else:
            prefab = _pick_from_pool(pool, idx)
        display_label = slot_label
        if wants_mesh:
            parts = slot_label.split()
            display_label = parts[-1] if parts else slot_label
        allow_count = kind in COUNT_ADJUST_KINDS
        entry: dict[str, Any] = {
            "id": card_id,
            "cardType": kind,
            "label": display_label if wants_mesh else (Path(prefab).stem if prefab else slot_label),
            "slotLabel": slot_label,
            "needsAiMesh": wants_mesh,
            "markerSlotKey": slot_key,
            "categoryKey": _category_key(kind, slot_label),
            "prefabPath": prefab,
            "instanceCount": 1,
            "targetInstanceCount": 1,
            "candidateIndex": idx,
            "poolSize": len(pool),
            "rawPoolSize": len(raw_pool),
            "status": "pending",
            "allowCountAdjust": allow_count,
            "cardSchemaVersion": CARD_SCHEMA_VERSION,
        }
        _apply_prev_card_state(entry, prev)
        if kind == "npc":
            entry["targetInstanceCount"] = 1
        elif prev and prev.get("targetInstanceCount") is not None:
            entry["targetInstanceCount"] = int(prev["targetInstanceCount"])
        entry["imageRel"] = (
            resolve_card_thumbnail(
                hub,
                catalog,
                card_id,
                entry.get("prefabPath"),
                entry.get("label") or card_id,
            )
            if entry.get("prefabPath") and not wants_mesh
            else _placeholder_thumb(
                hub,
                card_id,
                None,
                entry.get("label") or slot_label,
                generated_prop=True,
            )
        )
        cards.append(entry)

    cards.append(_build_player_card(hub, catalog, existing_by_id))

    return sorted(cards, key=lambda c: (c.get("cardType") or "", c.get("slotLabel") or c.get("label") or ""))


def _build_player_card(
    hub: Path, catalog: dict[str, Any], existing_by_id: dict[str, Any]
) -> dict[str, Any]:
    """Playable hero — morph/sculpt like NPCs."""
    card_id = "asset:player:hero"
    prev = existing_by_id.get(card_id)
    pools = catalog.get("pools") or {}
    raw_pool = list(pools.get("player") or pools.get("npc") or [])
    pool = _revise_pool(hub, catalog, raw_pool)
    default_idx = 0
    idx = int(prev.get("candidateIndex", default_idx)) if prev else default_idx
    prefab = _pick_from_pool(pool, idx) if pool else None
    entry: dict[str, Any] = {
        "id": card_id,
        "cardType": "player",
        "label": Path(prefab).stem if prefab else "Player Hero",
        "slotLabel": "Playable hero",
        "categoryKey": "player:hero",
        "prefabPath": prefab,
        "instanceCount": 1,
        "targetInstanceCount": 1,
        "candidateIndex": idx,
        "poolSize": len(pool),
        "rawPoolSize": len(raw_pool),
        "status": "pending",
        "allowCountAdjust": False,
        "cardSchemaVersion": CARD_SCHEMA_VERSION,
    }
    _apply_prev_card_state(entry, prev)
    entry["targetInstanceCount"] = 1
    entry["imageRel"] = resolve_card_thumbnail(
        hub,
        catalog,
        card_id,
        entry.get("prefabPath"),
        entry.get("label") or card_id,
    )
    return entry


def _label_for_category_key(category_key: str, prefab_stem: str) -> str:
    if category_key == "prop:grass":
        return "grass scatter"
    if category_key == "prop:tree":
        return "tree scatter"
    if category_key == "prop:rock":
        return "rock scatter"
    if category_key == "prop:platform":
        return "plat jump"
    if category_key == "npc:default":
        return prefab_stem or "host NPC"
    if category_key == "enemy:default":
        return "patrol enemy"
    if category_key == "collectible:default":
        return "pickup"
    return prefab_stem or "prop scatter"


def _marker_matches_card(
    marker: dict[str, Any],
    card: dict[str, Any],
    category_keys: list[str | None],
) -> bool:
    cid = str(card.get("id") or "")
    if marker.get("plannerCardId") == cid:
        return True
    slot_key = card.get("markerSlotKey")
    if slot_key:
        mk = str(marker.get("markerSlotKey") or _marker_slot_key(marker))
        return mk == slot_key
    kind = str(marker.get("kind") or "")
    if kind != str(card.get("cardType") or ""):
        return False
    key = _category_key(kind, str(marker.get("label") or ""))
    if key in category_keys:
        return True
    prefab = (card.get("prefabPath") or "").lower()
    label = str(marker.get("label") or "").lower()
    if prefab and Path(prefab).stem.lower() in label:
        return True
    return False


def _next_marker_slot(kind: str, category_key: str, index: int, stem: str) -> tuple[str, int, int, str]:
    label = _label_for_category_key(category_key, stem)
    k = (kind or "prop").lower()
    if k == "npc":
        slots = [(1, 1), (0, 1), (2, 1), (1, 0), (1, 2)]
        row, col = slots[index % len(slots)]
        return "play", row, col, label
    if k == "enemy":
        slots = [(2, 2), (0, 0), (2, 0), (0, 2)]
        row, col = slots[index % len(slots)]
        return "play", row, col, label
    if k == "collectible":
        slots = [(1, 2), (2, 1), (0, 2), (1, 0)]
        row, col = slots[index % len(slots)]
        return "play", row, col, label
    slots = [
        (0, 0, "grass scatter"),
        (0, 2, "grass scatter"),
        (2, 0, "grass scatter"),
        (2, 2, "grass scatter"),
        (1, 0, "tree scatter"),
        (0, 1, "rock scatter"),
        (2, 1, "rock scatter"),
        (1, 2, "prop scatter"),
    ]
    row, col, lab = slots[index % len(slots)]
    return "play", row, col, lab


def apply_card_counts_to_brief(doc: dict[str, Any]) -> None:
    """Sync targetInstanceCount on cards into layoutPlan.markers."""
    from planner_concept_render import derive_layout_plan

    brief = doc.setdefault("brief", {})
    cfg = doc.get("sessionConfig") or {}
    layout = brief.get("layoutPlan")
    if not isinstance(layout, dict) or not layout.get("markers"):
        layout = derive_layout_plan(brief, cfg)
        brief["layoutPlan"] = layout
    markers: list[dict[str, Any]] = list(layout.get("markers") or [])

    for card in doc.get("conceptCards") or []:
        cid = str(card.get("id") or "")
        if not cid.startswith("asset:"):
            continue
        kind = str(card.get("cardType") or "prop")
        if kind == "npc":
            target = 1
        elif kind in COUNT_ADJUST_KINDS:
            target = int(card.get("targetInstanceCount", card.get("instanceCount", 1)))
            target = max(0, min(500, target))
        else:
            target = 1
        card["targetInstanceCount"] = target
        cat_keys = list(card.get("mergedCategoryKeys") or [])
        if card.get("categoryKey"):
            cat_keys.append(card.get("categoryKey"))
        cat_keys = list({k for k in cat_keys if k})

        match_idx = [i for i, m in enumerate(markers) if _marker_matches_card(m, card, cat_keys)]
        current = len(match_idx)
        if current > target:
            for i in sorted(match_idx[target:], reverse=True):
                markers.pop(i)
        elif current < target:
            stem = Path(card.get("prefabPath") or card.get("label") or "prop").stem
            cat_key = card.get("categoryKey") or ""
            base_label = card.get("slotLabel") or _label_for_category_key(cat_key, stem)
            for n in range(target - current):
                zone, row, col, lab = _next_marker_slot(kind, cat_key, current + n, stem)
                markers.append(
                    {
                        "kind": kind,
                        "zone": zone,
                        "row": row,
                        "col": col,
                        "label": f"{base_label} ({stem})" if kind == "enemy" else f"{lab} ({stem})",
                        "slot": n,
                        "plannerCardId": cid,
                    }
                )

    _stamp_marker_slot_keys(markers)
    layout["markers"] = markers
    brief["layoutPlan"] = layout


def build_layout_card(doc: dict[str, Any], existing: list[dict[str, Any]] | None = None) -> dict[str, Any]:
    prev = next((c for c in (existing or []) if c.get("id") == "layout"), None)
    detail = doc.get("conceptLayoutDetailRel")
    full = doc.get("conceptImageRel")
    layout_ts = doc.get("layoutImageTs") or doc.get("conceptImageTs")
    return {
        "id": "layout",
        "cardType": "layout",
        "label": "Layout + legend",
        "imageRel": detail or full,
        "imageRelFull": full,
        "status": (prev or {}).get("status", "pending"),
        "lastRevisionNote": (prev or {}).get("lastRevisionNote"),
        "reviseCount": (prev or {}).get("reviseCount"),
        "imageTs": (prev or {}).get("imageTs") or layout_ts,
        "instanceCount": 1,
        "cardSchemaVersion": CARD_SCHEMA_VERSION,
    }


def build_density_card(doc: dict[str, Any], existing: list[dict[str, Any]] | None = None) -> dict[str, Any]:
    prev = next((c for c in (existing or []) if c.get("id") == "density"), None)
    detail = doc.get("conceptDensityDetailRel")
    full = doc.get("conceptDensityImageRel")
    density_ts = doc.get("densityImageTs") or doc.get("conceptImageTs")
    return {
        "id": "density",
        "cardType": "density",
        "label": "Trail & prop density",
        "imageRel": detail or full,
        "imageRelFull": full,
        "status": (prev or {}).get("status", "pending"),
        "lastRevisionNote": (prev or {}).get("lastRevisionNote"),
        "reviseCount": (prev or {}).get("reviseCount"),
        "imageTs": (prev or {}).get("imageTs") or density_ts,
        "instanceCount": 1,
        "cardSchemaVersion": CARD_SCHEMA_VERSION,
    }


def concept_cards_schema_stale(doc: dict[str, Any], cards: list[dict[str, Any]] | None = None) -> bool:
    """True when asset cards need a v2 rebuild — layout/density omit cardSchemaVersion by design."""
    if int(doc.get("conceptCardsSchemaVersion") or 0) >= CARD_SCHEMA_VERSION:
        return False
    items = cards if cards is not None else (doc.get("conceptCards") or [])
    if not items:
        return True
    for c in items:
        cid = str(c.get("id") or "")
        if cid in ("layout", "density"):
            continue
        if int(c.get("cardSchemaVersion") or 1) < CARD_SCHEMA_VERSION:
            return True
    return False


def sync_concept_cards(hub: Path, doc: dict[str, Any]) -> list[dict[str, Any]]:
    existing = doc.get("conceptCards") or []
    layout = build_layout_card(doc, existing)
    density = build_density_card(doc, existing)
    assets = build_asset_cards(hub, doc, existing)
    cards = [layout, density, *assets]
    doc["conceptCards"] = cards
    doc["conceptCardsSchemaVersion"] = CARD_SCHEMA_VERSION
    return cards


def _raw_pool_for_card(card: dict[str, Any], pools: dict[str, list[str]]) -> list[str]:
    kind = card.get("cardType") or "prop"
    label = card.get("label") or ""
    if card.get("prefabPath"):
        bucket = _classify_prefab_path(card["prefabPath"])
        hit = pools.get(bucket) or []
        if hit:
            return hit
    cat_keys = list(card.get("mergedCategoryKeys") or [])
    if card.get("categoryKey"):
        cat_keys.append(card["categoryKey"])
    for key in cat_keys:
        if not str(key).startswith("prop:"):
            continue
        sub = str(key).split(":", 1)[-1]
        bucket = {
            "tree": "prop_tree",
            "grass": "prop_grass",
            "rock": "prop_rock",
        }.get(sub, "prop_bush")
        hit = pools.get(bucket) or []
        if hit:
            return hit
    return _pool_for_marker_kind(kind, label, pools)


def _pool_for_card(hub: Path, catalog: dict[str, Any], card: dict[str, Any], pools: dict[str, list[str]]) -> list[str]:
    raw = _raw_pool_for_card(card, pools)
    if not raw and card.get("prefabPath"):
        bucket = _classify_prefab_path(card["prefabPath"])
        raw = pools.get(bucket) or []
    return _revise_pool(hub, catalog, raw)


def revise_asset_card(hub: Path, card: dict[str, Any]) -> tuple[dict[str, Any], bool]:
    """Returns (card, needs_mesh_regen) when no other kit prefab is available."""
    if card_wants_ai_mesh(card):
        card["needsAiMesh"] = True
        card["status"] = "pending"
        card["label"] = mesh_source_label(card)
        return card, True

    catalog = load_or_build_catalog(hub)
    pools = catalog.get("pools") or {}
    label = card.get("label") or ""
    raw_pool = _raw_pool_for_card(card, pools)
    if not raw_pool and card.get("prefabPath"):
        bucket = _classify_prefab_path(card["prefabPath"])
        raw_pool = pools.get(bucket) or []

    current = card.get("prefabPath")
    next_idx = int(card.get("candidateIndex", 0))
    prefab, next_idx, path_pool = _pick_revise_prefab(raw_pool, current, next_idx)
    display_pool = _revise_pool(hub, catalog, raw_pool)

    card["candidateIndex"] = next_idx
    card["poolSize"] = max(len(path_pool), len(display_pool), 1)
    card["rawPoolSize"] = len(raw_pool)
    card["prefabPath"] = prefab
    card["label"] = Path(prefab).stem if prefab else label
    card["status"] = "pending"
    card["imageRel"] = resolve_card_thumbnail(
        hub, catalog, card["id"], prefab, card.get("label") or card["id"]
    )
    same_path = prefab == current
    same_preview = (
        bool(current)
        and bool(prefab)
        and _preview_identity(hub, catalog, prefab) == _preview_identity(hub, catalog, current)
    )
    needs_mesh = card_can_generate_mesh(hub, card) and (same_path or same_preview)
    return card, needs_mesh


def ensure_card_image_timestamps(doc: dict[str, Any]) -> bool:
    """Give every card its own imageTs so global conceptImageTs bumps never invalidate all URLs."""
    from datetime import datetime, timezone

    try:
        global_ts = int(doc.get("conceptImageTs") or 0)
    except (TypeError, ValueError):
        global_ts = 0
    if global_ts <= 0:
        global_ts = int(datetime.now(timezone.utc).timestamp())
    changed = False
    for card in doc.get("conceptCards") or []:
        if not card.get("imageRel"):
            continue
        if card.get("imageTs"):
            continue
        card["imageTs"] = global_ts
        changed = True
    return changed


def normalize_generated_prop_cards(hub: Path, doc: dict[str, Any]) -> bool:
    """Point prop cards at stable Gen_* prefabs and refresh previews after regen drift."""
    from datetime import datetime, timezone

    catalog = load_or_build_catalog(hub)
    changed = False
    for card in doc.get("conceptCards") or []:
        if card.get("cardType") != "prop":
            continue
        source = mesh_source_label(card)
        prefab = str(card.get("prefabPath") or "")
        if "Generated/PlannerProps" in prefab.replace("\\", "/") or card.get("meshSourceLabel"):
            stable = stable_generated_prefab_rel(card)
            if (hub / stable).is_file():
                if prefab != stable:
                    card["prefabPath"] = stable
                    changed = True
        if card.get("label") != source:
            card["label"] = source
            changed = True
        rel = card.get("imageRel")
        path = hub / rel if rel else None
        needs_thumb = (
            path is None
            or not path.is_file()
            or _is_bad_saved_thumb(path)
            or _is_placeholder_thumb(path)
        )
        if not needs_thumb:
            continue
        new_rel = resolve_card_thumbnail(
            hub,
            catalog,
            str(card.get("id") or ""),
            card.get("prefabPath"),
            source,
        )
        new_path = hub / new_rel if new_rel else None
        if (
            new_path is not None
            and new_path.is_file()
            and not _is_bad_saved_thumb(new_path)
            and not _is_placeholder_thumb(new_path)
        ):
            card["imageRel"] = new_rel
            card["imageTs"] = int(datetime.now(timezone.utc).timestamp())
            changed = True
    return changed


def refresh_bad_card_thumbnails(hub: Path, doc: dict[str, Any]) -> bool:
    """Re-resolve cards stuck on Unity's 128px package icon or other bad thumbs."""
    from datetime import datetime, timezone

    catalog = load_or_build_catalog(hub)
    changed = False
    for card in doc.get("conceptCards") or []:
        cid = str(card.get("id") or "")
        if not cid.startswith("asset:"):
            continue
        rel = card.get("imageRel")
        path = hub / rel if rel else None
        if path is not None and path.is_file() and not _is_bad_saved_thumb(path):
            continue
        new_rel = resolve_card_thumbnail(
            hub,
            catalog,
            cid,
            card.get("prefabPath"),
            card.get("label") or cid,
        )
        new_path = hub / new_rel if new_rel else None
        if (
            new_path is not None
            and new_path.is_file()
            and not _is_bad_saved_thumb(new_path)
            and new_rel != rel
        ):
            card["imageRel"] = new_rel
            card["imageTs"] = int(datetime.now(timezone.utc).timestamp())
            changed = True
    return changed


def repair_missing_card_thumbnails(hub: Path, doc: dict[str, Any]) -> bool:
    """Regenerate only cards whose imageRel file is missing (no mass unlink)."""
    from datetime import datetime, timezone

    catalog = load_or_build_catalog(hub)
    changed = ensure_card_image_timestamps(doc)
    for card in doc.get("conceptCards") or []:
        rel = card.get("imageRel")
        if not rel:
            continue
        if (hub / rel).is_file():
            continue
        cid = str(card.get("id") or "")
        if cid in ("layout", "density"):
            continue
        if not cid.startswith("asset:"):
            continue
        new_rel = resolve_card_thumbnail(
            hub,
            catalog,
            cid,
            card.get("prefabPath"),
            card.get("label") or cid,
        )
        if not new_rel or not (hub / new_rel).is_file():
            new_rel = _placeholder_thumb(
                hub,
                cid,
                card.get("prefabPath"),
                card.get("label") or cid,
            )
        if new_rel and (hub / new_rel).is_file():
            card["imageRel"] = new_rel
            card["imageTs"] = int(datetime.now(timezone.utc).timestamp())
            changed = True
    return changed


def refresh_ai_mesh_card_previews(hub: Path, doc: dict[str, Any]) -> bool:
    """Strip wrong kit thumbnails from orb/AI slots — placeholder until user clicks Generate."""
    from datetime import datetime, timezone

    changed = False
    for card in doc.get("conceptCards") or []:
        cid = str(card.get("id") or "")
        if not cid.startswith("asset:"):
            continue
        if not card_wants_ai_mesh(card):
            continue
        card["needsAiMesh"] = True
        slot = str(card.get("slotLabel") or "")
        if slot:
            parts = slot.split()
            short = parts[-1] if parts else slot
            if card.get("label") != short:
                card["label"] = short
                changed = True
        prefab = str(card.get("prefabPath") or "")
        if prefab and not _is_generated_prop_prefab(prefab):
            card["prefabPath"] = None
            changed = True
        if _is_generated_prop_prefab(str(card.get("prefabPath") or "")):
            continue
        rel = card.get("imageRel")
        path = hub / rel if rel else None
        if path is not None and path.is_file() and not _is_placeholder_thumb(path):
            card["prefabPath"] = None
            new_rel = _placeholder_thumb(
                hub,
                cid,
                None,
                card.get("label") or slot or cid,
                generated_prop=True,
            )
            card["imageRel"] = new_rel
            card["imageTs"] = int(datetime.now(timezone.utc).timestamp())
            changed = True
        elif not rel or not (path and path.is_file()):
            new_rel = _placeholder_thumb(
                hub,
                cid,
                None,
                card.get("label") or slot or cid,
                generated_prop=True,
            )
            if rel != new_rel:
                card["imageRel"] = new_rel
                card["imageTs"] = int(datetime.now(timezone.utc).timestamp())
                changed = True
    return changed


def cards_for_public(hub: Path, doc: dict[str, Any]) -> list[dict[str, Any]]:
    import urllib.parse

    hub_q = urllib.parse.quote(str(hub), safe="")
    global_ts = str(doc.get("conceptImageTs") or doc.get("updatedUtc") or "")
    out: list[dict[str, Any]] = []
    for card in doc.get("conceptCards") or []:
        c = dict(card)
        rel = c.get("imageRel")
        if rel:
            rev = c.get("candidateIndex", 0)
            card_ts = str(c.get("imageTs") or global_ts)
            ts_q = urllib.parse.quote(card_ts)
            c["imageUrl"] = (
                f"/api/planner/asset?hub={hub_q}&rel={urllib.parse.quote(rel)}"
                f"&t={ts_q}&r={rev}"
            )
        c["isPlaceholderPreview"] = card_image_is_placeholder(hub, c)
        c["meshPreviewReady"] = bool(
            c.get("prefabPath") and thumb_is_real_preview(hub, c.get("imageRel"))
        )
        c["needsAiMesh"] = card_wants_ai_mesh(c)
        c["canGenerateMesh"] = card_can_generate_mesh(hub, c)
        c["canSculptCharacter"] = card_can_sculpt_character(hub, c)
        out.append(c)
    return out


def approved_cards_manifest(cards: list[dict[str, Any]]) -> list[dict[str, Any]]:
    manifest: list[dict[str, Any]] = []
    for c in cards or []:
        if c.get("status") != "accepted":
            continue
        manifest.append(
            {
                "id": c.get("id"),
                "cardType": c.get("cardType"),
                "label": c.get("label"),
                "imageRel": c.get("imageRel"),
                "prefabPath": c.get("prefabPath"),
                "instanceCount": c.get("instanceCount", 1),
                "targetInstanceCount": c.get("targetInstanceCount", c.get("instanceCount", 1)),
                "categoryKey": c.get("categoryKey"),
                "markerSlotKey": c.get("markerSlotKey"),
                "slotLabel": c.get("slotLabel"),
                "allowCountAdjust": c.get("allowCountAdjust", False),
            }
        )
    return manifest


_KIT_EXPORT_LOCK = threading.Lock()
_KIT_EXPORT_HUBS: set[str] = set()


def card_image_is_placeholder(hub: Path, card: dict[str, Any]) -> bool:
    rel = card.get("imageRel")
    if not rel:
        return True
    return _is_placeholder_thumb(hub / rel)


def props_need_unity_thumbs(hub: Path, doc: dict[str, Any]) -> bool:
    """True when prop cards still show labeled placeholders (need AssetPreview export)."""
    for card in doc.get("conceptCards") or []:
        if card.get("cardType") != "prop":
            continue
        if not card.get("prefabPath"):
            continue
        if card_image_is_placeholder(hub, card):
            return True
    return False


def generated_props_missing(hub: Path) -> bool:
    root = hub / GENERATED_PROPS_ROOT
    if not root.is_dir():
        return True
    return not any(root.rglob("*.prefab"))


CARD_MESH_REQUEST_REL = Path(
    "Assets/EnvironmentKit/Generated/planner-concept-card-mesh.request.json"
)
CARD_MESH_DONE_REL = Path(
    "Assets/EnvironmentKit/Generated/planner-concept-card-mesh.done.json"
)
CARD_SCULPT_REQUEST_REL = Path(
    "Assets/EnvironmentKit/Generated/planner-concept-card-sculpt.request.json"
)
CARD_SCULPT_DONE_REL = Path(
    "Assets/EnvironmentKit/Generated/planner-concept-card-sculpt.done.json"
)


def normalize_sculpt_hint(raw: str | None) -> str:
    words = re.sub(r"[^\w\s-]", "", str(raw or "")).split()
    return " ".join(words[:2]).strip().lower()


def _is_generated_character_prefab(prefab_rel: str | None) -> bool:
    if not prefab_rel:
        return False
    return "plannercharacters" in prefab_rel.replace("\\", "/").lower()


def character_sculpt_source_prefab(card: dict[str, Any]) -> str | None:
    src = card.get("sourcePrefabPath")
    if src:
        return str(src)
    prefab = str(card.get("prefabPath") or "")
    if prefab and not _is_generated_character_prefab(prefab):
        return prefab
    return prefab or None


def card_can_sculpt_character(hub: Path, card: dict[str, Any]) -> bool:
    """NPC, enemy, and player cards support Cursor morph/sculpt."""
    if str(card.get("cardType") or "").lower() not in CHARACTER_SCULPT_KINDS:
        return False
    if card.get("status") == "accepted":
        return False
    return bool(character_sculpt_source_prefab(card))


def card_can_generate_mesh(hub: Path, card: dict[str, Any]) -> bool:
    """Prop/collectible cards can generate an original Hub-owned mesh."""
    if str(card.get("cardType") or "").lower() not in ("prop", "collectible"):
        return False
    if card.get("status") == "accepted":
        return False
    return True


def generate_mesh_for_concept_card(
    hub: Path,
    card: dict[str, Any],
    *,
    ai_spec: dict[str, Any] | None = None,
    on_progress: Callable[[str, int, str], None] | None = None,
) -> tuple[bool, str, str | None, str | None]:
    """Ask Unity headless batchmode (preferred) to build one prop prefab + preview."""
    import time

    card_id = str(card.get("id") or "")
    label = mesh_source_label(card)
    category = str(card.get("categoryKey") or "")
    regen_index = int(card.get("meshRegenCount") or 0) + 1
    seed = int(hashlib.md5(f"{card_id}:{regen_index}".encode()).hexdigest()[:8], 16)

    request = hub / CARD_MESH_REQUEST_REL
    done = hub / CARD_MESH_DONE_REL
    request.parent.mkdir(parents=True, exist_ok=True)
    if done.is_file():
        try:
            done.unlink()
        except OSError:
            pass

    payload: dict[str, Any] = {
        "cardId": card_id,
        "categoryKey": category,
        "label": label,
        "seed": seed,
        "regenIndex": regen_index,
    }
    if ai_spec:
        payload["aiSpec"] = ai_spec
    request.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    if on_progress:
        on_progress("unity_mesh", 48, "Unity headless — building mesh…")

    mesh_method = "EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.GenerateConceptCardMesh"
    mesh_log = "Assets/EnvironmentKit/Generated/planner-concept-card-mesh.log"

    if _prefer_unity_headless() or not _unity_editor_has_hub_open(hub):
        _run_unity_batchmethod(hub, mesh_method, log_rel=mesh_log, timeout_sec=240)

    deadline = time.time() + 240
    started = time.time()
    editor_requeued = False
    while time.time() < deadline:
        if done.is_file():
            break
        if (
            not editor_requeued
            and _unity_editor_has_hub_open(hub)
            and not request.is_file()
            and not done.is_file()
        ):
            request.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
            editor_requeued = True
        if on_progress:
            elapsed = time.time() - started
            pct = 48 + int(min(40, elapsed / 240 * 40))
            on_progress("unity_mesh", pct, "Unity headless — building mesh…")
        time.sleep(0.4)

    if not done.is_file():
        return (
            False,
            "Unity headless did not finish — set UNITY_PATH, close other Unity instances on Hub, or retry.",
            None,
            None,
        )

    try:
        result = json.loads(done.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return False, "Unity mesh generation returned invalid JSON.", None, None

    if not result.get("ok"):
        return False, str(result.get("message") or "Mesh generation failed."), None, None

    prefab = str(result.get("prefabPath") or "")
    if not prefab or not (hub / prefab).is_file():
        return False, "Unity reported success but prefab file is missing.", None, None

    thumb_rel = str(result.get("thumbRel") or "") or None
    msg = str(result.get("message") or "Mesh generated.")
    if ai_spec:
        notes = str(ai_spec.get("styleNotes") or "").strip()
        if notes:
            msg = f"AI mesh — {notes}"
        else:
            msg = f"AI mesh for {label}."
    return True, msg, prefab, thumb_rel


def generate_sculpt_for_concept_card(
    hub: Path,
    card: dict[str, Any],
    *,
    sculpt_hint: str,
    ai_spec: dict[str, Any] | None = None,
    on_progress: Callable[[str, int, str], None] | None = None,
) -> tuple[bool, str, str | None]:
    """Ask Unity to morph a kit character into a Hub-owned sculpted variant."""
    import time

    card_id = str(card.get("id") or "")
    label = mesh_source_label(card)
    card_type = str(card.get("cardType") or "npc").lower()
    source_prefab = character_sculpt_source_prefab(card)
    if not source_prefab:
        return False, "No source character prefab for sculpt.", None

    regen_index = int(card.get("sculptRegenCount") or 0) + 1
    seed = int(hashlib.md5(f"{card_id}:{sculpt_hint}:{regen_index}".encode()).hexdigest()[:8], 16)

    request = hub / CARD_SCULPT_REQUEST_REL
    done = hub / CARD_SCULPT_DONE_REL
    request.parent.mkdir(parents=True, exist_ok=True)
    if done.is_file():
        try:
            done.unlink()
        except OSError:
            pass

    payload: dict[str, Any] = {
        "cardId": card_id,
        "sourcePrefabPath": source_prefab,
        "label": label,
        "sculptHint": sculpt_hint,
        "cardType": card_type,
        "seed": seed,
        "regenIndex": regen_index,
    }
    if ai_spec:
        payload["aiSpec"] = ai_spec
    request.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    if on_progress:
        on_progress("unity_sculpt", 48, "Unity morphing character…")

    if not _unity_editor_has_hub_open(hub):
        unity = _resolve_unity_exe()
        if unity is None:
            return (
                False,
                "UNITY_PATH not set — open Hub in Unity Editor, then Resculpt again.",
                None,
            )
        log_path = hub / "Assets/EnvironmentKit/Generated/planner-concept-card-sculpt.log"
        cmd = [
            str(unity),
            "-batchmode",
            "-nographics",
            "-projectPath",
            str(hub),
            "-executeMethod",
            "EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.GenerateConceptCardSculpt",
            "-logFile",
            str(log_path),
            "-quit",
        ]
        try:
            subprocess.run(cmd, capture_output=True, text=True, timeout=180)
        except subprocess.TimeoutExpired:
            return False, "Unity character sculpt timed out.", None

    deadline = time.time() + 180
    started = time.time()
    while time.time() < deadline:
        if done.is_file():
            break
        if on_progress:
            elapsed = time.time() - started
            pct = 48 + int(min(40, elapsed / 180 * 40))
            on_progress("unity_sculpt", pct, "Unity applying morph + preview…")
        time.sleep(0.4)

    if not done.is_file():
        return (
            False,
            "Unity did not sculpt the character — keep Hub open in Unity Editor and retry.",
            None,
        )

    try:
        result = json.loads(done.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return False, "Unity sculpt returned invalid JSON.", None

    if not result.get("ok"):
        return False, str(result.get("message") or "Character sculpt failed."), None

    prefab = str(result.get("prefabPath") or "")
    if not prefab or not (hub / prefab).is_file():
        return False, "Unity reported success but sculpted prefab is missing.", None

    msg = str(result.get("message") or "Character sculpted.")
    if ai_spec:
        notes = str(ai_spec.get("styleNotes") or "").strip()
        if notes:
            msg = f"Cursor sculpt — {notes}"
    return True, msg, prefab


def kit_catalog_thumb_count(hub: Path) -> int:
    catalog = load_or_build_catalog(hub)
    thumbs = catalog.get("prefabThumbs") or {}
    if thumbs:
        return len(thumbs)
    thumbs_dir = hub / CATALOG_THUMBS_REL
    if thumbs_dir.is_dir():
        return len(list(thumbs_dir.glob("*.png")))
    return 0


KIT_CATALOG_REQUEST_REL = Path(
    "Assets/EnvironmentKit/Generated/planner-kit-catalog-export.request"
)
KIT_CATALOG_DONE_REL = Path(
    "Assets/EnvironmentKit/Generated/planner-kit-catalog-export.done.json"
)
CARD_THUMB_REQUEST_REL = Path(
    "Assets/EnvironmentKit/Generated/planner-concept-card-thumb.request.json"
)
CARD_THUMB_DONE_REL = Path(
    "Assets/EnvironmentKit/Generated/planner-concept-card-thumb.done.json"
)


def _unity_editor_has_hub_open(hub: Path) -> bool:
    hub_s = str(hub.resolve())
    try:
        out = subprocess.run(
            ["pgrep", "-fl", "MacOS/Unity"],
            capture_output=True,
            text=True,
            timeout=5,
        )
    except (OSError, subprocess.TimeoutExpired):
        return False
    for line in out.stdout.splitlines():
        if "-projectpath" in line.lower() and hub_s.lower() in line.lower():
            return True
    return False


def export_prefab_thumbnail_sync(
    hub: Path,
    prefab_rel: str,
    *,
    timeout_sec: int = 60,
    on_progress: Callable[[str, int, str], None] | None = None,
) -> tuple[bool, str]:
    """Ask Unity to render one prefab preview — may take up to a minute for AssetPreview."""
    import time

    if not prefab_rel:
        return False, "No prefab path"
    request = hub / CARD_THUMB_REQUEST_REL
    done = hub / CARD_THUMB_DONE_REL
    request.parent.mkdir(parents=True, exist_ok=True)
    if done.is_file():
        try:
            done.unlink()
        except OSError:
            pass
    request.write_text(
        json.dumps({"prefabPath": prefab_rel}, indent=2) + "\n",
        encoding="utf-8",
    )

    thumb_method = "EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.ExportConceptCardThumbnail"
    thumb_log = "Assets/EnvironmentKit/Generated/planner-concept-card-thumb.log"
    if _prefer_unity_headless() or not _unity_editor_has_hub_open(hub):
        _run_unity_batchmethod(
            hub, thumb_method, log_rel=thumb_log, timeout_sec=timeout_sec + 60
        )
    elif not done.is_file():
        _run_unity_batchmethod(
            hub, thumb_method, log_rel=thumb_log, timeout_sec=timeout_sec + 60
        )

    deadline = time.time() + timeout_sec
    started = time.time()
    while time.time() < deadline:
        if done.is_file():
            try:
                payload = json.loads(done.read_text(encoding="utf-8"))
            except json.JSONDecodeError:
                payload = {}
            ok = bool(payload.get("ok"))
            msg = str(payload.get("message") or "")
            thumb_rel = str(payload.get("thumbRel") or "")
            if ok and thumb_rel:
                return True, msg or "Preview exported"
            return False, msg or "Unity did not export a preview"
        if on_progress:
            elapsed = time.time() - started
            pct = 20 + int(min(65, elapsed / max(1, timeout_sec) * 65))
            on_progress("unity_preview", pct, "Unity rendering 3D preview…")
        time.sleep(0.4)
    return False, "Timed out waiting for Unity headless preview export"


def ensure_card_thumbnail(
    hub: Path,
    card: dict[str, Any],
    *,
    wait_unity: bool = True,
    on_progress: Callable[[str, int, str], None] | None = None,
) -> str | None:
    """Resolve or export a real 3D preview for one concept card."""
    from datetime import datetime, timezone

    cid = str(card.get("id") or "")
    prefab = str(card.get("prefabPath") or "")
    if not cid.startswith("asset:") or not prefab:
        return card.get("imageRel")

    rel = card.get("imageRel")
    path = hub / rel if rel else None
    if (
        path is not None
        and path.is_file()
        and not _is_bad_saved_thumb(path)
        and not _is_placeholder_thumb(path)
    ):
        return rel

    catalog = load_or_build_catalog(hub)
    cat_file = _catalog_thumb_file(hub, prefab)
    if wait_unity and (not cat_file.is_file() or _is_bad_saved_thumb(cat_file)):
        export_prefab_thumbnail_sync(
            hub, prefab, timeout_sec=60, on_progress=on_progress
        )
        catalog = load_or_build_catalog(hub)
        _ingest_unity_thumbs(hub, catalog)
    if on_progress:
        on_progress("copy_thumb", 88, "Updating card preview…")

    new_rel = resolve_card_thumbnail(
        hub,
        catalog,
        cid,
        prefab,
        card.get("label") or cid,
    )
    new_path = hub / new_rel if new_rel else None
    if (
        new_path is not None
        and new_path.is_file()
        and not _is_bad_saved_thumb(new_path)
        and not _is_placeholder_thumb(new_path)
    ):
        card["imageRel"] = new_rel
        card["imageTs"] = int(datetime.now(timezone.utc).timestamp())
        return new_rel
    if new_rel:
        card["imageRel"] = new_rel
        card["imageTs"] = int(datetime.now(timezone.utc).timestamp())
    return new_rel


def thumb_is_real_preview(hub: Path, rel: str | None) -> bool:
    if not rel:
        return False
    path = hub / rel
    return (
        path.is_file()
        and path.stat().st_size > 800
        and not _is_placeholder_thumb(path)
        and not _is_bad_saved_thumb(path)
    )


def cards_missing_previews(hub: Path, doc: dict[str, Any]) -> list[dict[str, Any]]:
    """Cards that still need a Unity 3D preview export."""
    missing: list[dict[str, Any]] = []
    for card in doc.get("conceptCards") or []:
        cid = str(card.get("id") or "")
        if not cid.startswith("asset:"):
            continue
        if not card.get("prefabPath"):
            continue
        if card_image_is_placeholder(hub, card):
            missing.append(card)
            continue
        rel = card.get("imageRel")
        if not rel or not (hub / rel).is_file() or _is_bad_saved_thumb(hub / rel):
            missing.append(card)
    return missing


def _request_kit_catalog_from_open_editor(hub: Path, *, timeout_sec: int = 600) -> tuple[bool, str]:
    """Ask an already-running Unity Editor to export kit thumbnails via request file."""
    import time

    request = hub / KIT_CATALOG_REQUEST_REL
    done = hub / KIT_CATALOG_DONE_REL
    request.parent.mkdir(parents=True, exist_ok=True)
    if done.is_file():
        try:
            done.unlink()
        except OSError:
            pass
    request.write_text(_utc_now_iso(), encoding="utf-8")

    deadline = time.time() + timeout_sec
    while time.time() < deadline:
        if done.is_file():
            try:
                payload = json.loads(done.read_text(encoding="utf-8"))
            except json.JSONDecodeError:
                payload = {}
            ok = bool(payload.get("ok"))
            msg = str(payload.get("message") or "")
            count = int(payload.get("thumbCount") or kit_catalog_thumb_count(hub))
            if ok and count > 0:
                return True, msg or f"Exported {count} prefab thumbnail(s)"
            return False, msg or f"Unity export finished but thumbs={count}"
        time.sleep(0.5)
    return False, "Timed out waiting for Unity Editor to export kit catalog (is Hub project focused?)"


def _utc_now_iso() -> str:
    from datetime import datetime, timezone

    return datetime.now(timezone.utc).isoformat()


def _prefer_unity_headless() -> bool:
    """Default: concept-card mesh/preview uses Unity -batchmode (works with editor in background)."""
    return os.environ.get("PLANNER_UNITY_HEADLESS", "1").strip().lower() not in (
        "0",
        "false",
        "no",
    )


def _run_unity_batchmethod(
    hub: Path,
    execute_method: str,
    *,
    log_rel: str,
    timeout_sec: int = 180,
) -> tuple[bool, str]:
    unity = _resolve_unity_exe()
    if unity is None:
        return False, "UNITY_PATH not set"
    log_path = hub / log_rel
    log_path.parent.mkdir(parents=True, exist_ok=True)
    cmd = [
        str(unity),
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(hub),
        "-executeMethod",
        execute_method,
        "-logFile",
        str(log_path),
        "-quit",
    ]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout_sec)
    except subprocess.TimeoutExpired:
        return False, "Unity batchmode timed out"
    if proc.returncode != 0:
        tail = (proc.stderr or proc.stdout or "")[-400:]
        return False, tail or f"Unity exited {proc.returncode}"
    return True, ""


def _resolve_unity_exe() -> Path | None:
    candidates = [
        os.environ.get("UNITY_PATH", "").strip(),
        "/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity",
    ]
    for raw in candidates:
        if not raw:
            continue
        p = Path(raw).expanduser()
        if p.is_file():
            return p
        if p.suffix == ".app":
            exe = p / "Contents/MacOS/Unity"
            if exe.is_file():
                return exe
    return None


def export_kit_catalog_sync(hub: Path, *, timeout_sec: int = 900) -> tuple[bool, str]:
    """Export AssetPreview thumbnails — via open Unity Editor or batchmode."""
    if _unity_editor_has_hub_open(hub):
        return _request_kit_catalog_from_open_editor(hub, timeout_sec=min(timeout_sec, 600))

    unity = _resolve_unity_exe()
    if unity is None:
        return False, "UNITY_PATH not set and default Unity editor not found"

    log_path = hub / "Assets/EnvironmentKit/Generated/planner-kit-catalog-export.log"
    log_path.parent.mkdir(parents=True, exist_ok=True)
    cmd = [
        str(unity),
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(hub),
        "-executeMethod",
        "EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.ExportPlannerKitCatalog",
        "-logFile",
        str(log_path),
        "-quit",
    ]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout_sec)
    except subprocess.TimeoutExpired:
        return False, f"Unity catalog export timed out after {timeout_sec}s"
    except OSError as ex:
        return False, str(ex)

    count = kit_catalog_thumb_count(hub)
    if proc.returncode != 0 or count == 0:
        tail = ""
        if log_path.is_file():
            lines = log_path.read_text(encoding="utf-8", errors="ignore").splitlines()
            tail = "\n".join(lines[-12:])
        return False, (
            f"Unity export exit {proc.returncode}; thumbs={count}. "
            f"See {log_path.relative_to(hub)}. {tail}"
        )
    return True, f"Exported {count} prefab thumbnail(s)"


def refresh_prop_cards_from_catalog(hub: Path, doc: dict[str, Any], *, force: bool = False) -> bool:
    """Re-resolve prop card imageRel from Unity-exported prefabThumbs."""
    if refresh_concept_card_thumbnails(hub, doc, force=force):
        return True
    changed = False
    catalog = load_or_build_catalog(hub)
    for card in doc.get("conceptCards") or []:
        if card.get("cardType") != "prop" or not card.get("prefabPath"):
            continue
        old = card.get("imageRel")
        new = resolve_card_thumbnail(
            hub,
            catalog,
            str(card.get("id") or ""),
            card.get("prefabPath"),
            card.get("label") or card.get("id") or "",
        )
        if new != old:
            card["imageRel"] = new
            changed = True
    return changed


def maybe_background_export_kit_catalog(hub: Path, doc: dict[str, Any]) -> bool:
    """Kick off a one-shot Unity thumbnail export when concept cards still use placeholders."""
    if doc.get("phase") != "awaiting_concept_approval":
        return False
    if doc.get("kitCatalogExporting"):
        return False
    if not cards_missing_previews(hub, doc):
        return False

    hub_key = str(hub.resolve())
    with _KIT_EXPORT_LOCK:
        if hub_key in _KIT_EXPORT_HUBS:
            return False
        _KIT_EXPORT_HUBS.add(hub_key)

    doc["kitCatalogExporting"] = True
    doc["kitCatalogExportMessage"] = "Unity is rendering 3D kit previews for prop cards…"

    def _worker() -> None:
        try:
            ok, msg = export_kit_catalog_sync(hub)
            from build_planner import _read_session, _write_session  # noqa: WPS433

            session = _read_session(hub) or doc
            session["kitCatalogExporting"] = False
            if ok:
                if refresh_prop_cards_from_catalog(hub, session):
                    from datetime import datetime, timezone

                    session["conceptImageTs"] = int(datetime.now(timezone.utc).timestamp())
                session["kitCatalogExportMessage"] = msg
                session.pop("kitCatalogExportError", None)
            else:
                # Partial export is OK — don't surface Unity texture warnings as a blocking UI error.
                count = kit_catalog_thumb_count(hub)
                if count > 0 and refresh_prop_cards_from_catalog(hub, session):
                    from datetime import datetime, timezone

                    session["conceptImageTs"] = int(datetime.now(timezone.utc).timestamp())
                    session.pop("kitCatalogExportError", None)
                    session["kitCatalogExportMessage"] = f"Loaded {count} prop preview(s) from kit catalog."
                elif count == 0:
                    session["kitCatalogExportError"] = msg
                    session["kitCatalogExportMessage"] = (
                        "Prop 3D previews need Unity — Window → Environment Kit → "
                        "Export planner kit catalog"
                    )
            _write_session(hub, session)
        finally:
            with _KIT_EXPORT_LOCK:
                _KIT_EXPORT_HUBS.discard(hub_key)

    threading.Thread(target=_worker, name="kit-catalog-export", daemon=True).start()
    return True
