# Research synthesis — surface prop spread & underground cave entrance

Applied in code (2026-05-28). Sources are in `Tools/cave-grader/research-catalog.seed.json` and `Assets/EnvironmentKit/ResearchCache/`.

**Open-world scale-up (planning only):** see [RESEARCH_OPEN_WORLD_STREAMING.md](RESEARCH_OPEN_WORLD_STREAMING.md) — 30+ curated URLs on chunk streaming, Unity Addressables, AAA references, indie/MMO patterns, and AI infinite-world research (2026-05-29 catalog batch).

## Surface vegetation — full tile coverage

| Source | URL | Finding | Kit implementation |
|--------|-----|---------|-------------------|
| Guerrilla (Horizon ZD vegetation placement) | https://www.guerrilla-games.com/read/horizon-zero-dawn-procedural-vegetation-placement | GPU scatter runs **after** terrain rules; placement graph defines density; points stay separate from heightfield | `SurfaceTerrainPropPlacementRegion` grid-first slots; `OrderSlotsForFullAreaSpread` across all locked FullWorld terrains (81-tile) |
| Far Cry 5 tool chain (GDC) | https://tools.engineer/gdc2018-procedural-world-generation-of-far-cry-5 | **64×64 m** sector bakes; masks passed between steps; biomes not one diagonal stripe | Per-tile grid on all `SurfaceTerrainTiles`; trail slots capped ~4% |
| Unity Terrain trees | https://docs.unity3d.com/6000.5/Documentation/Manual/terrain-Trees.html | Tree/detail layers use distance & density per terrain | Raised `TargetPerTile`; **unified 2× spread** + polish |

### Build pipeline order (surface props)

1. Lock terrains → plan  
2. **Unified spread** — each category (trees, grass, bushes, ground cover): **2×** grid spacing + interstitial offset on all tiles in one pass  
3. **Polish** — top-up toward contract target (optional density pass)  
4. Post-prop crater stabilization → terrain ladder  

## Underground cave walkway

| Source | URL | Finding | Kit implementation |
|--------|-----|---------|-------------------|
| USGS DS 926 | https://pubs.usgs.gov/ds/0926/ | Floridan aquifer **structural** surfaces & thickness — void below land surface | `CaveGeometryPaths.UndergroundDepthMeters`; maze route floor below mouth |
| FGS karst / subsidence GIS | https://geodata.dep.state.fl.us/search?layout=grid&tags=karst | Entrance at surface; collapse/sink features | `CarveTerrainBowlAtMouth`; `SurfaceTrailCaveMouthConnector` |
| Unity terrain heightmaps | https://docs.unity3d.com/6000.5/Documentation/Manual/terrain-Heightmaps.html | Carve depression at entrance | `CaveEntranceVolumeBuilder.CarveTerrainBowlAtMouth` |
| Elden Ring mine (reference) | visual ref `fromsoftware-cave-lighting` | Underground fog/ambient separate from surface | `CaveUndergroundAtmosphere` + `ExtendAtmosphereForSurfaceDescent` |

### Enforcer entry point

`CaveUndergroundEntranceEnforcer.Enforce` runs at cave step **Surface walk-in** (queued build 11):

- Align cave to `CaveOpenings` marker  
- Snap mouth to walkable surface (depth-only when XZ locked)  
- Lower cave root if route start &lt; 5.5 m below surface lip  
- Rebuild descent (project floor modules + collider ramp)  
- Extend surface trail to mouth  
- Material repair on cave root  

## Refresh research URL digests

From project root (Repo Test or Hub):

```bash
node Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/enrich-research-urls.mjs
```

Or Unity: **Window → Environment Kit → Cave Build → Diagnostics → Enrich Research URLs (online)**

Writes `Assets/EnvironmentKit/Generated/CaveBuildResearchUrlDigest.json` and merges into `CaveBuildResearch.json`.
