#!/usr/bin/env node
/**
 * Downloads approved Plan V4 previews + writes Mixamo batch script.
 * FBX requires Adobe login — run mixamo-download-helper.js in browser while signed in.
 */
import { mkdir, writeFile, readFile } from "node:fs/promises";
import { createWriteStream } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { pipeline } from "node:stream/promises";
import { Readable } from "node:stream";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const hub = root;
const outRoot = join(hub, "Assets/EnvironmentKit/AdobeImports");
const manifestPath = join(dirname(fileURLToPath(import.meta.url)), "asset-manifest.json");

async function download(url, dest) {
  if (!url) return false;
  const res = await fetch(url);
  if (!res.ok) throw new Error(`${res.status} ${url}`);
  await mkdir(dirname(dest), { recursive: true });
  await pipeline(Readable.fromWeb(res.body), createWriteStream(dest));
  return true;
}

const mixamoHelper = `// Paste into Mixamo.com console while logged in (Characters tab open).
// Downloads each character FBX for Unity (with skin). Allow multiple downloads.
(async () => {
  const chars = ${""};
})();
`;

async function main() {
  const manifest = JSON.parse(await readFile(manifestPath, "utf8"));
  const npcDir = join(outRoot, "Mixamo/Characters");
  const itemDir = join(outRoot, "Substance3D/Previews");
  await mkdir(npcDir, { recursive: true });
  await mkdir(itemDir, { recursive: true });

  const charIds = [];
  for (const npc of manifest.npcs) {
    const id = npc.characterId;
    if (!id || charIds.includes(id)) continue;
    charIds.push(id);
    const thumb = npc.thumbnail;
    const dest = join(npcDir, `${npc.slot}.png`);
    try {
      await download(thumb, dest);
      console.log("NPC thumb", npc.slot);
    } catch (e) {
      console.warn("NPC thumb fail", npc.slot, e.message);
    }
  }

  const seen = new Set();
  for (const item of manifest.items) {
    const sub = item.substance;
    if (!sub || !Array.isArray(sub) || sub.length < 3) continue;
    const img = sub[2];
    if (!img || seen.has(img)) continue;
    seen.add(img);
    const dest = join(itemDir, `${item.id}.jpg`);
    try {
      await download(img, dest);
      console.log("Item prev", item.id);
    } catch (e) {
      console.warn("Item prev fail", item.id, e.message);
    }
  }

  const helper = `// Mixamo batch — run on https://www.mixamo.com while logged in.
// 1. Open Characters, search each name, Download FBX For Unity, With Skin
// 2. Save to Assets/EnvironmentKit/AdobeImports/Mixamo/Characters/
const characters = ${JSON.stringify(manifest.npcs.map((n) => ({ slot: n.slot, name: n.mixamoName, id: n.characterId, url: n.mixamoUrl })), null, 2)};
console.table(characters);
`;
  await writeFile(join(outRoot, "MIXAMO_DOWNLOAD_CHECKLIST.md"), `# Mixamo download checklist\n\n${manifest.npcs.map((n) => `- [ ] **${n.slot}** — ${n.mixamoName} → [open](${n.mixamoUrl})`).join("\n")}\n`);
  await writeFile(join(outRoot, "SUBSTANCE_DOWNLOAD_CHECKLIST.md"), `# Substance 3D download checklist\n\nDownload FBX/GLB from each asset page (subscription required).\n\n${manifest.items.filter((i) => i.substance?.[1]).map((i) => `- [ ] **${i.id}** ${i.display} — ${i.substance[0]}`).join("\n")}\n`);
  await writeFile(join(outRoot, "mixamo-console-helper.js"), helper);
  console.log("Done →", outRoot);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
