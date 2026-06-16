import React, { useState, useMemo } from 'react';
import { 
  Play, Sliders, Server, Filter, CheckCircle2, XCircle, Search, 
  Trash2, Terminal, RefreshCw, Cpu, Award, HelpCircle, ArrowRight
} from 'lucide-react';
import { EpisodeDataRow, CheckpointManifest } from '../types';

interface PageTrainingStudioProps {
  agentId: string;
  onTrainingFinished: (manifest: CheckpointManifest) => void;
  isTrainingComplete: boolean;
}

// Initial mock dataset catalog rows for filtering/quality gating
const initialEpisodeRows: EpisodeDataRow[] = [
  { episodeId: 'eps_101_battle_obs', activity: 'battle', authority: 'ManualOverride', reward: 1.8, punishmentSpike: 2.1, approved: true, reason: 'Valid human behavior vector', timestamp: '2026-06-15T12:00:00Z' },
  { episodeId: 'eps_102_battle_obs', activity: 'battle', authority: 'ManualOverride', reward: 2.4, punishmentSpike: 1.5, approved: true, reason: 'High velocity alignment', timestamp: '2026-06-15T12:05:00Z' },
  { episodeId: 'eps_103_battle_obs', activity: 'battle', authority: 'WatchReplay', reward: 0.8, punishmentSpike: 0.1, approved: false, reason: 'Rejected: Passive WatchReplay source', timestamp: '2026-06-15T12:10:00Z' },
  { episodeId: 'eps_104_battle_obs', activity: 'battle', authority: 'ManualOverride', reward: -1.2, punishmentSpike: 16.4, approved: false, reason: 'Rejected: Punishment threshold > 15', timestamp: '2026-06-15T12:15:00Z' },
  { episodeId: 'eps_105_follow_obs', activity: 'follow', authority: 'ManualOverride', reward: 1.5, punishmentSpike: 0.8, approved: true, reason: 'Precise curve tracking', timestamp: '2026-06-15T12:20:00Z' },
  { episodeId: 'eps_106_follow_obs', activity: 'follow', authority: 'DefaultPolicy', reward: 0.4, punishmentSpike: 1.2, approved: true, reason: 'Autonomous alignment sync', timestamp: '2026-06-15T12:25:00Z' },
  { episodeId: 'eps_107_explore_obs', activity: 'explore', authority: 'ManualOverride', reward: 2.1, punishmentSpike: 2.5, approved: true, reason: 'Basalt cave discovered', timestamp: '2026-06-15T12:30:00Z' },
  { episodeId: 'eps_108_explore_obs', activity: 'explore', authority: 'WatchReplay', reward: 1.9, punishmentSpike: 0.4, approved: false, reason: 'Rejected: Passive WatchReplay source', timestamp: '2026-06-15T12:35:00Z' },
  { episodeId: 'eps_109_chat_obs', activity: 'chat', authority: 'ManualOverride', reward: 1.1, punishmentSpike: 0.0, approved: true, reason: 'Optimized vocab match', timestamp: '2026-06-15T12:40:00Z' }
];

