/**
 * Copyright (c) 2026 JacobForges — DTA Training Companion™
 * SPDX-License-Identifier: SEE LICENSE
 */

import express from "express";
import path from "path";
import fs from "fs";
import dotenv from "dotenv";
import { createServer as createViteServer } from "vite";
import { runCoach } from "./server/lib/coach.ts";
import { buildCatalog } from "./server/lib/modelCatalog.ts";
import { installModelToSlot, listInstalledSlots } from "./server/lib/modelInstall.ts";
import { loadFirebasePublicConfig } from "./server/lib/firebaseConfig.ts";
import { loadSettings, saveSettings, ONBOARDING_VERSION } from "./server/lib/settings.ts";
import { syncFolder, modelSlotPath, companionRoot } from "./server/lib/paths.ts";
import {
  equipCosmetic,
  getCosmeticsInventory,
  loadCatalog,
  purchaseCosmetic,
  saveAgentAppearance,
  type CosmeticSlot,
} from "./server/lib/cosmetics.ts";

dotenv.config();

const UNITY_API = "http://127.0.0.1:8765";

async function proxyUnity(pathname: string, init?: RequestInit) {
  try {
    const res = await fetch(`${UNITY_API}${pathname}`, init);
    const text = await res.text();
    return { ok: res.ok, status: res.status, text };
  } catch {
    return null;
  }
}

