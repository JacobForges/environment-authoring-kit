import React, { useState } from 'react';
import { 
  ResponsiveContainer, RadarChart, PolarGrid, PolarAngleAxis, PolarRadiusAxis, Radar,
  AreaChart, Area, XAxis, YAxis, Tooltip, CartesianGrid,
  BarChart, Bar, LineChart, Line, ScatterChart, Scatter, LabelList
} from 'recharts';
import { 
  GitBranch, GitCommit, Split, Sliders, Play, Info, Flame,
  Cpu, Activity, TrendingUp, Filter, BarChart3, Database
} from 'lucide-react';
import { AgentProfile, CheckpointManifest } from '../types';

interface PageBrainLabProps {
  agent: AgentProfile;
  checkpoint: CheckpointManifest | null;
}

export default function PageBrainLab({ agent, checkpoint }: PageBrainLabProps) {
  const [selectedChk, setSelectedChk] = useState<'current' | 'baseline'>('current');
  
  // Chart 1: Radar Progression (Baseline vs Trained)
  const radarData = [
    { subject: 'Battle Speed', baseline: 40, active_v7: 90, fullMark: 100 },
    { subject: 'Braking Target', baseline: 30, active_v7: 85, fullMark: 100 },
    { subject: 'Signal Navigate', baseline: 60, active_v7: 75, fullMark: 105 },
    { subject: 'Explore Curiosity', baseline: 50, active_v7: 80, fullMark: 100 },
    { subject: 'Chat Cooperative', baseline: 80, active_v7: 95, fullMark: 100 },
    { subject: 'Reasoning Coherence', baseline: 25, active_v7: 70, fullMark: 100 }
  ];

  // Chart 2: Area Memory Allocation Growth across epochs
  const areaMemoryData = [
    { epoch: 'E1', weights_kb: 450, memory_mb: 24 },
    { epoch: 'E5', weights_kb: 550, memory_mb: 32 },
    { epoch: 'E10', weights_kb: 750, memory_mb: 48 },
    { epoch: 'E15', weights_kb: 1020, memory_mb: 64 },
    { epoch: 'E20', weights_kb: 1400, memory_mb: 92 },
    { epoch: 'E25', weights_kb: 1840, memory_mb: 120 }
  ];

  // Chart 3: Ratios of Activity Mix Stacked Bar across 5 runs
  const stackedBarData = [
    { run: 'Run_11', Battle: 12, Follow: 15, Explore: 8, Chat: 3 },
    { run: 'Run_12', Battle: 18, Follow: 12, Explore: 10, Chat: 5 },
    { run: 'Run_13', Battle: 24, Follow: 8, Explore: 14, Chat: 2 },
    { run: 'Run_14', Battle: 30, Follow: 14, Explore: 16, Chat: 4 },
    { run: 'Run_15', Battle: 45, Follow: 25, Explore: 20, Chat: 10 }
  ];

  // Chart 4: Rewards Distribution Curve across steps (highlighting mean, variance)
  const scoreCurveData = [
    { step: 1, baselineReward: -0.2, trainedReward: 0.1 },
    { step: 5, baselineReward: 0.1, trainedReward: 0.5 },
    { step: 10, baselineReward: 0.4, trainedReward: 1.2 },
    { step: 15, baselineReward: 1.2, trainedReward: 2.1 },
    { step: 20, baselineReward: 0.8, trainedReward: 2.9 },
    { step: 25, baselineReward: -1.5, trainedReward: 3.4 },
    { step: 30, baselineReward: 0.3, trainedReward: 4.1 },
    { step: 35, baselineReward: 1.6, trainedReward: 4.8 },
    { step: 40, baselineReward: 0.9, trainedReward: 5.6 }
  ];

  // Chart 5: INT telemetry score scatter representings
  const intScatterData = [
    { observations: 22, score: 3.2, label: 'Default' },
    { observations: 34, score: 4.5, label: 'Default' },
    { observations: 45, score: 5.8, label: 'Cloned' },
    { observations: 56, score: 6.9, label: 'Cloned' },
    { observations: 68, score: 8.2, label: 'Optimal' },
    { observations: 78, score: 9.4, label: 'Optimal' }
  ];

  // Chart 9: Loss convergence line
  const lossConvergenceData = [
    { step: 100, train_loss: 2.4, val_loss: 2.8 },
    { step: 200, train_loss: 1.8, val_loss: 2.1 },
    { step: 400, train_loss: 1.1, val_loss: 1.4 },
    { step: 600, train_loss: 0.6, val_loss: 0.9 },
    { step: 800, train_loss: 0.3, val_loss: 0.5 },
    { step: 1000, train_loss: 0.12, val_loss: 0.31 }
  ];

  // Chart 10: Temperature Calibration heat density grid values
  const attentionGridValues = [
    [0.91, 0.82, 0.74, 0.45],
    [0.85, 0.94, 0.88, 0.51],
    [0.61, 0.78, 0.96, 0.64],
    [0.34, 0.52, 0.71, 0.89]
  ];

  return (
    <div className="space-y-8">
      {/* Page Header */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 border-b border-border-subtle pb-6">
        <div>
          <span className="text-xs font-mono text-accent-violet font-bold uppercase tracking-wider">weights & biases analytical workbench</span>
          <h2 className="text-3xl font-display font-bold text-white mt-1">Brain & Analytics Lab</h2>
          <p className="text-text-secondary text-sm font-sans mt-1">
            DTA lineage model inspection. Dive into active loss convergences, radar policy progress, temperature attention grids, and lineage checkpoint DAGs.
          </p>
        </div>
        
        {/* Toggle selectors */}
        <div className="flex items-center gap-2 bg-bg-panel border border-border-subtle p-1 rounded-xl font-mono text-xs">
          <button 
            onClick={() => setSelectedChk('current')}
            className={`px-3 py-1.5 rounded-lg transition-all ${selectedChk === 'current' ? 'bg-accent-cyan text-[#070B14] font-bold' : 'text-text-secondary hover:text-white'}`}
          >
            active_companion_v7
          </button>
          <button 
            onClick={() => setSelectedChk('baseline')}
            className={`px-3 py-1.5 rounded-lg transition-all ${selectedChk === 'baseline' ? 'bg-accent-cyan text-[#070B14] font-bold' : 'text-text-secondary hover:text-white'}`}
          >
            baseline_sentis_global
          </button>
        </div>
      </div>

      <div className="p-4 bg-accent-violet/5 border border-accent-violet/12 rounded-xl flex items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <Activity className="w-5 h-5 text-accent-violet shrink-0" />
          <div className="text-xs text-text-secondary font-sans leading-relaxed">
            <strong className="text-white">Active session check:</strong> Inspecting model hashes. Divergence threshold calibrated to <code className="text-accent-cyan">0.024</code>. All 4 telemetry lanes compiled perfectly with loss convergences matched.
          </div>
        </div>
      </div>

      {/* Analytics Main Dashboard Grid: 10 High Fidelity Analytics visualizations */}
      <div className="grid lg:grid-cols-12 gap-8">
        
        {/* Chart 1: Radar Progression Policy (Left col, Span 6) */}
        <div className="lg:col-span-6 bg-[#0F1629] p-6 rounded-2xl border border-border-subtle flex flex-col justify-between">
          <div className="border-b border-border-subtle pb-3 mb-4 flex justify-between items-center">
            <span className="text-xs font-mono text-accent-cyan font-bold block uppercase tracking-wider">Chart 1: Radar policy progression map</span>
            <span className="text-[10px] font-mono text-text-muted">Checkpoint lineage alignment</span>
          </div>
          <div className="h-72 flex items-center justify-center">
            <ResponsiveContainer width="100%" height="100%">
              <RadarChart cx="50%" cy="50%" outerRadius="80%" data={radarData}>
                <PolarGrid stroke="rgba(148, 163, 184, 0.1)" />
                <PolarAngleAxis dataKey="subject" stroke="#94A3B8" style={{ fontSize: '10px', fontFamily: 'monospace' }} />
                <PolarRadiusAxis angle={30} domain={[0, 100]} stroke="#64748B" style={{ fontSize: '9px' }} />
                <Radar name="Baseline Unity policy" dataKey="baseline" stroke="#A78BFA" fill="#A78BFA" fillOpacity={0.15} />
                <Radar name="Active companion v7" dataKey="active_v7" stroke="#22D3EE" fill="#22D3EE" fillOpacity={0.25} />
                <Tooltip 
                  contentStyle={{ backgroundColor: '#0F1629', borderColor: '#1E293B', color: '#F1F5F9' }}
                  itemStyle={{ fontSize: '10px', fontFamily: 'monospace' }}
                />
              </RadarChart>
            </ResponsiveContainer>
          </div>
          <p className="text-[10px] text-text-muted font-sans mt-3 text-center">
            Comparing model before training (violet envelope) vs custom behavior clone v7 (cyan envelope).
          </p>
        </div>

        {/* Chart 2: Area Memory Allocation Growth (Right col, Span 6) */}
        <div className="lg:col-span-6 bg-[#0F1629] p-6 rounded-2xl border border-border-subtle flex flex-col justify-between">
          <div className="border-b border-border-subtle pb-3 mb-4 flex justify-between items-center">
            <span className="text-xs font-mono text-accent-violet font-bold block uppercase tracking-wider">Chart 2: Area Adapter Memory Weights Allocation</span>
            <span className="text-[10px] font-mono text-text-muted">Active Sentinel bytes</span>
          </div>
          <div className="h-72">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={areaMemoryData} margin={{ top: 10, right: 30, left: 0, bottom: 0 }}>
                <defs>
                  <linearGradient id="colorMemory" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#A78BFA" stopOpacity={0.8}/>
                    <stop offset="95%" stopColor="#A78BFA" stopOpacity={0}/>
                  </linearGradient>
                </defs>
                <XAxis dataKey="epoch" stroke="#64748B" style={{ fontSize: '10px', fontFamily: 'monospace' }} />
                <YAxis stroke="#64748B" style={{ fontSize: '10px', fontFamily: 'monospace' }} />
                <CartesianGrid strokeDasharray="3 3" stroke="rgba(148, 163, 184, 0.05)" />
                <Tooltip 
                  contentStyle={{ backgroundColor: '#0F1629', borderColor: '#1E293B', color: '#F1F5F9' }}
                  itemStyle={{ fontSize: '10px', fontFamily: 'monospace' }}
                />
                <Area type="monotone" dataKey="weights_kb" stroke="#A78BFA" fillOpacity={1} fill="url(#colorMemory)" />
              </AreaChart>
            </ResponsiveContainer>
          </div>
          <p className="text-[10px] text-text-muted font-sans mt-3 text-center">
            Total active memory adapter size in kilobytes over training epoch configurations.
          </p>
        </div>

      </div>

      <div className="grid lg:grid-cols-3 gap-8">
        
        {/* Chart 3: Activity Mix Stacked Bar Chart */}
        <div className="bg-[#0F1629] p-6 rounded-2xl border border-border-subtle flex flex-col justify-between">
          <div className="border-b border-border-subtle pb-3 mb-4">
            <span className="text-xs font-mono text-accent-emerald font-bold block uppercase tracking-wider">Chart 3: Activity Mix stacked ratios</span>
          </div>
          <div className="h-48">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={stackedBarData}>
                <CartesianGrid strokeDasharray="3 3" stroke="rgba(148, 163, 184, 0.05)" />
                <XAxis dataKey="run" stroke="#64748B" style={{ fontSize: '9px', fontFamily: 'monospace' }} />
                <YAxis stroke="#64748B" style={{ fontSize: '9px', fontFamily: 'monospace' }} />
                <Tooltip 
                  contentStyle={{ backgroundColor: '#0F1629', borderColor: '#1E293B' }}
                  itemStyle={{ fontSize: '9px', fontFamily: 'monospace' }}
                />
                <Bar dataKey="Battle" stackId="a" fill="#22D3EE" />
                <Bar dataKey="Follow" stackId="a" fill="#A78BFA" />
                <Bar dataKey="Explore" stackId="a" fill="#10B981" />
              </BarChart>
            </ResponsiveContainer>
          </div>
          <p className="text-[10px] text-text-muted font-sans text-center mt-2">Historical ratios of imported episodes.</p>
        </div>

        {/* Chart 4: Rewards Distribution Curve */}
        <div className="bg-[#0F1629] p-6 rounded-2xl border border-border-subtle flex flex-col justify-between">
          <div className="border-b border-border-subtle pb-3 mb-4">
            <span className="text-xs font-mono text-accent-amber font-bold block uppercase tracking-wider">Chart 4: Rewards distribution curve</span>
          </div>
          <div className="h-48">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={scoreCurveData}>
                <CartesianGrid strokeDasharray="3 3" stroke="rgba(148, 163, 184, 0.05)" />
                <XAxis dataKey="step" stroke="#64748B" style={{ fontSize: '9px', fontFamily: 'monospace' }} />
                <YAxis stroke="#64748B" style={{ fontSize: '9px', fontFamily: 'monospace' }} />
                <Tooltip 
                  contentStyle={{ backgroundColor: '#0F1629', borderColor: '#1E293B' }}
                  itemStyle={{ fontSize: '9px', fontFamily: 'monospace' }}
                />
                <Line type="monotone" dataKey="baselineReward" stroke="#A78BFA" strokeWidth={1} dot={false} />
                <Line type="monotone" dataKey="trainedReward" stroke="#22D3EE" strokeWidth={2} dot={false} />
              </LineChart>
            </ResponsiveContainer>
          </div>
          <p className="text-[10px] text-text-muted font-sans text-center mt-2">Cyan reward mastery vs purple default spikes.</p>
        </div>

        {/* Chart 5: INT Score Scatter */}
        <div className="bg-[#0F1629] p-6 rounded-2xl border border-border-subtle flex flex-col justify-between">
          <div className="border-b border-border-subtle pb-3 mb-4">
            <span className="text-xs font-mono text-accent-rose font-bold block uppercase tracking-wider">Chart 5: INT score telemetry scatter</span>
          </div>
          <div className="h-48">
            <ResponsiveContainer width="100%" height="100%">
              <ScatterChart margin={{ top: 10, right: 10, bottom: 0, left: 0 }}>
                <CartesianGrid stroke="rgba(148, 163, 184, 0.05)" />
                <XAxis type="number" dataKey="observations" stroke="#64748B" style={{ fontSize: '9px' }} />
                <YAxis type="number" dataKey="score" stroke="#64748B" style={{ fontSize: '9px' }} />
                <Tooltip 
                  cursor={{ strokeDasharray: '3 3' }}
                  contentStyle={{ backgroundColor: '#0F1629', borderColor: '#1E293B' }}
                />
                <Scatter name="Observation density" data={intScatterData} fill="#22D3EE" />
              </ScatterChart>
            </ResponsiveContainer>
          </div>
          <p className="text-[10px] text-text-muted font-sans text-center mt-2">Divergent densities of neural decisions.</p>
        </div>

      </div>

      <div className="grid lg:grid-cols-12 gap-8">
        
        {/* Chart 6: Dual calibration gauges (Span 4) */}
        <div className="lg:col-span-4 bg-[#0F1629] p-6 rounded-2xl border border-border-subtle space-y-4">
          <div className="border-b border-border-subtle pb-3">
            <span className="text-xs font-mono text-accent-cyan font-bold block uppercase tracking-wider">Chart 6: Dual calibration SVG gauges</span>
          </div>

          <div className="grid grid-cols-2 gap-4 text-center">
            <div className="p-4 bg-bg-deep rounded-xl border border-border-subtle flex flex-col items-center justify-center">
              <svg className="w-16 h-16 transform -rotate-90" viewBox="0 0 100 100">
                <circle cx="50" cy="50" r="40" stroke="rgba(148, 163, 184, 0.08)" strokeWidth="8" fill="none" />
                <circle cx="50" cy="50" r="40" stroke="#10B981" strokeWidth="8" strokeDasharray="251" strokeDashoffset="25" strokeLinecap="round" fill="none" />
              </svg>
              <span className="text-xs font-mono font-bold text-white mt-2 block">90%</span>
              <span className="text-[9px] font-sans text-text-muted">ACTIVE CONGRUENT</span>
            </div>

            <div className="p-4 bg-bg-deep rounded-xl border border-border-subtle flex flex-col items-center justify-center">
              <svg className="w-16 h-16 transform -rotate-90" viewBox="0 0 100 100">
                <circle cx="50" cy="50" r="40" stroke="rgba(148, 163, 184, 0.08)" strokeWidth="8" fill="none" />
                <circle cx="50" cy="50" r="40" stroke="#F59E0B" strokeWidth="8" strokeDasharray="251" strokeDashoffset="125" strokeLinecap="round" fill="none" />
              </svg>
              <span className="text-xs font-mono font-bold text-white mt-2 block">50%</span>
              <span className="text-[9px] font-sans text-text-muted">BASELINE FUZZY</span>
            </div>
          </div>
          <p className="text-[10px] text-text-muted font-sans text-center">Comparing strategic confidence scores.</p>
        </div>

        {/* Chart 7: GIT Lineage Graph Map (Span 8) */}
        <div className="lg:col-span-8 bg-[#0F1629] p-6 rounded-2xl border border-border-subtle flex flex-col justify-between">
          <div className="border-b border-border-subtle pb-3 mb-4 flex justify-between items-center">
            <span className="text-xs font-mono text-accent-violet font-bold block uppercase tracking-wider">Chart 7: GIT checkpoint lineage graph DAG</span>
            <span className="text-[10px] font-mono text-[#A78BFA] bg-[#A78BFA]/10 px-2 py-0.5 rounded border border-[#A78BFA]/25">DIR ACYCLIC GRAPH</span>
          </div>

          <div className="p-4 bg-bg-deep rounded-xl border border-border-subtle space-y-4">
            <div className="flex flex-col md:flex-row items-center justify-around gap-6 relative">
              <div className="absolute left-1/4 right-1/4 top-1/2 h-[1px] bg-border-subtle border-dashed pointer-events-none hidden md:block" />
              
              {/* Node A */}
              <div className="flex flex-col items-center p-3 bg-bg-panel border border-[#A78BFA]/30 rounded-lg text-center z-10 w-44">
                <div className="w-6 h-6 rounded-full bg-[#A78BFA] text-[#070B14] text-xs font-bold flex items-center justify-center">A</div>
                <span className="font-mono text-[10px] text-white font-bold mt-1.5 block">baseline_sentis_v5</span>
                <span className="font-mono text-[8px] text-text-muted">SHA: 1FA9BC23</span>
              </div>
              
              <div className="text-text-muted text-xs font-mono">➜ branch out ➜</div>

              {/* Node B */}
              <div className="flex flex-col items-center p-3 bg-bg-panel border border-accent-cyan/30 rounded-lg text-center z-10 w-44">
                <div className="w-6 h-6 rounded-full bg-accent-cyan text-[#070B14] text-xs font-bold flex items-center justify-center">B</div>
                <span className="font-mono text-[10px] text-white font-bold mt-1.5 block">active_companion_v7</span>
                <span className="font-mono text-[8px] text-text-muted">SHA: {checkpoint ? checkpoint.sha256.substring(0, 8) : "F3A4BC8E"}</span>
              </div>
            </div>
          </div>
          <p className="text-[10px] text-text-muted font-sans text-center mt-3">Linear model derivative nodes synchronized directly from player action logs.</p>
        </div>

      </div>

      <div className="grid lg:grid-cols-2 gap-8">
        
        {/* Chart 9: Loss convergence line (Span 6) */}
        <div className="bg-[#0F1629] p-6 rounded-2xl border border-border-subtle flex flex-col justify-between">
          <div className="border-b border-border-subtle pb-3 mb-4">
            <span className="text-xs font-mono text-accent-rose font-bold block uppercase tracking-wider">Chart 9: Training vs validation loss convergence</span>
          </div>
          <div className="h-44">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={lossConvergenceData}>
                <CartesianGrid strokeDasharray="3 3" stroke="rgba(148, 163, 184, 0.05)" />
                <XAxis dataKey="step" stroke="#64748B" style={{ fontSize: '9px', fontFamily: 'monospace' }} />
                <YAxis stroke="#64748B" style={{ fontSize: '9px', fontFamily: 'monospace' }} />
                <Tooltip 
                  contentStyle={{ backgroundColor: '#0F1629', borderColor: '#1E293B' }}
                  itemStyle={{ fontSize: '9px', fontFamily: 'monospace' }}
                />
                <Line type="monotone" dataKey="train_loss" stroke="#F43F5E" strokeWidth={2} dot={false} />
                <Line type="monotone" dataKey="val_loss" stroke="#A78BFA" strokeWidth={1} dot={false} />
              </LineChart>
            </ResponsiveContainer>
          </div>
          <p className="text-[10px] text-text-muted font-sans text-center mt-2">Loss optimization. Minimal validation drift detected near step 1000.</p>
        </div>

        {/* Chart 10: Temperature Calibration heat density grid (Span 6) */}
        <div className="bg-[#0F1629] p-6 rounded-2xl border border-border-subtle flex flex-col justify-between">
          <div className="border-b border-border-subtle pb-3 mb-4">
            <span className="text-xs font-mono text-accent-cyan font-bold block uppercase tracking-wider">Chart 10: Attention weights weight calibration density grid</span>
          </div>
          <div className="grid grid-cols-4 gap-2 my-auto p-2 bg-bg-deep rounded-xl border border-border-subtle max-w-xs mx-auto">
            {attentionGridValues.flatMap((row, rIdx) => 
              row.map((val, cIdx) => (
                <div 
                  key={`${rIdx}-${cIdx}`}
                  className="aspect-square rounded flex items-center justify-center font-mono text-[9px] text-white font-bold transition-all relative group"
                  style={{
                    backgroundColor: `rgba(34, 211, 238, ${val - 0.2})`,
                    border: '1px solid rgba(148, 163, 184, 0.12)'
                  }}
                >
                  {val}
                  <span className="absolute bottom-full left-1/2 -translate-x-1/2 bg-bg-panel text-[8px] px-1 py-0.5 rounded opacity-0 group-hover:opacity-100 transition duration-150 z-20 pointer-events-none whitespace-nowrap">
                    Node Attention Vector
                  </span>
                </div>
              ))
            )}
          </div>
          <p className="text-[10px] text-text-muted font-sans text-center mt-2">Custom heatmap showing calibrated transformer attention heads.</p>
        </div>

      </div>

      {/* Chart 8: Checkpoint A vs Checkpoint B Side-by-side Metric check */}
      <div className="bg-bg-panel p-6 rounded-2xl border border-border-subtle space-y-6">
        <div className="border-b border-border-subtle pb-3 flex justify-between items-center">
          <h3 className="text-sm font-display font-bold text-white uppercase tracking-wider flex items-center gap-2">
            <Sliders className="w-4 h-4 text-accent-cyan" />
            Chart 8: Checkpoint A vs Checkpoint B parallel compare
          </h3>
          <span className="text-[10px] font-mono text-text-muted">Side-by-side compare matrix</span>
        </div>

        <div className="grid md:grid-cols-2 gap-8">
          {/* Version A: Baseline */}
          <div className="space-y-4">
            <div className="flex items-center justify-between border-b border-border-subtle pb-2">
              <span className="text-xs font-mono text-[#A78BFA] font-bold">Baseline model_original</span>
              <span className="text-[10px] font-mono text-text-muted">SHA: 1FA9BC23</span>
            </div>
            
            <div className="space-y-2 text-xs font-mono">
              <div className="flex justify-between">
                <span className="text-text-secondary">Battle Precision Score</span>
                <span className="text-white">40 / 100</span>
              </div>
              <div className="flex justify-between">
                <span className="text-text-secondary">Target braking delay</span>
                <span className="text-accent-rose">1.4s</span>
              </div>
              <div className="flex justify-between">
                <span className="text-text-secondary">Curiosity exploration reach</span>
                <span className="text-white">50%</span>
              </div>
              <div className="flex justify-between">
                <span className="text-text-secondary">Ledger Strikes recorded</span>
                <span className="text-accent-rose">3 strikes</span>
              </div>
            </div>
          </div>

          {/* Version B: Trained Client */}
          <div className="space-y-4">
            <div className="flex items-center justify-between border-b border-border-subtle pb-2">
              <span className="text-xs font-mono text-accent-cyan font-bold">Active trained_v7 companion</span>
              <span className="text-[10px] font-mono text-text-muted">SHA: 9FB8230F</span>
            </div>

            <div className="space-y-2 text-xs font-mono">
              <div className="flex justify-between">
                <span className="text-text-secondary">Battle Precision Score</span>
                <span className="text-accent-emerald font-bold">90 / 100 (+125%)</span>
              </div>
              <div className="flex justify-between">
                <span className="text-text-secondary">Target braking delay</span>
                <span className="text-accent-emerald font-bold">0.4s (-71%)</span>
              </div>
              <div className="flex justify-between">
                <span className="text-text-secondary">Curiosity exploration reach</span>
                <span className="text-accent-emerald font-bold">80% (+60%)</span>
              </div>
              <div className="flex justify-between">
                <span className="text-text-secondary">Ledger Strikes recorded</span>
                <span className="text-accent-emerald font-bold">0 strikes (-100%)</span>
              </div>
            </div>
          </div>
        </div>
      </div>

    </div>
  );
}
