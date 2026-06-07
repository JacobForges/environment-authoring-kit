# Research synthesis — open-world streaming & scale-up path

**Purpose:** Inform a move from “large outdoor zone + cave” (current kit: ~1 main terrain + asymmetric neighbors) toward **true open-world status** — continuous traversal, bounded memory, no single square play pad.

**Policy:** Sources below are catalogued for the Environment Kit research pipeline (`Tools/cave-grader/research-catalog.seed.json`). Florida LiDAR remains **structure-only** for caves; this doc is **surface/world scale** only.

**Last updated:** 2026-05-29 (batch 2: +30 sources, 60 total in catalog)

---

## Do I agree open-world bumps “wow”?

**Yes — if you mean “I can walk for minutes in one direction and the world keeps changing.”** That reads as a generational step up from a fixed 9-tile disk.

**No — if you mean “more terrain components in one scene.”** Commercial open worlds are defined by **streaming + density + landmarks**, not tile count alone. Without streaming, floating-origin discipline, and content per chunk, a 4×4 terrain grid still feels like a big square.

**Sweet spot for your kit:** **Horizon-style “large single biome”** first (2–8 km² walkable, stream 128–256 m cells), then optional **No Man’s Sky–lite** infinite extension via seeded procedural tiles — not full MMO sharding on day one.

---

## Category A — Streaming architecture (foundation)

| # | Source | URL | Summary (for kit) |
|---|--------|-----|-------------------|
| 1 | Guillermo Báez — *Streaming in Games* | https://www.guillermobaez.com/blog/streaming-in-games | Grid cells + sphere around player; hybrid with trigger volumes for interiors; UE World Partition as reference architecture. |
| 2 | Slashskill — *Practical Open World Streaming* | https://www.slashskill.com/practical-open-world-streaming-approaches-for-all-platforms/ | Cell sizes: mobile 64–128 m, console 128–256 m, PC 256–512 m; memory budgets per platform; predictive loading beats reactive. |
| 3 | ResearchGate — *Open World Streaming (Dungeon Siege lineage)* | https://www.researchgate.net/publication/303998274_Open_World_Streaming_Automatic_memory_management_in_open_world_games_without_loading_screens | Classic **world streamer** loads 8-connected grid nodes; entities assigned to cells; subset loaded at runtime. |
| 4 | Unity Discussions — *3D open world performance* | https://discussions.unity.com/t/3d-open-world-performance-where-do-i-even-start/1682649 | Floating-point pain ~±5000 units; streaming optional for indie asset limits; LODs mandatory; procedural worlds need origin shift + terrain streaming. |
| 5 | Unity Entities — *Loading scenes (streaming)* | https://docs.unity3d.com/Packages/com.unity.entities@6.5/manual/streaming-loading-scenes.html | DOTS `SceneSystem.LoadSceneAsync` — sections load async; seamless worlds larger than RAM. |
| 6 | Unity Entities — *Loading scenes at runtime* | https://docs.unity3d.com/Packages/com.unity.entities@0.50/manual/loading_scenes.html | Streaming = async by default; load/unload without blocking gameplay when done right. |

**Kit implication:** Add a **`WorldChunkStreamer`** concept: player chunk coordinate → load/unload terrain + surface props + cave entrance hooks for N×N neighborhood.

---

## Category B — Unity implementation (your engine)

| # | Source | URL | Summary |
|---|--------|-----|---------|
| 7 | Unity Manual — *Addressables: Load a scene* | https://docs.unity3d.com/Packages/com.unity.addressables@2.0/manual/LoadingScenes.html | `LoadSceneMode.Additive` + async; preload with `activateOnLoad: false`; priority queue for chunk order. |
| 8 | Unity — *Addressables memory management* | https://docs.unity3d.com/Packages/com.unity.addressables@1.3/manual/MemoryManagement.html | Scene unload/ref-count discipline; avoid leaking bundles when chunks cycle. |
| 9 | YouTube — *Level Streaming in Unity* | https://www.youtube.com/watch?v=JOPgk3D66kA | `LoadSceneAsync` / `UnloadSceneAsync` tutorial pattern for indie open worlds. |
| 10 | Unity Manual — *Terrain Tools / PaintContext* | https://docs.unity3d.com/6000.2/Documentation/ScriptReference/TerrainTools.PaintContext.html | Cross-tile sculpt — required when chunks are separate Terrain objects. |
| 11 | Unity Discussions — *World Building Challenge* | https://discussions.unity.com/t/world-building-challenge/886379 | Industry note: large maps often need **World Machine / Houdini** for cliffs; Gaia/Microverse for tiles; streaming comes after authoring workflow. |
| 12 | Meta — *Asset streaming sample (Addressables + grid LOD)* | https://developers.meta.com/horizon/documentation/unity/unity-sample-asset-streaming | Quadtree/grid subscenes per biome; LOD0/1/2 preload buffer; 264 cells — VR open-world pattern applicable to desktop. |

