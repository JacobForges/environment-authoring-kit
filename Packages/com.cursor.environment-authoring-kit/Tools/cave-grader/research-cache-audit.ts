#!/usr/bin/env node
/**
 * Research cache audit + cleanup:
 * - dedupe by canonical URL
 * - prune stale entry/image folders not referenced by index
 * - flag likely low-quality / speculative / stale references
 * - rewrite category indexes and root stats
 *
 * Optional daemon mode:
 *   node --import tsx research-cache-audit.ts --daemon --interval-min=30
 */
import {
  existsSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  renameSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { join } from "node:path";
import { resolveHubRoot } from "./hub-root.js";
const RESEARCH_MIN_YEAR = 2025;

const RESEARCH_CACHE_REL = "Assets/EnvironmentKit/ResearchCache";
const RESEARCH_CACHE_INDEX_REL = `${RESEARCH_CACHE_REL}/index.json`;
const RESEARCH_CACHE_GENERATED_REL =
  "Assets/EnvironmentKit/Generated/CaveBuildResearchCache.json";

type CacheEntry = {
  id: string;
  category: string;
  sourceType: string;
  rungs: string[];
  title: string;
  lab?: string;
  year: number;
  venue?: string;
  url: string;
  pdfUrl?: string;
  topics: string;
  provenInProduction: boolean;
  imageUrls?: string[];
  localImages?: { bytes?: number }[];
  serialized: {
    fetchedUtc: string;
    fetchSkipped: boolean;
    summary: string;
    keyPoints: string[];
    implementationNotes?: string;
  };
  metaPath: string;
  contentPath: string;
  imagesManifestPath?: string;
};

type CategoryBlock = {
  id: string;
  label: string;
  entryIds: string[];
};

type ResearchCacheIndex = {
  version: number;
  generatedUtc: string;
  policy: string;
  hubRelativeRoot: string;
  categories: Record<string, CategoryBlock>;
  entries: Record<string, CacheEntry>;
  floridaTerrain?: unknown;
  stats: Record<string, number>;
};

type DuplicateGroup = {
  canonicalUrl: string;
  keptId: string;
  removedIds: string[];
};

type QualityFlag = {
  id: string;
  severity: "warn" | "error";
  reason: string;
};

type AuditResult = {
  generatedUtc: string;
  indexPath: string;
  strictPolicyAutoDelete: boolean;
  removedDuplicateEntries: number;
  removedPolicyEntries: number;
  removedStaleEntryDirs: number;
  removedStaleImageDirs: number;
  duplicateGroups: DuplicateGroup[];
  qualityFlags: QualityFlag[];
  policyRemovedIds: string[];
  suggestedAddOns: string[];
  experimentalIdeas: string[];
  keptEntries: number;
};

const SUSPICIOUS_KEYWORDS = [
  "ideation",
  "poker",
  "monetization",
  "social intelligence",
  "world model",
];

const args = new Set(process.argv.slice(2));
const daemon = args.has("--daemon");
const dryRun = args.has("--dry-run");
const removeMode = args.has("--delete") ? "delete" : "archive";
const strictPolicyAutoDelete = !args.has("--no-policy-delete");
const intervalMinArg = process.argv.find((a) => a.startsWith("--interval-min="));
const intervalMin = Math.max(5, Number.parseInt(intervalMinArg?.split("=")[1] ?? "30", 10) || 30);

const hubRoot = resolveHubRoot();
const paths = {
  root: join(hubRoot, RESEARCH_CACHE_REL),
  index: join(hubRoot, RESEARCH_CACHE_INDEX_REL),
  entries: join(hubRoot, RESEARCH_CACHE_REL, "entries"),
  images: join(hubRoot, RESEARCH_CACHE_REL, "images"),
  categories: join(hubRoot, RESEARCH_CACHE_REL, "categories"),
};
const archiveRoot = join(paths.root, "_archive");
const auditJsonPath = join(hubRoot, "Assets/EnvironmentKit/Generated/ResearchCacheAuditReport.json");
const auditMdPath = join(hubRoot, "Assets/EnvironmentKit/Generated/ResearchCacheAuditReport.md");

function nowStamp(): string {
  return new Date().toISOString().replace(/[:.]/g, "-");
}

function canonicalizeUrl(url: string): string {
  try {
    const u = new URL(url.trim());
    u.hash = "";
    const dropParams = [
      "utm_source",
      "utm_medium",
      "utm_campaign",
      "utm_term",
      "utm_content",
      "fbclid",
      "gclid",
    ];
    for (const p of dropParams) u.searchParams.delete(p);
    const qp = [...u.searchParams.entries()].sort(([a], [b]) => a.localeCompare(b));
    u.search = "";
    for (const [k, v] of qp) u.searchParams.append(k, v);
    return u.toString().replace(/\/$/, "").toLowerCase();
  } catch {
    return url.trim().replace(/\/$/, "").toLowerCase();
  }
}

function loadIndexOrThrow(): ResearchCacheIndex {
  if (!existsSync(paths.index)) {
    throw new Error(
      `Missing ${RESEARCH_CACHE_INDEX_REL}. Run: npm run sync-research-cache`
    );
  }
  return JSON.parse(readFileSync(paths.index, "utf8")) as ResearchCacheIndex;
}

function scoreEntry(e: CacheEntry): number {
  let score = 0;
  if (e.provenInProduction) score += 100;
  if (e.sourceType === "engine_doc") score += 40;
  if ((e.localImages?.length ?? 0) > 0) score += 20;
  if (e.year >= RESEARCH_MIN_YEAR) score += 10;
  score += Math.max(0, (e.rungs?.length ?? 0) * 2);
  score += Math.max(0, Math.min(6, e.title.length / 20));
  return score;
}

function pickCanonical(entries: CacheEntry[]): CacheEntry {
  return [...entries].sort((a, b) => {
    const ds = scoreEntry(b) - scoreEntry(a);
    if (ds !== 0) return ds;
    const ys = b.year - a.year;
    if (ys !== 0) return ys;
    return a.id.localeCompare(b.id);
  })[0];
}

function dedupeByUrl(entries: CacheEntry[]): { kept: CacheEntry[]; groups: DuplicateGroup[] } {
  const byUrl = new Map<string, CacheEntry[]>();
  for (const e of entries) {
    const key = canonicalizeUrl(e.url);
    const bucket = byUrl.get(key) ?? [];
    bucket.push(e);
    byUrl.set(key, bucket);
  }

  const keep = new Map<string, CacheEntry>();
  const groups: DuplicateGroup[] = [];
  for (const [canonicalUrl, bucket] of byUrl.entries()) {
    if (bucket.length === 1) {
      keep.set(bucket[0].id, bucket[0]);
      continue;
    }

    const primary = pickCanonical(bucket);
    keep.set(primary.id, primary);
    groups.push({
      canonicalUrl,
      keptId: primary.id,
      removedIds: bucket.filter((e) => e.id !== primary.id).map((e) => e.id),
    });
  }

  return { kept: [...keep.values()], groups };
}

function isResearchyUrl(url: string): boolean {
  const host = (() => {
    try {
      return new URL(url).hostname.toLowerCase();
    } catch {
      return "";
    }
  })();

  return (
    host.includes("arxiv.org") ||
    host.includes("ieee.org") ||
    host.includes("acm.org") ||
    host.includes("unity.com") ||
    host.includes("docs.unity3d.com") ||
    host.includes("gpuopen.com") ||
    host.includes("microsoft.com") ||
    host.includes("nvidia.com") ||
    host.includes("ubisoft.com") ||
    host.includes("ubisoft-laforge.github.io") ||
    host.includes("ea.com") ||
    host.includes("sony") ||
    host.includes("activision.com") ||
    host.includes("deepmind.google") ||
    host.includes("technology.riotgames.com") ||
    host.includes("riotgames.com") ||
    host.includes("bungie.net") ||
    host.includes("dev.epicgames.com") ||
    host.includes("sidefx.com") ||
    host.includes("unity-research.github.io") ||
    host.includes("unity-technologies.github.io") ||
    host.includes("usgs.gov") ||
    host.includes("noaa.gov") ||
    host.includes("floridadep.gov") ||
    host.includes("dep.state.fl.us") ||
    host.includes("nwfwmd.state.fl.us")
  );
}

function isTerrainCriticalEntry(e: CacheEntry): boolean {
  const blob = `${e.category} ${e.id} ${e.title} ${e.topics}`.toLowerCase();
  return [
    "terrain",
    "lidar",
    "dem",
    "heightmap",
    "hillshade",
    "navmesh",
    "georef",
    "florida",
    "karst",
    "hydro",
  ].some((k) => blob.includes(k));
}

function hasSpeculativeKeywords(e: CacheEntry): boolean {
  const blob = `${e.title} ${e.topics} ${e.venue ?? ""}`.toLowerCase();
  return SUSPICIOUS_KEYWORDS.some((s) => blob.includes(s));
}

function collectQualityFlags(entries: CacheEntry[]): QualityFlag[] {
  const flags: QualityFlag[] = [];

  for (const e of entries) {
    if (e.year < RESEARCH_MIN_YEAR && e.sourceType !== "engine_doc") {
      flags.push({
        id: e.id,
        severity: "warn",
        reason: `Year ${e.year} below policy floor ${RESEARCH_MIN_YEAR}`,
      });
    }

    if (!isResearchyUrl(e.url)) {
      flags.push({
        id: e.id,
        severity: isTerrainCriticalEntry(e) ? "error" : "warn",
        reason: `Unexpected host for research URL: ${e.url}`,
      });
    }

    if (hasSpeculativeKeywords(e) && e.provenInProduction) {
      flags.push({
        id: e.id,
        severity: isTerrainCriticalEntry(e) ? "error" : "warn",
        reason: "Entry marked proven but contains speculative-topic keywords",
      });
    }
  }

  return flags;
}

function shouldDropByPolicy(e: CacheEntry): boolean {
  // Option A catalog: classic GDC/AAA streaming refs intentionally pre-2025.
  if (e.topics.includes("open_world_streaming")) return false;
  if (e.sourceType !== "engine_doc" && e.year < RESEARCH_MIN_YEAR) return true;
  if (isTerrainCriticalEntry(e) && !isResearchyUrl(e.url)) return true;
  if (isTerrainCriticalEntry(e) && e.provenInProduction && hasSpeculativeKeywords(e)) return true;
  return false;
}

function suggestAddOns(result: {
  removedPolicyEntries: number;
  removedDuplicateEntries: number;
  qualityFlags: number;
  keptEntries: number;
}): string[] {
  const list = [
    "Auto-quarantine entries from unknown domains before they can enter prompts.",
    "Per-domain trust scores that decay if links repeatedly fail or redirect.",
    "Citation freshness check that auto-opens tickets for stale high-impact entries.",
    "Cross-file consistency check for `meta.json` vs `content.md` mismatches.",
    "URL health scanner (HTTP status + redirect chain) with automatic demotion.",
    "Duplicate-image perceptual hash dedupe across `images/*/ref-*.png`.",
    "Rung-coverage scoring so each rung keeps minimum validated sources.",
    "Policy drift alert if year-floor violations spike between runs.",
    "Known-bad keyword classifier (marketing/speculative content) with auto-prune.",
    "Weighted quality score per entry combining source, recency, and usage.",
    "Ground-truth allowlist for federal/state geodata portals used in terrain.",
    "Auto-generate changelog diff for every audit run (added/removed/promoted).",
    "Failure replay pack: persist samples of removed entries for manual review.",
    "Prompt-budget optimizer: pick highest-quality sources under token constraints.",
    "Anomaly detector on audit metrics (duplicates, bad-host count, stale spikes).",
  ];

  // Keep exactly 15, but tweak ordering by observed problems.
  if (result.removedPolicyEntries > 0) {
    list.unshift("Hard-delete legacy (pre-policy-year) entries before prompt generation.");
  }
  if (result.removedDuplicateEntries > 0) {
    list.unshift("Pre-index canonical URL merge to prevent duplicate entry folder creation.");
  }
  if (result.qualityFlags > result.keptEntries * 0.4) {
    list.unshift("Escalate to strict mode when quality-flag density exceeds 40%.");
  }
  return list.slice(0, 15);
}

function buildExperimentalIdeas(entries: CacheEntry[], flags: QualityFlag[]): string[] {
  const ideas: string[] = [];
  const byCategory = new Map<string, number>();
  for (const e of entries) byCategory.set(e.category, (byCategory.get(e.category) ?? 0) + 1);
  const topCat = [...byCategory.entries()].sort((a, b) => b[1] - a[1])[0];
  if (topCat) {
    ideas.push(
      `Experimental: train a small heuristic recommender from kept-entry metadata to suggest underrepresented categories versus dominant '${topCat[0]}' (${topCat[1]} entries).`
    );
  }

  const badHostCount = flags.filter((f) => f.reason.startsWith("Unexpected host")).length;
  if (badHostCount > 0) {
    ideas.push(
      `Experimental: infer a dynamic host allowlist by observing hosts that survive 5 consecutive audits; auto-demote outliers (${badHostCount} currently flagged).`
    );
  }

  const speculativeCount = flags.filter((f) => f.reason.includes("speculative-topic")).length;
  if (speculativeCount > 0) {
    ideas.push(
      `Experimental: lightweight text classifier on title/topics to predict speculative risk and suggest replacement sources (${speculativeCount} speculative-risk flags).`
    );
  }

  ideas.push(
    "Experimental: generate candidate new research links per rung from retained high-quality domains, then stage them in quarantine for human approval."
  );
  ideas.push(
    "Experimental: trend model over audit history to predict which rungs will run out of clean references first."
  );
  return ideas.slice(0, 5);
}

function safeRemove(path: string): void {
  if (!existsSync(path)) return;
  rmSync(path, { recursive: true, force: true });
}

function archiveOrDelete(path: string, reason: string): void {
  if (!existsSync(path)) return;
  if (dryRun) return;

  if (removeMode === "delete") {
    safeRemove(path);
    return;
  }

  mkdirSync(archiveRoot, { recursive: true });
  const target = join(
    archiveRoot,
    `${nowStamp()}-${reason}-${path.split("/").pop() ?? "item"}`
  );
  renameSync(path, target);
}

function writeAuditReports(result: AuditResult): void {
  mkdirSync(join(hubRoot, "Assets/EnvironmentKit/Generated"), { recursive: true });
  writeFileSync(auditJsonPath, JSON.stringify(result, null, 2), "utf8");

  const md = [
    "# Research cache audit report",
    "",
    `- Generated: ${result.generatedUtc}`,
    `- Index: ${result.indexPath}`,
    `- Strict policy auto-delete: ${result.strictPolicyAutoDelete}`,
    `- Kept entries: ${result.keptEntries}`,
    `- Removed duplicate entries: ${result.removedDuplicateEntries}`,
    `- Removed policy entries: ${result.removedPolicyEntries}`,
    `- Removed stale entry dirs: ${result.removedStaleEntryDirs}`,
    `- Removed stale image dirs: ${result.removedStaleImageDirs}`,
    "",
    "## Duplicate groups",
    ...(result.duplicateGroups.length
      ? result.duplicateGroups.flatMap((g) => [
          `- URL: ${g.canonicalUrl}`,
          `  - kept: ${g.keptId}`,
          ...g.removedIds.map((id) => `  - removed: ${id}`),
        ])
      : ["- none"]),
    "",
    "## Quality flags",
    ...(result.qualityFlags.length
      ? result.qualityFlags.map((f) => `- [${f.severity}] ${f.id}: ${f.reason}`)
      : ["- none"]),
    "",
    "## Policy removed IDs",
    ...(result.policyRemovedIds.length
      ? result.policyRemovedIds.map((id) => `- ${id}`)
      : ["- none"]),
    "",
    "## Suggested logic add-ons (15)",
    ...result.suggestedAddOns.map((x, i) => `${i + 1}. ${x}`),
    "",
    "## Experimental data-driven ideas",
    ...result.experimentalIdeas.map((x) => `- ${x}`),
    "",
  ].join("\n");

  writeFileSync(auditMdPath, md, "utf8");
}

function rebuildIndexFromKept(
  base: ResearchCacheIndex,
  kept: CacheEntry[]
): ResearchCacheIndex {
  const nextEntries: Record<string, CacheEntry> = {};
  const categories: Record<string, CategoryBlock> = {};

  for (const [id, block] of Object.entries(base.categories)) {
    categories[id] = { id, label: block.label, entryIds: [] };
  }

  for (const entry of kept) {
    nextEntries[entry.id] = entry;
    if (!categories[entry.category]) {
      categories[entry.category] = {
        id: entry.category,
        label: entry.category.replace(/_/g, " "),
        entryIds: [],
      };
    }
    categories[entry.category].entryIds.push(entry.id);
  }

  for (const block of Object.values(categories)) block.entryIds.sort();

  const withLocalImages = kept.filter((e) => (e.localImages?.length ?? 0) > 0).length;
  const withFetchedBytes = kept.filter((e) =>
    (e.localImages ?? []).some((i) => (i.bytes ?? 0) > 0)
  ).length;

  return {
    ...base,
    generatedUtc: new Date().toISOString(),
    categories,
    entries: nextEntries,
    stats: {
      ...base.stats,
      totalEntries: kept.length,
      provenEntries: kept.filter((e) => e.provenInProduction).length,
      withLocalImages,
      withFetchedBytes,
    },
  };
}

function writeCategoryIndexes(index: ResearchCacheIndex): void {
  mkdirSync(paths.categories, { recursive: true });
  for (const [cat, block] of Object.entries(index.categories)) {
    if (!block.entryIds.length) continue;
    const out = {
      id: block.id,
      label: block.label,
      entryCount: block.entryIds.length,
      entries: block.entryIds
        .map((id) => index.entries[id])
        .filter(Boolean)
        .map((e) => ({
          id: e.id,
          title: e.title,
          url: e.url,
          metaPath: e.metaPath,
          contentPath: e.contentPath,
          imagesManifestPath: e.imagesManifestPath,
        })),
    };
    const catPath = join(paths.categories, cat);
    mkdirSync(catPath, { recursive: true });
    writeFileSync(join(catPath, "index.json"), JSON.stringify(out, null, 2), "utf8");
  }
}

function runAuditOnce(): AuditResult {
  const index = loadIndexOrThrow();
  const entries = Object.values(index.entries);

  const policyRemoved = strictPolicyAutoDelete ? entries.filter(shouldDropByPolicy) : [];
  const policyRemovedIds = policyRemoved.map((e) => e.id);
  const afterPolicy = strictPolicyAutoDelete ? entries.filter((e) => !shouldDropByPolicy(e)) : entries;

  const { kept, groups } = dedupeByUrl(afterPolicy);
  const keptIdSet = new Set(kept.map((e) => e.id));
  const qualityFlags = collectQualityFlags(kept);
  const suggestedAddOns = suggestAddOns({
    removedPolicyEntries: policyRemovedIds.length,
    removedDuplicateEntries: groups.reduce((n, g) => n + g.removedIds.length, 0),
    qualityFlags: qualityFlags.length,
    keptEntries: kept.length,
  });
  const experimentalIdeas = buildExperimentalIdeas(kept, qualityFlags);

  let removedStaleEntryDirs = 0;
  let removedStaleImageDirs = 0;

  for (const g of groups) {
    for (const id of g.removedIds) {
      archiveOrDelete(join(paths.entries, id), "duplicate-entry");
      archiveOrDelete(join(paths.images, id), "duplicate-image");
    }
  }

  for (const id of policyRemovedIds) {
    // User requested strict mode auto-delete for policy violations.
    if (!dryRun) {
      safeRemove(join(paths.entries, id));
      safeRemove(join(paths.images, id));
    }
  }

  if (existsSync(paths.entries)) {
    for (const name of readdirSync(paths.entries)) {
      const full = join(paths.entries, name);
      if (name.startsWith(".") || keptIdSet.has(name)) continue;
      archiveOrDelete(full, "stale-entry");
      removedStaleEntryDirs++;
    }
  }

  if (existsSync(paths.images)) {
    for (const name of readdirSync(paths.images)) {
      const full = join(paths.images, name);
      if (name.startsWith(".") || name === "_archive" || keptIdSet.has(name)) continue;
      if (name === "florida-hillshades-index.json") continue;
      archiveOrDelete(full, "stale-image");
      removedStaleImageDirs++;
    }
  }

  const rebuilt = rebuildIndexFromKept(index, kept);
  const result: AuditResult = {
    generatedUtc: new Date().toISOString(),
    indexPath: RESEARCH_CACHE_INDEX_REL,
    strictPolicyAutoDelete,
    removedDuplicateEntries: groups.reduce((n, g) => n + g.removedIds.length, 0),
    removedPolicyEntries: policyRemovedIds.length,
    removedStaleEntryDirs,
    removedStaleImageDirs,
    duplicateGroups: groups,
    qualityFlags,
    policyRemovedIds,
    suggestedAddOns,
    experimentalIdeas,
    keptEntries: kept.length,
  };

  if (!dryRun) {
    writeFileSync(paths.index, JSON.stringify(rebuilt, null, 2), "utf8");
    writeCategoryIndexes(rebuilt);
    writeFileSync(
      join(hubRoot, RESEARCH_CACHE_GENERATED_REL),
      JSON.stringify(
        {
          generatedUtc: rebuilt.generatedUtc,
          indexPath: RESEARCH_CACHE_INDEX_REL,
          policy: rebuilt.policy,
          stats: rebuilt.stats,
          auditPath: "Assets/EnvironmentKit/Generated/ResearchCacheAuditReport.json",
        },
        null,
        2
      ),
      "utf8"
    );
  }

  writeAuditReports(result);
  return result;
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function main(): Promise<void> {
  console.log(
    `[ResearchCacheAudit] Hub=${hubRoot} mode=${daemon ? "daemon" : "oneshot"} ${dryRun ? "(dry-run)" : ""}`
  );

  if (!daemon) {
    const result = runAuditOnce();
    console.log(
      `[ResearchCacheAudit] done kept=${result.keptEntries} duplicates=${result.removedDuplicateEntries} qualityFlags=${result.qualityFlags.length}`
    );
    return;
  }

  console.log(`[ResearchCacheAudit] periodic bot started every ${intervalMin} minute(s).`);
  // eslint-disable-next-line no-constant-condition
  while (true) {
    try {
      const result = runAuditOnce();
      console.log(
        `[ResearchCacheAudit] tick kept=${result.keptEntries} duplicates=${result.removedDuplicateEntries} qualityFlags=${result.qualityFlags.length}`
      );
    } catch (err) {
      console.error("[ResearchCacheAudit] tick failed:", err);
    }
    await sleep(intervalMin * 60_000);
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
