#!/usr/bin/env node
/**
 * Fetches/summarizes every URL in CaveBuildResearch.json + ResearchCache indexes.
 * Writes Assets/EnvironmentKit/Generated/CaveBuildResearchUrlDigest.json
 * and merges urlDigests + lastEnrichedUtc into CaveBuildResearch.json.
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const hubRoot = process.env.HUB_ROOT || path.resolve(__dirname, "../../../..");
const researchPath = path.join(hubRoot, "Assets/EnvironmentKit/Generated/CaveBuildResearch.json");
const digestPath = path.join(hubRoot, "Assets/EnvironmentKit/Generated/CaveBuildResearchUrlDigest.json");
const cacheIndexPath = path.join(hubRoot, "Assets/EnvironmentKit/ResearchCache/index.json");

function buildLocalUrlIndex(research, cacheIndex) {
  const map = new Map();
  const add = (url, rec) => {
    if (!url || !url.startsWith("http")) return;
    const key = canonicalUrl(url);
    const prev = map.get(key) || {};
    map.set(key, { ...prev, ...rec, url: key });
  };

  if (research?.papers) {
    for (const p of research.papers) {
      const summary = [p.topics, p.venue, p.lab].filter(Boolean).join(" — ");
      add(p.url, { title: p.title, summary, source: "CaveBuildResearch.papers", year: p.year });
      if (p.pdfUrl) add(p.pdfUrl, { title: p.title, summary, source: "CaveBuildResearch.papers.pdf" });
    }
  }
  if (research?.engineReferences) {
    for (const e of research.engineReferences) {
      const summary = [e.topic, e.useInProject].filter(Boolean).join(" — ");
      add(e.url, { title: e.title || e.id, summary, source: "CaveBuildResearch.engineReferences" });
    }
  }
  if (research?.labIndices) {
    for (const [lab, url] of Object.entries(research.labIndices)) {
      add(url, { title: lab, summary: `Lab index — ${lab}`, source: "CaveBuildResearch.labIndices" });
    }
  }
  if (cacheIndex?.entries) {
    const entryList = Array.isArray(cacheIndex.entries)
      ? cacheIndex.entries
      : Object.values(cacheIndex.entries);
    for (const e of entryList) {
      const ser = e.serialized?.summary || "";
      const summary = [e.category, e.topics, ser, e.useInProject].filter(Boolean).join(" — ");
      if (e.url) add(e.url, { title: e.title || e.id, summary, source: "ResearchCache.index" });
      if (e.pdfUrl) add(e.pdfUrl, { title: e.title || e.id, summary, source: "ResearchCache.index.pdf" });
    }
  }

  return map;
}

function loadCacheEntryMetas(hubRoot, cacheIndex) {
  const map = new Map();
  if (!cacheIndex?.entries) return map;
  const entryList = Array.isArray(cacheIndex.entries)
    ? cacheIndex.entries
    : Object.values(cacheIndex.entries);
  for (const e of entryList) {
    const metaPath = path.join(hubRoot, "Assets/EnvironmentKit/ResearchCache/entries", e.id, "meta.json");
    if (!fs.existsSync(metaPath)) continue;
    try {
      const meta = JSON.parse(fs.readFileSync(metaPath, "utf8"));
      const summary = [meta.summary, meta.useInProject, meta.category].filter(Boolean).join(" — ");
      if (meta.url) {
        const key = canonicalUrl(meta.url);
        map.set(key, {
          title: meta.title || e.id,
          summary,
          source: `ResearchCache/entries/${e.id}/meta.json`,
        });
      }
    } catch {
      /* skip */
    }
  }
  return map;
}

