import React from 'react';
import { 
  ArrowUpRight, AlertTriangle, Cpu, CheckCircle2, Award, RefreshCw, 
  ChevronRight, Play, Server, User, HelpCircle, Activity, ExternalLink
} from 'lucide-react';
import { ResponsiveContainer, PieChart, Pie, Cell, Tooltip, Legend } from 'recharts';
import { AgentProfile, PlayerProfile, CheckpointManifest } from '../types';

interface PageHomeLabProps {
  agent: AgentProfile;
  player: PlayerProfile;
  checkpoint: CheckpointManifest | null;
  onNavigatePage: (page: 'train' | 'brain' | 'deploy') => void;
  onRefreshData: () => void;
  isTrainingComplete: boolean;
}

export default function PageHomeLab({
  agent,
  player,
  checkpoint,
  onNavigatePage,
  onRefreshData,
  isTrainingComplete
}: PageHomeLabProps) {

  // Readiness Ring SVG stats
  const gameplayProgress = Math.min(100, Math.max(20, (agent.xp / 3500) * 100));
  const chatProgress = 80;
  const reasoningProgress = 60;

  // Pie chart Activity Mix data
  const activityData = [
    { name: 'Battle Focus', value: 45, color: '#22D3EE' },
    { name: 'Follow Core', value: 25, color: '#A78BFA' },
    { name: 'Explore Bio', value: 20, color: '#10B981' },
    { name: 'Chat Interaction', value: 10, color: '#F59E0B' }
  ];

  return (
    <div className="space-y-8">
      
      {/* Row 1 — Hero strip (Full width dashboard) */}
      <div className="bg-bg-panel border border-border-subtle rounded-2xl p-6 md:p-8 relative overflow-hidden">
        <div className="absolute inset-0 dot-grid opacity-20 pointer-events-none" />
        
        <div className="relative z-10 flex flex-col lg:flex-row lg:items-center justify-between gap-8">
          
          {/* Left Block: Identity */}
          <div className="flex items-center gap-4">
            <div className="w-16 h-16 rounded-2xl bg-gradient-to-tr from-accent-cyan to-accent-violet p-1 shadow-lg shrink-0">
              <div className="w-full h-full bg-bg-panel rounded-xl flex items-center justify-center">
                <Cpu className="w-8 h-8 text-white" />
              </div>
            </div>
            
            <div>
              <div className="flex items-center gap-2">
                <span className="text-[10px] font-mono text-accent-cyan bg-accent-cyan/10 border border-accent-cyan/15 px-2 py-0.5 rounded">
                  ACTIVE AGENT ID: {agent.agentId}
                </span>
                <span className="text-[10px] font-mono text-text-muted">
                  OWNER: {player.displayName}
                </span>
              </div>
              <h1 className="font-display text-3xl font-bold text-white mt-1">{agent.displayName}</h1>
              <p className="text-text-secondary text-xs font-mono font-medium mt-1">
                LATEST COMPILE REVISION: v{checkpoint ? checkpoint.checkpointVersion : 6} · HASH: {checkpoint ? checkpoint.sha256.substring(0, 12) : "D3F8AC09AB21"}
              </p>
            </div>
          </div>

          {/* Center Block: SVG Readiness Ring Gauge */}
          <div className="flex items-center gap-6">
            <div className="relative w-24 h-24 shrink-0 flex items-center justify-center">
              <svg className="w-full h-full transform -rotate-90" viewBox="0 0 100 100">
                {/* Lane 1: Gameplay (outer) */}
                <circle cx="50" cy="50" r="40" stroke="rgba(148, 163, 184, 0.08)" strokeWidth="6" fill="transparent" />
                <circle cx="50" cy="50" r="40" stroke="#22D3EE" strokeWidth="6" fill="transparent"
                  strokeDasharray={`${2 * Math.PI * 40}`}
                  strokeDashoffset={`${2 * Math.PI * 40 * (1 - gameplayProgress / 100)}`}
                  strokeLinecap="round"
                />

                {/* Lane 2: Chat */}
                <circle cx="50" cy="50" r="30" stroke="rgba(148, 163, 184, 0.08)" strokeWidth="6" fill="transparent" />
                <circle cx="50" cy="50" r="30" stroke="#A78BFA" strokeWidth="6" fill="transparent"
                  strokeDasharray={`${2 * Math.PI * 30}`}
                  strokeDashoffset={`${2 * Math.PI * 30 * (1 - chatProgress / 100)}`}
                  strokeLinecap="round"
                />

                {/* Lane 3: Reasoning */}
                <circle cx="50" cy="50" r="20" stroke="rgba(148, 163, 184, 0.08)" strokeWidth="6" fill="transparent" />
                <circle cx="50" cy="50" r="20" stroke="#10B981" strokeWidth="6" fill="transparent"
                  strokeDasharray={`${2 * Math.PI * 20}`}
                  strokeDashoffset={`${2 * Math.PI * 20 * (1 - reasoningProgress / 100)}`}
                  strokeLinecap="round"
                />
              </svg>

              {/* Readout */}
              <div className="absolute flex flex-col items-center">
                <span className="text-[10px] font-mono font-bold text-white leading-none">94%</span>
                <span className="text-[7px] font-sans text-text-muted uppercase tracking-wider">Ready</span>
              </div>
            </div>

            <div className="space-y-1 text-xs font-mono">
              <div className="flex items-center gap-1.5">
                <span className="w-2.5 h-2.5 rounded-full bg-accent-cyan" />
                <span className="text-text-secondary">Gameplay Lane: {Math.floor(gameplayProgress)}%</span>
              </div>
              <div className="flex items-center gap-1.5">
                <span className="w-2.5 h-2.5 rounded-full bg-accent-violet" />
                <span className="text-text-secondary">Chat Lane: {chatProgress}%</span>
              </div>
              <div className="flex items-center gap-1.5">
                <span className="w-2.5 h-2.5 rounded-full bg-accent-emerald" />
                <span className="text-text-secondary">Reasoning Lane: {reasoningProgress}%</span>
              </div>
            </div>
          </div>

          {/* Right Block: Primary Action CTAs */}
          <div className="flex flex-col sm:flex-row items-stretch lg:items-center gap-3">
            <button
              onClick={() => onNavigatePage('train')}
              className="px-5 py-3 bg-bg-elevated hover:bg-bg-elevated/80 border border-border-subtle rounded-xl text-xs font-semibold font-sans cursor-pointer transition flex items-center justify-center gap-2 text-white"
            >
              <Play className="w-4 h-4 text-accent-cyan fill-current" />
              Train in Lab
            </button>
            
            <button
              onClick={() => onNavigatePage('deploy')}
              disabled={!isTrainingComplete}
              className={`px-5 py-3 rounded-xl text-xs font-bold font-sans cursor-pointer transition flex items-center justify-center gap-2 ${
                isTrainingComplete
                  ? 'bg-accent-emerald text-[#070B14] hover:bg-accent-emerald/90 animate-pulse'
                  : 'bg-emerald-950/20 text-[#10B981]/40 border border-[#10B981]/15 cursor-not-allowed'
              }`}
            >
              <Award className="w-4 h-4" />
              Deploy to Game
            </button>
          </div>

        </div>

      </div>

      {/* Row 2 — Three columns layout */}
      <div className="grid lg:grid-cols-12 gap-8">
        
        {/* Left 4cols: Train Readiness Pipeline */}
        <div className="lg:col-span-4 bg-bg-panel p-6 rounded-2xl border border-border-subtle space-y-6">
          <div className="flex items-center justify-between border-b border-border-subtle pb-3">
            <h3 className="text-sm font-display font-bold text-white uppercase tracking-wider flex items-center gap-2">
              <Activity className="w-4 h-4 text-accent-cyan" />
              Dataset Pipeline
            </h3>
            <span className="text-[10px] font-mono text-text-muted">min req 24 rows</span>
          </div>

          <div className="space-y-4">
            
            {/* Action Item 1: Battle datasets */}
            <div className="space-y-2 p-3 bg-bg-deep rounded-xl border border-border-subtle">
              <div className="flex justify-between items-center text-xs font-mono">
                <span className="text-text-primary font-bold">Battle telemetry</span>
                <span className="px-2 py-0.5 bg-accent-emerald/10 border border-accent-emerald/20 text-accent-emerald text-[9px] rounded">READY</span>
              </div>
              <div className="w-full h-1 bg-bg-panel rounded-full overflow-hidden">
                <div className="bg-accent-emerald h-full rounded-full" style={{ width: '100%' }} />
              </div>
              <div className="flex justify-between text-[9px] font-mono text-text-muted">
                <span>31 rows collected</span>
                <span>Threshold met</span>
              </div>
            </div>

            {/* Action Item 2: Follow vectors */}
            <div className="space-y-2 p-3 bg-bg-deep rounded-xl border border-border-subtle">
              <div className="flex justify-between items-center text-xs font-mono">
                <span className="text-text-primary font-bold">Following Precision</span>
                <span className="px-2 py-0.5 bg-accent-emerald/10 border border-accent-emerald/20 text-accent-emerald text-[9px] rounded">READY</span>
              </div>
              <div className="w-full h-1 bg-bg-panel rounded-full overflow-hidden">
                <div className="bg-accent-emerald h-full rounded-full" style={{ width: '100%' }} />
              </div>
              <div className="flex justify-between text-[9px] font-mono text-text-muted">
                <span>28 rows collected</span>
                <span>Threshold met</span>
              </div>
            </div>

            {/* Action Item 3: Reasoning dataset */}
            <div className="space-y-2 p-3 bg-bg-deep rounded-xl border border-border-subtle">
              <div className="flex justify-between items-center text-xs font-mono">
                <span className="text-text-primary font-bold">Reasoning calibration</span>
                <span className="px-2 py-0.5 bg-accent-amber/10 border border-accent-amber/20 text-accent-amber text-[9px] rounded">COLLECTING</span>
              </div>
              <div className="w-full h-1 bg-bg-panel rounded-full overflow-hidden">
                <div className="bg-accent-amber h-full rounded-full" style={{ width: '60%' }} />
              </div>
              <div className="flex justify-between text-[9px] font-mono text-text-muted">
                <span>14 / 24 rows loaded</span>
                <span>Sync overdue</span>
              </div>
            </div>

          </div>
        </div>

        {/* Center 5cols: Activity Mix donut */}
        <div className="lg:col-span-5 bg-bg-panel p-6 rounded-2xl border border-border-subtle flex flex-col justify-between">
          <div className="flex items-center justify-between border-b border-border-subtle pb-3">
            <h3 className="text-sm font-display font-bold text-white uppercase tracking-wider">
              Telemetry Activity Mix
            </h3>
            <span className="text-[10px] font-mono text-text-muted">episode_*.jsonl</span>
          </div>

          <div className="h-44 my-4 flex items-center justify-center">
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Pie
                  data={activityData}
                  cx="50%"
                  cy="50%"
                  innerRadius={50}
                  outerRadius={70}
                  paddingAngle={4}
                  dataKey="value"
                >
                  {activityData.map((entry, index) => (
                    <Cell key={`cell-${index}`} fill={entry.color} />
                  ))}
                </Pie>
                <Tooltip 
                  contentStyle={{ backgroundColor: '#0F1629', borderColor: '#1E293B', color: '#F1F5F9' }}
                  itemStyle={{ fontSize: '11px', fontFamily: 'monospace' }}
                />
              </PieChart>
            </ResponsiveContainer>
          </div>

          <div className="grid grid-cols-2 gap-3 pt-2">
            {activityData.map((act) => (
              <div key={act.name} className="flex items-center gap-2 text-[10px] font-mono">
                <span className="w-2.5 h-2.5 rounded-sm" style={{ backgroundColor: act.color }} />
                <span className="text-text-secondary truncate">{act.name}</span>
                <span className="text-white font-bold ml-auto tabular-nums">{act.value}%</span>
              </div>
            ))}
          </div>
        </div>

        {/* Right 3cols: Miniature AI Coach streamlined synopsis */}
        <div className="lg:col-span-3 bg-[#0F1629] p-6 rounded-2xl border border-border-subtle flex flex-col justify-between">
          <div className="space-y-4">
            <div className="flex items-center justify-between border-b border-border-subtle pb-3">
              <span className="text-xs font-mono text-accent-cyan font-bold uppercase tracking-wider">COACH TELEMETRY SYNOPSIS</span>
              <span className="text-[9px] font-mono px-1.5 py-0.2 bg-accent-cyan/10 border border-accent-cyan/20 text-accent-cyan rounded">HIGH</span>
            </div>

            <div className="space-y-3 font-sans text-xs text-text-secondary leading-relaxed">
              <p>
                Slate Canyons telemetry detects consistent <strong className="text-white">braking friction</strong> in tight, basalt curved sectors.
              </p>
              <p>
                Deploying <strong className="text-accent-cyan">bc_adapter v7</strong> will recalibrate focus activity multipliers, reducing curve drift from 1.4s lagging down to 0.4s.
              </p>
            </div>
          </div>

          <button
            onClick={() => onNavigatePage('brain')}
            className="w-full mt-4 py-2.5 bg-bg-elevated hover:bg-bg-elevated/80 border border-border-subtle text-text-primary text-xs font-semibold rounded-xl transition cursor-pointer flex items-center justify-center gap-1.5"
          >
            <span>View Labs Analytics</span>
            <ChevronRight className="w-3.5 h-3.5 text-accent-cyan" />
          </button>
        </div>

      </div>

      {/* Row 3 — System Model + User Model parallel dashboards */}
      <div className="grid md:grid-cols-2 gap-8">
        
        {/* System Model Info */}
        <div className="bg-bg-panel p-6 rounded-2xl border border-border-subtle space-y-4">
          <div className="flex items-center justify-between border-b border-border-subtle pb-3">
            <h4 className="text-xs font-mono text-accent-violet font-bold uppercase tracking-wider flex items-center gap-1.5">
              <Server className="w-3.5 h-3.5 text-accent-violet" />
              SYSTEM MODEL CONFIG (REACTIVE SPECTRA)
            </h4>
            <span className="text-[10px] font-mono text-text-muted">Ref: ONNX SENTIS</span>
          </div>

          <div className="grid grid-cols-2 gap-4 text-xs font-mono">
            <div className="p-3 bg-bg-deep rounded-xl border border-border-subtle space-y-1">
              <span className="text-text-muted block text-[10px] uppercase">Gameplay Adapter</span>
              <span className="text-white font-bold">{checkpoint ? "ONNX Loaded" : "Standard Fallback"}</span>
            </div>
            
            <div className="p-3 bg-bg-deep rounded-xl border border-border-subtle space-y-1">
              <span className="text-text-muted block text-[10px] uppercase">Ledger mark strikes</span>
              <span className={`font-bold ${agent.strikes > 0 ? 'text-accent-rose' : 'text-accent-emerald'}`}>{agent.strikes} Marks</span>
            </div>

            <div className="p-3 bg-bg-deep rounded-xl border border-border-subtle space-y-1">
              <span className="text-text-muted block text-[10px] uppercase">Active Brain Mode</span>
              <span className="text-accent-violet font-bold truncate block">{agent.mood}</span>
            </div>

            <div className="p-3 bg-bg-deep rounded-xl border border-border-subtle space-y-1">
              <span className="text-text-muted block text-[10px] uppercase">Trained Method</span>
              <span className="text-white font-bold">PyTorch BC adapter</span>
            </div>
          </div>
        </div>

        {/* User Model Info */}
        <div className="bg-bg-panel p-6 rounded-2xl border border-border-subtle space-y-4">
          <div className="flex items-center justify-between border-b border-border-subtle pb-3">
            <h4 className="text-xs font-mono text-accent-emerald font-bold uppercase tracking-wider flex items-center gap-1.5">
              <User className="w-3.5 h-3.5 text-accent-emerald" />
              USER PERSPECTIVE MODEL (INTENT MATRICES)
            </h4>
            <span className="text-[10px] font-mono text-text-muted">Cohorts SPRING26</span>
          </div>

          <div className="grid grid-cols-2 gap-4 text-xs font-mono">
            <div className="p-3 bg-bg-deep rounded-xl border border-border-subtle space-y-1">
              <span className="text-text-muted block text-[10px] uppercase">Primary Player</span>
              <span className="text-white font-bold truncate block">{player.displayName}</span>
            </div>

            <div className="p-3 bg-bg-deep rounded-xl border border-border-subtle space-y-1">
              <span className="text-text-muted block text-[10px] uppercase">Stated Training Goal</span>
              <span className="text-accent-emerald font-bold">Optimize battle brakes</span>
            </div>

            <div className="p-3 bg-bg-deep rounded-xl border border-border-subtle space-y-1">
              <span className="text-text-muted block text-[10px] uppercase">Classroom syndicates</span>
              <span className="text-white font-bold">Spring 2026 Cohorts</span>
            </div>

            <div className="p-3 bg-bg-deep rounded-xl border border-border-subtle space-y-1">
              <span className="text-text-muted block text-[10px] uppercase">Linked Game Clients</span>
              <span className="text-white font-bold">{player.isLinked ? "Verified" : "Sync code generated"}</span>
            </div>
          </div>
        </div>

      </div>

    </div>
  );
}
