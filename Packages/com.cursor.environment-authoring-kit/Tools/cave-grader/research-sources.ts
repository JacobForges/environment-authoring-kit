import type { PromptRung, QualityReport } from "./prompt-ladder.js";
import {
  RESEARCH_MIN_YEAR,
  buildResearchManifestJson,
  allCatalogUrls,
  papersForMinYear,
} from "./research-catalog.js";
import {
  PROMPT_BUDGET,
  allLabNames,
  pickEngineRefsForPrompt,
  pickLabIndicesForPrompt,
  pickPapersForPrompt,
} from "./research-prompt-budget.js";
import {
  formatCacheAwareSearchBlock,
  formatResearchCacheBlock,
} from "./research-cache-prompt.js";

export { RESEARCH_MIN_YEAR, buildResearchManifestJson, allCatalogUrls };

export function buildWebSearchQueries(
  activeRung: PromptRung,
  report: QualityReport
): string[] {
  const failing = report.stages
    .filter((s) => s.score < 95)
    .map((s) => `${s.id} (${s.score})`)
    .slice(0, 4);

  const year = RESEARCH_MIN_YEAR;
  const labs =
    "EA SEED OR Microsoft Research OR NVIDIA Research OR Ubisoft La Forge OR Sony OR Activision OR Unity OR Epic OR Google DeepMind OR Riot";

  const byRung: Partial<Record<PromptRung, string[]>> = {
    research: [
      `prestige game R&D lab PCG papers ${year} arXiv FDG IEEE CoG`,
    ],
    compile_gate: [
      `Unity editor C# compile error fix EnvironmentAuthoringKit ${year}`,
      `Unity HashSet CopyTo array without LINQ ${year}`,
    ],
    visual_shell: [`Ubisoft La Forge PCG environment ${year}`, `NVIDIA 3D world generation ${year}`, `FDG high dimensional PCG ${year}`],
    ground_placement: [
      `Florida panhandle LiDAR bare earth cave mouth ${year} Bay Washington Jackson Calhoun`,
      `USGS Floridan aquifer structural thickness DS 926 karst ${year}`,
      `Sony underground world placement ${year}`,
    ],
    floor_collision: [`Sony PS5 automated gameplay testing ${year}`, `Activision Research QA ${year}`],
    navmesh: [`Unity 6 NavMesh procedural mesh ${year}`, `IEEE CoG NavMesh level ${year}`],
    materials: [`Ubisoft La Forge Chord PBR ${year}`, `Unity Holo-Gen PBR ${year}`],
    performance: [`Riot Swarm performance optimization ${year}`, `Epic GPU procedural generation ${year}`],
    other: [`Microsoft EvoTest game agents ${year}`, `Google DeepMind multi-agent games ${year}`],
  };

  const queries = [
    `${labs} procedural cave ${activeRung} ${year}`,
    ...(byRung[activeRung] ?? []),
  ];
  if (failing.length) {
    queries.push(`${labs} fix cave stages ${year}: ${failing.join(", ")}`);
  }

  return queries.slice(0, PROMPT_BUDGET.maxSearchQueries);
}

/** Slim block for agent prompt — full catalog remains in CaveBuildResearch.json. */
export function formatResearchSourcesBlock(
  activeRung: string,
  meatLoopPass = -1,
  hubRoot?: string
): string {
  const hub = hubRoot?.replace(/\/$/, "") ?? "";
  if (hub) {
    const cacheBlock = formatResearchCacheBlock(activeRung, hub);
    const paperBlock = formatResearchPapersSubset(activeRung, meatLoopPass);
    return [cacheBlock, paperBlock].filter(Boolean).join("\n");
  }
  return formatResearchPapersSubset(activeRung, meatLoopPass);
}

export function buildTerrainWebSearchQueries(activeRung: string): string[] {
  const year = RESEARCH_MIN_YEAR;
  const byRung: Record<string, string[]> = {
    heightfield_no_craters: [
      `USGS 3DEP bare earth DEM crater removal ${year}`,
      `Unity terrain heightmap smooth playable ring ${year}`,
    ],
    playable_slopes: [`trail maximum grade percent walkable game ${year}`],
    trail_walkability: [`Unity terrain trail spline walk route ${year}`],
    surface_navmesh: [`Unity 6 NavMesh terrain bake ${year}`],
    prop_trees: [`procedural forest scatter game terrain ${year}`],
    cave_mouth_grounding: [
      `cave entrance terrain alignment game ${year}`,
      `Florida panhandle LiDAR cave mouth ${year}`,
    ],
  };
  return [
    `Florida panhandle LiDAR hillshade terrain game ${year}`,
    ...(byRung[activeRung] ?? [`open world terrain grading ${year}`]),
  ].slice(0, PROMPT_BUDGET.maxSearchQueries);
}

function formatResearchPapersSubset(
  activeRung: string,
  meatLoopPass = -1
): string {
  const papers = pickPapersForPrompt(activeRung, meatLoopPass);
  const indices = pickLabIndicesForPrompt(activeRung);
  const engine = pickEngineRefsForPrompt(activeRung);
  const totalLabs = allLabNames().length;
  const totalPapers = papersForMinYear().length;

  const lines: string[] = [
    "## Prestige R&D research (prompt subset — do not fetch everything)",
    "",
    `**Full catalog on disk:** \`Assets/EnvironmentKit/Generated/CaveBuildResearch.json\` — **${totalPapers} papers**, **${totalLabs} lab indices**.`,
    `**This prompt includes only ${papers.length} papers + ${indices.length} lab indices** so you stay focused on rung **${activeRung}**.`,
    "",
    `Policy: ${RESEARCH_MIN_YEAR}–2026 **proven production** sources only (AAA testing, shipped Unity 6 docs, GPUOpen production tooling). Speculative preprints excluded from manifest.`,
    "",
    "### Papers to fetch for this rung (required)",
  ];

  for (const p of papers) {
    lines.push(`- **${p.lab}** — ${p.title} (${p.venue}, ${p.year})`);
    lines.push(`  - ${p.pdfUrl ?? p.url}`);
    lines.push(`  - ${p.topics}`);
    if (p.imageUrls?.length) {
      for (const img of p.imageUrls) lines.push(`  - Reference image: ${img}`);
    }
  }
  lines.push("");

  lines.push("### Lab indices (optional, max 3 — discover newer papers)");
  for (const i of indices) {
    lines.push(`- **${i.lab}**: ${i.url}`);
  }
  lines.push("");

  if (engine.length) {
    lines.push("### Unity 6 engine reference (implementation)");
    for (const e of engine) {
      lines.push(`- ${e.title}: ${e.url}`);
    }
    lines.push("");
  }

  lines.push(
    "All labs in full manifest:",
    allLabNames().join(", "),
    "",
    "If you need more sources, open `CaveBuildResearch.json` and pick **at most 3** extra **provenInProduction** papers — do not fetch the entire list.",
    ""
  );

  return lines.join("\n");
}

export function formatSearchQueriesBlock(
  activeRung: PromptRung,
  report: QualityReport,
  hubRoot?: string
): string {
  const hub = hubRoot?.replace(/\/$/, "") ?? "";
  if (hub) return formatCacheAwareSearchBlock(activeRung, report, hub);

  const queries = buildWebSearchQueries(activeRung, report);
  return [
    `## Targeted queries (max ${PROMPT_BUDGET.maxSearchQueries}, prestige labs, ${RESEARCH_MIN_YEAR})`,
    "",
    ...queries.map((q, i) => `${i + 1}. \`${q}\``),
    "",
  ].join("\n");
}

export function allResearchUrls(): string[] {
  return allCatalogUrls();
}
