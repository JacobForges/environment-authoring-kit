#!/usr/bin/env npx tsx
/**
 * Close-up LiDAR hillshade segments (play-scale, meters-accurate) for Unity terrain stamp.
 * Exports one segment per county centered on the county — NOT a county-wide satellite overview.
 *
 * Usage (from Hub):
 *   HUB_ROOT=/Users/jacob/Hub npm run sync-florida-hillshades
 *   HUB_ROOT=/Users/jacob/Hub npm run sync-florida-hillshades -- --segment-meters=512 --pixels=1024
 *   HUB_ROOT=/Users/jacob/Hub npm run sync-florida-hillshades -- --counties=bay,jackson --elev-grid=32
 */
import { mkdirSync, readFileSync, writeFileSync, existsSync } from "node:fs";
import { join } from "node:path";
import {
  FLORIDA_TARGET_COUNTIES,
  type FloridaCountyId,
  countyById,
} from "./florida-county-bboxes.js";
import {
  decodeRgbPng,
  encodeRgbPng,
  fillNodata,
  gridFromElevations,
  hillshadeRgb,
} from "./hillshade-raster.js";

import { resolveHubRoot } from "./hub-root.js";

const hubRoot = resolveHubRoot();
const imagesRoot = join(hubRoot, "Assets/EnvironmentKit/ResearchCache/images");

const UA = "EnvironmentKit-FloridaHillshade/1.0 (research cache; public USGS/NOAA data)";

function parseArgs() {
  const countiesArg = process.argv.find((a) => a.startsWith("--counties="));
  const gridArg = process.argv.find((a) => a.startsWith("--grid="));
  const segmentArg = process.argv.find((a) => a.startsWith("--segment-meters="));
  const pixelsArg = process.argv.find((a) => a.startsWith("--pixels="));
  const elevArg = process.argv.find((a) => a.startsWith("--elev-grid="));
  const elevGridOnly = process.argv.includes("--elev-grid-only");
  const elevFromHillshade = process.argv.includes("--elev-from-hillshade");
  const counties = countiesArg
    ? (countiesArg.split("=")[1]?.split(",").map((s) => s.trim().toLowerCase()) as FloridaCountyId[])
    : FLORIDA_TARGET_COUNTIES.map((c) => c.id);
  const gridSize = Math.min(48, Math.max(12, parseInt(gridArg?.split("=")[1] ?? "24", 10) || 24));
  const segmentMeters = Math.min(
    1024,
    Math.max(128, parseInt(segmentArg?.split("=")[1] ?? "512", 10) || 512)
  );
  const pixels = Math.min(2048, Math.max(256, parseInt(pixelsArg?.split("=")[1] ?? "1024", 10) || 1024));
  const elevGrid = elevArg
    ? Math.min(64, Math.max(8, parseInt(elevArg.split("=")[1] ?? "32", 10) || 32))
    : 0;
  return { counties, gridSize, segmentMeters, pixels, elevGrid, elevGridOnly };
}

function segmentBboxFromCenter(
  centerLon: number,
  centerLat: number,
  segmentMeters: number
): { west: number; south: number; east: number; north: number } {
  const latRad = (centerLat * Math.PI) / 180;
  const mPerLon = 111_320 * Math.cos(latRad);
  const mPerLat = 110_540;
  const halfLon = segmentMeters * 0.5 / mPerLon;
  const halfLat = segmentMeters * 0.5 / mPerLat;
  return {
    west: centerLon - halfLon,
    east: centerLon + halfLon,
    south: centerLat - halfLat,
    north: centerLat + halfLat,
  };
}

async function fetchJson(url: string): Promise<unknown> {
  const res = await fetch(url, {
    headers: { "User-Agent": UA },
    signal: AbortSignal.timeout(30_000),
  });
  if (!res.ok) throw new Error(`HTTP ${res.status} ${url}`);
  return res.json();
}

async function fetchImageBuffer(url: string): Promise<Buffer | null> {
  try {
    const res = await fetch(url, {
      headers: { "User-Agent": UA },
      signal: AbortSignal.timeout(45_000),
    });
    if (!res.ok) return null;
    const buf = Buffer.from(await res.arrayBuffer());
    return buf.length > 500 ? buf : null;
  } catch {
    return null;
  }
}