**Kit implication:** Each **terrain chunk** = optional Addressable scene or generated in-place with shared `TerrainData` template + seeded height bake per `(chunkX, chunkZ)`.

---

## Category C — AAA production references (terrain + streaming)

| # | Source | URL | Summary |
|---|--------|-----|---------|
| 13 | Guerrilla — *Streaming the World of Horizon Zero Dawn* | https://www.guerrilla-games.com/read/Streaming-the-World-of-Horizon-Zero-Dawn | Decima: asset pipeline + low/high-level streaming + scheduling; **vast world without loading screens** — target quality bar. |
| 14 | GDC Vault — *HZD GPU procedural placement* | https://www.gdcvault.com/play/1024120/GPU-Based-Run-Time-Procedural | Runtime fills world around player (trees, rocks, gameplay) — pairs with streaming, not pre-placed everything. |
| 15 | GDC — *Far Cry 5 world generation* | https://www.gamedeveloper.com/design/video-the-world-generation-tech-behind-i-far-cry-5-i- | 64 m sector bakes; masks between steps — align with kit’s phased surface pipeline. |
| 16 | Epic — *World Partition (UE5)* | https://dev.epicgames.com/documentation/en-us/unreal-engine/world-partition-in-unreal-engine | Single persistent level → grid cells; streaming sources; HLOD; **conceptual blueprint** even in Unity. |
| 17 | Epic — *World Building Guide* | https://dev.epicgames.com/community/learning/knowledge-base/r6wl/unreal-engine-world-building-guide | Data layers, OFPA, spatial load flags — design vocabulary for kit “enhancement phases.” |
| 18 | Toxigon — *UE5 World Partition optimization* | https://toxigon.com/ue5-world-partition-optimization | Cell size vs foliage density (200–300 m); biome-specific streaming distances; HLOD pre-bake. |

**Kit implication:** **Phase 1 wow** = Horizon-like streaming + GPU/CPU scatter per loaded chunk. **Not** one-shot 120-step bake of entire world.

---

## Category D — Indie / procedural open world (achievable heroes)

| # | Source | URL | Summary |
|---|--------|-----|---------|
| 19 | GDC — *No Man's Sky continuous world generation* | https://gdcvault.com/play/1024265/Continuous-World-Generation-in-No | Voxel → mesh → texture → populate; **continuous** planet scale; small team architecture reference. |
| 20 | GDC — *No Man's Sky — Building Worlds Using Math(s)* | https://www.gdcvault.com/play/1024514/Building-Worlds-Using | Deterministic noise; seed-shared planets; math-driven terrain without art per planet. |
| 21 | Game Developer — *NMS continuous generation article* | https://www.gamedeveloper.com/programming/video-how-continuous-world-generation-works-in-i-no-man-s-sky-i- | Pipeline breakdown for programmers — good bridge doc. |
| 22 | Princeton — *Infinigen* | https://infinigen.org/ | Infinite procedural worlds (CVPR 2023); terrain module; Blender — batch offline tiles for Unity import. |
| 23 | arXiv — *Infinigen paper* | https://arxiv.org/abs/2306.09310 | Fully procedural natural worlds; infinite variation — research-grade PCG at scale. |
| 24 | Godot Open World Database | https://github.com/DigitallyTailored/Godot-Open-World-Database | Chunk sizes 8–64 m by content type; `load_range` / `batch_time_limit_ms` — indie-friendly streamer API patterns. |
| 25 | gd-agentic-skills — *godot-genre-open-world* | https://github.com/thedivergentai/gd-agentic-skills/blob/main/skills/godot-genre-open-world/SKILL.md | Floating origin >5000 units; threaded chunk load; **density > size** warning. |

