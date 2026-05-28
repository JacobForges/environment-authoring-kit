import type { PromptRung } from "./prompt-ladder.js";
import {
  RESEARCH_MIN_YEAR,
  LAB_INDEX_URLS,
  PRESTIGE_LAB_PAPERS,
  UNITY6_ENGINE_REFS,
  type ResearchEntry,
  papersForMinYear,
} from "./research-catalog.js";

/** Keep agent prompts small — full catalog lives in CaveBuildResearch.json on disk. */
export const PROMPT_BUDGET = {
  maxPapersInPrompt: 5,
  maxLabIndicesInPrompt: 3,
  maxSearchQueries: 6,
  maxEngineRefsInPrompt: 2,
} as const;

/** Which ladder rungs each lab index is most relevant for (for prompt subset). */
const LAB_RUNG_AFFINITY: Record<string, PromptRung[]> = {
  "EA SEED": ["floor_collision", "other", "navmesh"],
  "Microsoft Research": ["other", "visual_shell", "navmesh"],
  "NVIDIA Research": ["visual_shell", "performance", "other"],
  "Ubisoft La Forge": ["visual_shell", "materials"],
  "Sony Interactive Entertainment": ["ground_placement", "floor_collision"],
  "Sony AI": ["navmesh", "other"],
  "Activision Research": ["floor_collision", "other", "performance"],
  "Unity Technologies": ["navmesh", "floor_collision", "materials"],
  "Epic Games / GPUOpen": ["visual_shell", "performance"],
  "Google DeepMind": ["other", "navmesh"],
  "Riot Games Technology": ["performance", "navmesh", "other"],
  "Bungie": ["performance", "visual_shell"],
  "Meta AI": ["other", "visual_shell"],
  "Blizzard / Activision": ["other", "performance"],
  "King (Activision Blizzard)": ["other"],
  "IEEE CoG": ["visual_shell", "navmesh", "other"],
  FDG: ["visual_shell", "ground_placement", "other"],
  "ACM / arXiv": ["visual_shell", "other"],
};

const RUNG_KEYWORDS: Record<PromptRung, string[]> = {
  research: ["pcg", "procedural", "game", "research", "lab", "arxiv", "fdg"],
  compile_gate: ["compile", "csharp", "unity", "editor", "error", "diagnostic"],
  visual_shell: ["pcg", "mesh", "world", "environment", "geometry", "shell", "layer", "procedural", "3d-generalist", "cosmos", "hdpcg", "electric dreams", "gpu work"],
  ground_placement: ["placement", "root", "terrain", "underground", "depth", "alignment", "streaming"],
  floor_collision: ["collision", "spawn", "walkable", "controller", "meshcollider", "playtesting", "qa", "automating gameplay"],
  navmesh: ["navmesh", "navigation", "bake", "agent", "walkable"],
  materials: ["pbr", "material", "texture", "chord", "holo-gen", "rendering"],
  performance: ["performance", "optimization", "triangle", "gpu", "batching", "nitrogen", "cpu"],
  other: ["benchmark", "rl", "agent", "quality", "testing"],
};

/** Surface terrain ladder rungs (grade-and-fix --workflow=terrain). */
const TERRAIN_RUNG_KEYWORDS: Record<string, string[]> = {
  terrain_research: ["terrain", "dem", "lidar", "hillshade", "florida", "usgs", "research"],
  heightfield_no_craters: ["terrain", "heightmap", "dem", "crater", "bowl", "erosion", "lidar", "bare earth"],
  playable_slopes: ["slope", "grade", "terrain", "walkable", "trail", "smooth"],
  trail_walkability: ["trail", "path", "walk", "route", "navmesh", "spline", "grade"],
  surface_navmesh: ["navmesh", "navigation", "terrain", "bake", "walkable"],
  prop_trees: ["vegetation", "forest", "tree", "placement", "scatter", "ecology"],
  prop_grass: ["grass", "meadow", "vegetation", "ground cover", "terrain"],
  prop_bushes: ["bush", "shrub", "vegetation", "understory"],
  prop_ground_cover: ["flower", "ground cover", "vegetation", "prop"],
  surface_playtest: ["playtest", "integration", "water", "entrance", "validation"],
  cave_mouth_grounding: ["cave", "mouth", "entrance", "alignment", "terrain", "opening", "descent"],
};