/** USGS 3DEP ImageServer hillshade export (preferred when reachable). */
function hillshadeExportUrl(west: number, south: number, east: number, north: number, size: number) {
  const renderingRule = JSON.stringify({
    rasterFunction: "Hillshade",
    rasterFunctionArguments: { Azimuth: 315, Altitude: 45, ZFactor: 2 },
  });
  const q = new URLSearchParams({
    f: "image",
    bbox: `${west},${south},${east},${north}`,
    bboxSR: "4326",
    size: `${size},${size}`,
    imageSR: "4326",
    format: "png",
    renderingRule,
  });
  return `https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer/exportImage?${q}`;
}

/** Fallback: National Map hillshade base (Web Mercator bbox). */
function hillshadeMapExportUrl(west: number, south: number, east: number, north: number, size: number) {
  const toMerc = (lon: number, lat: number) => {
    const x = (lon * 20037508.34) / 180;
    const y =
      (Math.log(Math.tan(((90 + lat) * Math.PI) / 360)) / (Math.PI / 180)) *
      (20037508.34 / 180);
    return [x, y];
  };
  const [x0, y0] = toMerc(west, south);
  const [x1, y1] = toMerc(east, north);
  const q = new URLSearchParams({
    f: "image",
    bbox: `${x0},${y0},${x1},${y1}`,
    bboxSR: "3857",
    size: `${size},${size}`,
    format: "png",
    transparent: "false",
  });
  return `https://basemap.nationalmap.gov/arcgis/rest/services/USGSHillShadeOnlyBase/MapServer/export?${q}`;
}

function parseEpqsElevation(data: unknown): number {
  if (!data || typeof data !== "object") return NaN;
  const o = data as Record<string, unknown>;
  const raw = o.elevation ?? o.value;
  if (typeof raw === "number" && Number.isFinite(raw)) return raw;
  if (typeof raw === "string") {
    const n = parseFloat(raw);
    return Number.isFinite(n) ? n : NaN;
  }

  return NaN;
}

/** Build elevation-grid.json from an existing hillshade PNG (seconds, no EPQS). */
function writeElevationGridFromHillshadePng(
  pngPath: string,
  outDir: string,
  seg: { west: number; south: number; east: number; north: number },
  segmentMeters: number,
  gridSize: number,
  countyId: string
): boolean {
  if (!existsSync(pngPath)) return false;

  const { width, height, rgb } = decodeRgbPng(readFileSync(pngPath));
  const baseByCounty: Record<string, number> = {
    bay: 12,
    washington: 28,
    jackson: 35,
    calhoun: 38,
  };
  const base = baseByCounty[countyId] ?? 25;
  const reliefMeters = 20;
  const samples: number[] = [];

  for (let j = 0; j < gridSize; j++) {
    for (let i = 0; i < gridSize; i++) {
      const px = Math.round((i / Math.max(1, gridSize - 1)) * (width - 1));
      const py = Math.round((j / Math.max(1, gridSize - 1)) * (height - 1));
      const idx = (py * width + px) * 3;
      const lum = (rgb[idx] + rgb[idx + 1] + rgb[idx + 2]) / (3 * 255);
      samples.push(base + (lum - 0.5) * reliefMeters * 2);
    }
  }

  const ok = writeElevationGrid(
    outDir,
    seg.west,
    seg.south,
    seg.east,
    seg.north,
    segmentMeters,
    gridSize,
    samples);
  if (ok) {
    console.log(
      `[Hillshade] elevation-grid from hillshade.png (local decode, county base ~${base}m) — no EPQS re-download.`
    );
  }

  return ok;
}

async function sampleEpqsGrid(
  west: number,
  south: number,
  east: number,
  north: number,
  gridSize: number
): Promise<number[]> {
  const samples: number[] = [];
  const delayMs = 35;
  for (let j = 0; j < gridSize; j++) {
    for (let i = 0; i < gridSize; i++) {
      const lon = west + ((east - west) * i) / (gridSize - 1);
      const lat = south + ((north - south) * j) / (gridSize - 1);
      const url = `https://epqs.nationalmap.gov/v1/json?x=${lon}&y=${lat}&units=Meters&wkid=4326`;
      try {
        samples.push(parseEpqsElevation(await fetchJson(url)));
      } catch {
        samples.push(NaN);
      }
      await new Promise((r) => setTimeout(r, delayMs));
    }
    process.stdout.write(`  EPQS row ${j + 1}/${gridSize}\r`);
  }
  console.log("");
  return samples;
}

