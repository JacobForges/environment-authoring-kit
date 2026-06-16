/**
 * Copyright (c) 2026 JacobForges — DTA Training Companion™
 */

import React, { useEffect, useMemo, useState } from 'react';
import { Sparkles, Shirt, X } from 'lucide-react';
import { companionApi } from '../api/client';
import type { AgentAppearance, CosmeticItem, CosmeticSlot, PlayerProfile } from '../types';

const SLOTS: { id: CosmeticSlot; label: string }[] = [
  { id: 'helmet', label: 'Helmet' },
  { id: 'visor', label: 'Visor' },
  { id: 'chassis', label: 'Chassis' },
  { id: 'trail', label: 'Trail' },
  { id: 'emblem', label: 'Emblem' },
  { id: 'voice', label: 'Voice' },
];

const RARITY_COLORS: Record<string, string> = {
  Common: 'text-text-secondary border-border-subtle',
  Rare: 'text-accent-cyan border-accent-cyan/30',
  Epic: 'text-accent-violet border-accent-violet/30',
  Legendary: 'text-accent-amber border-accent-amber/30',
};

interface PageAgentEditorProps {
  activeAgentId: string;
  playerProfile: PlayerProfile;
  onCurrencyChange: (gold: number, gems: number) => void;
}

function AvatarPreview({ items, appearance }: { items: CosmeticItem[]; appearance: AgentAppearance }) {
  const bySlot = useMemo(() => {
    const map: Partial<Record<CosmeticSlot, CosmeticItem>> = {};
    for (const slot of SLOTS) {
      const id = appearance.equipped[slot.id];
      const item = id ? items.find((i) => i.id === id) : undefined;
      if (item) map[slot.id] = item;
    }
    return map;
  }, [items, appearance]);

  const chassis = bySlot.chassis?.previewColor || '#334155';
  const helmet = bySlot.helmet?.previewColor || '#475569';
  const visor = bySlot.visor?.previewColor || '#22D3EE';
  const trail = bySlot.trail?.previewColor || '#A78BFA';
  const emblem = bySlot.emblem?.previewColor || '#22D3EE';

  return (
    <div className="relative w-full max-w-xs mx-auto aspect-[3/4] flex items-end justify-center">
      <div
        className="absolute bottom-8 w-32 h-4 rounded-full blur-md opacity-60"
        style={{ backgroundColor: trail }}
      />
      <svg viewBox="0 0 200 280" className="w-full h-full drop-shadow-2xl">
        <ellipse cx="100" cy="248" rx="52" ry="12" fill={trail} opacity="0.35" />
        <rect x="62" y="130" width="76" height="110" rx="18" fill={chassis} stroke="#1E293B" strokeWidth="2" />
        <rect x="78" y="148" width="44" height="28" rx="6" fill={visor} opacity="0.85" />
        <path d="M72 95 Q100 55 128 95 L122 118 Q100 108 78 118 Z" fill={helmet} stroke="#0F172A" strokeWidth="2" />
        <circle cx="100" cy="168" r="10" fill={emblem} opacity="0.9" />
        <rect x="88" y="200" width="24" height="48" rx="8" fill={chassis} opacity="0.9" />
      </svg>
    </div>
  );
}