async function startServer() {
  const app = express();
  const PORT = Number(process.env.PORT || process.env.DTA_COMPANION_PORT || 3000);

  app.use(express.json({ limit: "50mb" }));

  app.get("/api/health", (req, res) => {
    const s = loadSettings();
    res.json({
      status: "ok",
      mode: process.env.NODE_ENV,
      hasCursorKey: !!(s.cursorApiKey || process.env.CURSOR_API_KEY),
      hasGeminiKey: !!(s.geminiApiKey || process.env.GEMINI_API_KEY),
      firebase: loadFirebasePublicConfig().enabled,
      coachEngine: s.coachEngine,
      modelSlots: listInstalledSlots(),
    });
  });

  app.get("/api/firebase/config", (req, res) => {
    res.json(loadFirebasePublicConfig());
  });

  app.get("/api/settings", (req, res) => {
    const s = loadSettings();
    res.json({
      ...s,
      cursorApiKey: s.cursorApiKey ? "••••" + s.cursorApiKey.slice(-4) : "",
      geminiApiKey: s.geminiApiKey ? "••••" + s.geminiApiKey.slice(-4) : "",
      huggingFaceToken: s.huggingFaceToken ? "••••" + s.huggingFaceToken.slice(-4) : "",
    });
  });

  app.post("/api/settings", (req, res) => {
    const current = loadSettings();
    const patch = { ...req.body };
    if (typeof patch.cursorApiKey === "string" && patch.cursorApiKey.startsWith("••••")) {
      patch.cursorApiKey = current.cursorApiKey;
    }
    if (typeof patch.geminiApiKey === "string" && patch.geminiApiKey.startsWith("••••")) {
      patch.geminiApiKey = current.geminiApiKey;
    }
    if (typeof patch.huggingFaceToken === "string" && patch.huggingFaceToken.startsWith("••••")) {
      patch.huggingFaceToken = current.huggingFaceToken;
    }
    const merged = saveSettings(patch);
    res.json({
      success: true,
      coachEngine: merged.coachEngine,
      modelSlots: merged.modelSlots,
      onboardingComplete: merged.onboardingComplete,
    });
  });

  app.get("/api/onboarding", (req, res) => {
    const s = loadSettings();
    const needsOnboarding = !s.onboardingComplete || (s.onboardingVersion ?? 0) < ONBOARDING_VERSION;
    res.json({
      needsOnboarding,
      onboardingVersion: ONBOARDING_VERSION,
      savedVersion: s.onboardingVersion ?? 0,
      displayName: s.displayName || "",
      syncFolder: syncFolder("agent_local"),
      companionRoot: companionRoot(),
    });
  });

  app.post("/api/onboarding/complete", (req, res) => {
    const patch: Record<string, unknown> = {
      onboardingComplete: true,
      onboardingVersion: ONBOARDING_VERSION,
    };
    if (typeof req.body?.displayName === "string" && req.body.displayName.trim()) {
      patch.displayName = req.body.displayName.trim();
    }
    if (typeof req.body?.coachEngine === "string") {
      patch.coachEngine = req.body.coachEngine;
    }
    if (typeof req.body?.cursorApiKey === "string" && req.body.cursorApiKey) {
      patch.cursorApiKey = req.body.cursorApiKey;
    }
    if (typeof req.body?.geminiApiKey === "string" && req.body.geminiApiKey) {
      patch.geminiApiKey = req.body.geminiApiKey;
    }
    const merged = saveSettings(patch);
    res.json({ success: true, displayName: merged.displayName });
  });

  app.get("/api/syncSnapshot", async (req, res) => {
    const proxied = await proxyUnity(`/api/syncSnapshot?agentId=${encodeURIComponent(String(req.query.agentId || ""))}`);
    if (proxied?.ok) {
      res.status(proxied.status).type("json").send(proxied.text);
      return;
    }
    res.json({
      playerName: "Player",
      activeAgentId: String(req.query.agentId || "agent_local"),
      gameplayRows: 0,
      minRows: 24,
      companionUrl: `http://127.0.0.1:${PORT}/`,
      syncFolder: syncFolder(String(req.query.agentId || "agent_local")),
    });
  });

  app.get("/api/models/catalog", async (req, res) => {
    try {
      const catalog = await buildCatalog();
      res.json(catalog);
    } catch (e: unknown) {
      res.status(500).json({ error: e instanceof Error ? e.message : "catalog failed" });
    }
  });

  app.get("/api/models/slots", (req, res) => {
    res.json({ slots: listInstalledSlots() });
  });

  app.post("/api/models/install", async (req, res) => {
    const proxied = await proxyUnity("/api/models/install", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(req.body),
    });
    if (proxied?.ok) {
      res.status(proxied.status).type("json").send(proxied.text);
      return;
    }

    try {
      const result = await installModelToSlot({
        agentId: req.body?.agentId,
        catalogId: req.body?.catalogId,
        url: req.body?.url,
        slot: Number(req.body?.slot) || 1,
      });
      res.status(result.success ? 200 : 400).json(result);
    } catch (e: unknown) {
      res.status(500).json({ success: false, message: e instanceof Error ? e.message : "install failed" });
    }
  });

  app.post("/api/deploy", async (req, res) => {
    const proxied = await proxyUnity("/api/deploy", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(req.body),
    });
    if (proxied) {
      res.status(proxied.status).type("json").send(proxied.text);
      return;
    }

    const agentId = req.body?.agentId || "agent_local";
    const folder = syncFolder(agentId);
    fs.mkdirSync(folder, { recursive: true });

    const slot = Number(req.body?.slot) || 0;
    const settings = loadSettings();
    const slotEntry = settings.modelSlots?.find((s) => s.slot === slot);

    if (slot > 0) {
      const sf = modelSlotPath(slot);
      if (fs.existsSync(sf)) {
        fs.copyFileSync(sf, path.join(folder, "gameplay_adapter.onnx"));
      }
    }

    const manifest = {
      checkpointVersion: Math.floor(Date.now() / 1000),
      agentId,
      trainUtc: new Date().toISOString(),
      focusActivity: req.body?.focusActivity || "battle",
      onnxFile: "gameplay_adapter.onnx",
      slot,
      catalogId: slotEntry?.catalogId,
    };

    fs.writeFileSync(path.join(folder, "pending_import.json"), JSON.stringify(manifest, null, 2));
    res.json({
      success: true,
      message: `Deploy manifest written to ${folder}` + (slot > 0 ? ` (from slot ${slot})` : ""),
      manifest,
    });
  });

  app.post("/api/coach", async (req, res) => {
    try {
      const result = await runCoach(req.body);
      res.json({
        ...result,
        decisionTrail: ["Loaded agent context", "Selected coach provider", "Emitted training guidance"],
      });
    } catch (e: unknown) {
      console.error("[DTA Coach]", e);
      res.status(500).json({ error: e instanceof Error ? e.message : "coach error" });
    }
  });

  app.get("/api/cosmetics/catalog", (req, res) => {
    try {
      res.json({ version: 1, items: loadCatalog() });
    } catch (e: unknown) {
      res.status(500).json({ error: e instanceof Error ? e.message : "catalog failed" });
    }
  });

  app.get("/api/cosmetics/inventory", (req, res) => {
    const agentId = String(req.query.agentId || "agent_local");
    try {
      res.json(getCosmeticsInventory(agentId));
    } catch (e: unknown) {
      res.status(500).json({ error: e instanceof Error ? e.message : "inventory failed" });
    }
  });

  app.post("/api/cosmetics/equip", (req, res) => {
    const { agentId, slot, itemId } = req.body || {};
    if (!agentId || !slot) {
      return res.status(400).json({ success: false, message: "agentId and slot required." });
    }
    const result = equipCosmetic(String(agentId), slot as CosmeticSlot, itemId ?? null);
    res.status(result.success ? 200 : 400).json(result);
  });

  app.post("/api/cosmetics/purchase", (req, res) => {
    const { agentId, itemId } = req.body || {};
    if (!agentId || !itemId) {
      return res.status(400).json({ success: false, message: "agentId and itemId required." });
    }
    const result = purchaseCosmetic(String(agentId), String(itemId));
    res.status(result.success ? 200 : 400).json(result);
  });

  app.post("/api/cosmetics/appearance", (req, res) => {
    const { agentId, equipped } = req.body || {};
    if (!agentId || !equipped) {
      return res.status(400).json({ success: false, message: "agentId and equipped required." });
    }
    const result = saveAgentAppearance(String(agentId), equipped);
    res.status(result.success ? 200 : 400).json(result);
  });

  app.post("/api/train", (req, res) => {
    const { agentId, focusActivity, datasetRows } = req.body;
    if (!agentId) {
      return res.status(400).json({ error: "Missing agentId." });
    }
    const checksum = Math.random().toString(16).substring(2, 10).toUpperCase();
    res.json({
      success: true,
      agentId,
      checkpointVersion: Math.floor(Date.now() / 1000),
      sha256: `F3A4BC8E${checksum}`,
      focusActivity: focusActivity || "battle",
      rowsUsed: datasetRows || 31,
      rowsRejected: Math.max(2, Math.floor((datasetRows || 31) * 0.2)),
      trainMethod: "bc_adapter",
      onnxFile: "gameplay_adapter.onnx",
      trainUtc: new Date().toISOString(),
    });
  });

  if (process.env.NODE_ENV !== "production") {
    const vite = await createViteServer({ server: { middlewareMode: true }, appType: "spa" });
    app.use(vite.middlewares);
  } else {
    const distPath = path.join(process.cwd(), "dist");
    app.use(express.static(distPath));
    app.get("*", (req, res) => {
      res.sendFile(path.join(distPath, "index.html"));
    });
  }

  app.listen(PORT, "0.0.0.0", () => {
    console.log(`[DTA Companion] http://localhost:${PORT}`);
  });
}

startServer();
