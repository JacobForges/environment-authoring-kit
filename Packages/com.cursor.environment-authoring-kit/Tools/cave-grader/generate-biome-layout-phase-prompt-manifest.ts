/**
 * Export BiomeLayoutPhasePromptManifest.json for agents + Unity surface prop pass.
 * Run: HUB_ROOT=/Users/jacob/Hub node --import tsx generate-biome-layout-phase-prompt-manifest.ts
 */
import { existsSync, mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import {
  BIOME_LAYOUT_FEATHER_RULES,
  BIOME_LAYOUT_MASTER_IMAGE_REL,
  BIOME_LAYOUT_PRESET_PROMPTS,
  BIOME_LAYOUT_SCALE_BLOCK,
  formatBiomeLayoutPresetPromptBlock,
} from "./biome-layout-concept-prompts.js";

const hub = (process.env.HUB_ROOT ?? "/Users/jacob/Hub").replace(/\/$/, "");
const genDir = join(hub, "Assets/EnvironmentKit/Generated");
const outPath = join(genDir, "BiomeLayoutPhasePromptManifest.json");

mkdirSync(genDir, { recursive: true });

const manifest = {
  generatedUtc: new Date().toISOString(),
  presetCount: BIOME_LAYOUT_PRESET_PROMPTS.length,
  masterConceptImageRel: BIOME_LAYOUT_MASTER_IMAGE_REL,
  masterConceptExists: existsSync(join(hub, BIOME_LAYOUT_MASTER_IMAGE_REL)),
  scaleBlock: BIOME_LAYOUT_SCALE_BLOCK,
  featherRulesBlock: BIOME_LAYOUT_FEATHER_RULES,
  presets: BIOME_LAYOUT_PRESET_PROMPTS.map((p) => ({
    index: p.index,
    id: p.id,
    label: p.label,
    dominantBiomes: p.dominantBiomes,
    propSetSummary: p.propSetSummary,
    promptCore: p.promptCore,
    ringLayout: p.ringLayout,
    builderNotes: p.builderNotes,
    conceptImageRel: p.conceptImageRel,
    conceptImageExists: existsSync(join(hub, p.conceptImageRel)),
    fullWorldConceptImageRel: p.fullWorldConceptImageRel,
    fullWorldConceptExists: existsSync(join(hub, p.fullWorldConceptImageRel)),
    promptBlock: formatBiomeLayoutPresetPromptBlock(p.index, hub),
  })),
};

writeFileSync(outPath, JSON.stringify(manifest, null, 2), "utf8");
console.log("wrote", outPath);
