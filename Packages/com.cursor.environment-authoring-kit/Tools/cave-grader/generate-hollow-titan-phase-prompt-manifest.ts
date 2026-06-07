/**
 * Export HollowTitanPhasePromptManifest.json for agents + Unity meat phases.
 * Run: HUB_ROOT=/Users/jacob/Hub node --import tsx generate-hollow-titan-phase-prompt-manifest.ts
 */
import { existsSync, mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import {
  HOLLOW_TITAN_MASTER_IMAGE_REL,
  HOLLOW_TITAN_PHASE_PROMPTS,
  HOLLOW_TITAN_SCALE_BLOCK,
} from "./hollow-titan-concept-prompts.js";

const hub = (process.env.HUB_ROOT ?? "/Users/jacob/Hub").replace(/\/$/, "");
const genDir = join(hub, "Assets/EnvironmentKit/Generated");
const outPath = join(genDir, "HollowTitanPhasePromptManifest.json");

mkdirSync(genDir, { recursive: true });

const manifest = {
  generatedUtc: new Date().toISOString(),
  landmarkId: "HollowTitanLandmark",
  phaseCount: HOLLOW_TITAN_PHASE_PROMPTS.length,
  masterConceptImageRel: HOLLOW_TITAN_MASTER_IMAGE_REL,
  masterConceptExists: existsSync(join(hub, HOLLOW_TITAN_MASTER_IMAGE_REL)),
  scaleBlock: HOLLOW_TITAN_SCALE_BLOCK,
  similarityRule:
    "Same mystical hollow-tree silhouette family per buildSeed — floor radii, branch scatter, and spawn offsets vary by seed.",
  phases: HOLLOW_TITAN_PHASE_PROMPTS.map((p) => ({
    index: p.index,
    id: p.id,
    label: p.label,
    promptCore: p.promptCore,
    builderNotes: p.builderNotes,
    conceptImageRel: p.conceptImageRel,
    conceptImageExists: existsSync(join(hub, p.conceptImageRel)),
    promptBlock: [
      `Master: ${HOLLOW_TITAN_MASTER_IMAGE_REL}`,
      `Phase: ${p.conceptImageRel}`,
      "",
      p.promptCore,
      "",
      ...p.builderNotes.map((n) => `- ${n}`),
    ].join("\n"),
  })),
};

writeFileSync(outPath, JSON.stringify(manifest, null, 2), "utf8");
console.log("wrote", outPath);
