/**
 * One research entry per Hub FullWorld concept (0–9) — category fullworld_generation_style.
 */
import type { ResearchEntry } from "./research-catalog.js";
import {
  FULLWORLD_CONCEPT_PRESET_PROMPTS,
  GRID_SCALE_BLOCK,
} from "./fullworld-concept-grid-vocabulary.js";

const C = "fullworld_generation_style";

function concept(
  id: string,
  title: string,
  notes: string,
  topics: string
): ResearchEntry {
  return {
    lab: "Environment Authoring Kit",
    title: `Concept: ${title}`,
    year: 2026,
    venue: "Hub concept picker",
    url: `https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_LAYOUT_IDEAL.md#hub-concept-${id}`,
    topics: `${C}, ${topics}, preset_${id}, open_world_289`,
    provenInProduction: true,
    notes: `${notes}\n\n${GRID_SCALE_BLOCK}`,
  };
}

export const FULLWORLD_GENERATION_STYLE_PAPERS: ResearchEntry[] =
  FULLWORLD_CONCEPT_PRESET_PROMPTS.map((p) =>
    concept(p.id, p.title, `${p.layoutFocus} — ${p.promptCore}`, p.id.replace("concept_", ""))
  );
