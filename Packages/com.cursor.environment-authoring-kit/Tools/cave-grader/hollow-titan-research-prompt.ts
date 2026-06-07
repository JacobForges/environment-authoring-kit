/**
 * Consolidated Hollow Titan research prompt — concept images + ResearchCache categories.
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import {
  HOLLOW_TITAN_RESEARCH_AGENT_PROMPT_JSON_REL,
  HOLLOW_TITAN_RESEARCH_AGENT_PROMPT_REL,
  HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_JSON_REL,
  HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_MD_REL,
} from "./hollow-titan-agent-artifact-paths.js";
import { HOLLOW_TITAN_DO_NOT_PROMPT_BULLETS } from "./hollow-titan-do-not-papers.js";
import { HOLLOW_TITAN_RESEARCH_PROMPT_BULLETS } from "./hollow-titan-research-papers.js";
import {
  HOLLOW_TITAN_MASTER_IMAGE_REL,
  HOLLOW_TITAN_PHASE_PROMPTS,
  HOLLOW_TITAN_SCALE_BLOCK,
  getHollowTitanPhasePrompt,
} from "./hollow-titan-concept-prompts.js";
import { formatResearchCacheBlockForCategories } from "./research-cache-prompt.js";
import type { ResearchCategory } from "./research-store.js";
import { lookupForCategories } from "./research-store.js";
import type { CacheEntry } from "./research-store.js";

export const HOLLOW_TITAN_FEATHER_RULES = [
  "Landmark-only: one Hollow Titan per FullWorld seed on a single surface peak tile.",
  "Never place on cave mouths, labyrinth annex benches, or trail corridor tiles.",
  "Ground snap via SampleHeight + base clearance; re-snap after terrain sculpt.",
  "Exterior dressing feathers LP tree rings by height band — hero trunk center, oak/beech curtain, spruce/pine canopy.",
  "Spawn tables are landmark-scoped — never world scatter enemy/loot tables.",
  "Similar mystical hollow-tree silhouette per buildSeed — not a pixel clone of concept.png.",
].join("\n");

const HT = "hollow_titan" as const;
const HT_DO_NOT = "hollow_titan_do_not" as const;

/** Global categories for consolidated research prompt — hollow_titan first. */
export const HOLLOW_TITAN_GLOBAL_RESEARCH_CATEGORIES: ResearchCategory[] = [
  HT,
  HT_DO_NOT,
  "fullworld_do_not",
  "fullworld_layout_ideal",
  "surface_props",
  "visual_reference",
  "ground_placement",
];

/** Per meat-phase (0–11) ResearchCache categories — always hollow_titan + hollow_titan_do_not. */
export const HOLLOW_TITAN_PHASE_RESEARCH_CATEGORIES: Record<number, ResearchCategory[]> = {
  0: [HT, HT_DO_NOT, "fullworld_do_not", "terrain", "ground_placement", "mountain_terrain"],
  1: [HT, HT_DO_NOT, "terrain", "ground_placement", "engine_docs"],
  2: [HT, HT_DO_NOT, "surface_props", "visual_reference", "materials_lighting"],
  3: [HT, HT_DO_NOT, "mesh_shell", "visual_reference"],
  4: [HT, HT_DO_NOT, "floor_collision", "adventure"],
  5: [HT, HT_DO_NOT, "adventure", "visual_reference"],
  6: [HT, HT_DO_NOT, "surface_props", "visual_reference"],
  7: [HT, HT_DO_NOT, "visual_reference", "adventure"],
  8: [HT, HT_DO_NOT, "adventure", "qa_testing"],
  9: [HT, HT_DO_NOT, "materials_lighting", "engine_docs"],
  10: [HT, HT_DO_NOT, "adventure", "qa_testing"],
  11: [HT, HT_DO_NOT, "adventure", "qa_testing"],
};

export function researchCategoriesForPhase(phaseIndex: number): ResearchCategory[] {
  return (
    HOLLOW_TITAN_PHASE_RESEARCH_CATEGORIES[phaseIndex] ?? HOLLOW_TITAN_GLOBAL_RESEARCH_CATEGORIES
  );
}