function writeElevationGrid(
  outDir: string,
  west: number,
  south: number,
  east: number,
  north: number,
  segmentMeters: number,
  gridSize: number,
  samples: number[]
): boolean {
  const finite = samples.filter((v) => Number.isFinite(v));
  if (finite.length < 4) {
    console.warn(
      `[Hillshade] elevation-grid NOT written — only ${finite.length}/${samples.length} valid EPQS samples (API may have failed).`
    );
    return false;
  }
  const minElev = Math.min(...finite);
  const maxElev = Math.max(...finite);
  const payload = {
    width: gridSize,
    height: gridSize,
    segmentSizeMeters: segmentMeters,
    bbox: { west, south, east, north },
    minElevationMeters: minElev,
    maxElevationMeters: maxElev,
    nodata: -9999,
    values: samples.map((v) => (Number.isFinite(v) ? v : -9999)),
  };
  const outPath = join(outDir, "elevation-grid.json");
  writeFileSync(outPath, JSON.stringify(payload), "utf8");
  console.log(
    `[Hillshade] Wrote ${outPath} (${gridSize}×${gridSize}, ${minElev.toFixed(1)}–${maxElev.toFixed(1)} m).`
  );
  return true;
}

async function renderCountyHillshade(
  countyId: FloridaCountyId,
  gridSize: number,
  segmentMeters: number,
  pixels: number,
  elevGrid: number
): Promise<{ path: string; source: string } | null> {
  const county = countyById(countyId);
  if (!county) {
    console.warn(`[Hillshade] Unknown county: ${countyId}`);
    return null;
  }

  const outDir = join(imagesRoot, `fl-${countyId}-hillshade`);
  mkdirSync(outDir, { recursive: true });
  const outPath = join(outDir, "hillshade.png");
  const manifestPath = join(outDir, "manifest.json");

  const seg = segmentBboxFromCenter(county.centerLon, county.centerLat, segmentMeters);
  const metersPerPixel = segmentMeters / pixels;

  console.log(
    `[Hillshade] ${county.name} — close-up segment ${segmentMeters}m @ ${pixels}px (${metersPerPixel.toFixed(2)} m/px)…`
  );

  let buf =
    (await fetchImageBuffer(
      hillshadeExportUrl(seg.west, seg.south, seg.east, seg.north, pixels)
    )) ??
    (await fetchImageBuffer(
      hillshadeMapExportUrl(seg.west, seg.south, seg.east, seg.north, pixels)
    ));

  let source = "usgs-3dep-segment-export";
  if (buf) {
    writeFileSync(outPath, buf);
  } else {
    console.log(`[Hillshade] ${county.name} — EPQS grid ${gridSize}×${gridSize} on segment (local hillshade)…`);
    const samples = await sampleEpqsGrid(seg.west, seg.south, seg.east, seg.north, gridSize);
    let grid = gridFromElevations(gridSize, gridSize, samples);
    grid = fillNodata(grid);
    const rgb = hillshadeRgb(grid, { azimuthDeg: 315, altitudeDeg: 45, zFactor: 2.5 });
    buf = encodeRgbPng(gridSize, gridSize, rgb);
    writeFileSync(outPath, buf);
    source = "epqs-segment-synthetic-hillshade";
    if (elevGrid <= 0)
      writeElevationGrid(outDir, seg.west, seg.south, seg.east, seg.north, segmentMeters, gridSize, samples);
  }

  if (elevGrid > 0) {
    console.log(`[Hillshade] ${county.name} — elevation grid ${elevGrid}×${elevGrid} for height stamp…`);
    const elevSamples = await sampleEpqsGrid(seg.west, seg.south, seg.east, seg.north, elevGrid);
    const wrote = writeElevationGrid(
      outDir,
      seg.west,
      seg.south,
      seg.east,
      seg.north,
      segmentMeters,
      elevGrid,
      elevSamples);
    if (!wrote) {
      writeElevationGridFromHillshadePng(
        outPath,
        outDir,
        seg,
        segmentMeters,
        elevGrid,
        countyId);
    }
  }

  const relPath = `Assets/EnvironmentKit/ResearchCache/images/fl-${countyId}-hillshade/hillshade.png`;
  const manifest = {
    county: county.name,
    countyId,
    bbox: { west: county.west, south: county.south, east: county.east, north: county.north },
    center: { lon: county.centerLon, lat: county.centerLat },
    segment: {
      sizeMeters: segmentMeters,
      pixels,
      metersPerPixel,
      bbox: seg,
    },
    generatedUtc: new Date().toISOString(),
    source,
    bytes: buf.length,
    caveStructureOnly: true,
    policy:
      "Close-up bare-earth segment for play-scale terrain (not county-wide satellite stretch). Underground void layout ignores bathymetry/water table.",
    relativePath: relPath,
  };
  writeFileSync(manifestPath, JSON.stringify(manifest, null, 2), "utf8");

  console.log(
    `[Hillshade] ${county.name} → ${relPath} (${source}, ${buf.length} bytes, ${metersPerPixel.toFixed(2)} m/px)`
  );
  return { path: relPath, source };
}