**Kit implication:** Your **layout roll + Florida DEM stamp per chunk** is the right indie version of NMS/Infinigen — deterministic, seed-stable, no art per chunk.

---

## Category E — MMO / server-scale (future-facing)

| # | Source | URL | Summary |
|---|--------|-----|---------|
| 26 | WoW dev — *World, tiles, chunks (DeepWiki)* | https://deepwiki.com/wowdev/noggit3/2.1-world-tiles-and-chunks | 64×64 tiles × 16×16 chunks; 33.33 yd chunks — hierarchical terrain editing at MMO scale. |
| 27 | ESEngine — *@esengine/world-streaming* | https://esengine.cn/en/modules/world-streaming/ | 512-unit chunks; load radius 2 / unload 4; max loads per frame — tunable budgets. |
| 28 | PocketMine — *World chunk management* | https://pmmp-pocketmine-mp.mintlify.app/concepts/worlds | 16×16 block columns; async population; loaders keep chunks alive — multiplayer mental model. |
| 29 | The Melding Wars — *zone / gtchunk* | https://github.com/themeldingwars/Documentation/wiki/zone | 512×512 zones; chunk file references — MMO zone file pattern. |
| 30 | Medium — *So You Want to Build an MMO 8/18* | https://medium.com/@alexander.bakharev_16063/so-you-want-to-build-an-mmo-8-18-world-design-level-architecture-c07798d17f1c | Seamless vs zoned vs instanced; server meshing; **do not start here** for solo kit. |

**Kit implication:** Open-world **single-player** first; replicate chunk seeds over network later if ever needed.

---

## Category F — AI / next-gen infinite worlds (research lane)

| # | Source | URL | Summary |
|---|--------|-----|---------|
| 31 | arXiv — *WorldGen (traversable 3D worlds)* | https://arxiv.org/html/2511.16825 | Text → layout (PG constraints) → diffusion assets; **traversable** emphasis — aligns with kit grading. |
| 32 | arXiv — *WorldGrow (AAAI 2026)* | https://ojs.aaai.org/index.php/AAAI/article/view/37571 | Block-wise infinite 3D scene growth; coarse-to-fine — future “expand world” agent. |
| 33 | GitHub — *WorldGrow* | https://github.com/world-grow/WorldGrow | Explicit 3D infinite worlds; walkable — watch for Unity export path. |
| 34 | *InfiniteDiffusion / Terrain Diffusion* | https://xandergos.github.io/terrain-diffusion/ | Infinite terrain with seed consistency; constant-time random access — Minecraft-mod proven. |
| 35 | arXiv — *SimWorld Studio* | https://arxiv.org/html/2605.09423 | UE5 + coding agents generate **interactive** worlds — matches your Cursor-first R&D direction. |
| 36 | NVIDIA — *3D-GENERALIST* | https://research.nvidia.com/publication/2026-03_3d-generalist-vision-language-action-models-crafting-3d-worlds | VLA for layout/materials/lighting — long-horizon research, not v0.4 dependency. |

**Kit implication:** Use AI for **grading, repair, and chunk recipes** — not replacing deterministic Florida + cave queue until trajectories are stable.

---

## Category H — Floating origin & coordinate precision (batch 2)

| # | Source | URL | Summary (for kit) |
|---|--------|-----|-------------------|
| 37 | Netherlands3D — Floating Origin | https://netherlands3d.eu/docs/developers/features/floating-origin/ | Shift entire world when camera drifts from Unity origin; dual-track real-world (WGS84) vs local transforms — required for **4 km+** Option A. |
| 38 | Coherence — World Origin Shifting | https://docs.coherence.io/2.1/manual/advanced-topics/big-worlds/world-origin-shifting | Per-client floating origin with **64-bit absolute** server coords; reference if kit ever adds multiplayer. |
| 39 | Epic — World Origin Rebasing (UE) | https://dev.epicgames.com/documentation/en-us/unreal-engine/world-origin-rebasing-in-unreal-engine | Cross-engine rebasing vocabulary; same problem as Unity ±5000 unit jitter at scale. |

