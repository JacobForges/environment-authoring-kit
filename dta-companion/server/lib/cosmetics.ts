/**
 * Copyright (c) 2026 JacobForges — DTA Training Companion™
 * SPDX-License-Identifier: SEE LICENSE
 */

import fs from "fs";
import path from "path";
import { companionRoot, syncFolder } from "./paths.ts";

export type CosmeticSlot = "helmet" | "visor" | "chassis" | "trail" | "emblem" | "voice";

export type CosmeticRarity = "Common" | "Rare" | "Epic" | "Legendary";

export interface CatalogCosmeticItem {
  id: string;
  name: string;
  description: string;
  slot: CosmeticSlot;
  rarity: CosmeticRarity;
  priceGold: number;
  priceGems: number;
  previewColor?: string;
  iconKey?: string;
}

export interface CosmeticItem extends CatalogCosmeticItem {
  owned: boolean;
  equipped: boolean;
}

export interface AgentAppearance {
  agentId: string;
  equipped: Record<CosmeticSlot, string | null>;
}

interface AgentCosmeticState {
  owned: string[];
  equipped: Record<CosmeticSlot, string | null>;
}

export interface CosmeticsStore {
  currencyGold: number;
  currencyGems: number;
  agents: Record<string, AgentCosmeticState>;
}

const SLOTS: CosmeticSlot[] = ["helmet", "visor", "chassis", "trail", "emblem", "voice"];

const DEFAULT_EQUIPPED = (): Record<CosmeticSlot, string | null> => ({
  helmet: null,
  visor: null,
  chassis: "chassis_slate",
  trail: null,
  emblem: null,
  voice: null,
});

const DEFAULT_STORE: CosmeticsStore = {
  currencyGold: 450,
  currencyGems: 15,
  agents: {},
};

function cosmeticsStorePath() {
  return path.join(companionRoot(), "cosmetics.json");
}

function catalogPath() {
  return path.join(process.cwd(), "catalog", "cosmetics.json");
}

export function loadCatalog(): CatalogCosmeticItem[] {
  const raw = JSON.parse(fs.readFileSync(catalogPath(), "utf8")) as { items: CatalogCosmeticItem[] };
  return raw.items;
}

export function loadCosmeticsStore(): CosmeticsStore {
  try {
    if (fs.existsSync(cosmeticsStorePath())) {
      const parsed = JSON.parse(fs.readFileSync(cosmeticsStorePath(), "utf8")) as CosmeticsStore;
      return { ...DEFAULT_STORE, ...parsed, agents: parsed.agents || {} };
    }
  } catch {
    // ignore corrupt file
  }
  return { ...DEFAULT_STORE, agents: {} };
}

export function saveCosmeticsStore(store: CosmeticsStore) {
  fs.mkdirSync(companionRoot(), { recursive: true });
  fs.writeFileSync(cosmeticsStorePath(), JSON.stringify(store, null, 2));
}

function agentState(store: CosmeticsStore, agentId: string): AgentCosmeticState {
  if (!store.agents[agentId]) {
    store.agents[agentId] = {
      owned: ["chassis_slate"],
      equipped: DEFAULT_EQUIPPED(),
    };
  }
  return store.agents[agentId];
}

function mergeInventory(store: CosmeticsStore, agentId: string): CosmeticItem[] {
  const state = agentState(store, agentId);
  const equippedSet = new Set(
    SLOTS.map((s) => state.equipped[s]).filter((id): id is string => !!id),
  );
  return loadCatalog().map((item) => ({
    ...item,
    owned: state.owned.includes(item.id) || item.id === "chassis_slate",
    equipped: equippedSet.has(item.id),
  }));
}

export function getCosmeticsInventory(agentId: string) {
  const store = loadCosmeticsStore();
  const state = agentState(store, agentId);
  return {
    agentId,
    currencyGold: store.currencyGold,
    currencyGems: store.currencyGems,
    items: mergeInventory(store, agentId),
    appearance: {
      agentId,
      equipped: { ...state.equipped },
    } satisfies AgentAppearance,
  };
}