async function buildElevationGridsFromHillshadeOnly(
  counties: FloridaCountyId[],
  segmentMeters: number,
  elevGrid: number
): Promise<void> {
  const gridSize = elevGrid > 0 ? elevGrid : 32;
  console.log(
    `[Hillshade] Building elevation-grid.json from existing hillshade PNGs (${gridSize}×${gridSize}) — instant, no network.`
  );

  for (const id of counties) {
    const county = countyById(id);
    if (!county) continue;

    const outDir = join(imagesRoot, `fl-${id}-hillshade`);
    const pngPath = join(outDir, "hillshade.png");
    const seg = segmentBboxFromCenter(county.centerLon, county.centerLat, segmentMeters);
    writeElevationGridFromHillshadePng(pngPath, outDir, seg, segmentMeters, gridSize, id);
  }
}

async function backfillElevationGridsOnly(
  counties: FloridaCountyId[],
  segmentMeters: number,
  elevGrid: number
): Promise<void> {
  if (elevGrid <= 0) {
    console.error("[Hillshade] --elev-grid-only requires --elev-grid=N (e.g. 32).");
    process.exit(1);
  }

  console.log(
    `[Hillshade] Elevation-grid only (no PNG re-download) — ${counties.length} counties @ ${elevGrid}×${elevGrid}…`
  );

  for (const id of counties) {
    const county = countyById(id);
    if (!county) continue;

    const outDir = join(imagesRoot, `fl-${id}-hillshade`);
    mkdirSync(outDir, { recursive: true });
    const seg = segmentBboxFromCenter(county.centerLon, county.centerLat, segmentMeters);

    if (!existsSync(join(outDir, "hillshade.png"))) {
      console.warn(`[Hillshade] Skip ${id} — missing hillshade.png (run full sync first).`);
      continue;
    }

    console.log(`[Hillshade] ${county.name} — EPQS elevation grid ${elevGrid}×${elevGrid}…`);
    const elevSamples = await sampleEpqsGrid(seg.west, seg.south, seg.east, seg.north, elevGrid);
    writeElevationGrid(outDir, seg.west, seg.south, seg.east, seg.north, segmentMeters, elevGrid, elevSamples);
  }
}

async function main() {
  const { counties, gridSize, segmentMeters, pixels, elevGrid, elevGridOnly, elevFromHillshade } =
    parseArgs();
  mkdirSync(imagesRoot, { recursive: true });

  if (elevFromHillshade) {
    await buildElevationGridsFromHillshadeOnly(
      counties as FloridaCountyId[],
      segmentMeters,
      elevGrid > 0 ? elevGrid : 128);
    return;
  }

  if (elevGridOnly) {
    await backfillElevationGridsOnly(counties as FloridaCountyId[], segmentMeters, elevGrid);
    return;
  }

  const results: { countyId: string; path: string; source: string }[] = [];
  for (const id of counties) {
    const r = await renderCountyHillshade(id as FloridaCountyId, gridSize, segmentMeters, pixels, elevGrid);
    if (r) results.push({ countyId: id, path: r.path, source: r.source });
  }

  const indexPath = join(imagesRoot, "florida-hillshades-index.json");
  writeFileSync(
    indexPath,
    JSON.stringify(
      {
        generatedUtc: new Date().toISOString(),
        segmentMeters,
        segmentPixels: pixels,
        counties: results,
        dataNote:
          "Close-up play-scale LiDAR segments (meters-accurate UV in Unity). Full 1 m panhandle tiles on NOAA S3 for production; re-run sync-florida-hillshades after changing segment size.",
        bulkDem:
          "https://noaa-nos-coastal-lidar-pds.s3.amazonaws.com/dem/FL_Panhandle_DEM_2018_8942/index.html",
      },
      null,
      2
    ),
    "utf8"
  );

  console.log(`[Hillshade] Done — ${results.length} segment(s) → ${indexPath}`);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
