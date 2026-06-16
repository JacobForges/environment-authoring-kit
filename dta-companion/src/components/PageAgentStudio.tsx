/**
 * Copyright (c) 2026 JacobForges — DTA Training Companion™
 */

import React, { useState } from 'react';
import { Palette, ShoppingBag } from 'lucide-react';
import PageAgentEditor from './PageAgentEditor';
import PageCosmeticStore from './PageCosmeticStore';
import type { PlayerProfile } from '../types';

interface PageAgentStudioProps {
  activeAgentId: string;
  playerProfile: PlayerProfile;
  onCurrencyChange: (gold: number, gems: number) => void;
}

export default function PageAgentStudio({
  activeAgentId,
  playerProfile,
  onCurrencyChange,
}: PageAgentStudioProps) {
  const [tab, setTab] = useState<'editor' | 'store'>('editor');

  return (
    <div className="space-y-6">
      <div className="flex gap-2 bg-bg-panel border border-border-subtle p-1 rounded-xl w-fit font-mono text-xs">
        <button
          onClick={() => setTab('editor')}
          className={`flex items-center gap-2 px-4 py-2 rounded-lg transition cursor-pointer ${
            tab === 'editor'
              ? 'bg-accent-cyan text-[#070B14] font-bold'
              : 'text-text-secondary hover:text-white'
          }`}
        >
          <Palette className="w-3.5 h-3.5" />
          Agent Editor
        </button>
        <button
          onClick={() => setTab('store')}
          className={`flex items-center gap-2 px-4 py-2 rounded-lg transition cursor-pointer ${
            tab === 'store'
              ? 'bg-accent-violet text-white font-bold'
              : 'text-text-secondary hover:text-white'
          }`}
        >
          <ShoppingBag className="w-3.5 h-3.5" />
          Cosmetic Store
        </button>
      </div>

      {tab === 'editor' ? (
        <PageAgentEditor
          activeAgentId={activeAgentId}
          playerProfile={playerProfile}
          onCurrencyChange={onCurrencyChange}
        />
      ) : (
        <PageCosmeticStore
          activeAgentId={activeAgentId}
          playerProfile={playerProfile}
          onCurrencyChange={onCurrencyChange}
        />
      )}
    </div>
  );
}
