/**
 * Save approved planner web research into ResearchCache.
 * Usage: npx tsx save-planner-research.ts <hubRoot> <bundle.json>
 */
import { readFileSync, writeFileSync, existsSync } from "node:fs";
import { join } from "node:path";
import {
  type CacheEntry,
  type ResearchCategory,
  type SourceType,
  RESEARCH_CACHE_INDEX_REL,
  RESEARCH_CACHE_REL,
  writeEntryFiles,
  buildIndexFromEntries,
  writeCategoryIndexes,
} from "./research-store.js";

const hubRoot = process.argv[2];
const bundlePath = process.argv[3];
if (!hubRoot || !bundlePath) {
  console.error("Usage: tsx save-planner-research.ts <hubRoot> <bundle.json>");
  process.exit(1);
}

type PlannerItem = {
  id: string;
  title: string;
  url: string;
  category?: string;
  summary?: string;
  sourceType?: string;
  topics?: string;
  year?: number;
};

const bundle = JSON.parse(readFileSync(bundlePath, "utf8")) as { items?: PlannerItem[] };
const items = bundle.items ?? [];
const now = new Date().toISOString();
const entries: CacheEntry[] = [];

for (const item of items) {
  const id = item.id || `planner-${Date.now()}`;
  const category = (item.category || "fullworld_generation_style") as ResearchCategory;
  const sourceType = (item.sourceType || "visual_ref") as SourceType;
  const metaPath = `${RESEARCH_CACHE_REL}/entries/${id}/meta.json`;
  const contentPath = `${RESEARCH_CACHE_REL}/entries/${id}/content.md`;
  entries.push({
    id,
    category,
    sourceType,
    rungs: ["research", "terrain_integration"],
    title: item.title,
    year: item.year ?? new Date().getFullYear(),
    url: item.url,
    topics: item.topics ?? "planner_session, web_research",
    provenInProduction: false,
    serialized: {
      fetchedUtc: now,
      fetchSkipped: false,
      summary: item.summary ?? `Planner-approved web research: ${item.title}`,
      keyPoints: [
        "Approved via AI Build Planner internet research step",
        `Source: ${item.url}`,
      ],
      implementationNotes:
        "Use as art-direction and layout guidance for this build session only.",
    },
    metaPath,
    contentPath,
  });
}

for (const entry of entries) {
  writeEntryFiles(hubRoot, entry);
}

const indexPath = join(hubRoot, RESEARCH_CACHE_INDEX_REL);
let existing: CacheEntry[] = [];
if (existsSync(indexPath)) {
  try {
    const idx = JSON.parse(readFileSync(indexPath, "utf8")) as { entries?: CacheEntry[] };
    existing = idx.entries ?? [];
  } catch {
    existing = [];
  }
}
const byId = new Map<string, CacheEntry>();
for (const e of existing) byId.set(e.id, e);
for (const e of entries) byId.set(e.id, e);
const merged = [...byId.values()];
const index = buildIndexFromEntries(merged, hubRoot);
writeFileSync(indexPath, JSON.stringify(index, null, 2) + "\n", "utf8");
writeCategoryIndexes(hubRoot, index);

const pointer = join(hubRoot, "Assets/EnvironmentKit/Generated/CaveBuildResearchCache.json");
writeFileSync(
  pointer,
  JSON.stringify(
    {
      version: 1,
      syncedUtc: now,
      entryCount: merged.length,
      plannerSaved: entries.length,
      indexRel: RESEARCH_CACHE_INDEX_REL,
    },
    null,
    2
  ) + "\n",
  "utf8"
);

console.log(`[planner] Saved ${entries.length} research entries to ResearchCache`);
