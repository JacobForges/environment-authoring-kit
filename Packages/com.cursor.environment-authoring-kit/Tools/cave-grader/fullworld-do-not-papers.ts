/**
 * FullWorld anti-patterns — modular Grid blockout vs 49-tile terrain massif.
 * Agents must read category `fullworld_do_not` before mountain/surface layout work.
 */
import type { ResearchEntry } from "./research-catalog.js";

const C = "fullworld_do_not";

export const FULLWORLD_DO_NOT_PAPERS: ResearchEntry[] = [
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: Environment/Grid/EnvironmentRooms as FullWorld play space",
    year: 2026,
    venue: "Kit contract",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_DO_NOT.md",
    topics: `${C}, Ground=Grid, EnvironmentRooms, modular blockout, terrain-first, anti-pattern`,
    provenInProduction: true,
    notes:
      "FullWorld success = SurfaceTerrainMain + 8 play tiles + 16 foothill + 24 peak + labyrinth. Modular room kit stays optional overlay; never authoritative layout.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: PreserveBlockoutGridLayout or carve terrain under room footprints",
    year: 2026,
    venue: "WorldGenerationRequest",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_DO_NOT.md#do-not--modular-blockout-as-the-world",
    topics: `${C}, PreserveBlockoutGridLayout, CarveTerrainToBlockoutFootprints, play disk alignment`,
    provenInProduction: true,
    notes:
      "EnsureFullWorldSurfaceContract sets both false. Enabling aligns DEM to room cluster and benches heightmaps under footprints — destroys mountain edges.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: confuse terrain_tiling with Environment/Grid modular layout",
    year: 2026,
    venue: "Research categories",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_TERRAIN_TILING.md",
    topics: `${C}, terrain_tiling, SetNeighbors, heightmap stitch, not modular rooms`,
    provenInProduction: true,
    notes:
      "terrain_tiling = Unity Terrain neighbor stitch + SurfaceTerrainGridManifest. It does NOT mean EnvironmentRooms or Ground=Grid.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO: 49-tile mountain pipeline order after nine-tile lock",
    year: 2026,
    venue: "SurfaceMountainResearchPipeline",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_TERRAIN.md",
    topics: `${C}, mountain_wilderness_tiles, foothill, peak, labyrinth, SurfaceMountainResearchPipeline`,
    provenInProduction: true,
    notes:
      "mountain_wilderness_tiles → research → lidar → foothill_sculpt → peak_sculpt → labyrinth → cliffs → trails → smooth. Paced 0.05s light steps.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: FullWorld before surface-only + Florida hillshades",
    year: 2026,
    venue: "Preflight / RESEARCH_WORLD_LAYOUT_PLACEMENT",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_DO_NOT.md#do-not--pipeline-order-dem-and-mountains-10-more",
    topics: `${C}, FullWorld 120, surface-only, florida hillshade, preflight`,
    provenInProduction: true,
    notes: "Fastest mistake: FullWorld before 9-tile surface pass and local fl-* hillshades — flat plates on Grid.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: purge GeneratedSurfaceWorld or re-sculpt play disk after DEM lock",
    year: 2026,
    venue: "SurfaceFloridaDemBuildState",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_DO_NOT.md#do-not--pipeline-order-dem-and-mountains-10-more",
    topics: `${C}, GeneratedSurfaceWorld, nine-tile lock, authoritative DEM, outer ring only`,
    provenInProduction: true,
    notes: "After nine-tile lock, macro sculpt is foothill/peak rings only — not center tiles.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: outer rings before nine-tile lock or manifest-only 9-tile audit",
    year: 2026,
    venue: "SurfaceTerrainGridManifest",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_DO_NOT.md#do-not--pipeline-order-dem-and-mountains-10-more",
    topics: `${C}, mountain_wilderness_tiles, mergeTargetTileId, 49 tiles, layout audit`,
    provenInProduction: true,
    notes: "Verify ~49 manifest entries before calling mountain pipeline done.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: full cave rebuild for surface/mountain bot failures",
    year: 2026,
    venue: "CaveBuildRouteProbe / meat loop",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_DO_NOT.md#do-not--pipeline-order-dem-and-mountains-10-more",
    topics: `${C}, targeted fix, surface bot, mountain ladder, no full rebuild`,
    provenInProduction: true,
    notes: "Use failing rung JSON + fullworld_do_not — not LavaTube purge for missing peaks.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: heavy smooth on LiDAR preserve disk or visible EnvironmentRooms as ground",
    year: 2026,
    venue: "mountain_play_smooth",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_DO_NOT.md#do-not--pipeline-order-dem-and-mountains-10-more",
    topics: `${C}, preserve disk, mountain_play_smooth, EnvironmentRooms visible, scene framing`,
    provenInProduction: true,
    notes: "Smooth trails + inner play band only; Scene success = terrain silhouettes at edges, not room floors.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: labyrinth per-edge heightmap grid (520+ queue steps)",
    year: 2026,
    venue: "SurfaceMountainLabyrinthAuthor",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_LABYRINTH.md",
    topics: `${C}, labyrinth_do_not, BuildCorridorPolylines, pixel grid, build time, 45 minutes`,
    provenInProduction: true,
    notes:
      "The dark noisy rectangular foothill block is a failed per-edge carve. Use BuildCarvePolylines spine batch only; target FullWorld build under 45 minutes.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO: Ground anchor on SurfaceTerrainMain for FullWorld",
    year: 2026,
    venue: "SceneGroundResolver",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_DO_NOT.md#do--terrain-first-fullworld-user-target",
    topics: `${C}, SurfaceTerrainMain, SceneGroundResolver, terrain-first, Ground tag`,
    provenInProduction: true,
    notes:
      "If log shows Ground=Grid at start, kit rebinds to SurfaceTerrainMain once terrain exists. Tag terrain as Ground in scene for preflight PASS.",
  },
];

