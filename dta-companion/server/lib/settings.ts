import fs from "fs";
import { companionRoot, settingsPath } from "./paths.ts";

export type CoachEngine =
  | "auto"
  | "cursor"
  | "gemini-3.5-flash"
  | "local-gguf"
  | "rules-only";

export interface ModelSlot {
  slot: number;
  catalogId: string;
  displayName: string;
  installedUtc: string;
  source?: string;
}

export interface CompanionSettings {
  coachEngine: CoachEngine;
  cursorApiKey: string;
  geminiApiKey: string;
  localGgufEndpoint: string;
  huggingFaceToken: string;
  modelSlots: ModelSlot[];
  onboardingComplete?: boolean;
  onboardingVersion?: number;
  displayName?: string;
}

/** Bump when onboarding copy or required steps change. */
export const ONBOARDING_VERSION = 1;

const DEFAULTS: CompanionSettings = {
  coachEngine: "auto",
  cursorApiKey: "",
  geminiApiKey: "",
  localGgufEndpoint: "http://127.0.0.1:8080/v1/chat/completions",
  huggingFaceToken: "",
  modelSlots: [],
  onboardingComplete: false,
  onboardingVersion: 0,
  displayName: "",
};

export function loadSettings(): CompanionSettings {
  try {
    if (fs.existsSync(settingsPath())) {
      return { ...DEFAULTS, ...JSON.parse(fs.readFileSync(settingsPath(), "utf8")) };
    }
  } catch {
    // ignore
  }
  return {
    ...DEFAULTS,
    cursorApiKey: process.env.CURSOR_API_KEY || "",
    geminiApiKey: process.env.GEMINI_API_KEY || "",
  };
}

export function saveSettings(patch: Partial<CompanionSettings>) {
  fs.mkdirSync(companionRoot(), { recursive: true });
  const merged = { ...loadSettings(), ...patch };
  fs.writeFileSync(settingsPath(), JSON.stringify(merged, null, 2));
  return merged;
}

export function assignModelSlot(slot: number, entry: { catalogId: string; displayName: string; source?: string }) {
  const settings = loadSettings();
  const slots = [...(settings.modelSlots || [])].filter((s) => s.slot !== slot);
  slots.push({
    slot,
    catalogId: entry.catalogId,
    displayName: entry.displayName,
    source: entry.source,
    installedUtc: new Date().toISOString(),
  });
  return saveSettings({ modelSlots: slots });
}
