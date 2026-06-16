import React, { useState, useEffect } from 'react';
import {
  Cpu, HardDrive, Sparkles, Eye, FolderOpen, Save, CheckCircle2, Key, Zap,
} from 'lucide-react';
import { companionApi, type CoachEngine } from '../api/client';

interface PageSettingsProps {
  onEngineChange: (engine: CoachEngine) => void;
  activeEngine: CoachEngine;
  activeAgentId: string;
  onRestartOnboarding?: () => void;
}

export default function PageSettings({ onEngineChange, activeEngine, activeAgentId, onRestartOnboarding }: PageSettingsProps) {
  const [cursorKey, setCursorKey] = useState('');
  const [geminiKey, setGeminiKey] = useState('');
  const [hfToken, setHfToken] = useState('');
  const [localEndpoint, setLocalEndpoint] = useState('http://127.0.0.1:8080/v1/chat/completions');
  const [firebaseOn, setFirebaseOn] = useState(false);
  const [isSaved, setIsSaved] = useState(false);

  useEffect(() => {
    companionApi.loadSettings().then((s) => {
      if (typeof s.coachEngine === 'string') onEngineChange(s.coachEngine as CoachEngine);
      if (typeof s.localGgufEndpoint === 'string') setLocalEndpoint(s.localGgufEndpoint);
    }).catch(() => null);
    companionApi.firebaseConfig().then((c) => setFirebaseOn(!!c.enabled)).catch(() => null);
  }, []);

  const handleSave = async () => {
    await companionApi.saveSettings({
      coachEngine: activeEngine,
      cursorApiKey: cursorKey,
      geminiApiKey: geminiKey,
      huggingFaceToken: hfToken,
      localGgufEndpoint: localEndpoint,
    });
    setIsSaved(true);
    setTimeout(() => setIsSaved(false), 2000);
  };

  const engines: Array<{
    id: CoachEngine;
    name: string;
    description: string;
    icon: typeof Cpu;
    badge: string;
  }> = [
    {
      id: 'auto',
      name: 'Auto (recommended)',
      description: 'Uses your Cursor API key first, then Gemini, then offline rules.',
      icon: Zap,
      badge: 'DEFAULT',
    },
    {
      id: 'cursor',
      name: 'Cursor API (Composer)',
      description: 'Your Cursor API key — primary coach when set.',
      icon: Sparkles,
      badge: 'CURSOR',
    },
    {
      id: 'gemini-3.5-flash',
      name: 'Google Gemini',
      description: 'Your Gemini API key from AI Studio.',
      icon: Sparkles,
      badge: 'GOOGLE',
    },
    {
      id: 'local-gguf',
      name: 'Local LLM (llama.cpp / MLX)',
      description: 'OpenAI-compatible endpoint on your machine.',
      icon: HardDrive,
      badge: 'LOCAL',
    },
    {
      id: 'rules-only',
      name: 'Offline rules coach',
      description: 'No external API calls.',
      icon: Cpu,
      badge: 'OFFLINE',
    },
  ];

  return (
    <div className="space-y-8">
      <div className="border-b border-border-subtle pb-6">
        <span className="text-xs font-mono text-accent-cyan font-bold uppercase tracking-wider">companion core settings</span>
        <h2 className="text-3xl font-display font-semibold text-white mt-1">Laboratory Workspace Settings</h2>
        <p className="text-text-secondary text-sm mt-1">
          Firebase: {firebaseOn ? 'configured' : 'not configured — copy firebase-applet-config.example.json → firebase-applet-config.json'}
        </p>
      </div>

      <div className="grid lg:grid-cols-12 gap-8">
        <div className="lg:col-span-7 space-y-6">
          <div className="bg-bg-panel p-6 rounded-2xl border border-border-subtle space-y-6">
            <h3 className="text-sm font-display font-bold text-white uppercase tracking-wider flex items-center gap-2">
              <Sparkles className="w-4 h-4 text-accent-cyan" />
              Coach engine
            </h3>
            <div className="space-y-4">
              {engines.map((eng) => {
                const isSelected = activeEngine === eng.id;
                const Icon = eng.icon;
                return (
                  <div
                    key={eng.id}
                    onClick={() => onEngineChange(eng.id)}
                    className={`p-5 rounded-xl border transition cursor-pointer select-none space-y-3 ${
                      isSelected ? 'bg-accent-cyan/5 border-accent-cyan/40' : 'bg-bg-deep border-border-subtle'
                    }`}
                  >
                    <div className="flex justify-between items-center">
                      <div className="flex items-center gap-3">
                        <Icon className="w-5 h-5 text-accent-cyan" />
                        <h4 className="text-sm font-bold text-white">{eng.name}</h4>
                      </div>
                      <span className="text-[9px] font-mono font-bold px-2 py-0.5 rounded border border-border-subtle text-text-muted">
                        {eng.badge}
                      </span>
                    </div>
                    <p className="text-xs text-text-secondary pl-8">{eng.description}</p>
                  </div>
                );
              })}
            </div>
          </div>
        </div>

        <div className="lg:col-span-5 space-y-4">
          <div className="bg-bg-panel p-6 rounded-2xl border border-border-subtle space-y-4">
            <h3 className="text-sm font-bold text-white flex items-center gap-2">
              <Key className="w-4 h-4 text-accent-violet" /> API keys (stored locally)
            </h3>
            <label className="text-[10px] font-mono text-text-muted uppercase">Cursor API key</label>
            <input
              type="password"
              value={cursorKey}
              onChange={(e) => setCursorKey(e.target.value)}
              placeholder="cursor_… or from Cursor dashboard"
              className="w-full bg-bg-deep border border-border-subtle rounded-lg px-3 py-2 text-xs font-mono text-white"
            />
            <label className="text-[10px] font-mono text-text-muted uppercase">Gemini API key</label>
            <input
              type="password"
              value={geminiKey}
              onChange={(e) => setGeminiKey(e.target.value)}
              placeholder="AIza…"
              className="w-full bg-bg-deep border border-border-subtle rounded-lg px-3 py-2 text-xs font-mono text-white"
            />
            <label className="text-[10px] font-mono text-text-muted uppercase">Hugging Face token (optional, private models)</label>
            <input
              type="password"
              value={hfToken}
              onChange={(e) => setHfToken(e.target.value)}
              placeholder="hf_…"
              className="w-full bg-bg-deep border border-border-subtle rounded-lg px-3 py-2 text-xs font-mono text-white"
            />
            <label className="text-[10px] font-mono text-text-muted uppercase">Local LLM endpoint</label>
            <input
              type="text"
              value={localEndpoint}
              onChange={(e) => setLocalEndpoint(e.target.value)}
              className="w-full bg-bg-deep border border-border-subtle rounded-lg px-3 py-2 text-xs font-mono text-white"
            />
            <p className="text-[10px] text-text-muted">Active agent: <span className="text-accent-cyan">{activeAgentId || 'none'}</span></p>
            <button
              type="button"
              onClick={handleSave}
              className="w-full py-3 bg-bg-elevated border border-border-subtle rounded-xl text-xs font-semibold cursor-pointer flex items-center justify-center gap-2"
            >
              {isSaved ? <><CheckCircle2 className="w-4 h-4 text-accent-emerald" /> Saved</> : <><Save className="w-4 h-4" /> Save settings</>}
            </button>
            {onRestartOnboarding && (
              <button
                type="button"
                onClick={onRestartOnboarding}
                className="w-full py-2.5 bg-bg-deep border border-border-subtle rounded-xl text-[10px] font-mono text-text-muted hover:text-accent-cyan cursor-pointer"
              >
                Run setup wizard again
              </button>
            )}
          </div>
          <div className="p-4 bg-bg-panel rounded-xl border border-border-subtle text-xs text-text-secondary flex gap-3">
            <Eye className="w-4 h-4 text-accent-cyan shrink-0" />
            <div>Keys stay in <code className="text-accent-cyan">companion_settings.json</code> on your machine — never committed to git.</div>
          </div>
        </div>
      </div>
    </div>
  );
}