**Kit implication:** Add **`WorldFloatingOrigin`** session component: when player world position exceeds threshold (e.g. 2000 m), subtract delta from all streamed chunk roots + cave anchor.

---

## Category I — Unity streaming implementation (batch 2, gaps + depth)

| # | Source | URL | Summary |
|---|--------|-----|---------|
| 40 | Game Developer — NMS continuous generation | https://www.gamedeveloper.com/programming/video-how-continuous-world-generation-works-in-i-no-man-s-sky-i- | Programmer-facing pipeline breakdown; pairs with GDC Vault NMS talks. |
| 41 | YouTube — Level Streaming in Unity | https://www.youtube.com/watch?v=JOPgk3D66kA | `LoadSceneAsync` / `UnloadSceneAsync` additive pattern for indie chunk scenes. |
| 42 | Unity — Addressables memory management | https://docs.unity3d.com/Packages/com.unity.addressables@1.3/manual/MemoryManagement.html | Ref-count and bundle release when chunks cycle — avoid leak over 32×32 grid playtests. |
| 43 | Unity Entities — loading scenes at runtime | https://docs.unity3d.com/Packages/com.unity.entities@0.50/manual/loading_scenes.html | Async section load without blocking main thread. |
| 44 | Unity Discussions — World Building Challenge | https://discussions.unity.com/t/world-building-challenge/886379 | Author tiles in Houdini/World Machine first; streaming is a **runtime** concern after content exists. |
| 45 | Drafted by Machines — level streaming breakdown | https://daily.jovis.ai/game-development/level-streaming-in-unity-breaking-down-open-worlds/ | `loadDistance` / `unloadDistance` ring; occlusion culling per chunk scene. |
| 46 | WorldGrow (GitHub) | https://github.com/world-grow/WorldGrow | Block-wise infinite growth — Phase 4 research lane, not Option A v1. |

---

## Category J — Multi-tile terrain & advanced mesh terrain (batch 2)

| # | Source | URL | Summary |
|---|--------|-----|---------|
| 47 | Unity API — `Terrain.SetNeighbors` | https://docs.unity3d.com/ScriptReference/Terrain.SetNeighbors.html | LOD seam alignment across cardinal neighbors — **already used** in `SurfaceTerrainTileExpansion.RefreshTerrainConnectivity`. |
| 48 | Unity Discussions — seamless terrain | https://discussions.unity.com/t/seamless-terrain-question/389019 | Must call `SetNeighbors` on **both** tiles; dynamic load is separate from seam API. |
| 49 | Unity — `Terrain.drawInstanced` | https://docs.unity3d.com/6000.1/Documentation/ScriptReference/Terrain-drawInstanced.html | Instanced heightmap path when many chunks active in 5×5 ring. |
| 50 | Unity Manual — Terrain other settings | https://docs.unity3d.com/Manual/terrain-OtherSettings.html | Per-tile draw toggles; heightmap resolution vs LOD complexity tradeoffs. |
| 51 | Ray's Slice — geometry clipmapping | https://raygoe.net/2025/08/29/streaming-massive-terrains-with-geometry-clipmapping/ | Vertex-shader heightmap grids + clipmap rings; escape hatch if Unity Terrain hits bake limits at 4 km². |
| 52 | Unity Terrain Tools package | https://docs.unity3d.com/Packages/com.unity.terrain-tools@5.0/manual/index.html | Multi-tile toolbox for artist passes across chunk grid before runtime stream. |
| 53 | GitHub — DrawMeshInstancedIndirect vegetation | https://github.com/AkilarLiao/UnityURP-Procedural-DrawMeshInstancedIndirect | GPU scatter with FilterMap masks — per-chunk prop density without GameObjects per blade. |

**Kit implication:** Option A keeps **Unity Terrain per chunk** first; enable `drawInstanced` on loaded ring; clipmap path is Phase 3 fallback.

---

## Category K — Landmark density & “not empty” open world (batch 2)