export default function PageAgentEditor({
  activeAgentId,
  playerProfile,
  onCurrencyChange,
}: PageAgentEditorProps) {
  const [activeSlot, setActiveSlot] = useState<CosmeticSlot>('helmet');
  const [items, setItems] = useState<CosmeticItem[]>([]);
  const [appearance, setAppearance] = useState<AgentAppearance | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const inv = await companionApi.cosmeticsInventory(activeAgentId);
      setItems(inv.items);
      setAppearance(inv.appearance);
      onCurrencyChange(inv.currencyGold, inv.currencyGems);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load cosmetics');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
  }, [activeAgentId]);

  const ownedInSlot = items.filter((i) => i.slot === activeSlot && i.owned);
  const equippedId = appearance?.equipped[activeSlot] ?? null;

  const handleEquip = async (itemId: string) => {
    setBusy(true);
    setError(null);
    try {
      const res = await companionApi.cosmeticsEquip({ agentId: activeAgentId, slot: activeSlot, itemId });
      if (res.appearance) setAppearance(res.appearance);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Equip failed');
    } finally {
      setBusy(false);
    }
  };

  const handleUnequip = async () => {
    setBusy(true);
    setError(null);
    try {
      const res = await companionApi.cosmeticsEquip({ agentId: activeAgentId, slot: activeSlot, itemId: null });
      if (res.appearance) setAppearance(res.appearance);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unequip failed');
    } finally {
      setBusy(false);
    }
  };

  if (loading || !appearance) {
    return (
      <div className="p-12 text-center text-text-secondary font-mono text-sm animate-pulse">
        Loading agent appearance for {activeAgentId}…
      </div>
    );
  }

  return (
    <div className="space-y-8">
      <div className="border-b border-border-subtle pb-6">
        <span className="text-xs font-mono text-accent-cyan font-bold uppercase tracking-wider">
          agent studio · character editor
        </span>
        <h2 className="text-3xl font-display font-bold text-white mt-1">Agent Model Editor</h2>
        <p className="text-text-secondary text-sm font-sans mt-1">
          Customize {playerProfile.displayName}&apos;s active agent — equip owned cosmetics per slot.
        </p>
      </div>

      {error && (
        <p className="text-xs font-mono text-accent-rose bg-accent-rose/10 border border-accent-rose/20 rounded-lg px-3 py-2">
          {error}
        </p>
      )}

      <div className="grid lg:grid-cols-12 gap-8">
        <div className="lg:col-span-5 bg-bg-panel border border-border-subtle rounded-2xl p-6">
          <div className="flex items-center justify-between mb-4">
            <span className="text-xs font-mono text-accent-violet uppercase">Preview</span>
            <span className="text-[10px] font-mono text-text-muted">{activeAgentId}</span>
          </div>
          <AvatarPreview items={items} appearance={appearance} />
          <div className="mt-6 grid grid-cols-3 gap-2 text-[10px] font-mono">
            {SLOTS.map(({ id, label }) => (
              <div key={id} className="p-2 bg-bg-deep rounded border border-border-subtle text-center">
                <div className="text-text-muted">{label}</div>
                <div className="text-accent-cyan truncate">{appearance.equipped[id] || '—'}</div>
              </div>
            ))}
          </div>
        </div>

        <div className="lg:col-span-7 space-y-4">
          <div className="flex flex-wrap gap-2">
            {SLOTS.map(({ id, label }) => (
              <button
                key={id}
                onClick={() => setActiveSlot(id)}
                className={`px-3 py-1.5 rounded-lg text-xs font-mono transition cursor-pointer ${
                  activeSlot === id
                    ? 'bg-accent-cyan text-[#070B14] font-bold'
                    : 'bg-bg-panel border border-border-subtle text-text-secondary hover:text-white'
                }`}
              >
                {label}
              </button>
            ))}
          </div>

          <div className="bg-bg-panel border border-border-subtle rounded-2xl p-5 space-y-3">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-display font-bold text-white flex items-center gap-2">
                <Shirt className="w-4 h-4 text-accent-cyan" />
                Equipped — {activeSlot}
              </h3>
              {equippedId && (
                <button
                  onClick={handleUnequip}
                  disabled={busy || activeSlot === 'chassis'}
                  className="text-[10px] font-mono text-accent-rose hover:underline disabled:opacity-40 cursor-pointer"
                >
                  <X className="w-3 h-3 inline mr-1" />
                  Unequip
                </button>
              )}
            </div>

            {ownedInSlot.length === 0 ? (
              <p className="text-xs text-text-muted font-sans">No owned items in this slot. Visit the Cosmetic Store.</p>
            ) : (
              <div className="grid sm:grid-cols-2 gap-3">
                {ownedInSlot.map((item) => (
                  <button
                    key={item.id}
                    onClick={() => handleEquip(item.id)}
                    disabled={busy || item.equipped}
                    className={`text-left p-3 rounded-xl border transition cursor-pointer ${
                      item.equipped
                        ? 'border-accent-cyan bg-accent-cyan/10'
                        : 'border-border-subtle bg-bg-deep hover:border-accent-cyan/40'
                    }`}
                  >
                    <div className="flex items-center gap-2">
                      <span
                        className="w-6 h-6 rounded-md shrink-0 border border-white/10"
                        style={{ backgroundColor: item.previewColor || '#64748B' }}
                      />
                      <div>
                        <div className="text-xs font-bold text-white">{item.name}</div>
                        <div className={`text-[10px] font-mono ${RARITY_COLORS[item.rarity]}`}>{item.rarity}</div>
                      </div>
                      {item.equipped && <Sparkles className="w-4 h-4 text-accent-cyan ml-auto" />}
                    </div>
                    <p className="text-[10px] text-text-muted mt-2 leading-relaxed">{item.description}</p>
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
