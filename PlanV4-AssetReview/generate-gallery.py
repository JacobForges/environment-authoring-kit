#!/usr/bin/env python3
"""Generate Plan V4 visual asset review gallery. REVIEW ONLY — not imported into builds."""
import json
from pathlib import Path

ROOT = Path(__file__).parent

NPCS = [
    ("NPC_PlayGuide", "Remy", "Play guide", "037852b5-74da-44aa-878b-eccda13e5139"),
    ("NPC_PlayMerchant", "Sophie", "Merchant", "61bcdb20-7b85-4f2e-a109-2a0fbd54af78"),
    ("NPC_FoothillRanger", "Survivor A Lusth", "Foothill ranger", "52dcdacb-b43e-4efc-ab6d-9d2d6e09bc95"),
    ("NPC_FoothillHerbalist", "Kate", "Herbalist", "aba68976-90b0-4c6d-9f96-922dd1644be5"),
    ("NPC_PeakHermit", "Romero", "Peak hermit", "576b18a3-2e3e-4f50-b665-cbca337e0757"),
    ("NPC_PeakOreTrader", "Peasant Man", "Ore trader", "5fb4b535-034a-4011-af3b-2880391547a5"),
    ("NPC_PeakGuard_A", "Swat Guy", "Tree guard A", "cf73b862-b7ca-40e9-a156-1b95393d232e"),
    ("NPC_PeakGuard_B", "Swat Guy", "Tree guard B (tint)", "cf73b862-b7ca-40e9-a156-1b95393d232e"),
    ("NPC_HorizonWatcher", "Mannequin", "Horizon watcher", "999a29c0-8fb0-42d4-92c8-5b39ca94bd75"),
    ("NPC_Ambient_A", "Amy", "Ambient walker", "24c3eeb4-6c47-419d-a593-f7b2948b74c7"),
    ("NPC_Ambient_B", "Brian", "Ambient walker", "a58c06c4-3307-40e6-a02d-bfcd658bdbff"),
    ("NPC_Ambient_C", "Elizabeth", "Ambient walker", "5a0e290c-92ea-42e4-afea-cb94ba3fab6d"),
    ("BOSS_Stage", "Warrok W Kurniawan", "Boss", "efb06b46-a470-49b2-b7da-a06755d4dba7"),
    ("BOSS_Add_1", "Mutant", "Boss add", "cccc84b6-d072-4972-99da-75c5702e25f6"),
    ("BOSS_Add_2", "Vampire A Lusth", "Boss add", "90815396-6b00-4efc-b670-4c3497dbb605"),
    ("ENEMY_Foothill_1", "Zombiegirl W Kurniawan", "Foothill enemy", "2f8e576f-f69d-453e-830e-969a2f0217ea"),
    ("ENEMY_Peak_1", "Warzombie F Pedroso", "Peak enemy", "3576fd60-beef-49ec-a3d0-f93231f4fc29"),
    ("ENEMY_Annex_1", "Skeletonzombie T Avelange", "Annex enemy", "91d02eaa-1b0a-4d34-b859-01bcd092c713"),
]

