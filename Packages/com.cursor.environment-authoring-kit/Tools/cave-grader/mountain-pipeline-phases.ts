/**
 * Mountain massif pipeline — research, LiDAR category, outer ring, cliffs, trails.
 * ONE task per phase; paced in Unity via SurfaceMountainResearchPipeline.
 */
import type { PipelinePhaseDef } from "./pipeline-phase-types.js";

const FW_DO_NOT = "fullworld_do_not";
const IDEAL = "fullworld_layout_ideal";

export const MOUNTAIN_PIPELINE_PHASES: PipelinePhaseDef[] = [
  {
    id: "mountain_research_brief",
    title: "Mountain research brief — Appalachian + fullworld_do_not",
    rung: "ground_placement",
    researchCategories: [FW_DO_NOT, IDEAL, "mountain_terrain", "mountain_lidar", "terrain"],
    webSearchQueries: [
      "Appalachian terrain outer ring game heightfield",
      "open world mountain foothill seam play disk",
    ],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/CaveBuildResearchExecutionBrief.json",
      "Assets/EnvironmentKit/ResearchCache/categories/mountain_lidar/index.json",
    ],
    focus: "ONE task: sync mountain_research + mountain_lidar briefs before wilderness spawn.",
    allowed: ["Read ResearchCache briefs", "Update execution brief JSON only"],
    forbidden: [
      "Carve terrain or labyrinth",
      "Edit UndergroundCaveSystem",
      "Use Environment/Grid modular rooms as play space",
    ],
  },
  {
    id: "mountain_wilderness_tiles",
    title: "FullWorld outer rings — foothill + peak + horizon (81-tile grid)",
    rung: "ground_placement",
    researchCategories: [
      FW_DO_NOT,
      IDEAL,
      "mountain_terrain",
      "mountain_lidar",
      "terrain_tiling",
      "terrain",
    ],
    webSearchQueries: [
      "open world terrain second ring tiles streaming",
      "Unity terrain SetNeighbors grid stitch",
    ],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfaceMountainBuildLadderReport.json",
      "Assets/EnvironmentKit/Generated/SurfaceTerrainGridManifest.json",
    ],
    focus: "ONE task: spawn outer-ring tiles — per tile sculpt → denoise → seam → lock, then next.",
    allowed: [
      "Per-tile wilderness sculpt with varied peak heights",
      "SetNeighbors / heightmap seam on touched edges only",
      "81-tile grid placement (9 play + outer rings)",
    ],
    forbidden: [
      "Labyrinth carve (later phase)",
      "Per-cell maze edge heightmap grid (labyrinth_do_not)",
      "Flatten entire play disk",
    ],
  },
  {
    id: "mountain_foothill_sculpt",
    title: "Foothill ring — denoise + soft play seam",
    rung: "ground_placement",
    researchCategories: [FW_DO_NOT, IDEAL, "mountain_terrain", "mountain_lidar", "terrain"],
    webSearchQueries: ["open world terrain foothill transition play disk heightfield"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfaceTerrainBuildLadderReport.json",
      "Assets/EnvironmentKit/Generated/SurfaceMountainBuildLadderReport.json",
    ],
    focus: "ONE task: denoise foothill tiles and soft-lock inner seam — foothills must RISE from play disk, not dip.",
    allowed: ["Denoise foothill heightmaps", "Soft 5m inner seam blend"],
    forbidden: ["Full-ring re-sculpt", "Pull foothills below play-disk height", "Labyrinth carve"],
  },
  {
    id: "mountain_peak_sculpt",
    title: "Peak ring — denoise + foothill lock (no labyrinth here)",
    rung: "ground_placement",
    researchCategories: [FW_DO_NOT, IDEAL, "mountain_terrain", "mountain_lidar", "terrain"],
    webSearchQueries: ["Appalachian mountain heightfield outer tile seam"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfaceTerrainBuildLadderReport.json",
      "Assets/EnvironmentKit/Generated/SurfaceMountainBuildLadderReport.json",
    ],
    focus: "ONE task: denoise peak tiles and lock inner edge to foothill ring. Labyrinth is ONE pass in mountain_labyrinth_carve only.",
    allowed: ["Denoise peak tiles", "Soft lock peak→foothill inner seam"],
    forbidden: [
      "Labyrinth carve in this phase (duplicate pass removed for build time)",
      "Per-edge maze grid carve (labyrinth_do_not)",
      "Summit hub (later phase)",
    ],
  },
  {
    id: "mountain_wilderness_cave_mouth",
    title: "Mountain massif — wilderness cave mouth (one tile)",
    rung: "ground_placement",
    researchCategories: [FW_DO_NOT, IDEAL, "mountain_terrain", "mountain_lidar", "terrain"],
    webSearchQueries: ["open world mountain cave entrance terrain depression"],
    jsonPaths: ["Assets/EnvironmentKit/Generated/SurfaceMountainBuildLadderReport.json"],
    focus: "ONE task: shallow canyon mouth on three south peak annex tiles — lateral to route, seed-stable; not a deep bowl.",
    allowed: [
      "Carve one mouth per south peak tile (-1,-3), (0,-3), (1,-3)",
      "Approach bench + marker per mouth",
      "Denoise mouth tile after carve",
    ],
    forbidden: [
      "Deep 18m+ bowl",
      "CarveEntranceDepression vertical pit on peak ridgeline",
      "Mouth forward toward play (must bore into mountain)",
      "Mouths on north annex",
      "Labyrinth grid carve",
    ],
  },
  {
    id: "mountain_labyrinth_research",
    title: "Mountain labyrinth research — spine paths only",
    rung: "ground_placement",
    researchCategories: [
      FW_DO_NOT,
      IDEAL,
      "mountain_labyrinth",
      "labyrinth_do_not",
      "mountain_terrain",
      "adventure",
    ],
    webSearchQueries: [
      "Tomb Raider mountain approach maze level design",
      "open world terrain trail bench corridor",
    ],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/CaveBuildResearchExecutionBrief.json",
      "Assets/EnvironmentKit/ResearchCache/categories/mountain_labyrinth/index.json",
      "Assets/EnvironmentKit/ResearchCache/categories/labyrinth_do_not/index.json",
    ],
    focus:
      "ONE task: read WHAT IS a mountain labyrinth + 5 STYLE Example PNGs (creative adaptation OK) + labyrinth_do_not — plan wide bench spine from play disk **south** across 3×2 annex (6 terrains); NO thin jagged lines.",
    allowed: [
      "Read definition + style example images on disk",
      "Plan solution-path spine + north annex + optional secret spur",
      "Mix style ideas (Chartres arc + switchback + hub center, etc.)",
    ],
    forbidden: [
      "Thin hairline heightmap scratches / jagged shard splinters",
      "BuildCorridorPolylines per-edge carve",
      "Copy one example pixel-perfect",
    ],
  },
  {
    id: "mountain_labyrinth_carve",
    title: "Mountain labyrinth carve — spine batch (south foothill raised walls + peak mold)",
    rung: "ground_placement",
    researchCategories: [
      FW_DO_NOT,
      IDEAL,
      "mountain_labyrinth",
      "labyrinth_do_not",
      "mountain_terrain",
      "mountain_lidar",
    ],
    webSearchQueries: ["Unity terrain trail bench wide corridor open world"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfaceMountainBuildLadderReport.json",
      "Assets/EnvironmentKit/Generated/SurfaceWorldManifest.json",
    ],
    focus:
      "ONE task: carve 3–5 spine polylines in ONE heavy batch (~12m half-width), **south** 3×2 foothill+peak annex, seam once, denoise — never queue 500+ edge segments.",
    allowed: [
      "BuildCarvePolylines (connector + solution spine + north annex)",
      "South foothill row (y=-2): SculptLabyrinthWalkwayWithRaisedWalls — ~11.5m half-width benches + ~4.2m raised walls (movie-scale)",
      "South peak row (y=-3): MoldMountainCorridor ~16m half-width ridge-following mold",
      "Hub switchback Bezier spines (BuildCarvePolylines) — no grid raster",
      "QueueRestoreNonAnnexFoothillRollingHills after carve (13-tile foothill ring)",
      "Single seam pass after all spines",
    ],
    forbidden: [
      "BuildCorridorPolylines edge-by-edge queue (labyrinth_do_not)",
      "BuildAnnexPassageRunPolylines star/checkerboard grid (labyrinth_do_not)",
      "Jagged thin shard lines / hairline splinters (labyrinth_do_not ref PNG)",
      "Second labyrinth pass on peak_sculpt",
      "Pixelated rectangular heightmap grid stamp",
      "Per-segment editor queue (520/520 style)",
      "Carve outside south 3×2 annex (six terrains only)",
    ],
  },
  {
    id: "mountain_peak_summit_caps",
    title: "Summit hubs — plaza + knolls + trail spokes",
    rung: "ground_placement",
    researchCategories: [FW_DO_NOT, IDEAL, "mountain_terrain", "mountain_lidar", "terrain"],
    webSearchQueries: ["mountain summit lookout plateau game design"],
    jsonPaths: ["Assets/EnvironmentKit/Generated/SurfaceMountainBuildLadderReport.json"],
    focus:
      "ONE task: summit hub on flat peak plateaus — walkable plaza, 4 aspect knolls, spokes to play + labyrinth center, SummitHub markers.",
    allowed: [
      "Plaza flatten ~26m",
      "4 directional knolls as landmarks",
      "Trail spokes to play disk and MountainLabyrinth_Center",
    ],
    forbidden: [
      "Random 8-bump cap ring (legacy)",
      "Flatten entire peak tile",
      "Labyrinth re-carve",
    ],
  },
  {
    id: "mountain_cliffs",
    title: "Mountain cliff faces — outer perimeter only",
    rung: "ground_placement",
    researchCategories: [FW_DO_NOT, IDEAL, "mountain_terrain", "mountain_lidar", "terrain"],
    webSearchQueries: ["open world cliff band heightfield fall edge"],
    jsonPaths: ["Assets/EnvironmentKit/Generated/SurfaceMountainBuildLadderReport.json"],
    focus: "ONE task: cliff accent on wilderness outer perimeter + denoise — interiors stay walkable.",
    allowed: ["Outer band cliff fall-off", "Denoise after cliff pass"],
    forbidden: ["Flatten 9-tile play disk", "Re-run labyrinth", "Inner foothill dip"],
  },
  {
    id: "mountain_trails",
    title: "Perimeter trails — walkable benches on mountain ring",
    rung: "ground_placement",
    researchCategories: [FW_DO_NOT, IDEAL, "mountain_terrain", "terrain"],
    webSearchQueries: ["hiking trail bench terrain game maximum grade"],
    jsonPaths: [
      "Assets/EnvironmentKit/Generated/SurfaceWorldManifest.json",
      "Assets/EnvironmentKit/Generated/SurfaceMountainBuildLadderReport.json",
    ],
    focus: "ONE task: perimeter trail benches on foothill/peak only.",
    allowed: ["Trail benches on wilderness ring"],
    forbidden: ["Play disk flatten", "Labyrinth edge grid"],
  },
  {
    id: "mountain_play_smooth",
    title: "Play-band smooth — trails + inner disk only",
    rung: "ground_placement",
    researchCategories: [FW_DO_NOT, IDEAL, "terrain", "mountain_terrain"],
    webSearchQueries: ["Unity terrain selective smooth trail corridor"],
    jsonPaths: ["Assets/EnvironmentKit/Generated/SurfaceTerrainBuildLadderReport.json"],
    focus: "ONE task: selective smooth inner play disk and trail benches — skip outer cliff band.",
    allowed: ["Selective smooth play band", "Trail corridor smooth"],
    forbidden: ["Smooth entire 81-tile grid", "Undo summit hubs"],
  },
];

export function phaseForMountainIndex(index: number): PipelinePhaseDef | undefined {
  return MOUNTAIN_PIPELINE_PHASES[index];
}
