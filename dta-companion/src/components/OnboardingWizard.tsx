/**
 * Copyright (c) 2026 JacobForges — DTA Training Companion™
 */

import React, { useEffect, useState } from 'react';
import {
  ArrowRight, ArrowLeft, CheckCircle2, Cpu, Gamepad2, Key,
  Layers, Sparkles, User, X, Zap,
} from 'lucide-react';
import { companionApi, type CoachEngine } from '../api/client';

interface OnboardingWizardProps {
  onComplete: (displayName: string) => void;
  onDismiss?: () => void;
}

const STEPS = [
  { id: 'welcome', title: 'Welcome' },
  { id: 'purpose', title: 'Purpose' },
  { id: 'health', title: 'Lab check' },
  { id: 'coach', title: 'Coach' },
  { id: 'game', title: 'Link game' },
  { id: 'finish', title: 'Ready' },
] as const;

export default function OnboardingWizard({ onComplete, onDismiss }: OnboardingWizardProps) {
  const [step, setStep] = useState(0);
  const [displayName, setDisplayName] = useState('');
  const [coachEngine, setCoachEngine] = useState<CoachEngine>('auto');
  const [cursorKey, setCursorKey] = useState('');
  const [geminiKey, setGeminiKey] = useState('');
  const [health, setHealth] = useState<{
    ok: boolean;
    hasCursorKey?: boolean;
    hasGeminiKey?: boolean;
    firebase?: boolean;
  } | null>(null);
  const [syncFolder, setSyncFolder] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    companionApi.onboarding().then((o) => {
      if (o.displayName) setDisplayName(o.displayName);
      if (o.syncFolder) setSyncFolder(o.syncFolder);
    }).catch(() => null);
    companionApi.health().then((h) => setHealth({ ok: h.status === 'ok', ...h })).catch(() => setHealth({ ok: false }));
  }, [step]);

  const finish = async () => {
    setSaving(true);
    try {
      await companionApi.completeOnboarding({
        displayName: displayName.trim() || 'Trainee',
        coachEngine,
        cursorApiKey: cursorKey || undefined,
        geminiApiKey: geminiKey || undefined,
      });
      onComplete(displayName.trim() || 'Trainee');
    } catch {
      onComplete(displayName.trim() || 'Trainee');
    } finally {
      setSaving(false);
    }
  };

  const stepId = STEPS[step].id;
  const progress = ((step + 1) / STEPS.length) * 100;

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-bg-deep/95 backdrop-blur-md">
      <div className="w-full max-w-2xl bg-bg-panel border border-border-subtle rounded-3xl shadow-2xl overflow-hidden">
        <div className="h-1 bg-bg-deep">
          <div className="h-full bg-gradient-to-r from-accent-cyan to-accent-violet transition-all" style={{ width: `${progress}%` }} />
        </div>

        <div className="p-6 md:p-8 space-y-6">
          <div className="flex justify-between items-start gap-4">
            <div>
              <span className="text-[10px] font-mono text-accent-cyan uppercase tracking-wider">
                Setup {step + 1} / {STEPS.length} — {STEPS[step].title}
              </span>
              <h2 className="text-2xl font-display font-bold text-white mt-1">DTA Training Companion</h2>
            </div>
            {onDismiss && step > 0 && (
              <button type="button" onClick={onDismiss} className="p-2 text-text-muted hover:text-white cursor-pointer" aria-label="Close">
                <X className="w-5 h-5" />
              </button>
            )}
          </div>

          {stepId === 'welcome' && (
            <div className="space-y-4 text-sm text-text-secondary leading-relaxed">
              <p>
                This is your <strong className="text-white">external training laboratory</strong> for Deep Train Academy™.
                It runs beside the Unity game — not inside it — so you can coach agents, inspect brains, download models, and customize cosmetics on a full screen.
              </p>
              <label className="block text-[10px] font-mono text-text-muted uppercase">Your display name</label>
              <input
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                placeholder="e.g. Scout Pilot"
                className="w-full bg-bg-deep border border-border-subtle rounded-xl px-4 py-3 text-sm text-white font-mono"
              />
            </div>
          )}

          {stepId === 'purpose' && (
            <div className="space-y-4 text-sm">
              <div className="grid gap-3">
                {[
                  { icon: Gamepad2, title: 'Play in Unity', body: 'Collect gameplay samples, train in-world, compete.' },
                  { icon: Cpu, title: 'Lab in Companion', body: 'Analyze telemetry, run the AI coach, pick ONNX brains, deploy checkpoints.' },
                  { icon: Layers, title: 'Sync via disk', body: 'Deploy writes to your checkpoint folder; the game auto-imports on launch.' },
                ].map(({ icon: Icon, title, body }) => (
                  <div key={title} className="flex gap-3 p-4 bg-bg-deep rounded-xl border border-border-subtle">
                    <Icon className="w-5 h-5 text-accent-cyan shrink-0 mt-0.5" />
                    <div>
                      <p className="text-white font-semibold text-sm">{title}</p>
                      <p className="text-text-secondary text-xs mt-1">{body}</p>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}

          {stepId === 'health' && (
            <div className="space-y-4 text-sm">
              <div className={`p-4 rounded-xl border flex items-center gap-3 ${health?.ok ? 'border-accent-emerald/30 bg-accent-emerald/5' : 'border-accent-rose/30 bg-accent-rose/5'}`}>
                {health?.ok ? <CheckCircle2 className="w-5 h-5 text-accent-emerald" /> : <Cpu className="w-5 h-5 text-accent-rose" />}
                <div>
                  <p className="text-white font-semibold">{health?.ok ? 'Companion server online' : 'Server not reachable'}</p>
                  <p className="text-xs text-text-secondary mt-1">
                    {health?.ok
                      ? 'API, model market, and agent studio are ready.'
                      : 'Run Start-DTA-Companion or npm run dev, then refresh.'}
                  </p>
                </div>
              </div>
              {syncFolder && (
                <div className="p-4 bg-bg-deep rounded-xl border border-border-subtle">
                  <p className="text-[10px] font-mono text-text-muted uppercase mb-1">Checkpoint sync folder</p>
                  <code className="text-xs text-accent-cyan break-all">{syncFolder}</code>
                </div>
              )}
            </div>
          )}

          {stepId === 'coach' && (
            <div className="space-y-4 text-sm">
              <p className="text-text-secondary">
                <strong className="text-white">Auto (recommended)</strong> uses your Cursor API key first, then Gemini, then offline rules. Keys stay on your machine only.
              </p>
              <div className="flex flex-wrap gap-2">
                {(['auto', 'cursor', 'gemini-3.5-flash', 'rules-only'] as CoachEngine[]).map((e) => (
                  <button
                    key={e}
                    type="button"
                    onClick={() => setCoachEngine(e)}
                    className={`px-3 py-1.5 rounded-lg text-[10px] font-mono border cursor-pointer ${
                      coachEngine === e ? 'border-accent-cyan text-accent-cyan bg-accent-cyan/10' : 'border-border-subtle text-text-muted'
                    }`}
                  >
                    {e}
                  </button>
                ))}
              </div>
              <div className="space-y-3">
                <div>
                  <label className="text-[10px] font-mono text-text-muted uppercase flex items-center gap-1">
                    <Key className="w-3 h-3" /> Cursor API key (optional)
                  </label>
                  <input
                    type="password"
                    value={cursorKey}
                    onChange={(e) => setCursorKey(e.target.value)}
                    placeholder="Primary coach when set"
                    className="w-full mt-1 bg-bg-deep border border-border-subtle rounded-lg px-3 py-2 text-xs font-mono text-white"
                  />
                </div>
                <div>
                  <label className="text-[10px] font-mono text-text-muted uppercase flex items-center gap-1">
                    <Sparkles className="w-3 h-3" /> Gemini API key (optional)
                  </label>
                  <input
                    type="password"
                    value={geminiKey}
                    onChange={(e) => setGeminiKey(e.target.value)}
                    placeholder="Google AI Studio key"
                    className="w-full mt-1 bg-bg-deep border border-border-subtle rounded-lg px-3 py-2 text-xs font-mono text-white"
                  />
                </div>
              </div>
              <p className="text-[10px] text-text-muted">Skip keys now — rules-only coach works offline. Change anytime in Settings.</p>
            </div>
          )}

          {stepId === 'game' && (
            <div className="space-y-4 text-sm text-text-secondary">
              <ol className="list-decimal list-inside space-y-2">
                <li>Launch <strong className="text-white">Deep Train Academy</strong> and play until you have an agent id.</li>
                <li>On the <strong className="text-white">Entrance</strong> page, generate a link code or rely on automatic disk sync.</li>
                <li>Deploy a brain from <strong className="text-white">Model Market</strong> or <strong className="text-white">Deploy</strong> — the game imports on next launch.</li>
              </ol>
              <div className="p-4 bg-accent-violet/5 border border-accent-violet/20 rounded-xl flex gap-3">
                <Zap className="w-5 h-5 text-accent-violet shrink-0" />
                <p className="text-xs">Bundled builds ship <code className="text-accent-cyan">DTACompanion/</code> beside the game. You need Node.js installed once.</p>
              </div>
            </div>
          )}

          {stepId === 'finish' && (
            <div className="space-y-4 text-center py-4">
              <div className="w-16 h-16 mx-auto rounded-2xl bg-accent-cyan/10 border border-accent-cyan/30 flex items-center justify-center">
                <User className="w-8 h-8 text-accent-cyan" />
              </div>
              <p className="text-white font-display text-xl font-bold">You&apos;re set, {displayName.trim() || 'Trainee'}.</p>
              <p className="text-sm text-text-secondary max-w-md mx-auto">
                Visit <strong className="text-white">Agent Roster</strong> to pick an agent, <strong className="text-white">Agent Studio</strong> for cosmetics, and open the <strong className="text-white">Copilot</strong> rail for coaching.
              </p>
            </div>
          )}

          <div className="flex justify-between pt-2 border-t border-border-subtle">
            <button
              type="button"
              disabled={step === 0}
              onClick={() => setStep((s) => Math.max(0, s - 1))}
              className="px-4 py-2.5 rounded-xl text-xs font-semibold border border-border-subtle text-text-secondary disabled:opacity-30 cursor-pointer flex items-center gap-2"
            >
              <ArrowLeft className="w-4 h-4" /> Back
            </button>
            {step < STEPS.length - 1 ? (
              <button
                type="button"
                onClick={() => setStep((s) => s + 1)}
                className="px-5 py-2.5 rounded-xl text-xs font-bold bg-accent-cyan text-[#070B14] cursor-pointer flex items-center gap-2"
              >
                Continue <ArrowRight className="w-4 h-4" />
              </button>
            ) : (
              <button
                type="button"
                disabled={saving}
                onClick={finish}
                className="px-5 py-2.5 rounded-xl text-xs font-bold bg-accent-violet text-[#070B14] cursor-pointer flex items-center gap-2 disabled:opacity-60"
              >
                {saving ? 'Saving…' : 'Enter laboratory'} <CheckCircle2 className="w-4 h-4" />
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
