import type { PromptRung, QualityReport } from "./prompt-ladder.js";
import {
  RESEARCH_CACHE_GENERATED_REL,
  RESEARCH_CACHE_INDEX_REL,
  cachedUrls,
  loadIndex,
  lookupForRung,
} from "./research-store.js";
import { buildWebSearchQueries } from "./research-sources.js";
import { PROMPT_BUDGET } from "./research-prompt-budget.js";
import { formatFloridaTerrainBlock } from "./florida-research-paths.js";

/** Local library block — prefer disk over web/API. */
export function formatResearchCacheBlock(
  activeRung: string,
  hubRoot: string
): string {
  const lookup = lookupForRung(hubRoot, activeRung, 10);
  if (!lookup) {
    return [
      "## Research cache (not initialized)",
      "",
      `Run \`npm run sync-research-cache\` in Tools/cave-grader (or Build Complete Cave research phase).`,
      "",
    ].join("\n");
  }

  const { index, hits } = lookup;
  const lines: string[] = [
    "## Research cache — read local files FIRST (no API)",
    "",
    `**Index:** \`${hubRoot}/${RESEARCH_CACHE_INDEX_REL}\``,
    `**Pointer:** \`${RESEARCH_CACHE_GENERATED_REL}\` | **${index.stats.totalEntries}** entries, **${index.stats.provenEntries}** proven`,
    "",
    "**Policy:** Use **all** existing `ResearchCache/` data and images on disk first. Build pulls missing preview PNGs + FL hillshades every sync; HTTP-fetch only gaps. **Do not** re-download URLs listed under \"Cached URLs\" below.",
    "",
    "### This rung — open these files",
  ];

  for (const e of hits) {
    lines.push(`- **${e.title}** (${e.category}, ${e.year})`);
    lines.push(`  - Content: \`${hubRoot}/${e.contentPath}\``);
    lines.push(`  - Meta: \`${hubRoot}/${e.metaPath}\``);
    if (e.imagesManifestPath) {
      lines.push(`  - Images: \`${hubRoot}/${e.imagesManifestPath}\``);
      for (const img of e.localImages ?? []) {
        if (img.bytes)
          lines.push(`  - Local image: \`${hubRoot}/${img.relativePath}\` (${img.bytes} bytes)`);
      }
    }
    lines.push(`  - Summary: ${e.serialized.summary.slice(0, 200)}`);
  }

  lines.push("", "### Category folders (browse by topic)");
  const cats = [
    "terrain",
    "ground_placement",
    "mesh_shell",
    "visual_reference",
    "engine_docs",
    "qa_testing",
  ] as const;
  for (const cat of cats) {
    const block = index.categories[cat];
    if (!block?.entryIds.length) continue;
    lines.push(
      `- \`${cat}\`: \`${hubRoot}/Assets/EnvironmentKit/ResearchCache/categories/${cat}/index.json\` (${block.entryIds.length} entries)`
    );
  }

  const urls = [...cachedUrls(index)].slice(0, 24);
  lines.push("", "### Cached URLs — do NOT re-fetch (use local content.md)");
  for (const u of urls) lines.push(`- ${u}`);

  const florida = formatFloridaTerrainBlock(hubRoot, activeRung);
  if (florida) {
    lines.push("", florida);
  }

  return lines.join("\n");
}

/** Only suggest web queries for gaps not covered by cache. */
export function formatCacheAwareSearchBlock(
  activeRung: PromptRung,
  report: QualityReport,
  hubRoot: string
): string {
  const index = loadIndex(hubRoot);
  const allQueries = buildWebSearchQueries(activeRung, report);

  if (!index) {
    return [
      `## Targeted queries (max ${PROMPT_BUDGET.maxSearchQueries})`,
      "",
      ...allQueries.map((q, i) => `${i + 1}. \`${q}\``),
      "",
    ].join("\n");
  }

  const hits = lookupForRung(hubRoot, activeRung, 8)?.hits ?? [];
  const lines: string[] = [
    `## Web search — ONLY if local cache does not answer (max ${PROMPT_BUDGET.maxSearchQueries})`,
    "",
    `Local cache has **${hits.length}** entries for rung \`${activeRung}\`. Try \`entries/*/content.md\` first.`,
    "",
    "Allowed queries (cache miss / new 2025–2026 production source only):",
  ];

  const narrowed = allQueries.slice(0, Math.max(2, PROMPT_BUDGET.maxSearchQueries - 2));
  narrowed.forEach((q, i) => lines.push(`${i + 1}. \`${q}\``));

  lines.push(
    "",
    "When you find a new proven source, add a note in your plan — maintainer can run `sync-research-cache` to persist it.",
    ""
  );
  return lines.join("\n");
}
