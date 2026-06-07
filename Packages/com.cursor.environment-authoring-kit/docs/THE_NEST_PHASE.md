# The Nest Phase — blank-scene test project

**Purpose:** Prove Environment Authoring Kit works from an **empty Unity scene** to a **playable FullWorld demo** without manual Ground/grid setup.

**Location (local):** `~/Projects/the-nest-phase`  
**GitHub:** https://github.com/JacobForges/the-nest-phase

---

## Setup

1. Clone **Hub** to `~/Hub` (this repo).
2. Open **`~/Projects/the-nest-phase`** in Unity **6000.4.6f1**.
3. Run `./scripts/bootstrap.sh` once (CC0 symlink + npm).

Package reference (in Nest Phase `Packages/manifest.json`):

```json
"com.cursor.environment-authoring-kit": "file:../../Hub/Packages/com.cursor.environment-authoring-kit"
```

---

## Test procedure

| Step | Action | Expected |
|------|--------|----------|
| 1 | **File → New Scene** | Empty scene (camera only is fine) |
| 2 | **Window → Environment Kit → Hub** | Hub opens, Build tab visible |
| 3 | **Build Complete Cave** | Clone setup runs; Ground + PortalFive + grid hosts created |
| 4 | Wait for pipeline | Hub step counter advances; no BLOCK in preflight |
| 5 | **Play** | Third-person walk on terrain; cave portal linked |

---

## Auto-created scene objects

| Object | Role |
|--------|------|
| `Ground` | Tagged **Ground** — build anchor |
| `PortalFive` | Cave entrance placement |
| `SurfaceTerrainFlatHost` | Terrain tile parent |
| `SurfaceFullWorldGridAnchor` | Edge-to-edge grid snap origin |
| `EnvironmentRoot` | Kit pipeline root |

Code: `CaveBuildProjectSetup.EnsureMinimalSceneDefaults`, `SurfaceTerrainTileExpansion.EnsureDefaultGridAnchors`.

---

## Concept layouts (0–9)

Hub **Layout concept** dropdown + **Random on build** — see [FULLWORLD_GENERATION_PRESETS.md](FULLWORLD_GENERATION_PRESETS.md) (10 concepts, not 20).

Guide PNGs: `Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts/00..09/concept.png`

---

## Biome layout (11 zones)

| Zone | Tiles (target) |
|------|----------------|
| Play | 9 |
| Mixed transition | ~352 (rings 2–9) |
| Concept presets 0–9 | ~864 outer wedges |

See `FullWorldBiomeZoneLayout.cs`.

---

## Failure triage

| Symptom | Fix |
|---------|-----|
| No ground | Re-run Build — setup creates Ground automatically |
| Preflight BLOCK: prefab catalog | Import licensed cave modules or let starter cubes generate |
| Preflight BLOCK: Node | Install Node 18+; run bootstrap.sh |
| Package not found | Fix `manifest.json` path to Hub |
| Unity batch licensing | Open project in Unity Editor GUI (not headless `-createProject`) |

---

## Related

- [README.md](../../../README.md) — Hub root
- [FIRST_RUN_AFTER_SESSION.md](FIRST_RUN_AFTER_SESSION.md)
- [SURFACE_TERRAIN_GRID_AND_OPEN_WORLD.md](SURFACE_TERRAIN_GRID_AND_OPEN_WORLD.md)
