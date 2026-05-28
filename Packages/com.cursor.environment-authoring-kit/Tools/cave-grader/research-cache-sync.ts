#!/usr/bin/env npx tsx
/**
 * Sync categorized research library under Assets/EnvironmentKit/ResearchCache.
 * Optional: --fetch-images (HTTP download, no paid API).
 */
import {
  existsSync,
  readdirSync,
  statSync,
  readFileSync,
  unlinkSync,
  writeFileSync,
  mkdirSync,
} from "node:fs";
import { join } from "node:path";
import { buildFloridaTerrainSummary } from "./florida-research-paths.js";
import { parseRungArg } from "./prompt-ladder.js";
import {
  RESEARCH_CACHE_GENERATED_REL,
  RESEARCH_CACHE_INDEX_REL,
  buildAllCatalogEntries,
  buildIndexFromEntries,
  cachePaths,
  writeCategoryIndexes,
  writeEntryFiles,
  type CacheEntry,
  type LocalImageRef,
} from "./research-store.js";
import { resolveHubRoot } from "./hub-root.js";

const hubRoot = resolveHubRoot();
/** Force re-download every preview image. */
const fetchAllImages = process.argv.includes("--fetch-images");
/** Default on: reuse valid on-disk PNGs; download only missing/invalid. */
const fetchMissingImages = !process.argv.includes("--no-fetch-images");

function isRasterImageBuffer(buf: Buffer): boolean {
  if (buf.length < 12) return false;
  // PNG
  if (buf[0] === 0x89 && buf[1] === 0x50 && buf[2] === 0x4e && buf[3] === 0x47) return true;
  // JPEG
  if (buf[0] === 0xff && buf[1] === 0xd8 && buf[2] === 0xff) return true;
  // GIF
  if (buf[0] === 0x47 && buf[1] === 0x49 && buf[2] === 0x46) return true;
  // WebP (RIFF....WEBP)
  if (
    buf[0] === 0x52 &&
    buf[1] === 0x49 &&
    buf[2] === 0x46 &&
    buf[3] === 0x46 &&
    buf[8] === 0x57 &&
    buf[9] === 0x45 &&
    buf[10] === 0x42 &&
    buf[11] === 0x50
  ) {
    return true;
  }
  return false;
}