/** Map terrain rung ids to cave ladder rungs for lab/cache affinity. */
export function researchAliasesForRung(rung: string): PromptRung[] {
  const terrain: Record<string, PromptRung[]> = {
    terrain_research: ["research", "ground_placement"],
    heightfield_no_craters: ["ground_placement"],
    playable_slopes: ["ground_placement", "performance"],
    trail_walkability: ["navmesh", "ground_placement"],
    surface_navmesh: ["navmesh"],
    prop_trees: ["materials", "visual_shell"],
    prop_grass: ["materials"],
    prop_bushes: ["materials"],
    prop_ground_cover: ["materials"],
    surface_playtest: ["floor_collision", "visual_shell"],
    cave_mouth_grounding: ["ground_placement", "floor_collision"],
  };
  if (terrain[rung]) return terrain[rung];
  if (rung in RUNG_KEYWORDS) return [rung as PromptRung];
  if (rung.startsWith("prop_")) return ["materials"];
  return ["ground_placement", "other"];
}

export function keywordsForRung(rung: string): string[] {
  if (TERRAIN_RUNG_KEYWORDS[rung]) return TERRAIN_RUNG_KEYWORDS[rung];
  if (rung in RUNG_KEYWORDS) return RUNG_KEYWORDS[rung as PromptRung];
  if (rung.startsWith("prop_"))
    return ["vegetation", "grass", "tree", "forest", "placement", "terrain"];
  return RUNG_KEYWORDS.other;
}

function paperMatchesRung(p: ResearchEntry, rung: string): boolean {
  const blob = `${p.title} ${p.topics} ${p.venue}`.toLowerCase();
  if (keywordsForRung(rung).some((k) => blob.includes(k))) return true;
  return researchAliasesForRung(rung).some((alias) =>
    (RUNG_KEYWORDS[alias] ?? []).some((k) => blob.includes(k))
  );
}

function scorePaperForRung(p: ResearchEntry, rung: string): number {
  let score = p.year >= RESEARCH_MIN_YEAR ? 10 : 5;
  if (paperMatchesRung(p, rung)) score += 20;
  const aliases = new Set(researchAliasesForRung(rung));
  const labs = LAB_RUNG_AFFINITY[p.lab];
  if (labs?.some((r) => aliases.has(r))) score += 15;
  if (p.pdfUrl) score += 3;
  return score;
}

export function pickPapersForPrompt(
  rung: string,
  meatLoopPass: number
): ResearchEntry[] {
  const pool = papersForMinYear()
    .map((p) => ({ p, score: scorePaperForRung(p, rung) }))
    .sort((a, b) => b.score - a.score || b.p.year - a.p.year);

  const offset =
    Math.max(0, meatLoopPass) % Math.max(1, pool.length - PROMPT_BUDGET.maxPapersInPrompt + 1);
  return pool
    .slice(offset, offset + PROMPT_BUDGET.maxPapersInPrompt)
    .map((x) => x.p);
}

export function pickLabIndicesForPrompt(rung: string): { lab: string; url: string }[] {
  const aliases = new Set(researchAliasesForRung(rung));
  const entries = Object.entries(LAB_INDEX_URLS)
    .map(([lab, url]) => ({
      lab,
      url,
      rank: LAB_RUNG_AFFINITY[lab]?.some((r) => aliases.has(r)) ? 0 : 1,
    }))
    .sort((a, b) => a.rank - b.rank || a.lab.localeCompare(b.lab));

  return entries.slice(0, PROMPT_BUDGET.maxLabIndicesInPrompt).map(({ lab, url }) => ({ lab, url }));
}

export function pickEngineRefsForPrompt(rung: string): ResearchEntry[] {
  const aliases = researchAliasesForRung(rung);
  const wantsEngine = aliases.some((r) =>
    ["navmesh", "floor_collision", "materials", "visual_shell"].includes(r)
  );
  if (!wantsEngine && !rung.includes("nav") && !rung.includes("trail")) {
    return [];
  }
  return UNITY6_ENGINE_REFS.slice(0, PROMPT_BUDGET.maxEngineRefsInPrompt);
}

export function allLabNames(): string[] {
  return Object.keys(LAB_INDEX_URLS).sort();
}
