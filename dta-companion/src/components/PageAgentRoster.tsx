import React from 'react';
import { Cpu, Download, ArrowRight, UploadCloud, Info, AlertTriangle, ShieldCheck } from 'lucide-react';
import { AgentProfile } from '../types';

interface PageAgentRosterProps {
  agents: AgentProfile[];
  activeAgentId: string;
  onSelectAgent: (agentId: string) => void;
  onNavigateToImport: () => void;
}

export default function PageAgentRoster({
  agents,
  activeAgentId,
  onSelectAgent,
  onNavigateToImport
}: PageAgentRosterProps) {
  
  const getAvatarGradient = (id: string) => {
    if (id === 'av_blue_orb') return 'from-accent-cyan to-accent-violet';
    if (id === 'av_emerald_sph') return 'from-accent-emerald to-accent-cyan';
    return 'from-accent-amber to-accent-rose';
  };

  return (
    <div className="space-y-8">
      {/* Roster Header */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 border-b border-border-subtle pb-6">
        <div>
          <span className="text-xs font-mono text-accent-cyan font-bold uppercase tracking-wider">LABORATORY COMPANIONS RECON</span>
          <h2 className="text-3xl font-display font-bold text-white mt-1">Agent Tactical Roster</h2>
          <p className="text-text-secondary text-sm font-sans mt-1">
            Choose which squadmate brain adapter to mount into the workspace lab. Configure focuses, explore telemetry, and compile ONNX weights.
          </p>
        </div>

        <button
          onClick={onNavigateToImport}
          className="px-5 py-3 bg-accent-cyan/10 hover:bg-accent-cyan/15 text-accent-cyan border border-accent-cyan/20 rounded-xl text-xs font-semibold cursor-pointer transition flex items-center gap-2"
        >
          <UploadCloud className="w-4 h-4" />
          Import Agent Vault (.hubai)
        </button>
      </div>

      {/* Grid of Agent Cards */}
      <div className="grid md:grid-cols-2 lg:grid-cols-3 gap-6">
        {agents.map((agent) => {
          const isActive = agent.agentId === activeAgentId;
          const gradientClass = getAvatarGradient(agent.avatarId);

          return (
            <div
              key={agent.agentId}
              onClick={() => onSelectAgent(agent.agentId)}
              className={`group relative rounded-2xl p-6 bg-bg-panel border transition cursor-pointer select-none flex flex-col justify-between ${
                isActive 
                  ? 'border-accent-cyan/40 shadow-glow bg-bg-panel/90' 
                  : 'border-border-subtle hover:border-border-glow/30 hover:shadow-glow'
              }`}
            >
              <div>
                {/* Card Top: Gradient Avatar & Level Badge */}
                <div className="flex items-start justify-between mb-4">
                  <div className={`w-12 h-12 rounded-xl bg-gradient-to-tr ${gradientClass} p-[1.5px] shadow-md`}>
                    <div className="w-full h-full bg-bg-panel rounded-[11px] flex items-center justify-center">
                      <Cpu className="w-5 h-5 text-white" />
                    </div>
                  </div>

                  <div className="flex flex-col items-end">
                    <span className="text-[10px] font-mono text-text-muted">CALIBRATED LVL</span>
                    <span className="text-xl font-display font-semibold text-white tracking-tight tabular-nums">
                      L{agent.level}
                    </span>
                  </div>
                </div>

                {/* Agent Identity */}
                <div className="space-y-2">
                  <h3 className="font-display text-lg font-bold text-white group-hover:text-accent-cyan transition flex items-center gap-2">
                    {agent.displayName}
                    {isActive && (
                      <span className="w-2 h-2 rounded-full bg-accent-cyan" title="Active selection" />
                    )}
                  </h3>
                  
                  <div className="flex flex-wrap gap-2">
                    <span className="text-[10px] font-mono px-2 py-0.5 bg-bg-deep border border-border-subtle rounded text-text-secondary">
                      ID: {agent.agentId.substring(0, 10)}...
                    </span>
                    <span className="text-[10px] font-mono px-2 py-0.5 bg-accent-violet/10 border border-accent-violet/20 rounded text-accent-violet lowercase">
                      {agent.mood}
                    </span>
                  </div>
                </div>

                {/* Progress bar */}
                <div className="mt-5 space-y-1.5">
                  <div className="flex justify-between text-[10px] font-mono text-text-muted">
                    <span>COGNITIVE MATURITY (XP)</span>
                    <span className="tabular-nums">{agent.xp} XP</span>
                  </div>
                  <div className="w-full h-1.5 bg-bg-deep rounded-full overflow-hidden">
                    <div 
                      className="h-full bg-gradient-to-r from-accent-cyan to-accent-violet rounded-full transition-all duration-500"
                      style={{ width: `${Math.min(100, (agent.xp / 4000) * 100)}%` }}
                    />
                  </div>
                </div>
              </div>

              {/* Stats & Actions */}
              <div className="mt-6 pt-4 border-t border-border-subtle space-y-4">
                <div className="grid grid-cols-2 gap-2 text-[11px] font-mono text-text-secondary">
                  <div>
                    <span className="block text-[9px] text-text-muted uppercase">Ledger Strikes</span>
                    <span className={`font-bold ${agent.strikes > 0 ? 'text-accent-rose' : 'text-accent-emerald'}`}>
                      {agent.strikes} Strike{agent.strikes !== 1 ? 's' : ''}
                    </span>
                  </div>
                  <div>
                    <span className="block text-[9px] text-text-muted uppercase">Trained Checkpoints</span>
                    <span className="font-bold text-white tabular-nums">
                      {agent.rewardsCount || 4} Revisions
                    </span>
                  </div>
                </div>

                <div className="flex items-center justify-between pt-2">
                  <span className="text-[10px] font-mono text-text-muted">
                    Synced: {new Date(agent.updatedAt).toLocaleDateString()}
                  </span>
                  
                  <div className="flex items-center gap-2">
                    <a
                      href={`/api/agent/download/${agent.agentId}`}
                      onClick={(e) => {
                        e.preventDefault();
                        alert(`Downloading standard original ${agent.displayName}.hubai file bundle...`);
                      }}
                      className="p-1.5 bg-bg-deep hover:bg-bg-elevated text-text-secondary hover:text-white rounded border border-border-subtle transition"
                      title="Download source .hubai"
                    >
                      <Download className="w-3.5 h-3.5" />
                    </a>
                    
                    <button
                      onClick={() => onSelectAgent(agent.agentId)}
                      className={`px-3.5 py-1.5 rounded-lg text-xs font-semibold cursor-pointer transition flex items-center gap-1 ${
                        isActive 
                          ? 'bg-accent-cyan text-[#070B14] hover:bg-accent-cyan/90' 
                          : 'bg-bg-elevated hover:bg-bg-elevated/80 text-text-primary'
                      }`}
                    >
                      <span>Analyze Lab</span>
                      <ArrowRight className="w-3 h-3" />
                    </button>
                  </div>
                </div>
              </div>
            </div>
          );
        })}

        {/* Empty Onboarding Card */}
        <div 
          onClick={onNavigateToImport}
          className="border-2 border-dashed border-border-subtle hover:border-accent-cyan/30 rounded-2xl p-6 flex flex-col items-center justify-center text-center gap-3 cursor-pointer group transition min-h-[300px]"
        >
          <div className="w-10 h-10 rounded-full bg-bg-panel flex items-center justify-center border border-border-subtle group-hover:border-accent-cyan/20 transition-colors">
            <UploadCloud className="w-5 h-5 text-text-muted group-hover:text-accent-cyan transition-colors" />
          </div>
          <div>
            <h4 className="text-white text-sm font-semibold font-display">Expand Tactical Squad</h4>
            <p className="text-text-secondary text-xs font-sans mt-1 max-w-[200px]">
              Export your Agent Vault inside Unity and drop files here to load additional squad brains.
            </p>
          </div>
        </div>
      </div>

      <div className="p-4 bg-bg-panel rounded-xl border border-border-subtle flex items-start gap-3">
        <Info className="w-4 h-4 text-accent-cyan shrink-0 mt-0.5" />
        <div className="text-xs text-text-secondary leading-relaxed">
          <strong className="text-white">Active Workspace State:</strong> Each squadmate holds independent neural network manifests. Changing selection aligns local caches and populates parallel dashboard indices.
        </div>
      </div>
    </div>
  );
}