function writeAppearanceSync(agentId: string, equipped: Record<CosmeticSlot, string | null>) {
  const folder = syncFolder(agentId);
  fs.mkdirSync(folder, { recursive: true });
  const catalog = loadCatalog();
  const payload = {
    agentId,
    equipped,
    resolved: Object.fromEntries(
      SLOTS.map((slot) => {
        const id = equipped[slot];
        const item = id ? catalog.find((c) => c.id === id) : null;
        return [slot, item ? { id: item.id, previewColor: item.previewColor, iconKey: item.iconKey } : null];
      }),
    ),
    updatedUtc: new Date().toISOString(),
  };
  fs.writeFileSync(path.join(folder, "agent_appearance.json"), JSON.stringify(payload, null, 2));
}

export function equipCosmetic(agentId: string, slot: CosmeticSlot, itemId: string | null) {
  if (!SLOTS.includes(slot)) {
    return { success: false as const, message: "Invalid cosmetic slot." };
  }
  const store = loadCosmeticsStore();
  const state = agentState(store, agentId);
  const catalog = loadCatalog();

  if (itemId) {
    const item = catalog.find((c) => c.id === itemId);
    if (!item) return { success: false as const, message: "Unknown cosmetic item." };
    if (item.slot !== slot) return { success: false as const, message: "Item does not match slot." };
    if (!state.owned.includes(itemId) && itemId !== "chassis_slate") {
      return { success: false as const, message: "Item not owned." };
    }
    state.equipped[slot] = itemId;
  } else {
    state.equipped[slot] = slot === "chassis" ? "chassis_slate" : null;
  }

  saveCosmeticsStore(store);
  writeAppearanceSync(agentId, state.equipped);
  return { success: true as const, appearance: { agentId, equipped: { ...state.equipped } } };
}

export function purchaseCosmetic(agentId: string, itemId: string) {
  const store = loadCosmeticsStore();
  const state = agentState(store, agentId);
  const catalog = loadCatalog();
  const item = catalog.find((c) => c.id === itemId);
  if (!item) return { success: false as const, message: "Unknown cosmetic item." };
  if (state.owned.includes(itemId)) return { success: false as const, message: "Already owned." };

  if (item.priceGems > 0) {
    if (store.currencyGems < item.priceGems) {
      return { success: false as const, message: "Not enough gems." };
    }
    store.currencyGems -= item.priceGems;
  } else if (item.priceGold > 0) {
    if (store.currencyGold < item.priceGold) {
      return { success: false as const, message: "Not enough gold." };
    }
    store.currencyGold -= item.priceGold;
  }

  state.owned.push(itemId);
  saveCosmeticsStore(store);
  return {
    success: true as const,
    currencyGold: store.currencyGold,
    currencyGems: store.currencyGems,
    items: mergeInventory(store, agentId),
  };
}

export function saveAgentAppearance(agentId: string, equipped: Partial<Record<CosmeticSlot, string | null>>) {
  const store = loadCosmeticsStore();
  const state = agentState(store, agentId);
  for (const slot of SLOTS) {
    if (slot in equipped) {
      const itemId = equipped[slot] ?? null;
      if (itemId && !state.owned.includes(itemId) && itemId !== "chassis_slate") {
        return { success: false as const, message: `Cannot equip unowned item on ${slot}.` };
      }
      state.equipped[slot] = itemId ?? (slot === "chassis" ? "chassis_slate" : null);
    }
  }
  saveCosmeticsStore(store);
  writeAppearanceSync(agentId, state.equipped);
  return { success: true as const, appearance: { agentId, equipped: { ...state.equipped } } };
}

export function syncCurrencyFromProfile(currencyGold: number, currencyGems: number) {
  const store = loadCosmeticsStore();
  store.currencyGold = currencyGold;
  store.currencyGems = currencyGems;
  saveCosmeticsStore(store);
}