/** Appended to CaveBuildDoNotPrompt.md and research action plans for surface/mountain phases. */
export const FULLWORLD_DO_NOT_PROMPT_BULLETS: string[] = [
  "Do NOT use Environment/Grid/EnvironmentRooms as the FullWorld play layout — terrain tiles (SurfaceTerrainMain + neighbors + foothill + peak) are the world.",
  "Do NOT set PreserveBlockoutGridLayout or CarveTerrainToBlockoutFootprints for FullWorld mountain builds (both must stay false).",
  "Do NOT treat flat brown modular plates on Grid as successful terrain — that is blockout + mis-anchored DEM, not mountain edges.",
  "Do NOT skip outer-ring phases (16 foothill + 24 peak + mountain labyrinth) after nine-tile lock.",
  "Do NOT confuse terrain_tiling (Unity Terrain seam stitch) with Environment/Grid modular rooms.",
  "Do NOT run FullWorld 120 before a successful surface-only 9-tile pass and local Florida hillshades for the seed.",
  "Do NOT purge GeneratedSurfaceWorld or re-sculpt the inner 9 tiles after authoritative DEM + nine-tile lock — outer ring only.",
  "Do NOT attach foothill/peak tiles or run peak/Gaussian sculpt before nine-tile lock validates.",
  "Do NOT pass layout on 9 play tiles alone — require foothill/peak manifest entries; Full AAA also needs ~289 open-world tiles in grid manifest.",
  "Do NOT use LiDAR/DEM as full heightmap stamp on Full AAA — use directional sculpt + centered passes (≤38% guide bias).",
  "Do NOT omit the massive Hollow Titan surface landmark on FullWorld builds — one random surface tile per seed.",
  "Do NOT place Hollow Titan in cave geometry — surface terrain tiles only.",
  "Do NOT full-rebuild the cave to fix surface/mountain bot failures — use targeted rung fixes from JSON reports.",
  "Do NOT heavy-smooth or radiate-replace the main-land preserve disk (~45% center) after LiDAR; mountain_play_smooth is trails + inner band only.",
  "Do NOT carve labyrinth with BuildCorridorPolylines per-edge queue (~520 steps) — use spine batch only; see labyrinth_do_not.",
  "Do NOT use BuildAnnexPassageRunPolylines for heightmap carve — star/checkerboard grid; use BuildCarvePolylines spines on full south 3×2 annex only.",
  "Do NOT carve wilderness cave mouths with CarveEntranceDepression (vertical pits) — use CarveCliffTunnelMouth into the massif on north cliff faces.",
  "Do NOT scatter props on vertical cliff normals (slope > ~42°) — walkable tiles only; see prop_do_not.",
  "Do NOT wrap the entire map in a continuous cliff wall — selective outer-band cliffs + rolling foothills.",
  "Do NOT BUILD / GENERATE / MAKE the research database from agent chat — sync ResearchCache via Tools/cave-grader only.",
  "Do NOT run labyrinth in peak_sculpt and again in carve — single pass in mountain_labyrinth_carve only.",
  "DO anchor and sculpt on SurfaceTerrainMain; verify ~49 terrains in SurfaceTerrainGridManifest.json and visible mountain edges in Scene view.",
  "DO target FullWorld sequential build under 45 minutes (pace batching + no redundant denoise/seam passes).",
];
