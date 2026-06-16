/**
 * Copyright (c) 2026 JacobForges — DTA Training Companion™
 */

import React, { useEffect, useMemo, useState } from 'react';
import { Gem, Coins, ShoppingBag, Filter } from 'lucide-react';
import { companionApi } from '../api/client';
import type { CosmeticItem, CosmeticRarity, CosmeticSlot, PlayerProfile } from '../types';

const SLOTS: (CosmeticSlot | 'all')[] = ['all', 'helmet', 'visor', 'chassis', 'trail', 'emblem', 'voice'];
const RARITIES: (CosmeticRarity | 'all')[] = ['all', 'Common', 'Rare', 'Epic', 'Legendary'];

const RARITY_BADGE: Record<CosmeticRarity, string> = {
  Common: 'bg-slate-700/50 text-slate-300',
  Rare: 'bg-accent-cyan/15 text-accent-cyan',
  Epic: 'bg-accent-violet/15 text-accent-violet',
  Legendary: 'bg-accent-amber/15 text-accent-amber',
};

interface PageCosmeticStoreProps {
  activeAgentId: string;
  playerProfile: PlayerProfile;
  onCurrencyChange: (gold: number, gems: number) => void;
}

export default function PageCosmeticStore({
  activeAgentId,
  playerProfile,
  onCurrencyChange,
}: PageCosmeticStoreProps) {
  const [items, setItems] = useState<CosmeticItem[]>([]);
  const [gold, setGold] = useState(playerProfile.rpgStats.currencyGold);
  const [gems, setGems] = useState(playerProfile.rpgStats.currencyGems);
  const [slotFilter, setSlotFilter] = useState<CosmeticSlot | 'all'>('all');
  const [rarityFilter, setRarityFilter] = useState<CosmeticRarity | 'all'>('all');
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    try {
      const inv = await companionApi.cosmeticsInventory(activeAgentId);
      setItems(inv.items);
      setGold(inv.currencyGold);
      setGems(inv.currencyGems);
      onCurrencyChange(inv.currencyGold, inv.currencyGems);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
  }, [activeAgentId]);

  const filtered = useMemo(() => {
    return items.filter((item) => {
      if (slotFilter !== 'all' && item.slot !== slotFilter) return false;
      if (rarityFilter !== 'all' && item.rarity !== rarityFilter) return false;
      return true;
    });
  }, [items, slotFilter, rarityFilter]);

  const handlePurchase = async (item: CosmeticItem) => {
    if (item.owned) return;
    setBusyId(item.id);
    setMessage(null);
    try {
      const res = await companionApi.cosmeticsPurchase({ agentId: activeAgentId, itemId: item.id });
      if (res.currencyGold != null) setGold(res.currencyGold);
      if (res.currencyGems != null) setGems(res.currencyGems);
      onCurrencyChange(res.currencyGold ?? gold, res.currencyGems ?? gems);
      if (res.items) setItems(res.items);
      setMessage(`Purchased ${item.name}.`);
      await load();
    } catch (e) {
      setMessage(e instanceof Error ? e.message : 'Purchase failed');
    } finally {
      setBusyId(null);
    }
  };

  const canAfford = (item: CosmeticItem) => {
    if (item.priceGems > 0) return gems >= item.priceGems;
    return gold >= item.priceGold;
  };

  return (
    <div className="space-y-8">
      <div className="flex flex-col md:flex-row md:items-end justify-between gap-4 border-b border-border-subtle pb-6">
        <div>
          <span className="text-xs font-mono text-accent-violet font-bold uppercase tracking-wider">
            agent studio · cosmetic store
          </span>
          <h2 className="text-3xl font-display font-bold text-white mt-1">Cosmetic Store</h2>
          <p className="text-text-secondary text-sm font-sans mt-1">
            Spend training rewards on agent cosmetics for {activeAgentId}.
          </p>
        </div>
        <div className="flex gap-3 font-mono text-xs">
          <div className="flex items-center gap-2 px-3 py-2 bg-bg-panel border border-border-subtle rounded-lg">
            <Coins className="w-4 h-4 text-accent-amber" />
            <span className="text-white font-bold">{gold}</span>
            <span className="text-text-muted">gold</span>
          </div>
          <div className="flex items-center gap-2 px-3 py-2 bg-bg-panel border border-border-subtle rounded-lg">
            <Gem className="w-4 h-4 text-accent-violet" />
            <span className="text-white font-bold">{gems}</span>
            <span className="text-text-muted">gems</span>
          </div>
        </div>
      </div>

      {message && (
        <p className="text-xs font-mono text-accent-cyan bg-accent-cyan/10 border border-accent-cyan/20 rounded-lg px-3 py-2">
          {message}
        </p>
      )}

      <div className="flex flex-wrap items-center gap-3">
        <Filter className="w-4 h-4 text-text-muted" />
        <select
          value={slotFilter}
          onChange={(e) => setSlotFilter(e.target.value as CosmeticSlot | 'all')}
          className="bg-bg-panel border border-border-subtle rounded-lg px-3 py-1.5 text-xs font-mono text-text-secondary"
        >
          {SLOTS.map((s) => (
            <option key={s} value={s}>
              {s === 'all' ? 'All slots' : s}
            </option>
          ))}
        </select>
        <select
          value={rarityFilter}
          onChange={(e) => setRarityFilter(e.target.value as CosmeticRarity | 'all')}
          className="bg-bg-panel border border-border-subtle rounded-lg px-3 py-1.5 text-xs font-mono text-text-secondary"
        >
          {RARITIES.map((r) => (
            <option key={r} value={r}>
              {r === 'all' ? 'All rarities' : r}
            </option>
          ))}
        </select>
      </div>

      {loading ? (
        <div className="p-12 text-center text-text-secondary font-mono text-sm animate-pulse">Loading catalog…</div>
      ) : (
        <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-4">
          {filtered.map((item) => (
            <div
              key={item.id}
              className="bg-bg-panel border border-border-subtle rounded-2xl p-4 flex flex-col gap-3 hover:border-accent-violet/30 transition"
            >
              <div className="flex items-start gap-3">
                <div
                  className="w-12 h-12 rounded-xl border border-white/10 shrink-0"
                  style={{ backgroundColor: item.previewColor || '#475569' }}
                />
                <div className="min-w-0">
                  <div className="text-sm font-bold text-white truncate">{item.name}</div>
                  <div className="flex flex-wrap gap-1.5 mt-1">
                    <span className={`text-[9px] font-mono px-1.5 py-0.5 rounded ${RARITY_BADGE[item.rarity]}`}>
                      {item.rarity}
                    </span>
                    <span className="text-[9px] font-mono text-text-muted uppercase">{item.slot}</span>
                  </div>
                </div>
              </div>
              <p className="text-[11px] text-text-secondary leading-relaxed flex-1">{item.description}</p>
              <div className="flex items-center justify-between gap-2 pt-2 border-t border-border-subtle">
                <div className="text-[10px] font-mono text-text-muted">
                  {item.priceGems > 0 ? (
                    <span className="text-accent-violet flex items-center gap-1">
                      <Gem className="w-3 h-3" /> {item.priceGems}
                    </span>
                  ) : item.priceGold > 0 ? (
                    <span className="text-accent-amber flex items-center gap-1">
                      <Coins className="w-3 h-3" /> {item.priceGold}
                    </span>
                  ) : (
                    'Free'
                  )}
                </div>
                {item.owned ? (
                  <span className="text-[10px] font-mono text-accent-emerald">Owned</span>
                ) : (
                  <button
                    onClick={() => handlePurchase(item)}
                    disabled={busyId === item.id || !canAfford(item)}
                    className="flex items-center gap-1 px-3 py-1.5 bg-accent-violet text-white text-[10px] font-mono font-bold rounded-lg disabled:opacity-40 cursor-pointer"
                  >
                    <ShoppingBag className="w-3 h-3" />
                    Buy
                  </button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
