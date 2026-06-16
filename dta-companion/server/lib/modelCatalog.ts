import fs from "fs";
import path from "path";
import { hubRepoRoot } from "./paths.ts";
import { loadSettings } from "./settings.ts";

export interface CatalogEntry {
  id: string;
  name: string;
  description: string;
  source: "bundled" | "github" | "huggingface" | "url";
  url?: string;
  bundledResource?: string;
  kind: string;
  license: string;
  editable?: boolean;
  repoId?: string;
}

function loadBundled(): CatalogEntry[] {
  const hub = hubRepoRoot();
  const paths = [
    path.join(hub, "Assets", "StreamingAssets", "DTACompanion", "models-catalog.json"),
    path.join(process.cwd(), "catalog", "bundled-models.json"),
  ];
  for (const p of paths) {
    if (!fs.existsSync(p)) continue;
    try {
      const json = JSON.parse(fs.readFileSync(p, "utf8"));
      return json.models || [];
    } catch {
      // try next
    }
  }
  return [];
}

async function loadGithubManifest(): Promise<CatalogEntry[]> {
  const url =
    process.env.DTA_MODEL_CATALOG_URL ||
    process.env.GITHUB_MODEL_CATALOG_URL ||
    "https://raw.githubusercontent.com/JacobForges/environment-authoring-kit/main/dta-companion/catalog/github-models.json";

  try {
    const res = await fetch(url, { signal: AbortSignal.timeout(12_000) });
    if (!res.ok) return [];
    const json = (await res.json()) as { models?: CatalogEntry[] };
    return (json.models || []).map((m) => ({ ...m, source: m.source || "github" }));
  } catch {
    const local = path.join(process.cwd(), "catalog", "github-models.json");
    if (!fs.existsSync(local)) return [];
    try {
      const json = JSON.parse(fs.readFileSync(local, "utf8"));
      return json.models || [];
    } catch {
      return [];
    }
  }
}

async function loadHuggingFace(): Promise<CatalogEntry[]> {
  const settings = loadSettings();
  const headers: Record<string, string> = { Accept: "application/json" };
  if (settings.huggingFaceToken) {
    headers.Authorization = `Bearer ${settings.huggingFaceToken}`;
  }

  const queries = ["gameplay_adapter onnx", "competition gameplay onnx", "deep train academy"];
  const seen = new Set<string>();
  const out: CatalogEntry[] = [];

  for (const q of queries) {
    try {
      const url = `https://huggingface.co/api/models?search=${encodeURIComponent(q)}&limit=12&sort=downloads&direction=-1`;
      const res = await fetch(url, { headers, signal: AbortSignal.timeout(12_000) });
      if (!res.ok) continue;
      const models = (await res.json()) as Array<{ id: string; modelId?: string; pipeline_tag?: string }>;
      for (const m of models) {
        const id = m.id || m.modelId;
        if (!id || seen.has(id)) continue;
        seen.add(id);
        const onnxUrl = await resolveHfOnnxUrl(id, headers);
        if (!onnxUrl) continue;
        out.push({
          id: `hf-${id.replace(/\//g, "-")}`,
          name: id.split("/").pop() || id,
          description: `Hugging Face model ${id}. Downloads gameplay_adapter.onnx or first .onnx sibling.`,
          source: "huggingface",
          url: onnxUrl,
          repoId: id,
          kind: "adapter",
          license: "See model card on Hugging Face",
        });
      }
    } catch {
      // continue other queries
    }
  }

  return out.slice(0, 24);
}

async function resolveHfOnnxUrl(modelId: string, headers: Record<string, string>): Promise<string | null> {
  const candidates = [
    `https://huggingface.co/${modelId}/resolve/main/gameplay_adapter.onnx`,
    `https://huggingface.co/${modelId}/resolve/main/model.onnx`,
    `https://huggingface.co/${modelId}/resolve/main/adapter.onnx`,
  ];

  for (const url of candidates) {
    try {
      const res = await fetch(url, { method: "HEAD", headers, signal: AbortSignal.timeout(8_000) });
      if (res.ok) return url;
    } catch {
      // try next
    }
  }

  try {
    const treeUrl = `https://huggingface.co/api/models/${modelId}/tree/main`;
    const res = await fetch(treeUrl, { headers, signal: AbortSignal.timeout(8_000) });
    if (!res.ok) return null;
    const tree = (await res.json()) as Array<{ path: string; type: string }>;
    const onnx = tree.find((t) => t.type === "file" && t.path.endsWith(".onnx"));
    if (!onnx) return null;
    return `https://huggingface.co/${modelId}/resolve/main/${onnx.path}`;
  } catch {
    return null;
  }
}

function dedupe(models: CatalogEntry[]): CatalogEntry[] {
  const map = new Map<string, CatalogEntry>();
  for (const m of models) {
    if (!m.id) continue;
    map.set(m.id, m);
  }
  return [...map.values()];
}

export async function buildCatalog(): Promise<{ version: number; models: CatalogEntry[]; sources: Record<string, number> }> {
  const bundled = loadBundled();
  const github = await loadGithubManifest();
  const hf = await loadHuggingFace();
  const models = dedupe([...bundled, ...github, ...hf]);

  return {
    version: 2,
    models,
    sources: {
      bundled: bundled.length,
      github: github.length,
      huggingface: hf.length,
    },
  };
}

export function resolveDownloadUrl(entry: CatalogEntry | undefined, overrideUrl?: string): string | null {
  if (overrideUrl?.trim()) return overrideUrl.trim();
  if (!entry) return null;
  if (entry.url?.trim()) return entry.url.trim();
  if (entry.source === "bundled" && entry.bundledResource) {
    const hub = hubRepoRoot();
    const disk = path.join(hub, "Assets", "Resources", "Competition", "Models", entry.bundledResource);
    if (fs.existsSync(disk)) return `file://${disk}`;
  }
  return null;
}
