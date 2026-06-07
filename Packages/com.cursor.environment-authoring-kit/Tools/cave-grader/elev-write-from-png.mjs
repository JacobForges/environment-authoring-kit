#!/usr/bin/env node
/** One-shot: write elevation-grid.json from existing hillshade.png (no EPQS, no tsx). */
import { readFileSync, writeFileSync, existsSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { inflateSync } from "node:zlib";

const __dir = dirname(fileURLToPath(import.meta.url));
const hub =
  process.env.HUB_ROOT?.replace(/\/$/, "") ||
  join(__dir, "../../../..");
const imagesRoot = join(hub, "Assets/EnvironmentKit/ResearchCache/images");
const counties = ["bay", "washington", "jackson", "calhoun"];
const gridSize = 32;
const segmentMeters = 512;
const baseByCounty = { bay: 12, washington: 28, jackson: 35, calhoun: 38 };

function paeth(a, b, c) {
  const p = a + b - c;
  const pa = Math.abs(p - a);
  const pb = Math.abs(p - b);
  const pc = Math.abs(p - c);
  if (pa <= pb && pa <= pc) return a;
  if (pb <= pc) return b;
  return c;
}

function decodeRgbPng(buffer) {
  let pos = 8;
  let width = 0;
  let height = 0;
  const idat = [];
  while (pos + 12 <= buffer.length) {
    const len = buffer.readUInt32BE(pos);
    const type = buffer.toString("ascii", pos + 4, pos + 8);
    const data = buffer.subarray(pos + 8, pos + 8 + len);
    if (type === "IHDR") {
      width = data.readUInt32BE(0);
      height = data.readUInt32BE(4);
    } else if (type === "IDAT") idat.push(data);
    else if (type === "IEND") break;
    pos += 12 + len;
  }
  const raw = inflateSync(Buffer.concat(idat));
  const rgb = new Uint8Array(width * height * 3);
  let o = 0;
  for (let y = 0; y < height; y++) {
    const f = raw[o++];
    for (let x = 0; x < width; x++) {
      for (let c = 0; c < 3; c++) {
        const i = (y * width + x) * 3 + c;
        let v = raw[o++];
        if (f) {
          const L = x > 0 ? rgb[i - 3] : 0;
          const U = y > 0 ? rgb[i - width * 3] : 0;
          const UL = x > 0 && y > 0 ? rgb[i - width * 3 - 3] : 0;
          if (f === 1) v = (v + L) & 255;
          else if (f === 2) v = (v + U) & 255;
          else if (f === 3) v = (v + ((L + U) >> 1)) & 255;
          else if (f === 4) v = (v + paeth(L, U, UL)) & 255;
        }
        rgb[i] = v;
      }
    }
  }
  return { width, height, rgb };
}

function segmentBbox(centerLon, centerLat) {
  const latRad = (centerLat * Math.PI) / 180;
  const mPerLon = 111320 * Math.cos(latRad);
  const mPerLat = 110540;
  const hLon = (segmentMeters * 0.5) / mPerLon;
  const hLat = (segmentMeters * 0.5) / mPerLat;
  return {
    west: centerLon - hLon,
    east: centerLon + hLon,
    south: centerLat - hLat,
    north: centerLat + hLat,
  };
}

const centers = {
  bay: [-85.66, 30.17],
  washington: [-85.55, 30.6],
  jackson: [-85.22, 30.84],
  calhoun: [-85.2, 30.44],
};

let ok = 0;
for (const id of counties) {
  const outDir = join(imagesRoot, `fl-${id}-hillshade`);
  const png = join(outDir, "hillshade.png");
  if (!existsSync(png)) {
    console.error(`MISSING ${png}`);
    continue;
  }
  const { width, height, rgb } = decodeRgbPng(readFileSync(png));
  const base = baseByCounty[id] ?? 25;
  const relief = 20;
  const values = [];
  for (let j = 0; j < gridSize; j++) {
    for (let i = 0; i < gridSize; i++) {
      const px = Math.round((i / (gridSize - 1)) * (width - 1));
      const py = Math.round((j / (gridSize - 1)) * (height - 1));
      const idx = (py * width + px) * 3;
      const lum = (rgb[idx] + rgb[idx + 1] + rgb[idx + 2]) / (3 * 255);
      values.push(base + (lum - 0.5) * relief * 2);
    }
  }
  const minElev = Math.min(...values);
  const maxElev = Math.max(...values);
  const [lon, lat] = centers[id];
  const bbox = segmentBbox(lon, lat);
  const payload = {
    width: gridSize,
    height: gridSize,
    segmentSizeMeters: segmentMeters,
    bbox,
    minElevationMeters: minElev,
    maxElevationMeters: maxElev,
    nodata: -9999,
    source: "derived-from-hillshade-png",
    values,
  };
  mkdirSync(outDir, { recursive: true });
  const outPath = join(outDir, "elevation-grid.json");
  writeFileSync(outPath, JSON.stringify(payload));
  console.log(`WROTE ${outPath} (${minElev.toFixed(1)}–${maxElev.toFixed(1)} m)`);
  ok++;
}
process.exit(ok === counties.length ? 0 : 1);
