#!/usr/bin/env npx tsx
/**
 * Per-phase Hollow Titan research action plan (URLs + steps + do-not rules).
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import {
  HOLLOW_TITAN_ACTIVE_PHASE_PROMPT_REL,
  HOLLOW_TITAN_PHASE_PROMPT_MANIFEST_REL,
  HOLLOW_TITAN_RESEARCH_ACTION_PLAN_REL,
  HOLLOW_TITAN_RESEARCH_AGENT_PROMPT_REL,
  HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_JSON_REL,
} from "./hollow-titan-agent-artifact-paths.js";
import { HOLLOW_TITAN_DO_NOT_PROMPT_BULLETS } from "./hollow-titan-do-not-papers.js";
import {
  getHollowTitanPhasePrompt,
  HOLLOW_TITAN_MASTER_IMAGE_REL,
} from "./hollow-titan-concept-prompts.js";
import { researchCategoriesForPhase } from "./hollow-titan-research-prompt.js";
import { lookupForCategories } from "./research-store.js";
import type { ResearchCategory } from "./research-store.js";
import { resolveHubRoot } from "./hub-root.js";

type CacheEntry = {
  id: string;
  title?: string;
  url?: string;
  sourceUrl?: string;
  category?: string;
  contentPath?: string;
};

function parsePhaseIndex(argv: string[]): number {
  for (const a of argv) {
    if (a.startsWith("--phase=")) {
      const n = parseInt(a.slice("--phase=".length), 10);
      if (!Number.isNaN(n) && n >= 0 && n <= 11) return n;
    }
  }
  const fromEnv = process.env.HOLLOW_TITAN_ACTIVE_PHASE?.trim();
  if (fromEnv) {
    const n = parseInt(fromEnv, 10);
    if (!Number.isNaN(n) && n >= 0 && n <= 11) return n;
  }
  return 0;
}

function parseSeed(): number {
  const fromEnv = process.env.HOLLOW_TITAN_BUILD_SEED?.trim();
  if (fromEnv) {
    const n = parseInt(fromEnv, 10);
    if (!Number.isNaN(n)) return n;
  }
  return 0;
}

function researchUrlsForPhase(
  hubRoot: string,
  phaseIndex: number,
  limit = 10
): { id: string; url: string; localMd: string; category: string }[] {
  const phaseCats = researchCategoriesForPhase(phaseIndex) as ResearchCategory[];
  const prioritized: ResearchCategory[] = ["hollow_titan", "hollow_titan_do_not"];
  const orderedCats = [
    ...prioritized,
    ...phaseCats.filter((c) => !prioritized.includes(c)),
  ];

  const out: { id: string; url: string; localMd: string; category: string }[] = [];
  const seen = new Set<string>();

  for (const cat of orderedCats) {
    const lookup = lookupForCategories(hubRoot, [cat], limit);
    if (!lookup) continue;
    for (const e of lookup.hits) {
      if (seen.has(e.id)) continue;
      seen.add(e.id);
      const url =
        (e as CacheEntry & { url?: string }).url ??
        (e as CacheEntry & { sourceUrl?: string }).sourceUrl;
      if (!url) continue;
      const localMd = e.contentPath
        ? `${hubRoot}/${e.contentPath.replace(/^\//, "")}`
        : `${hubRoot}/Assets/EnvironmentKit/ResearchCache/entries/${e.id}/content.md`;
      out.push({
        id: e.id,
        url,
        localMd,
        category: (e as CacheEntry & { category?: string }).category ?? cat,
      });
      if (out.length >= limit) return out;
    }
  }
  return out;
}

function phaseDoNotRules(phaseIndex: number): string[] {
  const phase = getHollowTitanPhasePrompt(phaseIndex);
  const common = [
    ...HOLLOW_TITAN_DO_NOT_PROMPT_BULLETS,
    "Do not skip HollowTitanResearchExecutionBrief.json before C# edits.",
  ];

  const perPhase: Record<number, string[]> = {
    0: ["Do not pick a tile inside the nine-tile play disk center if it blocks karst readability."],
    2: [
      "Do not use CC0 L01 stack as primary trunk when LPMagicalForest prefabs are available.",
      "Do not omit BossStagePortal child on trunk shell.",
    ],
    3: ["Do not leave HollowVolume collider enabled — void must be walkable marker only."],
    6: [
      "Do not block main entrance arch with dead branch scatter.",
      "Do not use white CC0 limb proxies when LP Branch prefabs load.",
    ],
    7: [
      "Do not use cube arch when Log_Hollow_01 prefab is available.",
      "Do not orient entrance away from FloorPlan.EntranceForward.",
    ],
    8: ["Do not mix landmark spawns with SurfaceWorld enemy scatter tables."],
    10: ["Do not write world pickup loot IDs into landmark spawn manifest."],
  };

  return [
    ...common,
    ...(perPhase[phaseIndex] ?? []),
    `Phase focus: ${phase.label} — ${phase.promptCore.slice(0, 120)}…`,
  ];
}

function buildPlan(hubRoot: string, phaseIndex: number, seed: number) {
  const hub = hubRoot.replace(/\/$/, "");
  const phase = getHollowTitanPhasePrompt(phaseIndex);
  const urls = researchUrlsForPhase(hub, phaseIndex);

  const planSteps = [
    {
      order: 1,
      action: "Open master + phase concept images; read silhouette and scale cues.",
      imagePaths: [
        `${hub}/${HOLLOW_TITAN_MASTER_IMAGE_REL}`,
        `${hub}/${phase.conceptImageRel}`,
      ],
    },
    {
      order: 2,
      action: "Read HollowTitanResearchExecutionBrief.json — mandatory read order + cache entry ids.",
      jsonPaths: [HOLLOW_TITAN_RESEARCH_EXECUTION_BRIEF_JSON_REL],
    },
    {
      order: 3,
      action: "Read hollow_titan + hollow_titan_do_not ResearchCache entries (content.md) before editing C#.",
      researchEntryIds: urls.map((u) => u.id),
    },
    {
      order: 4,
      action: "Read consolidated research + active phase prompt.",
      jsonPaths: [
        HOLLOW_TITAN_RESEARCH_AGENT_PROMPT_REL,
        HOLLOW_TITAN_ACTIVE_PHASE_PROMPT_REL,
        HOLLOW_TITAN_PHASE_PROMPT_MANIFEST_REL,
      ],
    },
    {
      order: 5,
      action: `Apply minimal kit fix for phase ${phaseIndex} (${phase.id}) only.`,
      builderNotes: phase.builderNotes,
      csharpTypes: phase.builderNotes
        .filter((n) => n.includes(".") || n.includes("HollowTitan"))
        .slice(0, 6),
    },
    {
      order: 6,
      action: "Re-run Hollow Titan meat phase in Unity; verify live scene + spawn manifest.",
      manifestJson: "Assets/EnvironmentKit/Generated/HollowTitanLandmarkSpawnManifest.json",
    },
  ];

  return {
    generatedUtc: new Date().toISOString(),
    landmarkId: "HollowTitanLandmark",
    phaseIndex,
    phaseId: phase.id,
    phaseLabel: phase.label,
    buildSeed: seed,
    conceptImageRel: phase.conceptImageRel,
    masterConceptImageRel: HOLLOW_TITAN_MASTER_IMAGE_REL,
    researchCategories: researchCategoriesForPhase(phaseIndex),
    workflow: ["concept_images", "research_urls", "write_plan", "execute_phase", "verify_scene"],
    suggestedNextActions: [
      `Complete ${phase.label}: ${phase.promptCore}`,
      "Open HollowTitanActivePhasePrompt.md before any C# edit.",
      phaseIndex === 2 || phaseIndex === 6 || phaseIndex === 7
        ? "Verify LPMagicalForest prefabs in BiomePropCatalog.LpMagicalForestRoot — CC0 is fallback only."
        : "Honor buildSeed jitter for scatter offsets and floor radii.",
    ],
    doNot: phaseDoNotRules(phaseIndex),
    researchUrlsToOpen: urls,
    planSteps,
    webSearchQueries: [
      `medieval hollow dead tree landmark game environment ${phase.id.replace(/_/g, " ")}`,
      "LPMagicalForest stylized tree trunk modular placement game art",
    ],
  };
}

function main() {
  const hubRoot = resolveHubRoot();
  const hub = hubRoot.replace(/\/$/, "");
  const phaseIndex = parsePhaseIndex(process.argv);
  const seed = parseSeed();
  const gen = join(hub, "Assets/EnvironmentKit/Generated");
  mkdirSync(gen, { recursive: true });

  const plan = buildPlan(hub, phaseIndex, seed);
  writeFileSync(
    join(hub, HOLLOW_TITAN_RESEARCH_ACTION_PLAN_REL),
    JSON.stringify(plan, null, 2),
    "utf8"
  );

  console.log(
    `[CaveCursor:info] Hollow Titan research action plan: phase ${phaseIndex} (${plan.phaseId}), ${plan.researchUrlsToOpen.length} URLs`
  );
}

main();