async function downloadImage(url: string, dest: string): Promise<LocalImageRef | null> {
  try {
    const res = await fetch(url, {
      headers: { "User-Agent": "EnvironmentKit-ResearchCache/1.0" },
      signal: AbortSignal.timeout(25_000),
    });
    if (!res.ok) return null;
    const buf = Buffer.from(await res.arrayBuffer());
    if (buf.length < 200 || !isRasterImageBuffer(buf)) {
      if (existsSync(dest)) unlinkSync(dest);
      return null;
    }
    writeFileSync(dest, buf);
    return {
      sourceUrl: url,
      relativePath: dest.replace(join(hubRoot, ""), "").replace(/^\//, ""),
      bytes: buf.length,
      cachedUtc: new Date().toISOString(),
    };
  } catch {
    return null;
  }
}

/** Remove ref-*.png that are HTML error pages from prior failed fetches. */
function pruneInvalidCachedImages(imagesRoot: string): number {
  let removed = 0;
  if (!existsSync(imagesRoot)) return 0;
  for (const entryId of readdirSync(imagesRoot)) {
    const dir = join(imagesRoot, entryId);
    try {
      if (!statSync(dir).isDirectory()) continue;
    } catch {
      continue;
    }
    for (const name of readdirSync(dir)) {
      if (!/^ref-\d+\.png$/i.test(name)) continue;
      const path = join(dir, name);
      try {
        const head = readFileSync(path).subarray(0, 16);
        if (!isRasterImageBuffer(head)) {
          unlinkSync(path);
          removed++;
        }
      } catch {
        /* ignore */
      }
    }
  }
  return removed;
}

function loadExistingImageRef(
  entryId: string,
  index: number,
  sourceUrl: string,
  dest: string
): LocalImageRef | null {
  if (!existsSync(dest)) return null;
  try {
    const buf = readFileSync(dest);
    if (!isRasterImageBuffer(buf.subarray(0, 16))) return null;
    return {
      sourceUrl,
      relativePath: `Assets/EnvironmentKit/ResearchCache/images/${entryId}/ref-${index}.png`,
      bytes: buf.length,
      cachedUtc: new Date().toISOString(),
    };
  } catch {
    return null;
  }
}

async function hydrateImages(entry: CacheEntry): Promise<CacheEntry> {
  if (!entry.imageUrls?.length) return entry;
  if (!fetchMissingImages && !fetchAllImages) return entry;

  const { images } = cachePaths(hubRoot);
  const imgDir = join(images, entry.id);
  mkdirSync(imgDir, { recursive: true });

  const localImages: LocalImageRef[] = [];
  let reused = 0;
  let fetched = 0;

  for (let i = 0; i < entry.imageUrls.length; i++) {
    const url = entry.imageUrls[i];
    const dest = join(imgDir, `ref-${i}.png`);

    if (!fetchAllImages) {
      const existing = loadExistingImageRef(entry.id, i, url, dest);
      if (existing) {
        localImages.push(existing);
        reused++;
        continue;
      }
    }

    const cached = await downloadImage(url, dest);
    if (cached) {
      localImages.push(cached);
      fetched++;
    }
  }

  if (reused || fetched) {
    console.log(
      `[ResearchCache] images/${entry.id}: reused=${reused} fetched=${fetched}`
    );
  }

  entry.localImages = localImages.length ? localImages : undefined;
  return entry;
}

async function main() {
  const rung = parseRungArg(process.argv);
  const paths = cachePaths(hubRoot);
  mkdirSync(paths.root, { recursive: true });
  mkdirSync(paths.entries, { recursive: true });
  mkdirSync(paths.images, { recursive: true });
  mkdirSync(paths.categories, { recursive: true });

  const pruned = pruneInvalidCachedImages(paths.images);
  if (pruned > 0) console.log(`[ResearchCache] Removed ${pruned} invalid cached image(s) (HTML/non-image).`);

  let entries = buildAllCatalogEntries();
  if (fetchMissingImages) {
    const withUrls = entries.filter((e) => (e.imageUrls?.length ?? 0) > 0);
    console.log(
      fetchAllImages
        ? `[ResearchCache] Pulling ALL reference images (HTTP) — ${withUrls.length} entries…`
        : `[ResearchCache] Pulling missing reference images — ${withUrls.length} entries (reuse on-disk PNGs)…`
    );
    const hydrated: CacheEntry[] = [];
    let idx = 0;
    for (const e of entries) {
      if (!(e.imageUrls?.length ?? 0)) {
        hydrated.push(e);
        continue;
      }
      idx++;
      console.log(`[ResearchCache] images ${idx}/${withUrls.length} — ${e.id}`);
      hydrated.push(await hydrateImages(e));
    }
    entries = hydrated;
  }

  for (const e of entries) writeEntryFiles(hubRoot, e);

  const index = buildIndexFromEntries(entries, hubRoot);
  writeFileSync(paths.index, JSON.stringify(index, null, 2), "utf8");
  writeCategoryIndexes(hubRoot, index);

  const floridaTerrain = buildFloridaTerrainSummary(hubRoot);
  const pointer = {
    generatedUtc: index.generatedUtc,
    indexPath: RESEARCH_CACHE_INDEX_REL,
    activeRung: rung ?? null,
    policy:
      "Mandatory pull: refresh metadata every sync; reuse valid cached images; fetch missing/invalid previews; county hillshades via sync-research-pull.",
    stats: index.stats,
    floridaTerrain,
    imagePull: {
      fetchMissingImages,
      fetchAllImages,
      withLocalBytes: index.stats.withFetchedBytes,
    },
    attribution:
      "Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_DATA_ATTRIBUTION.md",
    usage:
      "Read index + categories/{category}/index.json + entries/{id}/content.md before web search. Images under ResearchCache/images/{id}/ and fl-{county}-hillshade/ for panhandle terrain.",
  };
  const genDir = join(hubRoot, "Assets/EnvironmentKit/Generated");
  mkdirSync(genDir, { recursive: true });
  writeFileSync(
    join(hubRoot, RESEARCH_CACHE_GENERATED_REL),
    JSON.stringify(pointer, null, 2),
    "utf8"
  );

  const readme = join(paths.root, "README.md");
  writeFileSync(
    readme,
    [
      "# Environment Kit Research Cache",
      "",
      "Categorized, serialized research pulled from proven 2025–2026 catalog URLs.",
      "",
      "- `index.json` — master catalog",
      "- `categories/{category}/index.json` — browse by topic",
      "- `entries/{id}/meta.json` + `content.md` — serialized summaries (no API)",
      "- `images/{id}/manifest.json` + `ref-*.png` — reference images (optional `--fetch-images`)",
      "- `images/fl-{bay|washington|jackson|calhoun}-hillshade/hillshade.png` — county terrain subsets (`npm run sync-florida-hillshades`)",
      "",
      "Refresh: `npm run sync-research-cache` in Tools/cave-grader",
      "County hillshades: `npm run sync-florida-hillshades` (USGS public elevation; cave structure only)",
      "Aquifer/karst refs: Floridan structural surfaces (USGS DS 926) — ignore water when sculpting caves",
      "",
      "## Attribution",
      "",
      "Government and open geospatial data credits:",
      "`Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_DATA_ATTRIBUTION.md`",
      "",
    ].join("\n"),
    "utf8"
  );

  console.log(
    `[ResearchCache] Synced ${index.stats.totalEntries} entries (${index.stats.provenEntries} proven) → ${RESEARCH_CACHE_INDEX_REL}`
  );
  if (fetchMissingImages) {
    console.log(
      `[ResearchCache] Local images on disk (with bytes): ${index.stats.withFetchedBytes}`
    );
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
