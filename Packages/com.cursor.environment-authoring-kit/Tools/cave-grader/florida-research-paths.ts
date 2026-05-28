/**
 * Local Florida panhandle terrain + aquifer research assets under ResearchCache.
 */
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { RESEARCH_CACHE_REL } from "./research-store.js";
import { FLORIDA_TARGET_COUNTIES, type FloridaCountyId } from "./florida-county-bboxes.js";

/** Cache entry ids — keep in sync with research-visual-references-florida-*.ts */
const AQUIFER_ENTRY_IDS = [
  "fl-aquifer-ds926-structural-surfaces",
  "fl-aquifer-pp1807-framework",
  "fl-aquifer-ds584-legacy-surfaces",
  "fl-fgs-subsidence-karst-incidents",
  "fl-fgdl-karst-open-data",
  "fl-fgs-ofms104-lidar-karst-geomorph",
  "fl-nwfwmd-gis-hydrogeology",
  "fl-usgs-water-science-floridan",
] as const;

const PANHANDLE_LIDAR_ENTRY_IDS = [
  "fl-panhandle-lidar-dem-2018",
  "fl-panhandle-inundation-dem-east",
  "fl-bay-county-panama-city-dem",
  "fl-pensacola-escambia-dem",
  "fl-jackson-calhoun-nwf-lidar",
  "fl-usgs-3dep-elevation",
  "fl-usgs-3dep-elevation-structure",
  "fl-panhandle-lidar-bare-earth-class2",
  "fl-statewide-lidar-assessment",
] as const;

export const FLORIDA_HILLSHADE_INDEX_REL =
  `${RESEARCH_CACHE_REL}/images/florida-hillshades-index.json`;

export type FloridaHillshadeManifest = {
  county: string;
  countyId: FloridaCountyId;
  relativePath: string;
  source?: string;
  bytes?: number;
  caveStructureOnly?: boolean;
};

export type FloridaHillshadesIndex = {
  generatedUtc: string;
  counties: { countyId: string; path: string; source: string }[];
  bulkDem?: string;
  dataNote?: string;
};

export function hillshadeRelPath(countyId: FloridaCountyId): string {
  return `${RESEARCH_CACHE_REL}/images/fl-${countyId}-hillshade/hillshade.png`;
}

export function hillshadeManifestRelPath(countyId: FloridaCountyId): string {
  return `${RESEARCH_CACHE_REL}/images/fl-${countyId}-hillshade/manifest.json`;
}

export function loadFloridaHillshadesIndex(hubRoot: string): FloridaHillshadesIndex | null {
  const path = join(hubRoot.replace(/\/$/, ""), FLORIDA_HILLSHADE_INDEX_REL);
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8")) as FloridaHillshadesIndex;
  } catch {
    return null;
  }
}

export function listLocalHillshadePaths(hubRoot: string): FloridaHillshadeManifest[] {
  const hub = hubRoot.replace(/\/$/, "");
  const out: FloridaHillshadeManifest[] = [];
  for (const c of FLORIDA_TARGET_COUNTIES) {
    const rel = hillshadeRelPath(c.id);
    const abs = join(hub, rel);
    if (!existsSync(abs)) continue;
    let source = "local";
    const manifestPath = join(hub, hillshadeManifestRelPath(c.id));
    if (existsSync(manifestPath)) {
      try {
        const m = JSON.parse(readFileSync(manifestPath, "utf8")) as {
          source?: string;
          bytes?: number;
        };
        source = m.source ?? source;
        out.push({
          county: c.name,
          countyId: c.id,
          relativePath: rel,
          source,
          bytes: m.bytes,
          caveStructureOnly: true,
        });
      } catch {
        out.push({ county: c.name, countyId: c.id, relativePath: rel, source, caveStructureOnly: true });
      }
    } else {
      out.push({ county: c.name, countyId: c.id, relativePath: rel, source, caveStructureOnly: true });
    }
  }
  return out;
}

/** JSON blob for CaveBuildResearchCache.json / index.json */
export function buildFloridaTerrainSummary(hubRoot: string) {
  const hillshades = listLocalHillshadePaths(hubRoot);
  const index = loadFloridaHillshadesIndex(hubRoot);
  const aquiferEntryIds = [...AQUIFER_ENTRY_IDS];
  const panhandleEntryIds = [...PANHANDLE_LIDAR_ENTRY_IDS];

  return {
    policy:
      "Cave structure only: bare-earth LiDAR (class 2), Floridan aquifer structural surfaces (USGS DS 926), karst/subsidence polygons. Do not use water table, TDS, bathymetry, inundation DEMs, or spring discharge for underground void layout.",
    counties: FLORIDA_TARGET_COUNTIES.map((c) => c.name),
    countyIds: FLORIDA_TARGET_COUNTIES.map((c) => c.id),
    aquiferEntryIds,
    panhandleEntryIds,
    hillshadeIndexPath: FLORIDA_HILLSHADE_INDEX_REL,
    hillshades,
    bulkDemUrl:
      index?.bulkDem ??
      "https://noaa-nos-coastal-lidar-pds.s3.amazonaws.com/dem/FL_Panhandle_DEM_2018_8942/index.html",
    syncCommands: [
      "npm run sync-research-pull",
      "npm run sync-research-cache",
      "npm run sync-florida-hillshades",
    ],
  };
}

export function formatFloridaTerrainBlock(hubRoot: string, activeRung: string): string {
  const summary = buildFloridaTerrainSummary(hubRoot);
  const show =
    activeRung === "ground_placement" ||
    activeRung === "research" ||
    activeRung === "visual_shell" ||
    activeRung === "other";

  if (!show) return "";

  const lines: string[] = [
    "## Florida panhandle terrain & aquifer (local — cave structure only)",
    "",
    `**Policy:** ${summary.policy}`,
    "",
    "**Counties:** Bay, Washington, Jackson, Calhoun",
    "",
    "### Open local files first",
  ];

  if (summary.hillshades.length) {
    lines.push("", "**County hillshade PNGs (bare-earth relief, max 5):**");
    for (const h of summary.hillshades.slice(0, 5)) {
      lines.push(
        `- **${h.county}:** \`${hubRoot}/${h.relativePath}\`${h.bytes ? ` (${h.bytes} bytes, ${h.source})` : ""}`
      );
      lines.push(`  - Manifest: \`${hubRoot}/${hillshadeManifestRelPath(h.countyId)}\``);
    }
  } else {
    lines.push(
      "",
      "_No county hillshades on disk yet._ Run:",
      "```bash",
      "cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader",
      "cd Tools/cave-grader && npm run sync-florida-hillshades",
      "```"
    );
  }

  lines.push("", "**Aquifer / karst cache entries** (`ResearchCache/entries/{id}/content.md`):");
  for (const id of summary.aquiferEntryIds.slice(0, 8)) {
    lines.push(`- \`${hubRoot}/${RESEARCH_CACHE_REL}/entries/${id}/content.md\``);
  }
  if (summary.aquiferEntryIds.length > 8) {
    lines.push(`- …and ${summary.aquiferEntryIds.length - 8} more (see \`floridaTerrain.aquiferEntryIds\` in cache pointer JSON)`);
  }

  lines.push(
    "",
    "**Full 1 m DEM/LAZ tiles:**",
    `- ${summary.bulkDemUrl}`,
    "",
    "**Attribution:** See `Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_DATA_ATTRIBUTION.md`.",
    ""
  );

  return lines.join("\n");
}