export default function PageTrainingStudio({
  agentId,
  onTrainingFinished,
  isTrainingComplete
}: PageTrainingStudioProps) {
  
  // Selection States
  const [focusActivity, setFocusActivity] = useState<'battle' | 'follow' | 'explore' | 'chat'>('battle');
  const [epochs, setEpochs] = useState<number>(20);
  const [learningRate, setLearningRate] = useState<number>(0.001);
  const [batchSize, setBatchSize] = useState<number>(32);
  const [searchQuery, setSearchQuery] = useState('');
  
  // Dataset table Rows
  const [episodeRows, setEpisodeRows] = useState<EpisodeDataRow[]>(initialEpisodeRows);
  
  // Terminal Simulations State
  const [isCompiling, setIsCompiling] = useState(false);
  const [compileProgress, setCompileProgress] = useState(0);
  const [terminalLogs, setTerminalLogs] = useState<string[]>([]);
  const [compiledResult, setCompiledResult] = useState<CheckpointManifest | null>(null);

  // Quality gate: Reject logic dynamically updated based on active focus selection
  const processedRows = useMemo(() => {
    return episodeRows.map(row => {
      let isApproved = true;
      let reason = 'Approved for cloning';

      // Constraint 1: Must match active focus activity selection
      if (row.activity !== focusActivity) {
        isApproved = false;
        reason = `Rejected: Mismatched focus (requires '${focusActivity}')`;
      }
      // Constraint 2: Reject WatchReplay passive data
      else if (row.authority === 'WatchReplay') {
        isApproved = false;
        reason = 'Rejected: Passive WatchReplay (spectator paths disallowed)';
      }
      // Constraint 3: Reject rapid punishment spikes
      else if (row.punishmentSpike > 15) {
        isApproved = false;
        reason = 'Rejected: Poison punishment spike (value > 15)';
      }
      // Constraint 4: Reject reward below mean floor for specific battle actions
      else if (focusActivity === 'battle' && row.reward < 1.0) {
        isApproved = false;
        reason = 'Rejected: Reward floor check failed (requires >= 1.0)';
      }

      return {
        ...row,
        approved: isApproved,
        reason
      };
    });
  }, [episodeRows, focusActivity]);

  // Approved vs Rejected breakdown counters
  const approvedCount = useMemo(() => processedRows.filter(r => r.approved).length, [processedRows]);
  const rejectedCount = useMemo(() => processedRows.filter(r => !r.approved).length, [processedRows]);

  // Start Compile Pipeline
  const startBehaviorCloning = async () => {
    if (approvedCount === 0) {
      alert("Quality Gate Alert: Cannot initialize compile job with 0 approved telemetry rows! Select an activity focus or add matching valid rows.");
      return;
    }

    setIsCompiling(true);
    setCompileProgress(0);
    setCompiledResult(null);
    setTerminalLogs([]);

    const steps = [
      "Initializing PyTorch Behavior Cloning Pipeline v6.0...",
      `Loading observations from telemetry catalog (Target Agent: ${agentId})...`,
      `Quality Gate applied: ${approvedCount} approved rows, ${rejectedCount} poisoned/passive rows excluded.`,
      `Initializing weights layer with model hyperparameters: Epochs=${epochs}, LR=${learningRate}, BatchSize=${batchSize}...`,
      "Starting Epoch 1: training_loss: 2.14, val_loss: 2.45",
      "Starting Epoch 5: training_loss: 1.48, val_loss: 1.82",
      "Starting Epoch 10: training_loss: 0.95, val_loss: 1.12",
      "Starting Epoch 15: training_loss: 0.42, val_loss: 0.65",
      "Starting Epoch 20: training_loss: 0.12, val_loss: 0.31",
      "Optimal loss convergence reached. Saving snapshot variables...",
      "Compiling neural network to standard Sentis ONNX layout...",
      "Computing SHA256 cryptographic seal..."
    ];

    let currentLogIndex = 0;
    
    const interval = setInterval(async () => {
      setCompileProgress(prev => {
        const nextProgress = prev + 10;
        
        // Output new logs at intervals
        if (nextProgress % 10 === 0 && currentLogIndex < steps.length) {
          setTerminalLogs(logs => [...logs, `[DTA-COMPILE] ${steps[currentLogIndex]}`]);
          currentLogIndex++;
        }

        if (nextProgress >= 100) {
          clearInterval(interval);
          finishCompilation();
        }
        return nextProgress;
      });
    }, 250);
  };

  const finishCompilation = async () => {
    try {
      // Direct full-stack server network save trigger call
      const res = await fetch("/api/train", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          agentId,
          focusActivity,
          datasetRows: approvedCount,
          hyperparameters: { epochs, learningRate, batchSize }
        })
      });
      const data = await res.json();
      
      if (data.success) {
        const manifest: CheckpointManifest = {
          checkpointVersion: 7,
          agentId,
          sha256: data.sha256,
          trainUtc: data.trainUtc,
          focusActivity: data.focusActivity,
          rowsUsed: data.rowsUsed,
          rowsRejected: data.rowsRejected,
          trainMethod: "bc_adapter",
          onnxFile: "gameplay_adapter.onnx",
          accuracy: 94.6
        };
        
        setCompiledResult(manifest);
        setTerminalLogs(logs => [...logs, `[DTA-SUCCESS] Compiled adapter model exported! Version 7 is now ready in companion.`]);
        onTrainingFinished(manifest);
      }
    } catch (e) {
      console.error("Compilation error:", e);
      setTerminalLogs(logs => [...logs, "[DTA-ERROR] Failed to save compiled weights to full-stack sever."]);
    } finally {
      setIsCompiling(false);
    }
  };

  const handleRowVeto = (id: string) => {
    setEpisodeRows(rows => rows.map(r => r.episodeId === id ? { ...r, approved: !r.approved, reason: r.approved ? 'Manual User Veto override' : 'Manually restored' } : r));
  };

  return (
    <div className="space-y-8">
      {/* Title */}
      <div className="border-b border-border-subtle pb-6">
        <span className="text-xs font-mono text-accent-cyan font-bold uppercase tracking-wider">behavior cloning suite</span>
        <h2 className="text-3xl font-display font-semibold text-white mt-1">Behavior Cloning & Training Studio</h2>
        <p className="text-text-secondary text-sm font-sans mt-1">
          Adjust behavior cloning learning metrics, configure quality filtering gates, and compile raw telemetry logs into structured game brains.
        </p>
      </div>

      <div className="grid lg:grid-cols-12 gap-8">
        
        {/* Left Column: Hyperparameters & Selection chips (Span 5) */}
        <div className="lg:col-span-5 space-y-6">
          <div className="bg-bg-panel p-6 rounded-2xl border border-border-subtle space-y-6">
            <h3 className="text-sm font-display font-bold text-white uppercase tracking-wider flex items-center gap-2">
              <Sliders className="w-4 h-4 text-accent-cyan" />
              1. Hyperparameter tuning
            </h3>

            {/* Select Target Focus Chip selections */}
            <div className="space-y-2">
              <label className="text-xs font-mono text-text-secondary uppercase">Active Focus Channel</label>
              <div className="grid grid-cols-2 gap-2">
                {(['battle', 'follow', 'explore', 'chat'] as const).map((mode) => (
                  <button
                    key={mode}
                    onClick={() => setFocusActivity(mode)}
                    className={`py-2 px-3 rounded-lg text-xs font-mono font-bold border transition text-center cursor-pointer ${
                      focusActivity === mode
                        ? 'bg-accent-cyan/10 border-accent-cyan text-accent-cyan'
                        : 'bg-bg-deep border-border-subtle text-text-secondary hover:text-white'
                    }`}
                  >
                    {mode.toUpperCase()} MODE
                  </button>
                ))}
              </div>
            </div>

            {/* Range slider: Learning Rate */}
            <div className="space-y-2">
              <div className="flex justify-between items-center text-xs font-mono">
                <span className="text-text-secondary">LEARNING RATE (LR)</span>
                <span className="text-accent-cyan font-bold tabular-nums">{learningRate.toFixed(4)}</span>
              </div>
              <input
                type="range"
                min="0.0001"
                max="0.01"
                step="0.0001"
                value={learningRate}
                onChange={(e) => setLearningRate(parseFloat(e.target.value))}
                className="w-full accent-accent-cyan bg-bg-deep rounded-lg appearance-none h-1.5 cursor-pointer"
              />
            </div>

            {/* Numeric Input: Epoch configs */}
            <div className="space-y-2">
              <div className="flex justify-between items-center text-xs font-mono">
                <span className="text-text-secondary">TRAINING EPOCHS</span>
                <span className="text-white font-bold tabular-nums">{epochs} epochs</span>
              </div>
              <input
                type="number"
                min="1"
                max="100"
                value={epochs}
                onChange={(e) => setEpochs(parseInt(e.target.value) || 20)}
                className="w-full bg-bg-deep border border-border-subtle focus:border-accent-cyan/40 px-3.5 py-2.5 rounded-lg text-xs font-mono outline-none transition text-white"
              />
            </div>

            {/* Radio selectors: Batch size */}
            <div className="space-y-2">
              <label className="text-xs font-mono text-text-secondary uppercase">Batch Size (SGD Channels)</label>
              <div className="flex gap-4">
                {[16, 32, 64].map((size) => (
                  <label key={size} className="flex items-center gap-2 text-xs font-mono text-text-primary cursor-pointer">
                    <input
                      type="radio"
                      name="batchSize"
                      checked={batchSize === size}
                      onChange={() => setBatchSize(size)}
                      className="accent-accent-cyan cursor-pointer"
                    />
                    <span>{size} units</span>
                  </label>
                ))}
              </div>
            </div>

            {/* Start Compile CTA */}
            <button
              onClick={startBehaviorCloning}
              disabled={isCompiling}
              className={`w-full py-4 rounded-xl text-xs font-semibold cursor-pointer transition flex items-center justify-center gap-2 ${
                isCompiling
                  ? 'bg-accent-violet/20 text-accent-violet border border-accent-violet/30 cursor-not-allowed'
                  : 'bg-accent-cyan text-[#070B14] hover:bg-accent-cyan/90 font-bold shadow-lg shadow-accent-cyan/15'
              }`}
            >
              <Cpu className="w-4 h-4 fill-current animate-pulse" />
              <span>{isCompiling ? "COMPILING BACKEND PYTORCH WEIGHTS..." : "START BEHAVIOR CLONING COMPILE"}</span>
            </button>
          </div>

          {/* Compilation result card */}
          {compiledResult && (
            <div className="bg-bg-panel p-6 rounded-2xl border border-accent-emerald/30 shadow-lg shadow-accent-emerald/5 space-y-4 animate-fade-in">
              <div className="flex items-center gap-2 text-accent-emerald">
                <CheckCircle2 className="w-5 h-5" />
                <h4 className="text-sm font-display font-bold">Checkpoint compilation complete!</h4>
              </div>

              <div className="p-4 bg-bg-deep rounded-xl border border-border-subtle space-y-2.5 text-xs font-mono">
                <div className="flex justify-between">
                  <span className="text-text-muted">CHECKSUM SHA-256:</span>
                  <span className="text-white truncate max-w-[140px]">{compiledResult.sha256}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-text-muted">MODEL FILE SIZE:</span>
                  <span className="text-white">1.84 MB (920k parameters)</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-text-muted">FOCUS DIRECTION:</span>
                  <span className="text-accent-cyan font-bold">{compiledResult.focusActivity.toUpperCase()}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-text-muted">VALIDATION ACCURACY:</span>
                  <span className="text-accent-emerald font-bold">94.62%</span>
                </div>
              </div>

              <p className="text-[10.5px] text-text-secondary leading-relaxed font-sans">
                Neural adapter weights have been recorded inside server sandbox databases. Deploy checkpoint to load into active Unity client runtime.
              </p>
            </div>
          )}
        </div>

        {/* Right Column: Filter/Quality table (Span 7) */}
        <div className="lg:col-span-7 space-y-6 flex flex-col h-full justify-between">
          
          <div className="bg-bg-panel p-6 rounded-2xl border border-border-subtle space-y-6">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-border-subtle pb-4">
              <div>
                <h3 className="text-sm font-display font-bold text-white uppercase tracking-wider flex items-center gap-2">
                  <Filter className="w-4 h-4 text-accent-cyan" />
                  2. Quality Gate Funnel Filters
                </h3>
                <p className="text-[11px] text-text-secondary mt-0.5">
                  Automated quality gates and manual override constraints.
                </p>
              </div>

              {/* Counts display */}
              <div className="flex gap-2 font-mono text-[9px]">
                <span className="px-2 py-0.5 bg-accent-emerald/10 border border-accent-emerald/20 text-accent-emerald rounded">
                  {approvedCount} APPROVED
                </span>
                <span className="px-2 py-0.5 bg-accent-rose/10 border border-accent-rose/20 text-accent-rose rounded">
                  {rejectedCount} EXCLUDED
                </span>
              </div>
            </div>

            {/* Quality gate descriptions */}
            <div className="p-3.5 bg-bg-deep rounded-xl border border-border-subtle text-xs text-text-secondary space-y-1.5 font-sans leading-relaxed">
              <strong className="text-white block">Active Quality Rules Configured:</strong>
              <ul className="list-disc list-inside space-y-1 text-text-muted text-[11px] font-mono">
                <li>Activity must strictly match <span className="text-accent-cyan font-bold">{focusActivity.toUpperCase()}</span> focus settings.</li>
                <li>Reject spectator runs having <span className="text-accent-rose">WatchReplay</span> authority channels.</li>
                <li>Veto frames matching panic damage/brakes spikes <span className="text-accent-rose">punishment &gt; 15</span>.</li>
              </ul>
            </div>

            {/* Table layout of files */}
            <div className="overflow-x-auto">
              <table className="w-full text-left border-collapse text-xs">
                <thead>
                  <tr className="border-b border-border-subtle text-[10px] font-mono text-text-muted uppercase">
                    <th className="py-2.5">EPISODE SOURCE ID</th>
                    <th className="py-2.5">AUTHORITY</th>
                    <th className="py-2.5 text-center">MEAN REWARD</th>
                    <th className="py-2.5 text-center">PUNISH SPIKE</th>
                    <th className="py-2.5 text-right">METRIC GATE</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border-subtle font-mono text-[11px] tabular-nums text-text-secondary">
                  {processedRows.map((row) => (
                    <tr key={row.episodeId} className="hover:bg-bg-deep/50 transition">
                      <td className="py-3 text-text-primary font-medium">{row.episodeId}</td>
                      <td className="py-3">
                        <span className="px-1.5 py-0.5 bg-bg-deep border border-border-subtle rounded text-[9px]">
                          {row.authority}
                        </span>
                      </td>
                      <td className="py-3 text-center">{row.reward.toFixed(2)}</td>
                      <td className="py-3 text-center text-accent-amber">{row.punishmentSpike.toFixed(1)}</td>
                      <td className="py-3 text-right">
                        <button
                          onClick={() => handleRowVeto(row.episodeId)}
                          className={`px-2 py-0.5 rounded text-[9px] font-bold border cursor-pointer transition ${
                            row.approved
                              ? 'bg-accent-emerald/10 border-accent-emerald/20 text-accent-emerald'
                              : 'bg-accent-rose/10 border-accent-rose/20 text-accent-rose'
                          }`}
                          title="Click to toggle approve override state"
                        >
                          {row.approved ? "APPROVED" : "HELD BACK"}
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          {/* Terminal Console emulator stdout */}
          <div className="bg-[#070B14] p-5 rounded-2xl border border-border-subtle font-mono text-xs text-text-secondary space-y-4">
            <div className="flex items-center justify-between border-b border-border-subtle pb-2 text-[10px]">
              <span className="text-[#A78BFA] flex items-center gap-1.5">
                <Terminal className="w-3.5 h-3.5" />
                DTA_COGNITIVE_PYTORCH_DAEMON.stdout
              </span>
              <span className="text-text-muted">PID: 30048</span>
            </div>

            <div className="space-y-1.5 h-36 overflow-y-auto text-[10px] leading-relaxed select-text font-mono">
              {terminalLogs.length > 0 ? (
                terminalLogs.map((log, i) => (
                  <div key={i} className={log.includes("SUCCESS") ? 'text-accent-emerald font-bold' : log.includes("Initializing") ? 'text-accent-cyan' : 'text-text-muted'}>
                    {log}
                  </div>
                ))
              ) : (
                <div className="text-text-muted italic">Console daemon idle. Configure parameters and trigger Behavior Cloning compile.</div>
              )}
            </div>

            {isCompiling && (
              <div className="space-y-1">
                <div className="flex justify-between text-[10px] text-text-muted">
                  <span>COMPILING TENSOR CORES...</span>
                  <span>{compileProgress}%</span>
                </div>
                <div className="w-full h-1 bg-bg-panel rounded-full overflow-hidden">
                  <div className="bg-[#A78BFA] h-full rounded-full transition-all duration-300" style={{ width: `${compileProgress}%` }} />
                </div>
              </div>
            )}
          </div>

        </div>

      </div>
    </div>
  );
}