function snippetFromContent(hubRoot: string, entry: CacheEntry, maxChars = 240): string {
  const path = join(hubRoot, entry.contentPath);
  if (!existsSync(path)) return entry.serialized.summary.slice(0, maxChars);
  try {
    const text = readFileSync(path, "utf8");
    const summaryIdx = text.indexOf("## Summary");
    const body = summaryIdx >= 0 ? text.slice(summaryIdx) : text;
    return body.replace(/\s+/g, " ").trim().slice(0, maxChars);
  } catch {
    return entry.serialized.summary.slice(0, maxChars);
  }
}

export function buildHollowTitanResearchSummaries(
  hubRoot: string,
  phaseIndex?: number,
  limit = 10
): { id: string; category: string; contentPath: string; snippet: string }[] {
  const cats =
    phaseIndex !== undefined
      ? researchCategoriesForPhase(phaseIndex)
      : HOLLOW_TITAN_GLOBAL_RESEARCH_CATEGORIES;
  const lookup = lookupForCategories(hubRoot, cats, limit);
  if (!lookup) return [];
  return lookup.hits.map((e) => ({
    id: e.id,
    category: e.category ?? "",
    contentPath: e.contentPath,
    snippet: snippetFromContent(hubRoot, e, 240),
  }));
}

export function formatHollowTitanConceptImagesBlock(hubRoot: string, phaseIndex?: number): string {
  const hub = hubRoot.replace(/\/$/, "");
  const lines: string[] = [
    "## Hollow Titan concept images (mandatory — open before C#)",
    "",
    `- **Master:** \`${hub}/${HOLLOW_TITAN_MASTER_IMAGE_REL}\`${existsSync(join(hub, HOLLOW_TITAN_MASTER_IMAGE_REL)) ? "" : " _(missing — run generate-hollow-titan-concept-images.py)_"}`,
    "",
    "### Per-phase concept guides",
    "",
  ];

  const phases =
    phaseIndex !== undefined
      ? [getHollowTitanPhasePrompt(phaseIndex)]
      : HOLLOW_TITAN_PHASE_PROMPTS;

  for (const p of phases) {
    const exists = existsSync(join(hub, p.conceptImageRel));
    lines.push(
      `- **Phase ${p.index}** \`${p.id}\`: \`${hub}/${p.conceptImageRel}\`${exists ? "" : " _(missing)_"}`
    );
  }
  lines.push("");
  return lines.join("\n");
}

export function buildHollowTitanResearchPromptMd(
  hubRoot: string,
  phaseIndex?: number
): string {
  const hub = hubRoot.replace(/\/$/, "");
  const phase =
    phaseIndex !== undefined ? getHollowTitanPhasePrompt(phaseIndex) : undefined;
  const cats =
    phaseIndex !== undefined
      ? researchCategoriesForPhase(phaseIndex)
      : HOLLOW_TITAN_GLOBAL_RESEARCH_CATEGORIES;

  const lines: string[] = [
    "# Hollow Titan — consolidated research agent prompt",
    "",
    `**Generated:** ${new Date().toISOString()}`,
    phase
      ? `**Active phase:** ${phase.index} — \`${phase.id}\` (${phase.label})`
      : "**Scope:** all 12 meat phases (0–11)",
    "",
    "## Execution brief (open first)",
    "",
    `- **JSON:** \`${hub}/${HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_JSON_REL}\``,
    `- **MD:** \`${hub}/${HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_MD_REL}\``,
    `- **Workflow:** \`Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/prompt-ladder/hollow-titan-research-workflow.md\``,
    "",
    formatHollowTitanConceptImagesBlock(hub, phaseIndex),
    "## Scale + placement rules",
    "",
    HOLLOW_TITAN_SCALE_BLOCK,
    "",
    "## Feather + landmark rules",
    "",
    HOLLOW_TITAN_FEATHER_RULES,
    "",
    formatResearchCacheBlockForCategories([HT, HT_DO_NOT], hub, 12),
    "",
    "## Phase-specific research",
    "",
    formatResearchCacheBlockForCategories(cats, hub, 8),
    "",
    "## Hollow Titan research bullets",
    "",
    ...HOLLOW_TITAN_RESEARCH_PROMPT_BULLETS.map((b) => `- ${b}`),
    "",
    "## Hollow Titan DO NOT",
    "",
    ...HOLLOW_TITAN_DO_NOT_PROMPT_BULLETS.map((b) => `- ${b}`),
    "",
    "## Global FullWorld guardrails",
    "",
    formatResearchCacheBlockForCategories(["fullworld_do_not", "fullworld_layout_ideal"], hub, 6),
    "",
    "## Research summaries (read content.md on disk)",
    "",
  ];

  const summaries = buildHollowTitanResearchSummaries(hub, phaseIndex, 12);
  if (!summaries.length) {
    lines.push("_No cache entries — run `npm run sync-research-pull`._", "");
  } else {
    for (const r of summaries) {
      lines.push(
        `- **${r.id}** (\`${r.category}\`) — \`${hub}/${r.contentPath}\``,
        `  - ${r.snippet}`
      );
    }
    lines.push("");
  }

  lines.push(
    "## Required generated artifacts",
    "",
    `- \`${hub}/${HOLLOW_TITAN_RESEARCH_AGENT_PROMPT_REL}\` — this file`,
    `- \`${hub}/Assets/EnvironmentKit/Generated/HollowTitanPhasePromptManifest.json\``,
    `- \`${hub}/Assets/EnvironmentKit/Generated/HollowTitanActivePhasePrompt.md\` — active meat-phase task`,
    `- \`${hub}/Assets/EnvironmentKit/Generated/HollowTitanResearchActionPlan.json\``,
    `- \`${hub}/${HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_JSON_REL}\``,
    `- \`${hub}/Assets/EnvironmentKit/ResearchCache/categories/hollow_titan/index.json\``,
    `- \`${hub}/Assets/EnvironmentKit/ResearchCache/index.json\``,
    "",
    "## Hyper-real exterior (current implementation)",
    "",
    "Phases 2 (TrunkShell), 6 (DeadBranchScatter), 7 (EntranceFraming) use **LPMagicalForest** prefabs via `HollowTitanExteriorDressing.cs`.",
    "Hero trunk: `Tree_Mammoth_Green_01`; curtain rings: oak/beech/poplar; canopy: spruce/pine.",
    "Dead branches: `Branch_01`–`Branch_04`, `Log_Branched_01`. Entrance: `Log_Hollow_01`.",
    "Base dress: mossy rocks, fern, vines. CC0 L01–L10 stack is **fallback only** when LP assets are missing.",
    ""
  );

  return lines.join("\n");
}

