#!/usr/bin/env node
/**
 * Batch-download Mixamo character FBX (Unity, with skin).
 *
 * Auth (pick one):
 *   export MIXAMO_BEARER='Bearer eyJ...'   # from Network → export POST → Request Headers → Authorization
 *   OR paste the same line into mixamo.local.env (gitignored) next to this file.
 *
 * NOT the job_result S3 URL from the response JSON — that is only a one-time file link.
 */
import { readFile, mkdir } from "node:fs/promises";
import { createWriteStream, existsSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { pipeline } from "node:stream/promises";
import { Readable } from "node:stream";

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, "..");
const outDir = join(root, "Assets/EnvironmentKit/AdobeImports/Mixamo/Characters");
const manifest = JSON.parse(await readFile(join(here, "asset-manifest.json"), "utf8"));

async function loadBearer() {
  if (process.env.MIXAMO_BEARER?.trim()) return process.env.MIXAMO_BEARER.trim();
  const envPath = join(here, "mixamo.local.env");
  if (!existsSync(envPath)) return null;
  const text = await readFile(envPath, "utf8");
  for (const line of text.split("\n")) {
    const m = line.match(/^\s*MIXAMO_BEARER\s*=\s*(.+)\s*$/);
    if (m) return m[1].replace(/^['"]|['"]$/g, "").trim();
  }
  return null;
}

const bearerRaw = await loadBearer();
if (!bearerRaw) {
  console.error(
    "Missing MIXAMO_BEARER.\n" +
      "  1. Network tab → export (POST) → Request Headers → Authorization\n" +
      "  2. export MIXAMO_BEARER='Bearer eyJ...' OR put MIXAMO_BEARER=Bearer eyJ... in mixamo.local.env"
  );
  process.exit(1);
}
if (bearerRaw.includes("amazonaws.com") || bearerRaw.includes("job_result")) {
  console.error(
    "That value looks like a download URL, not a Bearer token.\n" +
      "Copy Authorization from the export request headers (starts with Bearer eyJ)."
  );
  process.exit(1);
}
if (/…|\.\.\.|paste|eyJ\.\.\./i.test(bearerRaw) || bearerRaw.length < 80) {
  console.error(
    "MIXAMO_BEARER is still a placeholder (or too short).\n" +
      "Safari: Network → export (POST) → Headers → Request → Authorization\n" +
      "Copy the FULL value (Bearer eyJ... hundreds of characters). Not 'Bearer …' from docs."
  );
  process.exit(1);
}
for (const ch of bearerRaw) {
  if (ch.charCodeAt(0) > 255) {
    console.error(
      `Invalid character in token (Unicode ${ch.charCodeAt(0)}). You may have copied "…" instead of the real JWT.\n` +
        "Copy Authorization again from the export request — plain ASCII only."
    );
    process.exit(1);
  }
}

const headers = {
  Accept: "application/json",
  "Content-Type": "application/json",
  "X-Api-Key": "mixamo2",
  "X-Requested-With": "XMLHttpRequest",
  Authorization: bearerRaw.startsWith("Bearer ") ? bearerRaw : `Bearer ${bearerRaw}`,
};

async function exportCharacter(characterId, productName) {
  const body = {
    character_id: characterId,
    gms_hash: [],
    preferences: {
      format: "fbx7_2019",
      skin: "true",
      fps: "30",
      reducekf: "0",
    },
    product_name: productName,
    type: "Character",
  };
  const res = await fetch("https://www.mixamo.com/api/v1/animations/export", {
    method: "POST",
    headers,
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`export ${res.status} ${characterId}`);
  return res.json();
}

async function pollMonitor(characterId, dest) {
  const monitorUrl = `https://www.mixamo.com/api/v1/characters/${characterId}/monitor`;
  for (let i = 0; i < 90; i++) {
    await new Promise((r) => setTimeout(r, 2000));
    const res = await fetch(monitorUrl, { headers });
    if (!res.ok) continue;
    const json = await res.json();
    if (json.status === "completed" && json.job_result) {
      const dl = await fetch(json.job_result);
      if (!dl.ok) throw new Error(`fbx download ${dl.status}`);
      await mkdir(dirname(dest), { recursive: true });
      await pipeline(Readable.fromWeb(dl.body), createWriteStream(dest));
      return true;
    }
    if (json.status === "failed") throw new Error(json.message || "job failed");
  }
  throw new Error("timeout waiting for export");
}

const seen = new Set();
for (const npc of manifest.npcs) {
  const id = npc.characterId;
  if (!id || seen.has(id)) continue;
  seen.add(id);
  const dest = join(outDir, `${npc.slot}.fbx`);
  if (existsSync(dest)) {
    console.log("Skip (exists)", npc.slot);
    continue;
  }
  try {
    console.log("Exporting", npc.slot, npc.mixamoName);
    const job = await exportCharacter(id, npc.mixamoName);
    if (job.status === "completed" && job.job_result) {
      const dl = await fetch(job.job_result);
      if (!dl.ok) throw new Error(`fbx download ${dl.status}`);
      await mkdir(dirname(dest), { recursive: true });
      await pipeline(Readable.fromWeb(dl.body), createWriteStream(dest));
    } else {
      await pollMonitor(id, dest);
    }
    console.log("  Saved", dest);
  } catch (e) {
    console.warn("  FAIL", npc.slot, e.message);
  }
}

console.log("Done.");