# Curated Substance 3D picks (verified in browser 2026-06-01)
SUB = {
    "ARCH_SWORD": ("Chef Knife 01", "https://substance3d.adobe.com/assets/allassets/ec308aef5a2282c1904684ae54600c33c02b0969", "https://cdn.substance3d.com/v2/files/source/72271f16-bf2e-465e-9172-8f0c542bb5c7?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_SHIELD": ("Mech Shield Plate 11", "https://substance3d.adobe.com/assets/allassets/cb3bda22a4bfe4147c7e6765d8ac45805fab56cc", "https://cdn.substance3d.com/v2/files/source/6a91268f-26ec-4658-8444-d160be8b7101?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_HOOK": ("Mech Hand Grappling Hook 02", "https://substance3d.adobe.com/assets/allassets/5513657cb9fead5fd7c12537ccd91ca03ffd08b9", "https://cdn.substance3d.com/v2/files/source/28889009-5e09-45df-9012-369cc668272a?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_DISC": ("Star Shaped Primitive Shape 01", "https://substance3d.adobe.com/assets/allassets?assetType=Model&search=throwing+star", "https://cdn.substance3d.com/v2/files/source/c4fd0998-ac1b-42dc-a8a1-2c3fa859441e?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "REVIEW"),
    "ARCH_STAFF": ("Stylized Tree Branch 06", "https://substance3d.adobe.com/assets/allassets?assetType=Model&search=stylized+tree+branch", "https://cdn.substance3d.com/v2/files/source/8f1284dd-b4e0-425c-9245-f3ec3d134a2e?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_PICK": ("Climbing Pickaxe", "https://substance3d.adobe.com/assets/allassets/ef601c582f58fd795a72d87a2397caa27349589c", "https://cdn.substance3d.com/v2/files/source/197d7a5c-fca4-40c6-be60-90c3d2d402bb?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_SICKLE": ("Wood Chisel 03", "https://substance3d.adobe.com/assets/allassets/e029e1f921ec9f138b78eec0847e02089cc2fddd", "https://cdn.substance3d.com/v2/files/source/1977eb58-192a-47f1-b2c9-0805c1109310?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_POTION": ("Stylized Potion 01", "https://substance3d.adobe.com/assets/allassets/274eec081f9e6c88edf28d8ade7a235c6f70cd8a", "https://cdn.substance3d.com/v2/files/source/72271f16-bf2e-465e-9172-8f0c542bb5c7?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_GEM": ("Crystal Cluster 09", "https://substance3d.adobe.com/assets/allassets/2e768bf895441627d323ea78d62c1b8fc9479def", "https://cdn.substance3d.com/v2/files/source/d4934fe6-aadf-46db-9950-3273771f5c7e?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_COINS": ("Clip Clasp Coin Wallet Opened", "https://substance3d.adobe.com/assets/allassets/9b775652786379d1cedb7b8454f73737ab1bb140", "https://cdn.substance3d.com/v2/files/source/730583e9-81c0-4e70-b67f-c4b2bfe0b657?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "REVIEW"),
    "ARCH_DEAD_TREE": ("Stylized Tree Root 05", "https://substance3d.adobe.com/assets/allassets/5f14bd95775d9904e9a11741502d82c4c388ca4f", "https://cdn.substance3d.com/v2/files/source/c4fd0998-ac1b-42dc-a8a1-2c3fa859441e?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_LANTERN": ("Stylized Hanging Lantern 03", "https://substance3d.adobe.com/assets/allassets/d27758768edcd8462618e13e87c1c96a63f764aa", "https://cdn.substance3d.com/v2/files/source/e283ac02-9376-455a-9b18-ca30bba6cf03?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_CAMPFIRE": ("Stylized Bonfire", "https://substance3d.adobe.com/assets/allassets/7c34e4ae5abe5cc0b2caddbead6ec7a2a393d7a2", "https://cdn.substance3d.com/v2/files/source/a2e3735e-0a94-44c3-bc4b-c8af16a52536?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_PACK": ("Roll Top Backpack", "https://substance3d.adobe.com/assets/allassets/95c88225d0a806eb1829d1358d859ee360242557", "https://cdn.substance3d.com/v2/files/source/b9e016d5-8038-4106-ae0c-3931dbf1e354?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_FLASK": ("Army Flask 02", "https://substance3d.adobe.com/assets/allassets/5e01f92023bbfc9aaef4eda79bb09a370287b5d9", "https://cdn.substance3d.com/v2/files/source/184372af-9652-49f5-8830-7258342efbc0?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_COMPASS": ("Graphic Compass", "https://substance3d.adobe.com/assets/allassets/51279b98f6d63317bb48e8bac0668fb62103b1bd", "https://cdn.substance3d.com/v2/files/source/a58f6089-6cf8-481d-8bff-c7a9c2300c4a?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_FOOD": ("Bread Basket", "https://substance3d.adobe.com/assets/allassets/3e8c2a98e261509268cb2d18e534a8f5af10bc8b", "https://cdn.substance3d.com/v2/files/source/fb41b7c3-55b3-4a0e-b76b-cee0af0f6fcb?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_SCROLL": ("Ancient Egyptian Papyrus", "https://substance3d.adobe.com/assets/allassets/f55f96b7999fbc3066702465e3d3cb45100c769a", "https://cdn.substance3d.com/v2/files/source/77413cdb-2cae-487b-bd65-b56c433f3829?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_CHISEL": ("Wood Chisel 03", "https://substance3d.adobe.com/assets/allassets/e029e1f921ec9f138b78eec0847e02089cc2fddd", "https://cdn.substance3d.com/v2/files/source/1977eb58-192a-47f1-b2c9-0805c1109310?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_KEY": ("Fence Wood Old", "https://substance3d.adobe.com/assets/allassets/2b79115f748a73682030fd4ed5f5fba0abc9ff62", "https://cdn.substance3d.com/v2/files/source/9aa2011d-0e5d-4d09-8b81-ee57a7f23be8?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "REVIEW"),
    "ARCH_ROCKS": ("Cpg Pedestal Rocks Billy Balls", "https://substance3d.adobe.com/assets/allassets/9d332d8be6ee20a410f2710b81fd87e35bc68f25", "https://cdn.substance3d.com/v2/files/source/2f3b6da3-abcb-4fb5-bbd3-58413a7b2a94?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_SLING": ("KIT placeholder", "", "", "KIT"),
    "ARCH_WOOD_SHIELD": ("Wooden Shield — browse", "https://substance3d.adobe.com/assets/allassets?assetType=Model&search=wooden+shield", "", "PICK"),
    "ARCH_AMULET": ("Medieval Chandelier (pendant proxy)", "https://substance3d.adobe.com/assets/allassets/59195be1b6d2e1090619d3b5ba0a90ebb8534838", "https://cdn.substance3d.com/v2/files/source/1e7d2ff4-afbf-45d1-b817-e8bb963f0308?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "REVIEW"),
    "ARCH_JAR": ("Pendant Light Jar 02", "https://substance3d.adobe.com/assets/allassets/ab5da71e553e019cb95dab9f0387be27039404c6", "https://cdn.substance3d.com/v2/files/source/7fa85ba7-9d56-4b5f-ae54-42c018347919?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "OK"),
    "ARCH_BAR": ("Beverage Dispenser (bar proxy)", "https://substance3d.adobe.com/assets/allassets/6326a74c5b638d02b0ae73dd03fb2fca3a5c41f8", "https://cdn.substance3d.com/v2/files/source/f83530eb-4f7c-4c09-9c5d-0018740f0c25?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "REVIEW"),
    "ARCH_CABIN": ("Carport 02 (cabin proxy)", "https://substance3d.adobe.com/assets/allassets/4884632fd3f97bbac67ef150916d4b45fbee3f49", "https://cdn.substance3d.com/v2/files/source/3047caca-51ae-458a-9c96-54e6d61d8d13?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "REVIEW"),
    "ARCH_BRIDGE": ("Carport 02 (bridge proxy — replace)", "https://substance3d.adobe.com/assets/allassets?assetType=Model&search=wooden+bridge", "https://cdn.substance3d.com/v2/files/source/3047caca-51ae-458a-9c96-54e6d61d8d13?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "PICK"),
    "ARCH_MINE": ("Commercial Double Door 01", "https://substance3d.adobe.com/assets/allassets/f7f01bb08f27d307d9933bed616cf2bb1987b536", "https://cdn.substance3d.com/v2/files/source/606a953d-072c-4c3b-812d-59c506252fcb?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "REVIEW"),
    "ARCH_SHRINE": ("Fence Stone Set 01", "https://substance3d.adobe.com/assets/allassets/e0127c209eb0d9b93b1b2b9bcd8133ae543f7983", "https://cdn.substance3d.com/v2/files/source/76b1d18b-2f30-4a67-b1fa-3ae69cd7dedc?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "REVIEW"),
    "ARCH_HUT": ("Browse: wooden hut", "https://substance3d.adobe.com/assets/allassets?assetType=Model&search=wooden+hut", "", "PICK"),
    "ARCH_WELL": ("Fence Stone Set 01", "https://substance3d.adobe.com/assets/allassets/e0127c209eb0d9b93b1b2b9bcd8133ae543f7983", "https://cdn.substance3d.com/v2/files/source/76b1d18b-2f30-4a67-b1fa-3ae69cd7dedc?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "REVIEW"),
    "ARCH_HELM": ("Browse: medieval helmet", "https://substance3d.adobe.com/assets/allassets?assetType=Model&search=medieval+helmet", "", "PICK"),
    "ARCH_BOOTS": ("Leather Handbag Mini (boots proxy)", "https://substance3d.adobe.com/assets/allassets/6ebf1fe8cbc5db1a4f20e1e8b37c58f94026f43e", "https://cdn.substance3d.com/v2/files/source/6808a35f-9d20-4940-8561-3ff5f701b47b?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "REVIEW"),
    "ARCH_STATUE": ("Fence Stone Set 01", "https://substance3d.adobe.com/assets/allassets/e0127c209eb0d9b93b1b2b9bcd8133ae543f7983", "https://cdn.substance3d.com/v2/files/source/76b1d18b-2f30-4a67-b1fa-3ae69cd7dedc?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "REVIEW"),
    "ARCH_HORN": ("Browse: horn trophy", "https://substance3d.adobe.com/assets/allassets?assetType=Model&search=horn", "", "PICK"),
    "ARCH_SPIKE": ("Nail Polish 02 (spike proxy)", "https://substance3d.adobe.com/assets/allassets/99a12fa54951b162379b08df292824fef9a1c77c", "https://cdn.substance3d.com/v2/files/source/23b4e2e5-1c18-411d-a72d-07f869b8ba46?height=400&width=400&mode=fill&backgroundColor=44,44,44&format=jpg", "REVIEW"),
    "KIT_PLANT": ("Hub scene plants", "", "", "KIT"),
}

ITEMS = [
    ("W01", "Wayfarer blade", "ARCH_SWORD"),
    ("W02", "Round ward", "ARCH_SHIELD"),
    ("W03", "Line hook", "ARCH_HOOK"),
    ("W04", "Return disk", "ARCH_DISC"),
    ("W05", "Channel staff", "ARCH_STAFF"),
    ("W06", "Trail sling", "ARCH_SLING"),
    ("W07", "Stone pick", "ARCH_PICK"),
    ("W08", "Brush sickle", "ARCH_SICKLE"),
    ("W09", "Signal whistle", "ARCH_COMPASS"),
    ("W10", "Trail compass", "ARCH_COMPASS"),
    ("A01", "Travel cloak", "ARCH_AMULET"),
    ("A02", "Layered vest", "ARCH_PACK"),
    ("A03", "Echo helm", "ARCH_HELM"),
    ("A04", "Ridge boots", "ARCH_BOOTS"),
    ("A05", "Ward amulet", "ARCH_AMULET"),
    ("A06", "Mist shawl", "ARCH_AMULET"),
    ("A07", "Bark buckler", "ARCH_WOOD_SHIELD"),
    ("A08", "Root greaves", "ARCH_BOOTS"),
    ("P01", "Verdant draught", "ARCH_POTION"),
    ("P02", "Mist flask", "ARCH_FLASK"),
    ("P03", "Ember tonic", "ARCH_FLASK"),
    ("P04", "Spring water", "ARCH_POTION"),
    ("P05", "Antidote phial", "ARCH_POTION"),
    ("P06", "Trail pack", "ARCH_PACK"),
    ("P07", "Spore balm", "ARCH_JAR"),
    ("P08", "Ration bundle", "ARCH_FOOD"),
    ("C01", "Copper pile", "ARCH_COINS"),
    ("C02", "Silver pile", "ARCH_COINS"),
    ("C03", "Gold pile", "ARCH_COINS"),
    ("C04", "Platinum ingot", "ARCH_BAR"),
] + [(f"G-T{i:02d}", f"Gem tool {i}", "ARCH_GEM") for i in range(1, 7)] + [(f"G-C{i:02d}", f"Gem cabochon {i}", "ARCH_GEM") for i in range(1, 7)] + [(f"G-G{i:02d}", f"Gem cut {i}", "ARCH_GEM") for i in range(1, 7)] + [
    ("K01", "Annex maze key", "ARCH_KEY"),
    ("K02", "Peak gate key", "ARCH_KEY"),
    ("K03", "Hollow sigil", "ARCH_AMULET"),
    ("K04", "Trail marker token", "ARCH_COINS"),
    ("K05", "Geode chisel", "ARCH_CHISEL"),
    ("K06", "Labyrinth map scrap", "ARCH_SCROLL"),
    ("K07", "Hermit charm", "ARCH_AMULET"),
    ("K08", "Boss trophy shard", "ARCH_GEM"),
    ("B01", "Karst mushroom cap", "KIT_PLANT"),
    ("B02", "Crystal bud", "KIT_PLANT"),
    ("B03", "Moonspindle fiber", "KIT_PLANT"),
    ("B04", "Rose petal bundle", "KIT_PLANT"),
    ("B05", "Tropical resin", "KIT_PLANT"),
    ("B06", "Peak iron ore", "ARCH_ROCKS"),
    ("B07", "Basalt shard pile", "ARCH_ROCKS"),
    ("B08", "Mist lantern", "ARCH_LANTERN"),
    ("B09", "Horizon relic", "ARCH_STATUE"),
    ("B10", "Annex bench nail", "ARCH_SPIKE"),
    ("B11", "Zombie tooth", "KIT_PLANT"),
    ("B12", "Warzombie tag", "ARCH_SCROLL"),
    ("B13", "Skeleton rib", "ARCH_SPIKE"),
    ("B14", "Boss horn", "ARCH_HORN"),
    ("B15", "Platinum vault key", "ARCH_KEY"),
    ("L01", "Hollow Titan tree", "ARCH_DEAD_TREE"),
    ("L02", "Karst shrine", "ARCH_SHRINE"),
    ("L03", "Foothill cabin", "ARCH_CABIN"),
    ("L04", "Trail bridge", "ARCH_BRIDGE"),
    ("L05", "Peak ruin arch", "ARCH_SHRINE"),
    ("L06", "Mine head frame", "ARCH_MINE"),
    ("L07", "Horizon hut", "ARCH_HUT"),
    ("L08", "Stone well", "ARCH_WELL"),
    ("L09", "Boulder cluster", "ARCH_ROCKS"),
    ("L10", "Campfire ring", "ARCH_CAMPFIRE"),
]

def mixamo_thumb(uid: str) -> str:
    return f"https://www.mixamo.com/api/v1/characters/{uid}/assets/thumbnails/static.png"

def mixamo_search(name: str) -> str:
    from urllib.parse import quote
    q = quote(name.split()[0] if " " in name else name)
    return f"https://www.mixamo.com/#/?page=1&query={q}&type=Character"

def card(title, img, sub, link, badge=""):
    badge_html = f'<span class="badge {badge.lower()}">{badge}</span>' if badge else ""
    img_html = f'<img src="{img}" alt="{title}" loading="lazy"/>' if img else '<div class="noimg">No preview — open link</div>'
    return f"""<article class="card">{badge_html}<a href="{link}" target="_blank">{img_html}</a><h3>{title}</h3><p>{sub}</p><a class="link" href="{link}" target="_blank">Open on Adobe →</a></article>"""

npc_html = []
for slot, name, role, uid in NPCS:
    npc_html.append(card(f"{name}", mixamo_thumb(uid), f"{slot} — {role}", mixamo_search(name), "MIXAMO"))

item_html = []
for iid, display, arch in ITEMS:
    s = SUB.get(arch, ("?", "", "", "?"))
    name, page, img, status = s
    sub = f"{iid} · {display} · {arch}"
    if arch == "KIT_PLANT":
        item_html.append(card(display, "", "Uses Hub plant prefab (Plant_Mushlight, Crystalbud, etc.)", "file:///Users/jacob/Hub/Assets", "KIT"))
    else:
        item_html.append(card(f"{display}", img, f"{sub}<br/><strong>{name}</strong>", page, status))

html = f"""<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"/><title>Plan V4 Asset Review</title>
<style>
body{{font-family:system-ui,sans-serif;background:#111;color:#eee;margin:0;padding:24px}}
h1,h2{{margin:1em 0 .5em}}
.grid{{display:grid;grid-template-columns:repeat(auto-fill,minmax(200px,1fr));gap:16px}}
.card{{background:#1c1c1c;border:1px solid #333;border-radius:8px;padding:12px;position:relative}}
.card img{{width:100%;height:180px;object-fit:contain;background:#2a2a2a;border-radius:4px}}
.noimg{{height:180px;display:flex;align-items:center;justify-content:center;background:#2a2a2a;color:#888;font-size:12px;text-align:center;padding:8px}}
.card h3{{font-size:14px;margin:8px 0 4px}}
.card p{{font-size:11px;color:#aaa;margin:0}}
.link{{font-size:11px;color:#6cf}}
.badge{{position:absolute;top:8px;right:8px;font-size:10px;padding:2px 6px;border-radius:4px;background:#333}}
.badge.ok{{background:#163}}
.badge.review{{background:#663}}
.badge.pick{{background:#633}}
.badge.kit{{background:#336}}
.note{{background:#222;border-left:4px solid #6cf;padding:12px;margin:16px 0}}
</style></head><body>
<h1>Plan V4 — Asset visual review</h1>
<div class="note"><strong>REVIEW ONLY</strong> — not imported into Unity until you approve.<br/>
Open this file in a browser with internet. NPC previews load from Mixamo CDN. Item previews from Substance 3D CDN.<br/>
Badge <span class="badge ok">OK</span> = good pick · <span class="badge review">REVIEW</span> = acceptable proxy · <span class="badge pick">PICK</span> = you must choose on substance3d.adobe.com · <span class="badge kit">KIT</span> = existing Hub asset</div>
<h2>NPCs & enemies (18) — Mixamo</h2>
<div class="grid">{''.join(npc_html)}</div>
<h2>Items & landmarks (85 rows) — Substance 3D / Kit</h2>
<div class="grid">{''.join(item_html)}</div>
<p>Reply: <code>APPROVE MANIFEST</code> or list item IDs to swap (e.g. <code>SWAP W01 → [Substance asset name]</code>).</p>
</body></html>"""

(ROOT / "index.html").write_text(html, encoding="utf-8")

manifest = {
    "npcs": [{"slot": s, "mixamoName": n, "role": r, "characterId": u, "thumbnail": mixamo_thumb(u), "mixamoUrl": mixamo_search(n)} for s, n, r, u in NPCS],
    "items": [{"id": i, "display": d, "archetype": a, "substance": SUB.get(a)} for i, d, a in ITEMS],
}
(ROOT / "asset-manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
print("Wrote", ROOT / "index.html")
