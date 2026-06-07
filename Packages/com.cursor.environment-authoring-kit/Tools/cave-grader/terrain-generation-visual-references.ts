/**
 * Terrain generation visual references — play disk, Appalachian peaks, foothills.
 * 10 GOOD + 10 DO NOT per category (40 images). Linked to mountain/terrain pipeline phases.
 */
import type { VisualReference } from "./research-visual-references.js";
import type { ResearchEntry } from "./research-catalog.js";

const RAW =
  "https://github.com/cursor/environment-authoring-kit/raw/main/docs/images";
const KIT = "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_TERRAIN.md";

function img(name: string): string {
  return `${RAW}/${name}`;
}

type TerrainGenSpec = {
  id: string;
  title: string;
  filename: string;
  category: VisualReference["category"];
  notes: string;
  topics: string;
  antiPattern?: boolean;
  phaseIds: string[];
};

const SPECS: TerrainGenSpec[] = [
  // ── Play disk structures — GOOD ──
  {
    id: "play-disk-good-01",
    title: "GOOD: seamless nine-tile play disk — invisible tile edges",
    filename: "play-disk-good-01-seamless-grid.png",
    category: "terrain",
    notes:
      "Nine Unity terrains form one continuous walkable field. Heightmap edges copied/blended — no dark lines, no step at tile borders. Florida LiDAR detail preserved in center.",
    topics: "play_disk_structure, terrain_tiling, seamless stitch, nine-tile grid",
    phaseIds: ["surface_nine_tile_lock", "mountain_wilderness_tiles"],
  },
  {
    id: "play-disk-good-02",
    title: "GOOD: gentle Florida field with subtle LiDAR micro-relief",
    filename: "play-disk-good-02-florida-field.png",
    category: "terrain",
    notes:
      "Flat-to-rolling play disk: low amplitude height variation, no crop circles or radial stamps. Readable third-person traversal.",
    topics: "play_disk_structure, florida lidar, micro relief, walkable field",
    phaseIds: ["surface_lidar_stamp", "surface_nine_tile_lock"],
  },
  {
    id: "play-disk-good-03",
    title: "GOOD: play disk south edge ready for foothill merge",
    filename: "play-disk-good-03-south-merge-ready.png",
    category: "terrain",
    notes:
      "South play edge rises gently toward wilderness — no bowl, no cliff. Foothill ring can stitch without a visible trough.",
    topics: "play_disk_structure, foothill transition, south annex gate",
    phaseIds: ["mountain_foothill_sculpt", "mountain_wilderness_tiles"],
  },
  {
    id: "play-disk-good-04",
    title: "GOOD: alphamap grass/dirt blend without circular artifacts",
    filename: "play-disk-good-04-clean-alphamap.png",
    category: "terrain",
    notes:
      "Terrain paint layers follow slope and noise — no perfect circles, no stamp halos. Matches height detail scale.",
    topics: "play_disk_structure, alphamap, terrain paint, no crop circles",
    phaseIds: ["surface_nine_tile_polish", "mountain_play_smooth"],
  },
  {
    id: "play-disk-good-05",
    title: "GOOD: primary cave mouth depression on play disk only",
    filename: "play-disk-good-05-single-cave-mouth.png",
    category: "terrain",
    notes:
      "One authoritative walk-in mouth on the nine-tile disk — shallow, readable bench. No extra satellite openings on play tiles.",
    topics: "play_disk_structure, cave mouth, single entrance, portal five",
    phaseIds: ["surface_route_bot", "dem_georeference"],
  },
  {
    id: "play-disk-good-06",
    title: "GOOD: road/trail continuity across tile seams",
    filename: "play-disk-good-06-trail-continuity.png",
    category: "terrain",
    notes:
      "Trail bench height matches at every shared edge — player sees one path, not a kink or gap at tile boundary.",
    topics: "play_disk_structure, trail seam, height continuity",
    phaseIds: ["surface_route_bot", "mountain_trails"],
  },
  {
    id: "play-disk-good-07",
    title: "GOOD: uniform Y alignment — all nine tiles at ground level",
    filename: "play-disk-good-07-ground-level-grid.png",
    category: "terrain",
    notes:
      "All play terrains share SurfaceY — no floating plates or sunken corners. SetNeighbors connectivity confirmed.",
    topics: "play_disk_structure, ground placement, SetNeighbors, grid snap",
    phaseIds: ["mountain_wilderness_tiles", "surface_terrain_extend"],
  },
  {
    id: "play-disk-good-08",
    title: "GOOD: play disk center preserves DEM authority",
    filename: "play-disk-good-08-dem-authority-center.png",
    category: "terrain",
    notes:
      "Center tile keeps georeferenced DEM; outer play neighbors blend inward — not re-sculpted after lock.",
    topics: "play_disk_structure, DEM lock, nine-tile authority",
    phaseIds: ["dem_georeference", "surface_nine_tile_lock"],
  },
  {
    id: "play-disk-good-09",
    title: "GOOD: south labyrinth connector bench from play disk",
    filename: "play-disk-good-09-labyrinth-connector.png",
    category: "terrain",
    notes:
      "Wide carved bench leaves play disk south edge into annex — raised walls optional, floor never below play height.",
    topics: "play_disk_structure, labyrinth gate, south connector, bench trail",
    phaseIds: ["mountain_labyrinth_carve", "mountain_labyrinth_research"],
  },
  {
    id: "play-disk-good-10",
    title: "GOOD: 3×3 play disk with wilderness rings outside Chebyshev 2+",
    filename: "play-disk-good-10-rings-outside.png",
    category: "terrain",
    notes:
      "Mountains and labyrinth live on foothill/peak/horizon rings only — play disk stays open Florida field.",
    topics: "play_disk_structure, fullworld layout, chebyshev rings",
    phaseIds: ["mountain_wilderness_tiles", "mountain_research_brief"],
  },
  // ── Play disk — DO NOT ──
  {
    id: "play-disk-bad-01",
    title: "DO NOT: visible dark tile seam lines on play disk",
    filename: "play-disk-bad-01-visible-seams.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Black or shadowed lines at every terrain edge — skipped BlendSharedEdge or flatGridSkipSeamBlend. Unacceptable for shipping.",
    topics: "play_disk_do_not, visible seam, tile line, stitch failure",
    phaseIds: ["mountain_wilderness_tiles", "surface_nine_tile_lock"],
  },
  {
    id: "play-disk-bad-02",
    title: "DO NOT: crop circle height stamps on flat field",
    filename: "play-disk-bad-02-crop-circles.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Radial height brushes or circular noise stamps on play disk — looks like UFO landing sites. Remove radial sculpt on center tiles.",
    topics: "play_disk_do_not, crop circles, radial artifact, height stamp",
    phaseIds: ["surface_nine_tile_polish", "mountain_play_smooth"],
  },
  {
    id: "play-disk-bad-03",
    title: "DO NOT: bowl/dip at play-to-foothill inner edge",
    filename: "play-disk-bad-03-inner-bowl.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Play edge lower than foothill toe — visible trough before mountains rise. Foothills must rise from play, never carve inward.",
    topics: "play_disk_do_not, inner bowl, foothill dip, seam trough",
    phaseIds: ["mountain_foothill_sculpt", "mountain_wilderness_tiles"],
  },
  {
    id: "play-disk-bad-04",
    title: "DO NOT: multiple cave openings scattered on play disk",
    filename: "play-disk-bad-04-extra-cave-openings.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "CaveOpening_1…7 markers on play tiles — only one primary mouth on disk; POI caves belong on south peak wilderness.",
    topics: "play_disk_do_not, extra cave openings, satellite caves",
    phaseIds: ["surface_route_bot", "mountain_wilderness_cave_mouth"],
  },
  {
    id: "play-disk-bad-05",
    title: "DO NOT: floating or sunken terrain plates",
    filename: "play-disk-bad-05-floating-plates.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Tiles at mismatched transform Y — vertical gap at edges even when heightmaps match. Force grid snap before stitch.",
    topics: "play_disk_do_not, floating tile, Y mismatch, grid snap",
    phaseIds: ["mountain_wilderness_tiles", "surface_terrain_extend"],
  },
  {
    id: "play-disk-bad-06",
    title: "DO NOT: re-sculpt play disk after nine-tile DEM lock",
    filename: "play-disk-bad-06-resculpt-after-lock.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Macro mountain sculpt bleeding into Chebyshev≤1 tiles — destroys LiDAR authority. Outer ring only after lock.",
    topics: "play_disk_do_not, DEM lock, resculpt, fullworld_do_not",
    phaseIds: ["mountain_peak_sculpt", "mountain_foothill_sculpt"],
  },
  {
    id: "play-disk-bad-07",
    title: "DO NOT: vertical cliff wall at play disk edge",
    filename: "play-disk-bad-07-edge-cliff.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "90° wall where play meets wilderness — use 22–56m soft blend bands, not instant rise at tile border.",
    topics: "play_disk_do_not, edge cliff, hard vertical, play perimeter",
    phaseIds: ["mountain_foothill_sculpt", "mountain_wilderness_tiles"],
  },
  {
    id: "play-disk-bad-08",
    title: "DO NOT: modular Grid room footprints carved into play terrain",
    filename: "play-disk-bad-08-grid-footprints.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "EnvironmentRooms blockout benches under play disk — PreserveBlockoutGridLayout destroys natural field. Terrain-first layout.",
    topics: "play_disk_do_not, EnvironmentRooms, blockout footprint, fullworld_do_not",
    phaseIds: ["mountain_research_brief", "dem_georeference"],
  },
  {
    id: "play-disk-bad-09",
    title: "DO NOT: jagged heightmap noise on walkable play center",
    filename: "play-disk-bad-09-jagged-play-noise.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "High-frequency height noise on play disk — unwalkable micro-cliffs. Denoise center; reserve detail for wilderness rings.",
    topics: "play_disk_do_not, jagged noise, unwalkable, denoise",
    phaseIds: ["mountain_play_smooth", "surface_nine_tile_polish"],
  },
  {
    id: "play-disk-bad-10",
    title: "DO NOT: labyrinth carve bleeding into nine-tile play disk",
    filename: "play-disk-bad-10-labyrinth-on-play.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Maze grid carved on play tiles instead of south annex — annex is Chebyshev 2–3 south foothill/peak only.",
    topics: "play_disk_do_not, labyrinth on play, wrong ring, labyrinth_do_not",
    phaseIds: ["mountain_labyrinth_carve", "mountain_labyrinth_research"],
  },
  // ── Appalachian peaks — GOOD ──
  {
    id: "appalachian-peak-good-01",
    title: "GOOD: rounded Blue Ridge summit massif",
    filename: "appalachian-peak-good-01-rounded-summit.png",
    category: "mountain_lidar",
    notes:
      "Broad domed summit, long ridgeline — Appalachian rolling relief, not Alps knife-edge. Outer peak ring Chebyshev 3.",
    topics: "appalachian_peak, rounded summit, blue ridge, rolling ridgeline",
    phaseIds: ["mountain_peak_sculpt", "mountain_research_brief"],
  },
  {
    id: "appalachian-peak-good-02",
    title: "GOOD: ridge-and-valley silhouette from peak ring",
    filename: "appalachian-peak-good-02-ridge-valley.png",
    category: "mountain_lidar",
    notes:
      "Parallel ridges with soft saddles — varied peak heights per tile seed, readable from play disk south vista.",
    topics: "appalachian_peak, ridge and valley, varied massif, peak ring",
    phaseIds: ["mountain_peak_sculpt", "mountain_wilderness_tiles"],
  },
  {
    id: "appalachian-peak-good-03",
    title: "GOOD: peak inner edge blends to foothill height",
    filename: "appalachian-peak-good-03-foothill-blend.png",
    category: "mountain_lidar",
    notes:
      "5m symmetric seam band — peak samples lerp toward foothill neighbor height. No bowed gap, no vertical wall.",
    topics: "appalachian_peak, peak foothill seam, symmetric blend, invisible edge",
    phaseIds: ["mountain_peak_sculpt", "mountain_wilderness_tiles"],
  },
  {
    id: "appalachian-peak-good-04",
    title: "GOOD: denoised peak heightmap — smooth walkable benches",
    filename: "appalachian-peak-good-04-denoised-bench.png",
    category: "mountain_lidar",
    notes:
      "Macro shape from relief sampler; micro noise removed. Trail benches on shoulders, cliffs on outer perimeter only.",
    topics: "appalachian_peak, denoise, trail bench, outer cliff band",
    phaseIds: ["mountain_peak_sculpt", "mountain_trails"],
  },
  {
    id: "appalachian-peak-good-05",
    title: "GOOD: Smoky-style long crest across multiple peak tiles",
    filename: "appalachian-peak-good-05-long-crest.png",
    category: "mountain_lidar",
    notes:
      "Ridgeline continues across tile boundaries — edge heights stitched before detail pass. One continuous crest.",
    topics: "appalachian_peak, long crest, multi-tile ridge, seam first",
    phaseIds: ["mountain_peak_sculpt", "mountain_cliffs"],
  },
  {
    id: "appalachian-peak-good-06",
    title: "GOOD: south peak wilderness cave mouth — shallow canyon",
    filename: "appalachian-peak-good-06-cave-mouth.png",
    category: "mountain_lidar",
    notes:
      "Three south-facing mouths on peak annex tiles — lateral bench approach, not deep bowl. Distinct POI cave systems.",
    topics: "appalachian_peak, wilderness cave mouth, south peak, shallow canyon",
    phaseIds: ["mountain_wilderness_cave_mouth"],
  },
  {
    id: "appalachian-peak-good-07",
    title: "GOOD: rock dressing on peak slopes only",
    filename: "appalachian-peak-good-07-rock-dressing.png",
    category: "mountain_lidar",
    notes:
      "Slope-painted rock layers + scatter on peak shoulders — inner seam band stays soil/grass for invisible blend.",
    topics: "appalachian_peak, rock dressing, slope paint, peak cliffs phase",
    phaseIds: ["mountain_cliffs", "mountain_peak_sculpt"],
  },
  {
    id: "appalachian-peak-good-08",
    title: "GOOD: horizon ring soft backdrop behind peaks",
    filename: "appalachian-peak-good-08-horizon-backdrop.png",
    category: "mountain_lidar",
    notes:
      "Chebyshev 4 horizon tiles at 62% peak rise scale — distant massifs, not competing with play disk readability.",
    topics: "appalachian_peak, horizon ring, distant massif, chebyshev 4",
    phaseIds: ["mountain_wilderness_tiles", "mountain_peak_sculpt"],
  },
  {
    id: "appalachian-peak-good-09",
    title: "GOOD: peak tile edge matches neighbor — zero audit seam",
    filename: "appalachian-peak-good-09-zero-seam-audit.png",
    category: "mountain_lidar",
    notes:
      "WorldLayoutAudit seam ≤0.5m at peak↔peak and peak↔foothill edges after BlendSharedEdge batch.",
    topics: "appalachian_peak, zero seam, layout audit, height copy",
    phaseIds: ["mountain_wilderness_tiles", "mountain_peak_sculpt"],
  },
  {
    id: "appalachian-peak-good-10",
    title: "GOOD: Appalachian scale — playable not Everest",
    filename: "appalachian-peak-good-10-playable-scale.png",
    category: "mountain_lidar",
    notes:
      "Corner rise ~78m, edge ~56m — dramatic from play disk but third-person traversal still feasible on benches.",
    topics: "appalachian_peak, playable scale, corner rise, edge rise meters",
    phaseIds: ["mountain_research_brief", "mountain_peak_sculpt"],
  },
  // ── Appalachian peaks — DO NOT ──
  {
    id: "appalachian-peak-bad-01",
    title: "DO NOT: vertical wall extrusion at peak base (bowed gap)",
    filename: "appalachian-peak-bad-01-vertical-wall-gap.png",
    category: "mountain_lidar",
    antiPattern: true,
    notes:
      "Peak tile rises straight up from flat foothill — visible horizontal gap at base. Use symmetric edge blend, not fill-only toe ramp.",
    topics: "appalachian_peak_do_not, bowed gap, vertical wall, peak base",
    phaseIds: ["mountain_peak_sculpt", "mountain_wilderness_tiles"],
  },
  {
    id: "appalachian-peak-bad-02",
    title: "DO NOT: knife-edge Alpine spire on peak ring",
    filename: "appalachian-peak-bad-02-knife-spire.png",
    category: "mountain_lidar",
    antiPattern: true,
    notes:
      "Single-pixel spike summit — wrong biome for Florida-adjacent Appalachian stylization. Broad rounded crests required.",
    topics: "appalachian_peak_do_not, knife edge, alps spire, wrong biome",
    phaseIds: ["mountain_peak_sculpt", "mountain_research_brief"],
  },
  {
    id: "appalachian-peak-bad-03",
    title: "DO NOT: peak sculpt stamped on play disk tiles",
    filename: "appalachian-peak-bad-03-on-play-disk.png",
    category: "mountain_lidar",
    antiPattern: true,
    notes:
      "Mountain relief on Chebyshev≤1 — violates nine-tile lock. Peaks only on ring 3 (+ labyrinth south annex).",
    topics: "appalachian_peak_do_not, sculpt on play, chebyshev violation",
    phaseIds: ["mountain_peak_sculpt", "mountain_foothill_sculpt"],
  },
  {
    id: "appalachian-peak-bad-04",
    title: "DO NOT: blocky un-denoised heightmap shards",
    filename: "appalachian-peak-bad-04-blocky-shards.png",
    category: "mountain_lidar",
    antiPattern: true,
    notes:
      "Raw heightmap steps like Minecraft — run QueueDenoiseWildernessTiles before lock/stitch.",
    topics: "appalachian_peak_do_not, blocky shards, no denoise, jagged heightmap",
    phaseIds: ["mountain_peak_sculpt"],
  },
  {
    id: "appalachian-peak-bad-05",
    title: "DO NOT: peak inner dead zone then instant 80m cliff",
    filename: "appalachian-peak-bad-05-instant-cliff.png",
    category: "mountain_lidar",
    antiPattern: true,
    notes:
      "Flat inner band then vertical rise — PeakRiseInnerMeters too small + aggressive riseRamp. Use gradual 72m rise span.",
    topics: "appalachian_peak_do_not, instant cliff, rise ramp, inner dead zone",
    phaseIds: ["mountain_peak_sculpt", "mountain_wilderness_tiles"],
  },
  {
    id: "appalachian-peak-bad-06",
    title: "DO NOT: seal/fill-only peak base creating bulging toe",
    filename: "appalachian-peak-bad-06-bulging-toe.png",
    category: "mountain_lidar",
    antiPattern: true,
    notes:
      "QueueSealPeakFoothillBaseGaps raised toe without shaving high side — creates bowed outward lip. Prefer edge height copy.",
    topics: "appalachian_peak_do_not, bulging toe, seal gap, fill only lock",
    phaseIds: ["mountain_peak_sculpt"],
  },
  {
    id: "appalachian-peak-bad-07",
    title: "DO NOT: peak tile misaligned off grid (seam gap)",
    filename: "appalachian-peak-bad-07-off-grid-gap.png",
    category: "mountain_lidar",
    antiPattern: true,
    notes:
      "Transform XZ mismatch >1m before stitch — physical void at edge even with matching heights. EnforceWildernessGridLayout first.",
    topics: "appalachian_peak_do_not, off grid, transform gap, wilderness layout",
    phaseIds: ["mountain_wilderness_tiles"],
  },
  {
    id: "appalachian-peak-bad-08",
    title: "DO NOT: deep bowl wilderness cave mouth",
    filename: "appalachian-peak-bad-08-deep-bowl-mouth.png",
    category: "mountain_lidar",
    antiPattern: true,
    notes:
      "18m+ depression on peak tile — breaks ridgeline readability. Shallow canyon mouth with approach bench only.",
    topics: "appalachian_peak_do_not, deep bowl, cave mouth, wilderness POI",
    phaseIds: ["mountain_wilderness_cave_mouth"],
  },
  {
    id: "appalachian-peak-bad-09",
    title: "DO NOT: skip seam blend during flat grid place (flatGridSkipSeamBlend)",
    filename: "appalachian-peak-bad-09-skip-seam-blend.png",
    category: "mountain_lidar",
    antiPattern: true,
    notes:
      "Placed peak/horizon tiles without BlendSharedEdge — dark lines and height discontinuity. Always seam on place.",
    topics: "appalachian_peak_do_not, skip seam blend, flat grid, place seam",
    phaseIds: ["mountain_wilderness_tiles"],
  },
  {
    id: "appalachian-peak-bad-10",
    title: "DO NOT: identical peak height on all 24 peak tiles",
    filename: "appalachian-peak-bad-10-uniform-peaks.png",
    category: "mountain_lidar",
    antiPattern: true,
    notes:
      "Cookie-cutter summits — vary CornerPeakRise/EdgePeakRise per tile seed for natural Appalachian irregularity.",
    topics: "appalachian_peak_do_not, uniform peaks, no variation, seed stable variety",
    phaseIds: ["mountain_peak_sculpt", "mountain_wilderness_tiles"],
  },
  // ── Foothills — GOOD ──
  {
    id: "foothill-good-01",
    title: "GOOD: foothill ring rises gently from play disk",
    filename: "foothill-good-01-gentle-rise.png",
    category: "terrain",
    notes:
      "Chebyshev 2 foothills: smooth 56m blend band from play edge — terrain slopes up and out, never dips inward.",
    topics: "foothill_terrain, gentle rise, play edge, chebyshev 2",
    phaseIds: ["mountain_foothill_sculpt", "mountain_wilderness_tiles"],
  },
  {
    id: "foothill-good-02",
    title: "GOOD: invisible play↔foothill stitch band",
    filename: "foothill-good-02-invisible-stitch.png",
    category: "terrain",
    notes:
      "5m inner seam: heights copied from play neighbor then soft lerp — player cannot see tile boundary.",
    topics: "foothill_terrain, play foothill stitch, invisible seam, blend band",
    phaseIds: ["mountain_foothill_sculpt", "mountain_wilderness_tiles"],
  },
  {
    id: "foothill-good-03",
    title: "GOOD: south annex foothill row for labyrinth walls",
    filename: "foothill-good-03-labyrinth-annex.png",
    category: "terrain",
    notes:
      "South 3×2 annex foothill tiles (y=-2) — raised wall bands outside wide corridor, floor at or above play height.",
    topics: "foothill_terrain, south annex, labyrinth walls, raised bench",
    phaseIds: ["mountain_labyrinth_carve", "mountain_foothill_sculpt"],
  },
  {
    id: "foothill-good-04",
    title: "GOOD: contour-following foothill benches",
    filename: "foothill-good-04-contour-bench.png",
    category: "terrain",
    notes:
      "Rolling Appalachian foothills with walkable contour benches — switchback-friendly slopes, not sheer faces.",
    topics: "foothill_terrain, contour bench, walkable slope, appalachian rolling",
    phaseIds: ["mountain_foothill_sculpt", "mountain_trails"],
  },
  {
    id: "foothill-good-05",
    title: "GOOD: foothill-to-peak transition without trough",
    filename: "foothill-good-05-peak-transition.png",
    category: "terrain",
    notes:
      "Outer foothill edge meets peak inner edge at matched heights — continuous slope into peak mass, no valley gutter.",
    topics: "foothill_terrain, peak transition, no trough, outer edge",
    phaseIds: ["mountain_foothill_sculpt", "mountain_peak_sculpt"],
  },
  {
    id: "foothill-good-06",
    title: "GOOD: denoised foothill macro shape",
    filename: "foothill-good-06-denoised-macro.png",
    category: "terrain",
    notes:
      "Low-frequency rolling hills after denoise pass — detail noise reserved for peak ring and rock dressing.",
    topics: "foothill_terrain, denoise, macro shape, rolling hills",
    phaseIds: ["mountain_foothill_sculpt"],
  },
  {
    id: "foothill-good-07",
    title: "GOOD: foothill outer edge blends toward peak ring",
    filename: "foothill-good-07-outer-blend.png",
    category: "terrain",
    notes:
      "Foothill Chebyshev 2 outer samples seed peak inner edge — stitch group includes both rings before peak sculpt.",
    topics: "foothill_terrain, outer blend, peak seed, stitch group",
    phaseIds: ["mountain_wilderness_tiles", "mountain_peak_sculpt"],
  },
  {
    id: "foothill-good-08",
    title: "GOOD: trail spokes from play disk across foothills",
    filename: "foothill-good-08-trail-spokes.png",
    category: "terrain",
    notes:
      "Wide trail benches from play south edge through foothill ring toward labyrinth center — max grade enforced.",
    topics: "foothill_terrain, trail spokes, play disk gate, max grade",
    phaseIds: ["mountain_trails", "mountain_labyrinth_carve"],
  },
  {
    id: "foothill-good-09",
    title: "GOOD: foothill tile grid aligned — neighbors connected",
    filename: "foothill-good-09-grid-aligned.png",
    category: "terrain",
    notes:
      "All 16 foothill tiles at correct XZ origins with SetNeighbors — no physical gaps between adjacent foothill tiles.",
    topics: "foothill_terrain, grid aligned, SetNeighbors, 16 tile ring",
    phaseIds: ["mountain_wilderness_tiles", "terrain_tiling"],
  },
  {
    id: "foothill-good-10",
    title: "GOOD: Florida field to Appalachian foothill ecotone",
    filename: "foothill-good-10-ecotone.png",
    category: "terrain",
    notes:
      "Visual transition from flat LiDAR play disk to rolling foothills — alphamap and height change together over 40–56m band.",
    topics: "foothill_terrain, ecotone, florida to appalachian, blend band",
    phaseIds: ["mountain_foothill_sculpt", "surface_lidar_stamp"],
  },
  // ── Foothills — DO NOT ──
  {
    id: "foothill-bad-01",
    title: "DO NOT: foothill inner edge lower than play disk (bowl)",
    filename: "foothill-bad-01-inner-bowl.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Foothill carved below play height at inner band — visible moat around play disk. Inner lock must Max(playNorm+ε, h).",
    topics: "foothill_do_not, inner bowl, below play, moat",
    phaseIds: ["mountain_foothill_sculpt"],
  },
  {
    id: "foothill-bad-02",
    title: "DO NOT: lowered labyrinth floor trench in south foothill",
    filename: "foothill-bad-02-labyrinth-trench.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "FlattenTrailBench dug corridor below terrain — use SculptLabyrinthWalkwayWithRaisedWalls (raise walls, not lower floor).",
    topics: "foothill_do_not, labyrinth trench, lowered floor, raised walls required",
    phaseIds: ["mountain_labyrinth_carve"],
  },
  {
    id: "foothill-bad-03",
    title: "DO NOT: thin jagged labyrinth lines on foothill",
    filename: "foothill-bad-03-jagged-maze.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Per-edge grid carve — pixel maze shards. Use BuildCarvePolylines spine batch, 8–12m half-width.",
    topics: "foothill_do_not, jagged maze, per edge grid, labyrinth_do_not",
    phaseIds: ["mountain_labyrinth_carve", "mountain_labyrinth_research"],
  },
  {
    id: "foothill-bad-04",
    title: "DO NOT: visible seam gutter between foothill tiles",
    filename: "foothill-bad-04-tile-gutter.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Adjacent foothill tiles not stitched — linear ditch at shared edge. Batch BlendSharedEdge after ring place.",
    topics: "foothill_do_not, tile gutter, unstitched edge, seam failure",
    phaseIds: ["mountain_wilderness_tiles", "mountain_foothill_sculpt"],
  },
  {
    id: "foothill-bad-05",
    title: "DO NOT: per-tile inner lock stall during horizon place",
    filename: "foothill-bad-05-place-stall.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "QueueLockSingleMountainWildernessInnerEdge on every flat-grid tile — freezes build at 81/81. Defer lock to outer ring seam pipeline.",
    topics: "foothill_do_not, place stall, inner lock per tile, pipeline freeze",
    phaseIds: ["mountain_wilderness_tiles"],
  },
  {
    id: "foothill-bad-06",
    title: "DO NOT: foothill ring before nine-tile lock",
    filename: "foothill-bad-06-before-lock.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Spawning/sculpting foothills before DEM nine-tile validates — flat plates on Grid. Lock play disk first.",
    topics: "foothill_do_not, before nine tile lock, pipeline order, fullworld_do_not",
    phaseIds: ["mountain_research_brief", "mountain_wilderness_tiles"],
  },
  {
    id: "foothill-bad-07",
    title: "DO NOT: dark noisy rectangular foothill block (failed carve)",
    filename: "foothill-bad-07-noisy-block.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Full-tile height noise rectangle from bad labyrinth pass — denoise and re-carve spine polylines only.",
    topics: "foothill_do_not, noisy block, failed carve, fullworld_do_not",
    phaseIds: ["mountain_labyrinth_carve", "mountain_foothill_sculpt"],
  },
  {
    id: "foothill-bad-08",
    title: "DO NOT: foothill peak rise on north play edge (wrong side)",
    filename: "foothill-bad-08-north-rise.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Mountains blocking primary portal approach on north — wilderness rise should frame disk, not wall portal north.",
    topics: "foothill_do_not, north rise, portal approach, layout ideal",
    phaseIds: ["mountain_foothill_sculpt", "mountain_research_brief"],
  },
  {
    id: "foothill-bad-09",
    title: "DO NOT: horizon ring at full peak height competing with play disk",
    filename: "foothill-bad-09-horizon-too-tall.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Chebyshev 4 horizon at 100% peak scale — blocks vista and reads as second wall. Use HorizonPeakRiseScale 0.62.",
    topics: "foothill_do_not, horizon too tall, chebyshev 4, vista block",
    phaseIds: ["mountain_wilderness_tiles"],
  },
  {
    id: "foothill-bad-10",
    title: "DO NOT: labyrinth confined to single foothill tile",
    filename: "foothill-bad-10-single-tile-maze.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Maze on one tile instead of full south 3×2 annex (6 terrains) — carve spine polylines on all six south annex tiles.",
    topics: "foothill_do_not, single tile maze, south annex, six terrains",
    phaseIds: ["mountain_labyrinth_carve", "mountain_labyrinth_research"],
  },
  {
    id: "foothill-bad-11",
    title: "DO NOT: star/checkerboard passage grid (BuildAnnexPassageRunPolylines)",
    filename: "foothill-bad-11-star-grid-labyrinth.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Dense orthogonal star blocks from rasterizing every passage cell — use wide bench spines only (movie Labyrinth style), not per-cell H+V runs.",
    topics: "foothill_do_not, star grid, BuildAnnexPassageRunPolylines, labyrinth_do_not, fullworld_do_not",
    phaseIds: ["mountain_labyrinth_carve", "mountain_labyrinth_research"],
  },
  {
    id: "foothill-bad-12",
    title: "DO NOT: foothill row sculpted as peak maze blocks (not rolling hills)",
    filename: "foothill-bad-12-foothill-as-maze-blocks.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Foothills must stay rolling Appalachian transition (mountain_foothill_sculpt). Labyrinth raised walls belong on south annex corridors only — not replacing foothill denoise.",
    topics: "foothill_do_not, rolling hills, appalachian, labyrinth on foothill ring, fullworld_do_not",
    phaseIds: ["mountain_foothill_sculpt", "mountain_labyrinth_carve"],
  },
  {
    id: "appalachian-peak-bad-11",
    title: "DO NOT: vertical pit cave mouth on ridgeline (wrong carve axis)",
    filename: "appalachian-peak-bad-11-vertical-pit-mouth.png",
    category: "mountain_lidar",
    antiPattern: true,
    notes:
      "CarveEntranceDepression / downward bowl on summit reads as pits going down. Use CarveCliffTunnelMouth with intoMountain toward massif + north cliff face slot.",
    topics: "appalachian_peak_do_not, vertical pit, cave mouth, CarveCliffTunnelMouth, fullworld_do_not",
    phaseIds: ["mountain_wilderness_cave_mouth"],
  },
  {
    id: "play-disk-bad-11",
    title: "DO NOT: radial star of cave openings on play disk",
    filename: "play-disk-bad-11-radial-star-openings.png",
    category: "terrain",
    antiPattern: true,
    notes:
      "Nine-tile radial openings when wilderness mouths enabled — skip play-disk bowl; mouths only on south peak annex north cliff faces.",
    topics: "play_disk_do_not, radial star, cave openings, UseMountainWildernessCaveMouth, fullworld_do_not",
    phaseIds: ["mountain_wilderness_cave_mouth", "mountain_labyrinth_carve"],
  },
];