| # | Source | URL | Summary |
|---|--------|-----|---------|
| 54 | Radiator — BotW spatial composition | https://www.blog.radiator.debacle.us/2017/10/open-world-level-design-spatial.html | “Gravity” funnels and triangle landmarks — drives **POI seed table** for Option A. |
| 55 | Medium — BotW CEDEC map (120 sections) | https://medium.com/@gypsyOtoko/this-is-a-reposting-of-my-twitter-thread-summarizing-articles-of-the-botw-cedec-talks-which-can-be-91e9be85de51 | 120 dynamic sections ≈ our **32×32 chunk grid** mental model; procedural map textures nightly. |
| 56 | ZeldaMods — OpenWorldStage | https://zeldamods.org/wiki/Overview | Fixed map units vs auto-placement — kit: **POI table** + existing surface prop scatter. |
| 57 | IGN — Elden Ring map structure | https://sea.ign.com/elden-ring/173092/news/elden-ring-how-fromsofts-largest-most-free-form-map-works-summer-of-gaming | Six domains + legacy dungeons — maps to **one mega-cave under spawn + satellite cave POIs**. |
| 58 | Medium — FromSoftware world design | https://medium.com/@Jamesroha/world-design-lessons-from-fromsoftware-78cadc8982df | Density over raw size; mini-dungeons (catacombs) at trail forks. |
| 59 | Benicia Paw — Erdtree compass | https://beniciapaw.com/2022/04/01/elden-ring-how-to-design-an-open-world/ | Global landmark visible from most chunks — **twin peaks / vista** enhancement per seed. |
| 60 | Thesis — implicit wayfinding (Elden Ring) | https://www.theseus.fi/bitstream/10024/904850/2/Grau_Vesna.pdf | Terrain cues + site placement without UI — validates trail graph + sinkhole POI spacing. |

---

## Category L — AAA scale references & tools (batch 2)

| # | Source | URL | Summary |
|---|--------|-----|---------|
| 61 | GDC Vault — Red Dead Redemption 2 world rendering | https://www.gdcvault.com/play/1025313/Building-the-Beautiful-World-of-Red-Dead-Redemption-2-A-Exhaustive-Approach-to-Rendering | Distant terrain quality bar; HLOD / impostor ring for Phase 3. |
| 62 | GDC Vault — Witcher 3 open world design | https://www.gdcvault.com/play/1023017/The-Witcher-3-Open-World-Design | Handcrafted POI density in large zone — Option A is **Witcher-scale zone**, not infinite. |
| 63 | Game Developer — Assassin's Creed Unity procedural world | https://www.gamedeveloper.com/audio/video-creation-world-of-assassin-s-creed-unity-with-procedural-design | Modular blocks at city scale — analog to **chunk recipe** JSON per `(cx,cz)`. |
| 64 | Procedural Worlds — Gaia | https://procedural-worlds.com/products/gaia/ | Commercial tile stamping; compare to Florida DEM stamp per chunk. |
| 65 | Unity — Occlusion Culling | https://docs.unity3d.com/6000.5/Documentation/Manual/OcclusionCulling.html | Bake per loaded chunk cluster to stay inside frame budget. |
| 66 | Unity — LOD Groups | https://docs.unity3d.com/6000.5/Documentation/Manual/LevelOfDetail.html | Prop/tree LOD mandatory for 5×5 loaded ring (per Category G). |

---

## Category G — Performance budgets (non-negotiable)

| Source | URL | Rule of thumb |
|--------|-----|----------------|
| Slashskill (above) | https://www.slashskill.com/practical-open-world-streaming-approaches-for-all-platforms/ | Console ~2–3 GB streaming budget; 2 ms/frame load budget on console. |
| Unity open-world thread | https://discussions.unity.com/t/3d-open-world-performance-where-do-i-even-start/1682649 | Stay within ~5000 units of origin or implement **floating origin**. |
| Godot OW skill | https://github.com/thedivergentai/gd-agentic-skills/blob/main/skills/godot-genre-open-world/SKILL.md | Disable physics on chunks >2 cells away; prefetch movement direction. |

---

## How many terrains do commercial games use?

