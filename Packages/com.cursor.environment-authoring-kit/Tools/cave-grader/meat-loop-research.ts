/**
 * Research focus per meat-loop pass (aligned with CaveBuildMeatLoopPassPlan in Unity).
 */
export type MeatPassResearch = {
  title: string;
  researchFocus: string;
  categories: string[];
  entryIdPrefixes: string[];
  surfaceTask?: string;
  webSearchQueries?: string[];
};

export const MEAT_PASS_RESEARCH: MeatPassResearch[] = [
  { title: "Shell + geometry", researchFocus: "visual_shell, geometry_integrity", categories: ["visual_reference", "terrain"], entryIdPrefixes: ["ieee-", "unity6-terrain", "fl-usgs-3dep"] },
  { title: "Surface LiDAR + smooth", researchFocus: "hillshade stamp, terrain smooth", categories: ["ground_placement", "terrain"], entryIdPrefixes: ["fl-", "unity6-terrain-heightmap"], surfaceTask: "lidar_refine_smooth", webSearchQueries: ["Florida USGS 3DEP hillshade game terrain", "Unity terrain heightmap LiDAR stamp"] },
  {
    title: "Walkways + moving platforms",
    researchFocus: "moving platforms, gap pacing, walk colliders",
    categories: ["ground_placement", "visual_reference"],
    entryIdPrefixes: ["ieee-", "unity6-"],
    webSearchQueries: [
      "action adventure cave moving platform pacing design",
      "Unity moving platform CharacterController parenting",
    ],
  },
  {
    title: "Block tunnel + layout",
    researchFocus: "layout, block tunnel, platform spacing",
    categories: ["terrain", "visual_reference"],
    entryIdPrefixes: ["ieee-", "fl-fgs"],
    webSearchQueries: ["procedural cave layout platform jump gap commercial game"],
  },
  { title: "Surface vegetation (planned)", researchFocus: "satellite biomes, tree bush flower", categories: ["visual_reference", "terrain"], entryIdPrefixes: ["ieee-cog", "fl-"], surfaceTask: "intelligent_vegetation_trees", webSearchQueries: ["open world tree placement satellite", "AAA forest edge game environment"] },
  { title: "Lighting pass", researchFocus: "URP lighting", categories: ["visual_reference"], entryIdPrefixes: ["unity6-", "ieee-"] },
  {
    title: "Fog layout + surface roads",
    researchFocus: "fog zones open sky vs cave mist, road/water LiDAR",
    categories: ["terrain", "ground_placement", "visual_reference"],
    entryIdPrefixes: ["fl-", "fl-usgs-3dep", "unity6-"],
    surfaceTask: "roads_water_lidar",
    webSearchQueries: [
      "Unity URP fog underground only open sky surface",
      "LiDAR hydrography game terrain water trail road",
    ],
  },
  { title: "Enemies + mobs", researchFocus: "mob spawn, combat", categories: ["visual_reference"], entryIdPrefixes: ["ieee-", "fl-fgs-subsidence"] },
  { title: "Surface NavMesh + polish", researchFocus: "surface NavMesh, trails", categories: ["ground_placement"], entryIdPrefixes: ["unity6-"], surfaceTask: "surface_navmesh_polish", webSearchQueries: ["Unity NavMesh terrain trails"] },
  { title: "Materials refresh", researchFocus: "URP materials", categories: ["visual_reference"], entryIdPrefixes: ["unity6-terrain-paint"] },
  { title: "Performance trim", researchFocus: "XR budget, culling", categories: ["visual_reference"], entryIdPrefixes: ["ieee-", "unity6-"] },
  { title: "Enclosure + ribs", researchFocus: "occlusion shell", categories: ["terrain"], entryIdPrefixes: ["fl-", "ieee-"] },
  { title: "Surface flora pass 2", researchFocus: "flowers, bushes, shoreline", categories: ["visual_reference"], entryIdPrefixes: ["ieee-cog"], surfaceTask: "intelligent_vegetation_understory", webSearchQueries: ["understory bush flower game placement", "Florida scrub plants game art"] },
  { title: "Audio ambience", researchFocus: "ambient audio", categories: ["visual_reference"], entryIdPrefixes: ["ieee-"] },
  { title: "Playable world gate", researchFocus: "playable world, mouth, ship", categories: ["ground_placement", "terrain"], entryIdPrefixes: ["fl-", "unity6-terrain"], surfaceTask: "playable_world_gate", webSearchQueries: ["playable open world environment checklist"] },
  { title: "Final polish", researchFocus: "commercial production, LiDAR touch-up", categories: ["visual_reference", "terrain"], entryIdPrefixes: ["fl-aquifer", "ieee-", "unity6-"], surfaceTask: "lidar_touchup_final" },
];

export function getMeatPassResearch(pass: number): MeatPassResearch {
  if (pass < 0) return MEAT_PASS_RESEARCH[0];
  return MEAT_PASS_RESEARCH[pass % MEAT_PASS_RESEARCH.length];
}

export function parseMeatPassArg(argv: string[]): number | undefined {
  for (const arg of argv) {
    if (arg.startsWith("--meat-pass=")) {
      const n = parseInt(arg.slice("--meat-pass=".length), 10);
      return Number.isFinite(n) ? n : undefined;
    }
  }
  const env = process.env.CAVE_MEAT_PASS?.trim();
  if (env) {
    const n = parseInt(env, 10);
    if (Number.isFinite(n)) return n;
  }
  return undefined;
}
