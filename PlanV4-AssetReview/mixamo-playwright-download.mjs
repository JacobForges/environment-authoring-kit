#!/usr/bin/env node
/**
 * Opens Mixamo in a persistent browser profile, captures Bearer from export,
 * downloads all manifest characters to Mixamo/Characters/.
 * First run: log in in the window if needed; later runs reuse session.
 */
import { chromium } from "playwright";
import { createWriteStream, existsSync } from "node:fs";
import { readFile, mkdir } from "node:fs/promises";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { pipeline } from "node:stream/promises";
import { Readable } from "node:stream";
import { homedir } from "node:os";

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, "..");
const outDir = join(root, "Assets/EnvironmentKit/AdobeImports/Mixamo/Characters");
const manifest = JSON.parse(await readFile(join(here, "asset-manifest.json"), "utf8"));
const profileDir = join(homedir(), ".mixamo-playwright-profile");

const wait = (ms) => new Promise((r) => setTimeout(r, ms));

let bearer = process.env.MIXAMO_BEARER?.trim() || null;
if (bearer && !bearer.startsWith("Bearer ")) bearer = `Bearer ${bearer}`;

const context = await chromium.launchPersistentContext(profileDir, {
  headless: false,
  viewport: { width: 1280, height: 900 },
  channel: "chrome",
});
const page = context.pages()[0] || (await context.newPage());

page.on("request", (req) => {
  const auth = req.headers().authorization;
  if (auth?.includes("eyJ")) bearer = auth.startsWith("Bearer ") ? auth : `Bearer ${auth}`;
});

await page.goto("https://www.mixamo.com/#/?page=1&query=Remy&type=Character", {
  waitUntil: "domcontentloaded",
  timeout: 120000,
});

console.log("Browser open — if not logged in, sign in with Google, then wait…");
for (let i = 0; i < 120 && !bearer; i++) {
  await wait(1000);
  if (i % 15 === 14) console.log("  waiting for login / session…");
}
if (!bearer) {
  console.log("Triggering one export to capture token…");
  await page.locator('button:has-text("Download")').first().click({ timeout: 60000 }).catch(() => {});
  await wait(2000);
  await page.locator('.modal-footer button.btn-primary, button:has-text("Download")').last().click({ timeout: 15000 }).catch(() => {});
  for (let i = 0; i < 60 && !bearer; i++) await wait(1000);
}

if (!bearer) {
  console.error("No Bearer captured. Log in in the browser window and re-run this script.");
  await context.close();
  process.exit(1);
}

console.log("Got Bearer, downloading characters…");
const headers = {
  Accept: "application/json",
  "Content-Type": "application/json",
  "X-Api-Key": "mixamo2",
  "X-Requested-With": "XMLHttpRequest",
  Authorization: bearer,
};

async function pollMonitor(characterId) {
  const monitorUrl = `https://www.mixamo.com/api/v1/characters/${characterId}/monitor`;
  for (let i = 0; i < 90; i++) {
    await wait(2000);
    const json = await page.evaluate(
      async ({ monitorUrl, headers }) => {
        const res = await fetch(monitorUrl, { headers });
        return res.json();
      },
      { monitorUrl, headers }
    );
    if (json.status === "completed" && json.job_result) return json.job_result;
    if (json.status === "failed") throw new Error(json.message || "failed");
  }
  throw new Error("timeout");
}

const seen = new Set();
for (const npc of manifest.npcs) {
  const id = npc.characterId;
  if (!id || seen.has(id)) continue;
  seen.add(id);
  const dest = join(outDir, `${npc.slot}.fbx`);
  if (existsSync(dest)) {
    console.log("Skip", npc.slot);
    continue;
  }
  try {
    console.log("Export", npc.slot);
    const job = await page.evaluate(
      async ({ id, name, headers }) => {
        const res = await fetch("https://www.mixamo.com/api/v1/animations/export", {
          method: "POST",
          headers,
          body: JSON.stringify({
            character_id: id,
            gms_hash: [],
            preferences: { format: "fbx7_2019", skin: "true", fps: "30", reducekf: "0" },
            product_name: name,
            type: "Character",
          }),
        });
        if (!res.ok) throw new Error(`export ${res.status}`);
        return res.json();
      },
      { id, name: npc.mixamoName, headers }
    );
    const url =
      job.job_result || (job.status === "processing" ? await pollMonitor(id) : null);
    if (!url) throw new Error("no url");
    const res = await fetch(url);
    if (!res.ok) throw new Error(`dl ${res.status}`);
    await mkdir(outDir, { recursive: true });
    await pipeline(Readable.fromWeb(res.body), createWriteStream(dest));
    console.log("  Saved", dest);
  } catch (e) {
    console.warn("  FAIL", npc.slot, e.message);
  }
}

await context.close();
console.log("Done.");
