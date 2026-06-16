export type CoachEngine = 'auto' | 'cursor' | 'gemini-3.5-flash' | 'local-gguf' | 'rules-only';

export function apiBase(): string {
  if (typeof window === 'undefined') return 'http://127.0.0.1:3000';
  const { port, protocol, hostname } = window.location;
  if (port) return `${protocol}//${hostname}:${port}`;
  return 'http://127.0.0.1:37123';
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${apiBase()}${path}`, {
    headers: { 'Content-Type': 'application/json', ...(init?.headers || {}) },
    ...init,
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({ error: res.statusText }));
    throw new Error(err.error || err.message || res.statusText);
  }
  return res.json() as Promise<T>;
}

export const companionApi = {
  health: () =>
    request<{
      status: string;
      hasCursorKey?: boolean;
      hasGeminiKey?: boolean;
      firebase?: boolean;
      coachEngine?: string;
    }>('/api/health'),
  firebaseConfig: () => request<{ enabled: boolean }>('/api/firebase/config'),
  syncSnapshot: (agentId?: string) =>
    request<{
      playerName: string;
      activeAgentId: string;
      gameplayRows: number;
      minRows: number;
      companionUrl: string;
      syncFolder: string;
    }>(`/api/syncSnapshot${agentId ? `?agentId=${encodeURIComponent(agentId)}` : ''}`),
  saveSettings: (body: Record<string, unknown>) =>
    request<{ success: boolean }>('/api/settings', { method: 'POST', body: JSON.stringify(body) }),
  loadSettings: () => request<Record<string, unknown>>('/api/settings'),
  onboarding: () =>
    request<{
      needsOnboarding: boolean;
      onboardingVersion: number;
      savedVersion: number;
      displayName: string;
      syncFolder: string;
      companionRoot: string;
    }>('/api/onboarding'),
  completeOnboarding: (body: {
    displayName?: string;
    coachEngine?: CoachEngine;
    cursorApiKey?: string;
    geminiApiKey?: string;
  }) =>
    request<{ success: boolean; displayName?: string }>('/api/onboarding/complete', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  coach: (body: Record<string, unknown>) =>
    request<{ text: string; confidence?: string; provenance?: string }>('/api/coach', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  train: (body: Record<string, unknown>) =>
    request<Record<string, unknown>>('/api/train', { method: 'POST', body: JSON.stringify(body) }),
  deploy: (body: Record<string, unknown>) =>
    request<{ success: boolean; message?: string; manifest?: Record<string, unknown> }>('/api/deploy', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  catalog: () =>
    request<{ version: number; models: ModelCatalogEntry[]; sources?: Record<string, number> }>(
      '/api/models/catalog',
    ),
  modelSlots: () => request<{ slots: ModelSlotInfo[] }>('/api/models/slots'),
  installModel: (body: { agentId: string; catalogId?: string; url?: string; slot: number }) =>
    request<{ success: boolean; message?: string }>('/api/models/install', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  cosmeticsCatalog: () =>
    request<{ version: number; items: import('../types').CosmeticItem[] }>('/api/cosmetics/catalog'),
  cosmeticsInventory: (agentId: string) =>
    request<import('../types').CosmeticsInventory>(
      `/api/cosmetics/inventory?agentId=${encodeURIComponent(agentId)}`,
    ),
  cosmeticsEquip: (body: { agentId: string; slot: string; itemId: string | null }) =>
    request<{ success: boolean; message?: string; appearance?: import('../types').AgentAppearance }>(
      '/api/cosmetics/equip',
      { method: 'POST', body: JSON.stringify(body) },
    ),
  cosmeticsPurchase: (body: { agentId: string; itemId: string }) =>
    request<{
      success: boolean;
      message?: string;
      currencyGold?: number;
      currencyGems?: number;
      items?: import('../types').CosmeticItem[];
    }>('/api/cosmetics/purchase', { method: 'POST', body: JSON.stringify(body) }),
  cosmeticsAppearance: (body: { agentId: string; equipped: Record<string, string | null> }) =>
    request<{ success: boolean; message?: string; appearance?: import('../types').AgentAppearance }>(
      '/api/cosmetics/appearance',
      { method: 'POST', body: JSON.stringify(body) },
    ),
};

export interface ModelSlotInfo {
  slot: number;
  catalogId: string;
  displayName: string;
  installedUtc: string;
  source?: string;
}

export interface ModelCatalogEntry {
  id: string;
  name: string;
  description: string;
  source: 'bundled' | 'github' | 'huggingface' | 'url';
  bundledResource?: string;
  url?: string;
  kind: string;
  license: string;
  editable?: boolean;
  repoId?: string;
}