| Tier | Example style | Active terrain / landscape pieces at runtime | Notes |
|------|----------------|---------------------------------------------|--------|
| Level adventure | Classic Tomb Raider / Uncharted set pieces | **0–1** (+ meshes) | “Open” feel is gated geometry, not streaming continent. |
| Large zone | Horizon Zero Dawn, Far Cry regions | **Streaming cells** (not one Terrain); many **loaded** patches | GPU placement + streaming, not 9 static tiles. |
| True open world | BOTW, Elden Ring overworld | **Chunk stream** — often **4–16** heavy regions loaded | HLOD + impostors; entire world never in memory. |
| MMO | WoW, FF XIV | **Hierarchical tiles** (1000s on disk, **dozens** near player) | Server interest management. |
| Procedural infinite | No Man's Sky | **Continuous generate** — terrain is algorithmic, not hand-painted grid | Seed determines planet. |

**Your kit today:** 1 main + up to 8 neighbors ≈ **“large zone” prototype**, not yet **streaming open world**.

---

## Recommended plan (phased — no code yet)

### Phase 0 — Definition of done (2 weeks design)

- Pick target: **“Florida karst open zone”** = **4 km × 4 km** walkable @ 128 m cells = **32×32 chunk grid** (conceptual).
- Metrics: max load hitch < 50 ms; ≥30 min walk without repeating landmark; cave entrance every ~800 m along trail graph optional.
- Keep **121/122-step cave queue** for **per-chunk** or **startup region** only — do not bake whole planet in one FullWorld click.

### Phase 1 — Streaming core (MVP wow)

- **Chunk coordinate system** `(cx, cz)` + seed mix `hash(worldSeed, cx, cz)`.
- **Load radius 2, unload radius 3** (indie default from ESEngine/Godot patterns).
- **Floating origin** when player > 2000 m from session origin.
- Reuse existing: `SurfaceTerrainTileExpansion` logic → **on-demand** instead of all-at-build.
- Addressables or additive scenes per chunk (Meta sample pattern).

### Phase 2 — Authoring at scale (keeps your pipeline)

- **Offline/batched:** “Generate chunk recipe” = DEM stamp + vegetation contract + prop scatter (subset of current surface phases).
- **Enhancement hooks:** mountain/trail ridges **per chunk**; Tomb Raider labyrinth only in cave layer (unchanged).
- **Landmarks:** POI table (sinkhole, trail fork, vista) placed by seed — fixes “empty desert” problem.

### Phase 3 — Open-world feel polish

- Predictive prefetch along velocity vector.
- HLOD / impostor ring for distant chunks (terrain mesh simplification).
- Biome rings (coast → karst plain → scrub → swamp) as 1D noise on distance from spawn.

### Phase 4 — Optional infinite extension

- InfiniteDiffusion / Infinigen-style **height-only** generator for chunks beyond authored ring.
- Cave entrances as sparse POI graph, not full cave under every chunk.

### Phase 5 — AI automation (your differentiator)

- Cursor agent loops: “chunk failed burial probe → fix recipe → re-bake.”
- Research-backed prompts from this doc + `research-catalog.seed.json`.
- Not required for Phase 1 ship.

---

## What we should decide together

1. **Target scale:** 4 km² zone vs “infinite” from day one?
2. **Platform:** desktop only first (512 m cells OK) or Quest-style budgets (128 m)?
3. **Build UX:** one button “Expand world” vs player-triggered streaming only in Play mode?
4. **Cave strategy:** one mega-cave under spawn vs distributed cave POIs across chunks?

---

## Refresh commands

```bash
# After catalog seed JSON is updated:
cd Tools/cave-grader && npm run sync-research-pull

# URL digests (optional online):
node Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/enrich-research-urls.mjs
```

**Catalog mirror:** **60** open-world sources in `Tools/cave-grader/open-world-streaming-papers.ts` (regenerates `research-catalog.seed.json`). After `npm run sync-research-pull`, **60** entries in `Assets/EnvironmentKit/ResearchCache/index.json` carry `open_world_streaming` in topics (classic GDC refs exempt from the 2025 year audit via `research-cache-audit.ts`).

**Chosen direction:** **Option A** — Horizon-style **4 km × 4 km** zone, **128 m** cells, **32×32** chunk grid, **load radius 2 / unload 3**. Implementation plan: [`docs/PLAN_OPTION_A_HORIZON_ZONE.md`](PLAN_OPTION_A_HORIZON_ZONE.md).