export function buildHollowTitanResearchPromptJson(hubRoot: string, phaseIndex?: number) {
  const hub = hubRoot.replace(/\/$/, "");
  const phase =
    phaseIndex !== undefined ? getHollowTitanPhasePrompt(phaseIndex) : undefined;
  return {
    generatedUtc: new Date().toISOString(),
    phaseIndex: phaseIndex ?? -1,
    phaseId: phase?.id ?? "hollow_titan_all_phases",
    masterConceptImageRel: HOLLOW_TITAN_MASTER_IMAGE_REL,
    conceptImageRels: HOLLOW_TITAN_PHASE_PROMPTS.map((p) => p.conceptImageRel),
    researchCategories:
      phaseIndex !== undefined
        ? researchCategoriesForPhase(phaseIndex)
        : HOLLOW_TITAN_GLOBAL_RESEARCH_CATEGORIES,
    summaries: buildHollowTitanResearchSummaries(hub, phaseIndex, 12),
    markdownPath: HOLLOW_TITAN_RESEARCH_AGENT_PROMPT_REL,
    executionBriefJsonRel: HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_JSON_REL,
    executionBriefMdRel: HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_MD_REL,
    policy:
      "Open HollowTitanResearchExecutionBrief + concept images + hollow_titan content.md before C# edits; LPMagicalForest primary for exterior phases.",
  };
}

export function writeHollowTitanResearchPrompt(
  hubRoot: string,
  phaseIndex?: number
): { mdRel: string; jsonRel: string } {
  const hub = hubRoot.replace(/\/$/, "");
  const gen = join(hub, "Assets/EnvironmentKit/Generated");
  mkdirSync(gen, { recursive: true });

  const md = buildHollowTitanResearchPromptMd(hub, phaseIndex);
  const json = buildHollowTitanResearchPromptJson(hub, phaseIndex);

  writeFileSync(join(hub, HOLLOW_TITAN_RESEARCH_AGENT_PROMPT_REL), md, "utf8");
  writeFileSync(
    join(hub, HOLLOW_TITAN_RESEARCH_AGENT_PROMPT_JSON_REL),
    JSON.stringify(json, null, 2),
    "utf8"
  );

  return { mdRel: HOLLOW_TITAN_RESEARCH_AGENT_PROMPT_REL, jsonRel: HOLLOW_TITAN_RESEARCH_AGENT_PROMPT_JSON_REL };
}
