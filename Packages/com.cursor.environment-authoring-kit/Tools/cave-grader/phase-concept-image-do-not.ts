/**
 * Concept-image anti-patterns from Set 02 grading — wired into prompts + generation matrix.
 * Rejected PNGs live under ResearchCache/images/concepts/phases/_do-not/
 */
export type ConceptImageDoNot = {
  tag: string;
  title: string;
  /** Hub-relative PNG when archived from grading */
  imageRel?: string;
  /** Short rule for image generation prompts */
  generationAvoid: string;
  /** Folders this applies to (empty = all) */
  folders: string[];
};

export const PHASE_CONCEPT_IMAGE_DO_NOT: ConceptImageDoNot[] = [
  {
    tag: "game_view_not_scene",
    title: "DO NOT: Game view / Play Mode capture",
    generationAvoid: "Must be Unity Editor Scene tab with Hierarchy and Inspector — never Play Mode runtime.",
    folders: [],
  },
  {
    tag: "flat_tan_slabs",
    title: "DO NOT: flat tan play disk or beige labyrinth shelf",
    imageRel: "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_do-not/flat_tan_slab_play_disk.png",
    generationAvoid: "No uniform tan/straw flats; foothill labyrinth benches stay on rolling green heightmap.",
    folders: ["01_play_disk", "03_south_foothill_labyrinth_row", "07_mountain_trails"],
  },
  {
    tag: "crop_circles",
    title: "DO NOT: crop-circle / concentric terrace disks",
    generationAvoid: "No concentric rings, geoglyph swirls, or circular plateau play disks.",
    folders: ["00_fullworld_overview", "02_foothill_ring_sculpt", "03_south_foothill_labyrinth_row"],
  },
  {
    tag: "spline_chaos_152",
    title: "DO NOT: 100+ BuildCarvePolylines splines (edge-grid chaos)",
    imageRel: "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_do-not/spline_chaos_152_spines.png",
    generationAvoid: "Show 3–5 spine gizmos only — never dense orange spaghetti or console 'Total splines built: 152'.",
    folders: ["06_mountain_labyrinth_carve", "03_south_foothill_labyrinth_row"],
  },
  {
    tag: "circular_hedge_maze",
    title: "DO NOT: Chartres circular hedge maze on hilltop",
    generationAvoid: "No flat circular hedge/paver maze — outdoor bench carve on south annex only.",
    folders: ["06_mountain_labyrinth_carve"],
  },
  {
    tag: "schematic_diagram",
    title: "DO NOT: labeled 2D layout diagram without Terrain",
    generationAvoid: "No color-block schematics, sprite Mouth_South circles, or Play/Foothills/Peaks legend boards.",
    folders: ["00_fullworld_overview", "07_mountain_trails", "09_cave_primary_mouth"],
  },
  {
    tag: "miniature_scale",
    title: "DO NOT: diorama / hex tokens under 100 m",
    generationAvoid: "220 m terrain tiles must feel full scale; include 220 m status bar or Inspector terrain size.",
    folders: ["08_wilderness_cave_mouth"],
  },
  {
    tag: "wrong_category",
    title: "DO NOT: wrong pipeline phase content in image",
    generationAvoid: "Match folder topic only — no peak-ring shot under wilderness, no trail demo under labyrinth carve.",
    folders: [],
  },
  {
    tag: "user_rejected_set03",
    title: "DO NOT: user-rejected gallery picks (Set 03)",
    imageRel:
      "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_do-not/user_rejected/u2-01-04.png",
    generationAvoid:
      "User removed u2-01-04, u2-02-02, u2-02-04, u2-03-03, u2-04-01 from combined gallery #9,12,14,18,21 — do not recreate these compositions.",
    folders: [
      "01_play_disk",
      "02_foothill_ring_sculpt",
      "03_south_foothill_labyrinth_row",
      "04_peak_ring",
    ],
  },
];

export function formatConceptDoNotBlock(folders: string[], hubRoot: string): string {
  const hub = hubRoot.replace(/\/$/, "");
  const relevant = PHASE_CONCEPT_IMAGE_DO_NOT.filter(
    (d) => d.folders.length === 0 || d.folders.some((f) => folders.includes(f))
  );
  if (!relevant.length) return "";

  const lines = ["**Concept image DO NOT (Set 02 rejects):**", ""];
  for (const d of relevant.slice(0, 6)) {
    lines.push(`- **${d.title}** — ${d.generationAvoid}`);
    if (d.imageRel) lines.push(`  - See: \`${hub}/${d.imageRel}\``);
  }
  lines.push("");
  return lines.join("\n");
}
