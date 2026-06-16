import React, { useEffect, useState } from 'react';
import { Download, Package, ShoppingBag, Link2, RefreshCw } from 'lucide-react';
import { companionApi, ModelCatalogEntry } from '../api/client';

interface PageModelMarketProps {
  activeAgentId: string;
}

interface SlotInfo {
  slot: number;
  catalogId: string;
  displayName: string;
  installedUtc: string;
}

export default function PageModelMarket({ activeAgentId }: PageModelMarketProps) {
  const [models, setModels] = useState<ModelCatalogEntry[]>([]);
  const [sources, setSources] = useState<Record<string, number>>({});
  const [slots, setSlots] = useState<SlotInfo[]>([]);
  const [customUrl, setCustomUrl] = useState('');
  const [status, setStatus] = useState<string | null>(null);
  const [slot, setSlot] = useState(1);
  const [loading, setLoading] = useState(false);

  const refresh = async () => {
    setLoading(true);
    try {
      const [catalog, slotRes] = await Promise.all([
        companionApi.catalog(),
        companionApi.modelSlots(),
      ]);
      setModels(catalog.models || []);
      setSources(catalog.sources || {});
      setSlots(slotRes.slots || []);
    } catch {
      setStatus('Could not refresh catalog — is the companion server running?');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    refresh();
  }, []);

  const install = async (entry?: ModelCatalogEntry) => {
    if (!activeAgentId) {
      setStatus('Set active agent id (play game once or pick in roster).');
      return;
    }
    try {
      const res = await companionApi.installModel({
        agentId: activeAgentId,
        catalogId: entry?.id,
        url: entry?.editable ? customUrl : entry?.url,
        slot,
      });
      setStatus(res.message || (res.success ? 'Installed.' : 'Install failed.'));
      if (res.success) await refresh();
    } catch (e: unknown) {
      setStatus(e instanceof Error ? e.message : 'Install failed');
    }
  };

  const bySource = (src: string) => models.filter((m) => m.source === src);

  return (
    <div className="space-y-8">
      <div className="border-b border-border-subtle pb-6 flex flex-col md:flex-row md:justify-between gap-4">
        <div>
          <span className="text-xs font-mono text-accent-violet font-bold uppercase tracking-wider">model vault</span>
          <h2 className="text-3xl font-display font-semibold text-white mt-1">Brain Marketplace</h2>
          <p className="text-text-secondary text-sm mt-1 max-w-2xl">
            Auto-loaded from <strong className="text-white">bundled game brains</strong>, your{' '}
            <strong className="text-white">GitHub catalog</strong>, and{' '}
            <strong className="text-white">Hugging Face</strong>. Pick a slot (1–3), download, deploy to game.
          </p>
        </div>
        <button
          type="button"
          onClick={refresh}
          disabled={loading}
          className="px-4 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-xs font-mono text-accent-cyan cursor-pointer flex items-center gap-2 h-fit"
        >
          <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
          Refresh catalog
        </button>
      </div>

      <div className="grid md:grid-cols-3 gap-3">
        {[1, 2, 3].map((n) => {
          const filled = slots.find((s) => s.slot === n);
          return (
            <button
              key={n}
              type="button"
              onClick={() => setSlot(n)}
              className={`p-4 rounded-xl border text-left cursor-pointer transition ${
                slot === n ? 'border-accent-violet bg-accent-violet/10' : 'border-border-subtle bg-bg-panel'
              }`}
            >
              <span className="text-[10px] font-mono text-text-muted uppercase">Slot {n}</span>
              <p className="text-sm font-semibold text-white mt-1 truncate">
                {filled ? filled.displayName : 'Empty'}
              </p>
              {filled && (
                <p className="text-[10px] font-mono text-text-secondary mt-1">{filled.catalogId}</p>
              )}
            </button>
          );
        })}
      </div>

      <p className="text-[10px] font-mono text-text-muted">
        Catalog sources — bundled: {sources.bundled ?? 0} · github: {sources.github ?? 0} · huggingface:{' '}
        {sources.huggingface ?? 0}
      </p>

      {(['bundled', 'github', 'huggingface'] as const).map((src) => {
        const list = bySource(src);
        if (list.length === 0) return null;
        return (
          <div key={src} className="space-y-3">
            <h3 className="text-xs font-mono font-bold text-accent-cyan uppercase">{src}</h3>
            <div className="grid md:grid-cols-2 gap-4">
              {list.map((m) => (
                <ModelCard key={m.id} m={m} slot={slot} customUrl={customUrl} setCustomUrl={setCustomUrl} onInstall={() => install(m)} />
              ))}
            </div>
          </div>
        );
      })}

      {status && (
        <div className="p-4 bg-bg-deep border border-border-subtle rounded-xl text-xs font-mono text-accent-emerald flex items-center gap-2 whitespace-pre-wrap">
          <ShoppingBag className="w-4 h-4 shrink-0" />
          {status}
        </div>
      )}
    </div>
  );
}

const ModelCard: React.FC<{
  m: ModelCatalogEntry;
  slot: number;
  customUrl: string;
  setCustomUrl: (v: string) => void;
  onInstall: () => void;
}> = ({ m, slot, customUrl, setCustomUrl, onInstall }) => {
  return (
    <div className="bg-bg-panel border border-border-subtle rounded-2xl p-5 space-y-3">
      <div className="flex items-start justify-between gap-2">
        <div>
          <h3 className="text-white font-display font-semibold">{m.name}</h3>
          <p className="text-[10px] font-mono text-text-muted uppercase">{m.source} · {m.license}</p>
        </div>
        <Package className="w-5 h-5 text-accent-cyan shrink-0" />
      </div>
      <p className="text-xs text-text-secondary leading-relaxed">{m.description}</p>
      {m.editable ? (
        <div className="space-y-2">
          <input
            type="url"
            value={customUrl}
            onChange={(e) => setCustomUrl(e.target.value)}
            placeholder="https://huggingface.co/.../resolve/main/gameplay_adapter.onnx"
            className="w-full bg-bg-deep border border-border-subtle rounded-lg px-3 py-2 text-xs font-mono text-white"
          />
          <button
            type="button"
            onClick={onInstall}
            className="w-full py-2.5 bg-accent-violet hover:bg-accent-violet/90 text-[#070B14] text-xs font-bold rounded-lg cursor-pointer flex items-center justify-center gap-2"
          >
            <Link2 className="w-4 h-4" /> Install URL → slot {slot}
          </button>
        </div>
      ) : (
        <button
          type="button"
          onClick={onInstall}
          disabled={!m.url && m.source !== 'bundled'}
          className="w-full py-2.5 bg-accent-cyan hover:bg-accent-cyan/90 disabled:opacity-40 text-[#070B14] text-xs font-bold rounded-lg cursor-pointer flex items-center justify-center gap-2"
        >
          <Download className="w-4 h-4" /> Download → slot {slot}
        </button>
      )}
    </div>
  );
}
