import React, { useState } from 'react';
import { 
  Shield, Zap, User, Star, Plus, MapPin, Award, AlertTriangle, Cpu, Box, Sword
} from 'lucide-react';
import { PlayerProfile, AgentProfile } from '../types';

interface DashboardViewProps {
  playerProfile: PlayerProfile;
  agentProfile: AgentProfile;
  onJoinCohort: (cohortCode: string) => void;
}

export default function DashboardView({ playerProfile, agentProfile, onJoinCohort }: DashboardViewProps) {
  const [newCohortCode, setNewCohortCode] = useState<string>('');
  const [cohortMsg, setCohortMsg] = useState<string | null>(null);

  const handleJoinCohortSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newCohortCode.trim()) return;
    onJoinCohort(newCohortCode.trim().toUpperCase());
    setCohortMsg(`Successfully linked to classroom cohort "${newCohortCode.trim().toUpperCase()}"!`);
    setNewCohortCode('');
    setTimeout(() => setCohortMsg(null), 4000);
  };

  const rarityColor = (rarity: string) => {
    if (rarity === 'Legendary') return 'text-amber-400 bg-amber-400/5 border border-amber-400/15';
    if (rarity === 'Epic') return 'text-purple-400 bg-purple-400/5 border border-purple-400/15';
    return 'text-slate-400 bg-slate-900 border border-slate-800';
  };

  return (
    <div className="space-y-6 animate-fade-in" id="dashboard-view">
      
      {/* Dynamic Profile Sync Overview Banner */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-6 flex flex-col md:flex-row md:items-center justify-between gap-6">
        <div className="flex items-center gap-4">
          <div className="w-12 h-12 rounded-full bg-emerald-500/10 border border-emerald-500/20 flex items-center justify-center font-mono font-bold text-lg text-emerald-400 shrink-0">
            {playerProfile.displayName.slice(0, 2).toUpperCase()}
          </div>
          <div>
            <div className="flex items-center gap-2 flex-wrap">
              <h2 className="font-sans text-lg font-bold text-slate-100">{playerProfile.displayName}</h2>
              <span className="text-[10px] bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 px-2 py-0.5 rounded-full font-mono font-bold">
                Online Sync Linked
              </span>
            </div>
            <p className="text-xs text-slate-400 font-mono mt-0.5 mt-1">UGS ID: {playerProfile.ugsPlayerId}</p>
          </div>
        </div>

        {/* Sync Status / Time */}
        <div className="text-left md:text-right font-mono text-xs text-slate-400 space-y-1">
          <div><span className="text-slate-500">Last Synced Checkpoint:</span> {new Date(playerProfile.updatedAt).toLocaleTimeString()}</div>
          <div className="text-[11px] text-emerald-400/80">Updates flush on save checkpoint event every 4 seconds</div>
        </div>
      </div>

      {/* Cohort classroom join panel */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-5">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
          <div className="space-y-1">
            <h3 className="font-sans font-bold text-sm text-slate-100 tracking-wide flex items-center gap-2">
              <Award className="w-4 h-4 text-emerald-400" />
              Classroom Academic Cohorts
            </h3>
            <p className="text-xs text-slate-400">Join an isolated secondary training cohort registered by an educator.</p>
          </div>
          <form onSubmit={handleJoinCohortSubmit} className="flex gap-2">
            <input 
              type="text"
              placeholder="Enter Cohort Code"
              value={newCohortCode}
              onChange={(e) => setNewCohortCode(e.target.value)}
              className="bg-slate-950 border border-slate-800 rounded px-3 py-1.5 text-xs text-slate-200 focus:outline-none focus:border-emerald-500 uppercase font-mono w-40"
            />
            <button 
              type="submit"
              className="bg-emerald-600 hover:bg-emerald-500 text-white px-4 py-1.5 rounded text-xs font-semibold font-sans transition cursor-pointer flex items-center gap-1 shrink-0"
            >
              <Plus className="w-3.5 h-3.5" />
              Join Cohort
            </button>
          </form>
        </div>

        {cohortMsg && (
          <p className="text-xs text-emerald-400 font-mono bg-emerald-500/5 border border-emerald-500/10 rounded px-3 py-2 mt-4 animate-fade-in">
            {cohortMsg}
          </p>
        )}

        {/* Connected classroom cohorts lists */}
        {playerProfile.cohortIds.length > 0 && (
          <div className="flex flex-wrap gap-2 mt-4 pt-4 border-t border-slate-800/60">
            <span className="text-[10px] font-mono text-slate-500 uppercase tracking-wider self-center mr-2">Your Cohorts:</span>
            {playerProfile.cohortIds.map(cohortId => (
              <span key={cohortId} className="bg-slate-950 border border-slate-800 px-3 py-1 rounded text-xs text-slate-300 font-mono flex items-center gap-1.5">
                <span className="w-1.5 h-1.5 rounded-full bg-emerald-400"></span>
                {cohortId}
              </span>
            ))}
          </div>
        )}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        
        {/* RPG Character Stats Progression Cards */}
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-5 space-y-4 col-span-1 lg:col-span-2">
          <h3 className="font-sans font-bold text-sm text-slate-100 tracking-wide flex items-center gap-2 border-b border-slate-800 pb-2">
            <User className="w-4 h-4 text-emerald-400" />
            Character RPG Progression
          </h3>

          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <div className="bg-slate-950 p-4 rounded-lg border border-slate-800/80 text-center">
              <span className="text-[10px] font-mono text-slate-500 block uppercase">Level Rating</span>
              <span className="text-2xl font-bold font-sans text-white">Lvl {playerProfile.level}</span>
            </div>
            <div className="bg-slate-950 p-4 rounded-lg border border-slate-800/80 text-center">
              <span className="text-[10px] font-mono text-slate-500 block uppercase">Accumulated XP</span>
              <span className="text-xl font-bold font-sans text-sky-400">{playerProfile.xp.toLocaleString()}</span>
            </div>
            <div className="bg-slate-950 p-4 rounded-lg border border-slate-800/80 text-center">
              <span className="text-[10px] font-mono text-slate-500 block uppercase">Health Index</span>
              <span className="text-xl font-bold font-sans text-emerald-400">{playerProfile.rpgStats.health} HP</span>
            </div>
            <div className="bg-slate-950 p-4 rounded-lg border border-slate-800/80 text-center">
              <span className="text-[10px] font-mono text-slate-500 block uppercase">Stamina Index</span>
              <span className="text-xl font-bold font-sans text-amber-400">{playerProfile.rpgStats.stamina} ST</span>
            </div>
          </div>

          {/* Core equipment slot mappings */}
          <div className="space-y-2">
            <span className="text-[10px] font-mono text-slate-500 uppercase tracking-widest block">Active Equipment Matrix</span>
            <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
              {playerProfile.rpgStats.equipment.map((equip) => (
                <div key={equip.slot} className="bg-slate-950 p-3 rounded border border-slate-850/80 flex items-center gap-3">
                  <div className="p-2 bg-slate-900 border border-slate-800 rounded">
                    <Sword className="w-4 h-4 text-blue-400" />
                  </div>
                  <div>
                    <span className="text-[9px] text-slate-500 font-mono block uppercase">{equip.slot}</span>
                    <span className="text-xs text-slate-205 font-bold text-slate-200">{equip.name}</span>
                    <span className="text-[9px] text-emerald-400 block font-mono">{equip.bonus}</span>
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Discovery matrix: Biomes & bosses */}
          <div className="space-y-2 pt-2">
            <span className="text-[10px] font-mono text-slate-500 uppercase tracking-widest block">Explored Biome Logs</span>
            <div className="flex flex-wrap gap-1.5">
              {playerProfile.rpgStats.biomesDiscovered.map(biome => (
                <span key={biome} className="bg-slate-950 border border-slate-850/80 px-2.5 py-1 text-slate-400 rounded text-[11px] font-mono flex items-center gap-1 select-none">
                  <MapPin className="w-3 h-3 text-sky-400" />
                  {biome}
                </span>
              ))}
            </div>
          </div>
        </div>

        {/* Active AI Agent Stats Progression Card  */}
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-5 space-y-4 col-span-1">
          <h3 className="font-sans font-bold text-sm text-slate-100 tracking-wide flex items-center gap-2 border-b border-slate-800 pb-2">
            <Cpu className="w-4 h-4 text-blue-400" />
            Active AI Agent Progress
          </h3>

          <div className="space-y-4">
            <div className="flex items-center justify-between">
              <div>
                <h4 className="font-sans font-bold text-slate-200 text-sm">{agentProfile.displayName}</h4>
                <p className="text-[9.5px] font-mono text-slate-500">ID: {agentProfile.agentId}</p>
              </div>
              <span className="text-[10px] bg-blue-500/15 text-blue-400 border border-blue-500/20 px-2 py-0.5 rounded font-mono font-bold">
                Level {agentProfile.level}
              </span>
            </div>

            {/* XP GAUGE */}
            <div className="space-y-1.5">
              <div className="flex items-center justify-between text-[11px] font-mono text-slate-450 text-slate-400">
                <span>Agent XP Gauge</span>
                <span>{agentProfile.xp} / 5,000</span>
              </div>
              <div className="w-full bg-slate-950 h-2 rounded-full overflow-hidden border border-slate-800/80">
                <div 
                  className="bg-blue-500 h-full transition-all duration-500"
                  style={{ width: `${(agentProfile.xp / 5000) * 100}%` }}
                ></div>
              </div>
            </div>

            {/* MOOD & REWARDS */}
            <div className="grid grid-cols-2 gap-3 pt-2">
              <div className="bg-slate-950 p-2.5 rounded border border-slate-800/80">
                <span className="text-[9px] font-mono text-slate-550 text-slate-500 block uppercase">Active Mood</span>
                <span className="text-xs font-bold font-sans text-slate-300">{agentProfile.mood}</span>
              </div>
              <div className="bg-slate-950 p-2.5 rounded border border-slate-800/80">
                <span className="text-[9px] font-mono text-slate-550 text-slate-500 block uppercase">Reward Ledger</span>
                <span className="text-xs font-bold font-sans text-sky-400">{agentProfile.rewardsCount} counts</span>
              </div>
            </div>

            {/* COPPA Strikes indicator */}
            <div className="bg-slate-950 p-3.5 rounded-lg border border-slate-850 border-slate-800/80 space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-[10px] font-mono text-slate-400 font-semibold uppercase flex items-center gap-1">
                  <AlertTriangle className="w-3.5 h-3.5 text-yellow-500" /> Action Strikes Check
                </span>
                <span className="font-mono text-slate-300 text-xs font-bold">{agentProfile.strikes} / 3</span>
              </div>
              <div className="flex gap-2">
                {[1, 2, 3].map((val) => (
                  <div 
                    key={val} 
                    className={`h-1.5 flex-1 rounded-full ${
                      val <= agentProfile.strikes 
                        ? 'bg-rose-500 shadow-sm shadow-rose-950/20' 
                        : 'bg-slate-800'
                    }`}
                  ></div>
                ))}
              </div>
              <span className="text-[9px] text-slate-500 leading-relaxed block">
                Classroom safeguard: Automatically suspended in Unity core at 3 strikes. Reset requires teacher moderator bypass.
              </span>
            </div>

            {/* Training milestones */}
            <div className="space-y-1.5 pt-1">
              <span className="text-[10px] font-mono text-slate-500 uppercase tracking-widest block">AI Training Milestones</span>
              <div className="space-y-1 bg-slate-950/50 p-2 rounded border border-slate-850/60 max-h-[110px] overflow-y-auto">
                {agentProfile.trainingMilestones.map(milestone => (
                  <p key={milestone} className="text-[10px] font-sans text-slate-405 text-slate-400 flex items-center gap-1.5 py-0.5">
                    <Star className="w-3 h-3 text-amber-500 fill-amber-500/20" />
                    {milestone}
                  </p>
                ))}
              </div>
            </div>
          </div>
        </div>

      </div>

      {/* Capped inventory grid files */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-5 space-y-4">
        <h3 className="font-sans font-bold text-sm text-slate-100 tracking-wide flex items-center gap-2 border-b border-slate-800 pb-2">
          <Box className="w-4 h-4 text-emerald-400" />
          Capped System Inventory
        </h3>
        <div className="grid grid-cols-2 md:grid-cols-4 lg:grid-cols-6 gap-3">
          {playerProfile.rpgStats.inventory.map(item => (
            <div key={item.id} className="bg-slate-950 p-2.5 rounded border border-slate-850/60 flex flex-col justify-between h-20 text-xs font-sans">
              <div className="flex items-start justify-between gap-1">
                <span className="font-semibold text-slate-202 text-slate-300 truncate">{item.name}</span>
                <span className="font-mono text-[10px] text-slate-500">x{item.quantity}</span>
              </div>
              <span className={`text-[9px] font-mono font-bold px-1.5 py-0.5 rounded-full w-max ${rarityColor(item.rarity)}`}>
                {item.rarity}
              </span>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
