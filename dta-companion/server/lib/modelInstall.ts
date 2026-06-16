import fs from "fs";
import path from "path";
import { buildCatalog, resolveDownloadUrl, type CatalogEntry } from "./modelCatalog.ts";
import {
  assignModelSlot,
  loadSettings,
} from "./settings.ts";
import {
  modelSlotPath,
  syncFolder,
  unityAgentAdapterPath,
} from "./paths.ts";

async function downloadToFile(url: string, target: string, headers?: Record<string, string>) {
  if (url.startsWith("file://")) {
    const src = url.replace("file://", "");
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.copyFileSync(src, target);
    return;
  }

  const res = await fetch(url, { headers, signal: AbortSignal.timeout(120_000) });
  if (!res.ok) throw new Error(`Download failed (${res.status}): ${url}`);
  const buf = Buffer.from(await res.arrayBuffer());
  if (buf.length < 128) throw new Error("Downloaded file too small to be ONNX.");
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, buf);
}

export async function installModelToSlot(options: {
  agentId?: string;
  catalogId?: string;
  url?: string;
  slot: number;
}): Promise<{ success: boolean; message: string }> {
  const slot = options.slot;
  if (slot < 1 || slot > 3) {
    return { success: false, message: "Slot must be 1, 2, or 3." };
  }

  const catalog = await buildCatalog();
  const entry = catalog.models.find((m) => m.id === options.catalogId);
  const downloadUrl = resolveDownloadUrl(entry, options.url);
  if (!downloadUrl) {
    return { success: false, message: "No download URL for this model." };
  }

  const settings = loadSettings();
  const headers: Record<string, string> = {};
  if (settings.huggingFaceToken && downloadUrl.includes("huggingface.co")) {
    headers.Authorization = `Bearer ${settings.huggingFaceToken}`;
  }

  const slotFile = modelSlotPath(slot);
  await downloadToFile(downloadUrl, slotFile, headers);

  assignModelSlot(slot, {
    catalogId: entry?.id || options.catalogId || "custom-url",
    displayName: entry?.name || `Custom slot ${slot}`,
    source: entry?.source || "url",
  });

  let message = `Saved to slot ${slot}: ${slotFile}`;

  if (options.agentId) {
    const agentPath = unityAgentAdapterPath(options.agentId);
    const agentDir = path.dirname(agentPath);
    fs.mkdirSync(agentDir, { recursive: true });
    fs.copyFileSync(slotFile, agentPath);

    const syncDir = syncFolder(options.agentId);
    fs.mkdirSync(syncDir, { recursive: true });
    fs.copyFileSync(slotFile, path.join(syncDir, "gameplay_adapter.onnx"));

    const manifest = {
      checkpointVersion: Math.floor(Date.now() / 1000),
      agentId: options.agentId,
      trainUtc: new Date().toISOString(),
      focusActivity: "market_install",
      onnxFile: "gameplay_adapter.onnx",
      slot,
      catalogId: entry?.id,
    };
    fs.writeFileSync(path.join(syncDir, "pending_import.json"), JSON.stringify(manifest, null, 2));
    message += `\nCopied to game agent + sync folder (import on next launch).`;
  }

  return { success: true, message };
}

export function listInstalledSlots() {
  return loadSettings().modelSlots || [];
}

export async function findCatalogEntry(catalogId?: string): Promise<CatalogEntry | undefined> {
  if (!catalogId) return undefined;
  const catalog = await buildCatalog();
  return catalog.models.find((m) => m.id === catalogId);
}