function collectUrls(obj, out = new Set()) {
  if (obj == null) return out;
  if (typeof obj === "string") {
    const m = obj.match(/https?:\/\/[^\s"'<>]+/g);
    if (m) m.forEach((u) => out.add(u.replace(/[.,;)]+$/, "")));
    return out;
  }
  if (Array.isArray(obj)) {
    obj.forEach((v) => collectUrls(v, out));
    return out;
  }
  if (typeof obj === "object") {
    for (const v of Object.values(obj)) collectUrls(v, out);
  }
  return out;
}

function canonicalUrl(u) {
  try {
    const url = new URL(u);
    if (url.hostname === "arxiv.org" && url.pathname.startsWith("/abs/")) {
      return `https://arxiv.org/abs/${url.pathname.split("/").pop()}`;
    }
    url.hash = "";
    return url.toString();
  } catch {
    return u;
  }
}

function arxivId(u) {
  const m = u.match(/arxiv\.org\/(?:abs|pdf)\/(\d{4}\.\d{4,5})/);
  return m ? m[1] : null;
}

async function fetchArxiv(id) {
  const api = `https://export.arxiv.org/api/query?id_list=${id}`;
  const res = await fetch(api, { signal: AbortSignal.timeout(15000) });
  if (!res.ok) return null;
  const xml = await res.text();
  const title = xml.match(/<title>([^<]+)<\/title>/g)?.[1]?.replace(/<title>|<\/title>/g, "")?.trim();
  const summary = xml.match(/<summary>([\s\S]*?)<\/summary>/)?.[1]?.replace(/\s+/g, " ").trim();
  return { title, summary };
}

async function fetchHtmlMeta(u) {
  const res = await fetch(u, {
    signal: AbortSignal.timeout(12000),
    headers: { "User-Agent": "EnvironmentKit-ResearchBot/1.0 (local dev)" },
  });
  if (!res.ok) return { status: res.status };
  const html = (await res.text()).slice(0, 120000);
  const title =
    html.match(/<meta[^>]+property=["']og:title["'][^>]+content=["']([^"']+)["']/i)?.[1] ||
    html.match(/<title>([^<]{4,200})<\/title>/i)?.[1]?.trim();
  const description =
    html.match(/<meta[^>]+name=["']description["'][^>]+content=["']([^"']+)["']/i)?.[1] ||
    html.match(/<meta[^>]+property=["']og:description["'][^>]+content=["']([^"']+)["']/i)?.[1];
  return { status: res.status, title, description };
}

function projectRelevance(url, title, summary) {
  const text = `${url} ${title || ""} ${summary || ""}`.toLowerCase();
  const tags = [];
  if (/terrain|heightmap|lidar|dem|elevation|hillshade|usgs|noaa|florida|karst|aquifer/.test(text))
    tags.push("terrain");
  if (/navmesh|navigation|walk|pathfind|playtest|bot|qa|test/.test(text)) tags.push("playtest");
  if (/mesh|procedural|pcg|level|cave|environment|3d/.test(text)) tags.push("environment");
  if (/combat|agent|rl|reward|imitation/.test(text)) tags.push("combat_ai");
  if (/light|material|pbr|render|fog|atmosphere/.test(text)) tags.push("visual");
  if (/unity|unreal|engine|navmeshbuilder/.test(text)) tags.push("engine");
  if (tags.length === 0) tags.push("reference");
  return tags;
}

function applyLocalFallback(entry, localIndex) {
  const local = localIndex.get(entry.url);
  if (!local) return false;
  if (!entry.title && local.title) entry.title = local.title;
  if ((!entry.summary || entry.summary === "fetch failed") && local.summary) {
    entry.summary = local.summary;
  }
  entry.localSource = local.source;
  if (entry.status === "error" || entry.status === "pending") {
    entry.status = "cache_fallback";
  }
  return true;
}

async function enrichUrl(rawUrl, localIndex) {
  const url = canonicalUrl(rawUrl);
  const entry = {
    url,
    fetchedUtc: new Date().toISOString(),
    status: "pending",
    title: "",
    summary: "",
    projectTags: [],
    projectNotes: "",
  };

  const aid = arxivId(url);
  try {
    if (aid) {
      const arxiv = await fetchArxiv(aid);
      if (arxiv) {
        entry.status = "ok";
        entry.title = arxiv.title || "";
        entry.summary = (arxiv.summary || "").slice(0, 1200);
      } else {
        entry.status = "arxiv_miss";
      }
    } else if (/\.(pdf|zip|gdb|png|jpg)(\?|$)/i.test(url)) {
      entry.status = "binary_skip";
      entry.summary = "Binary/asset URL — use local cache or dataset metadata, not HTML fetch.";
    } else if (url.includes("ieeexplore.ieee.org")) {
      entry.status = "paywall_hint";
      entry.summary =
        "IEEE Xplore — use paper title from CaveBuildResearch.json; apply PCG/spatial-interface/CoG findings to layout grading.";
    } else {
      const meta = await fetchHtmlMeta(url);
      entry.status = meta.status === 200 ? "ok" : `http_${meta.status}`;
      entry.title = meta.title || "";
      entry.summary = (meta.description || "").slice(0, 1200);
    }
  } catch (e) {
    entry.status = "error";
    entry.summary = String(e.message || e).slice(0, 200);
  }

  applyLocalFallback(entry, localIndex);
  entry.projectTags = projectRelevance(url, entry.title, entry.summary);
  entry.projectNotes = buildProjectNotes(url, entry);
  return entry;
}

function buildProjectNotes(url, entry) {
  const u = url.toLowerCase();
  if (u.includes("terrain-heightmaps") || u.includes("terrain-painttexture"))
    return "Unity 6 heightmap/splat workflow for surface polish phases — avoid crater bowls, keep walkable slopes.";
  if (u.includes("navigation") || u.includes("navmesh"))
    return "Bake surface+cave NavMesh before playtest bot; human-scale agent radius must match Player CC.";
  if (u.includes("meshdata"))
    return "RouteTerrain MeshData strips — meat-loop additive only, no full cave teleport.";
  if (u.includes("usgs") || u.includes("noaa") || u.includes("lidar") || u.includes("3dep"))
    return "Florida panhandle bare-earth structure only — no water-table DEM for void layout; mouth on walkable surface.";
  if (u.includes("seed") && u.includes("testing"))
    return "AAA ML playtesting at scale — informs automated playtest bot + 60-phase polish ladder before bot run.";
  if (u.includes("sony") && u.includes("automating-gameplay"))
    return "Human-equivalent PS5 play automation — align bot walk/run/jump/attack/defend probe.";
  if (u.includes("2510.15120") || u.includes("2503.21474"))
    return "PCG quality metrics + Unity DRL levels — use for route probe grading and layout acceptance.";
  if (u.includes("guerrilla") || u.includes("vista"))
    return "Horizon vista composition — trail sightlines and opening readability on surface.";
  if (entry.summary) return entry.summary.slice(0, 280);
  return "Indexed for Environment Kit cave/surface pipeline — see ResearchCache entry if present.";
}

async function main() {
  const urls = new Set();
  let research = null;
  let cacheIndex = null;
  if (fs.existsSync(researchPath)) {
    research = JSON.parse(fs.readFileSync(researchPath, "utf8"));
    collectUrls(research, urls);
  }
  if (fs.existsSync(cacheIndexPath)) {
    cacheIndex = JSON.parse(fs.readFileSync(cacheIndexPath, "utf8"));
    collectUrls(cacheIndex, urls);
  }

  const localIndex = buildLocalUrlIndex(research, cacheIndex);
  for (const [url, rec] of loadCacheEntryMetas(hubRoot, cacheIndex)) {
    const prev = localIndex.get(url) || {};
    localIndex.set(url, { ...prev, ...rec, url });
  }

  const list = [...urls]
    .filter((u) => u.startsWith("http") && !u.includes("iec.ch"))
    .map(canonicalUrl)
    .sort();

  console.log(`Enriching ${list.length} URLs…`);
  const digests = [];
  for (let i = 0; i < list.length; i++) {
    const u = list[i];
    process.stderr.write(`[${i + 1}/${list.length}] ${u.slice(0, 72)}…\n`);
    digests.push(await enrichUrl(u, localIndex));
    await new Promise((r) => setTimeout(r, 350));
  }

  const out = {
    generatedUtc: new Date().toISOString(),
    hubRoot,
    urlCount: digests.length,
    policy:
      "Online enrichment + ResearchCache/CaveBuildResearch fallback for Environment Kit. Re-run before major builds or after cache pull.",
    localIndexCount: localIndex.size,
    entries: digests,
  };

  fs.mkdirSync(path.dirname(digestPath), { recursive: true });
  fs.writeFileSync(digestPath, JSON.stringify(out, null, 2));
  console.log(`Wrote ${digestPath}`);

  if (fs.existsSync(researchPath)) {
    const research = JSON.parse(fs.readFileSync(researchPath, "utf8"));
    research.lastUrlEnrichmentUtc = out.generatedUtc;
    research.urlDigests = digests;
    research.urlDigestIndexPath = "Assets/EnvironmentKit/Generated/CaveBuildResearchUrlDigest.json";
    fs.writeFileSync(researchPath, JSON.stringify(research, null, 2));
    console.log(`Merged urlDigests into ${researchPath}`);
  }
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
