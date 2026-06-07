#!/usr/bin/env node
/**
 * Grades research used for the active build situation; re-syncs cache when inaccurate or stale.
 *
 * Usage:
 *   node --import tsx research-situation-grader.ts [--phase=mountain_labyrinth_carve] [--repair]
 */
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { execSync } from "node:child_process";
import {
  loadActiveGenerationStyleId,
  loadActiveConceptImageRel,
  isIdealLayoutStyle,
  ACTIVE_GENERATION_STYLE_REL,
} from "./active-generation-style.js";
import { loadIndex, lookupForCategories, type ResearchCategory } from "./research-store.js";
import { resolveHubRoot } from "./hub-root.js";

const hubRoot = resolveHubRoot();
const gen = join(hubRoot, "Assets/EnvironmentKit/Generated");
const RESEARCH_MIN_YEAR = 2025;

type GradeReport = {
  generatedUtc: string;
  phaseId: string;
  styleId: string;
  passed: boolean;
  score: number;
  issues: string[];
  repairs: string[];
  entriesChecked: number;
};

function parseArg(name: string): string | undefined {
  const prefix = `--${name}=`;
  for (const a of process.argv.slice(2)) {
    if (a.startsWith(prefix)) return a.slice(prefix.length);
  }
  return undefined;
}

function categoriesForSituation(styleId: string, phaseId: string): ResearchCategory[] {
  const base: ResearchCategory[] = [
    "fullworld_do_not",
    "fullworld_layout_ideal",
    "fullworld_generation_style",
    "terrain",
    "ground_placement",
  ];
  if (
    styleId === "ideal_layout_49" ||
    styleId === "concept_00_ideal_labyrinth"
  ) {
    base.push("mountain_labyrinth", "mountain_terrain", "mountain_lidar", "labyrinth_do_not");
  }
  if (phaseId.includes("mountain")) {
    base.push("mountain_terrain", "mountain_lidar", "mountain_labyrinth", "terrain_tiling");
  }
  if (phaseId.includes("labyrinth")) base.push("labyrinth_do_not", "mountain_labyrinth");
  if (phaseId.includes("cave") || phaseId.includes("meat")) {
    base.push("mesh_shell", "adventure", "floor_collision", "visual_reference");
  }
  return [...new Set(base)];
}

function gradeEntry(
  e: {
    id: string;
    title?: string;
    url?: string;
    year?: number;
    category?: string;
    serialized?: { summary?: string };
    contentPath?: string;
  },
  hub: string
): string[] {
  const issues: string[] = [];
  if (!e.url || !e.url.startsWith("http")) issues.push(`${e.id}: missing valid source URL`);
  if ((e.year ?? 0) < RESEARCH_MIN_YEAR) issues.push(`${e.id}: year ${e.year} below ${RESEARCH_MIN_YEAR}`);
  if (!e.serialized?.summary || e.serialized.summary.length < 12)
    issues.push(`${e.id}: summary too short`);
  if (e.contentPath) {
    const md = join(hub, e.contentPath);
    if (!existsSync(md)) issues.push(`${e.id}: content.md missing on disk`);
    else {
      const text = readFileSync(md, "utf8");
      if (!text.includes("## Agent guidance")) issues.push(`${e.id}: content.md lacks Agent guidance section`);
      if (!text.includes("## Source")) issues.push(`${e.id}: content.md lacks Source section`);
    }
  }
  return issues;
}

function runRepair(categories: ResearchCategory[]): string[] {
  const repairs: string[] = [];
  const tools = join(hubRoot, "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader");
  try {
    execSync("npm run sync-research-cache", { cwd: tools, stdio: "pipe" });
    repairs.push("Ran sync-research-cache (refreshed entries + guidance sections).");
  } catch (e) {
    repairs.push(`sync-research-cache failed: ${String(e)}`);
  }
  try {
    execSync(
      `node --import tsx export-research-execution-brief.ts --rung=ground_placement`,
      { cwd: tools, stdio: "pipe" }
    );
    repairs.push("Refreshed TerrainResearchExecutionBrief.json.");
  } catch {
    repairs.push("Skipped execution brief refresh (non-fatal).");
  }
  return repairs;
}

function main() {
  const phaseId = parseArg("phase") ?? "surface_build";
  const repair = process.argv.includes("--repair");
  const styleId = loadActiveGenerationStyleId(hubRoot);
  const cats = categoriesForSituation(styleId, phaseId);
  const lookup = lookupForCategories(hubRoot, cats, 64);
  const index = lookup?.index ?? loadIndex(hubRoot);

  const issues: string[] = [];
  let checked = 0;
  for (const e of lookup?.hits ?? []) {
    checked++;
    issues.push(...gradeEntry(e, hubRoot));
  }

  if (isIdealLayoutStyle(hubRoot)) {
    const concept = join(hubRoot, loadActiveConceptImageRel(hubRoot));
    if (!existsSync(concept))
      issues.push("concept 0: guide concept.png missing — Generate FullWorld Concept Images (0–9).");
  }

  const styleEntry = index?.entries
    ? Object.values(index.entries).find((x) => x.topics?.includes(`preset_${styleId}`))
    : undefined;
  if (!styleEntry && !styleId.startsWith("concept_") && styleId !== "classic_fullworld") {
    issues.push(`fullworld_generation_style: no preset paper for styleId=${styleId}`);
  }

  const score = Math.max(0, 100 - issues.length * 8);
  const passed = issues.length === 0;
  const repairs: string[] = [];

  if (!passed && repair) repairs.push(...runRepair(cats));

  const report: GradeReport = {
    generatedUtc: new Date().toISOString(),
    phaseId,
    styleId,
    passed,
    score,
    issues,
    repairs,
    entriesChecked: checked,
  };

  writeFileSync(join(gen, "ResearchSituationGrade.json"), JSON.stringify(report, null, 2), "utf8");
  console.log(
    `[ResearchGrader] ${passed ? "PASS" : "FAIL"} score=${score} checked=${checked} issues=${issues.length}`
  );
  for (const i of issues.slice(0, 12)) console.log(`  - ${i}`);
  if (issues.length > 12) console.log(`  … +${issues.length - 12} more`);
}

main();