function toVisualRef(spec: TerrainGenSpec): VisualReference {
  return {
    id: spec.id,
    title: spec.title,
    year: 2026,
    category: spec.category,
    provenInProduction: true,
    docUrl: KIT,
    imageUrls: [img(spec.filename)],
    notes: spec.notes,
    region: spec.topics.split(",")[0],
  };
}

function toResearchEntry(spec: TerrainGenSpec): ResearchEntry {
  return {
    lab: "Environment Authoring Kit",
    title: spec.title,
    year: 2026,
    venue: spec.antiPattern ? "Terrain generation DO NOT" : "Terrain generation reference",
    url: KIT,
    topics: spec.topics,
    provenInProduction: true,
    imageUrls: [img(spec.filename)],
    notes: `${spec.notes} Pipeline phases: ${spec.phaseIds.join(", ")}.`,
  };
}

export const TERRAIN_GENERATION_VISUAL_REFERENCES: VisualReference[] = SPECS.map(toVisualRef);

export const TERRAIN_GENERATION_RESEARCH_ENTRIES: ResearchEntry[] = SPECS.map(toResearchEntry);

export const PLAY_DISK_GOOD_REFS = SPECS.filter((s) => s.id.startsWith("play-disk-good"));
export const PLAY_DISK_DO_NOT_REFS = SPECS.filter((s) => s.id.startsWith("play-disk-bad"));
export const APPALACHIAN_PEAK_GOOD_REFS = SPECS.filter((s) => s.id.startsWith("appalachian-peak-good"));
export const APPALACHIAN_PEAK_DO_NOT_REFS = SPECS.filter((s) => s.id.startsWith("appalachian-peak-bad"));
export const FOOTHILL_GOOD_REFS = SPECS.filter((s) => s.id.startsWith("foothill-good"));
export const FOOTHILL_DO_NOT_REFS = SPECS.filter((s) => s.id.startsWith("foothill-bad"));
