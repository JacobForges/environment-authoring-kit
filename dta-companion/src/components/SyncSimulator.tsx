import React, { useState } from 'react';
import { RefreshCw, Play, Send, Code, Database, UserCheck, ShieldAlert, Cpu } from 'lucide-react';
import { PlayerProfile, AgentProfile, SyncLogEntry } from '../types';

interface SyncSimulatorProps {
  playerProfile: PlayerProfile;
  agentProfile: AgentProfile;
  onSyncComplete: (updatedPlayer: PlayerProfile, updatedAgent: AgentProfile, log: SyncLogEntry) => void;
}

export default function SyncSimulator({ playerProfile, agentProfile, onSyncComplete }: SyncSimulatorProps) {
  const [localLevel, setLocalLevel] = useState<number>(playerProfile.level);
  const [localXP, setLocalXP] = useState<number>(playerProfile.xp);
  const [localGold, setLocalGold] = useState<number>(playerProfile.rpgStats.currencyGold);
  
  const [agentName, setAgentName] = useState<string>(agentProfile.displayName);
  const [agentXP, setAgentXP] = useState<number>(agentProfile.xp);
  const [agentLevel, setAgentLevel] = useState<number>(agentProfile.level);
  
  const [isSyncing, setIsSyncing] = useState<boolean>(false);
  const [logTrigger, setLogTrigger] = useState<SyncLogEntry[]>([]);

  const handleSimulateSync = () => {
    setIsSyncing(true);
    
    setTimeout(() => {
      // Create snapshot representation
      const payloadObj = {
        playerId: playerProfile.playerId,
        displayName: playerProfile.displayName,
        level: localLevel,
        xp: localXP,
        activeAgentId: playerProfile.activeAgentId,
        cohortIds: playerProfile.cohortIds,
        updatedAt: new Date().toISOString(),
        rpgStats: {
          ...playerProfile.rpgStats,
          currencyGold: localGold
        },
        agentData: {
          agentId: agentProfile.agentId,
          displayName: agentName,
          xp: agentXP,
          level: agentLevel,
          updatedAt: new Date().toISOString()
        }
      };

      // Conflict Rules Logic calculation
      let resolvedLevel = localLevel;
      let resolvedXP = localXP;
      let conflictWarning = false;
      let conflictMsg = "";

      // Server Wins aggregate safeguard check
      if (playerProfile.level > localLevel) {
        resolvedLevel = playerProfile.level; // Server wins
        resolvedXP = playerProfile.xp;
        conflictWarning = true;
        conflictMsg = `[CONFLICT RESOLUTION] Server current level (${playerProfile.level}) is higher than Unity Client offline state (${localLevel}). Server-wins aggregate invariant applied. Preserving Web level ${playerProfile.level}.`;
      } else {
        conflictMsg = `[SYNC SYNCED] Unity client profile successfully applied to Firebase Firestore document path /players/${playerProfile.playerId}.`;
      }

      const updatedPlayer: PlayerProfile = {
        ...playerProfile,
        level: resolvedLevel,
        xp: resolvedXP,
        updatedAt: new Date().toISOString(),
        rpgStats: {
          ...playerProfile.rpgStats,
          currencyGold: localGold
        }
      };

      const updatedAgent: AgentProfile = {
        ...agentProfile,
        displayName: agentName,
        xp: agentXP,
        level: agentLevel,
        updatedAt: new Date().toISOString()
      };

      const newLog: SyncLogEntry = {
        timestamp: new Date().toLocaleTimeString(),
        type: conflictWarning ? 'CONFLICT_RESOLVED' : 'UPLOAD',
        payload: JSON.stringify(payloadObj, null, 2),
        direction: 'UNITY -> WEB',
        status: conflictWarning ? 'WARNING' : 'SUCCESS'
      };

      setLogTrigger(prev => [newLog, ...prev]);
      setIsSyncing(false);
      onSyncComplete(updatedPlayer, updatedAgent, newLog);
    }, 1200);
  };

  return (
    <div className="bg-slate-900 border border-slate-800 rounded-xl p-6 space-y-6" id="sync-simulator">
      {/* Title */}
      <div className="flex items-center justify-between border-b border-sidebar-divider pb-4 border-slate-800">
        <div className="flex items-center gap-2">
          <Cpu className="w-5 h-5 text-sky-400" />
          <div>
            <h3 className="font-sans text-sm font-bold text-slate-205 text-slate-100 uppercase tracking-wider">Unity Local Live Client Simulator</h3>
            <p className="font-sans text-[11px] text-slate-400">Simulate save-state file flushes to evaluate API & database synchronization logic.</p>
          </div>
        </div>
        <span className="text-[10px] bg-sky-500/10 text-sky-400 border border-sky-500/20 rounded-full px-2 py-0.5 font-mono font-bold">
          X-Unity-Version: 6000.0.35f1
        </span>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        
        {/* State snapshot inputs */}
        <div className="space-y-4">
          <h4 className="text-xs font-mono font-bold text-slate-300 uppercase tracking-widest border-b border-slate-800 pb-1.5 flex items-center justify-between">
            <span>Unity persistentDataPath/</span>
            <span className="text-[10px] text-slate-500 capitalize font-normal">local changes</span>
          </h4>
          
          <div className="space-y-4 bg-slate-950/55 p-4 rounded-lg border border-slate-800/80">
            {/* Player details */}
            <div className="space-y-3">
              <span className="text-[10px] font-mono text-emerald-400 font-semibold uppercase block bg-emerald-500/5 px-2 py-0.5 rounded w-max">
                📁 player_profile.json
              </span>
              <div className="grid grid-cols-3 gap-3">
                <div>
                  <label className="block text-[10px] text-slate-400 font-medium mb-1 font-sans">Player Level</label>
                  <input 
                    type="number" 
                    value={localLevel} 
                    onChange={(e) => setLocalLevel(parseInt(e.target.value) || 1)}
                    className="w-full bg-slate-900 border border-slate-700/80 rounded px-2.5 py-1.5 text-xs text-white focus:outline-none focus:border-sky-500"
                  />
                  <span className="text-[9px] text-slate-500 block mt-0.5">Try lower/higher</span>
                </div>
                <div>
                  <label className="block text-[10px] text-slate-400 font-medium mb-1">Player XP</label>
                  <input 
                    type="number" 
                    value={localXP} 
                    onChange={(e) => setLocalXP(parseInt(e.target.value) || 0)}
                    className="w-full bg-slate-900 border border-slate-700/80 rounded px-2.5 py-1.5 text-xs text-white focus:outline-none focus:border-sky-500"
                  />
                </div>
                <div>
                  <label className="block text-[10px] text-slate-400 font-medium mb-1">Gold Coins</label>
                  <input 
                    type="number" 
                    value={localGold} 
                    onChange={(e) => setLocalGold(parseInt(e.target.value) || 0)}
                    className="w-full bg-slate-900 border border-slate-700/80 rounded px-2.5 py-1.5 text-xs text-white focus:outline-none focus:border-sky-500"
                  />
                </div>
              </div>
            </div>

            {/* Agent details */}
            <div className="space-y-3 pt-3 border-t border-slate-800/80">
              <span className="text-[10px] font-mono text-blue-400 font-semibold uppercase block bg-blue-500/5 px-2 py-0.5 rounded w-max">
                📁 Agents/{playerProfile.activeAgentId}/profile.json
              </span>
              <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
                <div className="col-span-1">
                  <label className="block text-[10px] text-slate-400 font-medium mb-1">Agent Moniker</label>
                  <input 
                    type="text" 
                    value={agentName} 
                    onChange={(e) => setAgentName(e.target.value)}
                    className="w-full bg-slate-900 border border-slate-700/80 rounded px-2.5 py-1.5 text-xs text-white focus:outline-none focus:border-sky-500"
                  />
                </div>
                <div>
                  <label className="block text-[10px] text-slate-400 font-medium mb-1">Agent Level</label>
                  <input 
                    type="number" 
                    value={agentLevel} 
                    onChange={(e) => setAgentLevel(parseInt(e.target.value) || 1)}
                    className="w-full bg-slate-900 border border-slate-700/80 rounded px-2.5 py-1.5 text-xs text-white focus:outline-none focus:border-sky-500"
                  />
                </div>
                <div>
                  <label className="block text-[10px] text-slate-400 font-medium mb-1">Agent XP</label>
                  <input 
                    type="number" 
                    value={agentXP} 
                    onChange={(e) => setAgentXP(parseInt(e.target.value) || 0)}
                    className="w-full bg-slate-900 border border-slate-700/80 rounded px-2.5 py-1.5 text-xs text-white focus:outline-none focus:border-sky-500"
                  />
                </div>
              </div>
            </div>
          </div>

          <button
            onClick={handleSimulateSync}
            disabled={isSyncing}
            className={`w-full font-mono text-xs font-bold py-3 px-4 rounded-lg flex items-center justify-center gap-2 border cursor-pointer select-none transition-all duration-300 ${
              isSyncing 
                ? 'bg-sky-500/10 text-sky-400 border-sky-500/30' 
                : 'bg-emerald-600 hover:bg-emerald-500 text-white border-emerald-700 hover:border-emerald-600 shadow-md shadow-emerald-950/20'
            }`}
          >
            <RefreshCw className={`w-4 h-4 ${isSyncing ? 'animate-spin' : ''}`} />
            {isSyncing ? 'TRANSMITTING ENCRYPTED CLOUD SNAPSHOT...' : 'TRIGGER COMPETITION CLOUD SYNC SNAPSHOT'}
          </button>
        </div>

        {/* Console sync logger log */}
        <div className="space-y-4">
          <h4 className="text-xs font-mono font-bold text-slate-300 uppercase tracking-widest border-b border-slate-800 pb-1.5 flex items-center justify-between">
            <span>Terminal Transmission Logs</span>
            <span className="text-[9px] text-[#A3E635] bg-lime-500/5 px-2 py-0.5 rounded border border-lime-500/10 font-normal">Active Listeners</span>
          </h4>

          <div className="bg-slate-950 rounded-lg border border-slate-800 p-4 h-[240px] flex flex-col justify-between">
            <div className="font-mono text-[10.5px] overflow-y-auto space-y-3 flex-1 scrollbar-thin pr-1">
              {logTrigger.length === 0 ? (
                <div className="text-slate-500 h-full flex flex-col items-center justify-center text-center p-4">
                  <Database className="w-8 h-8 text-slate-700 mb-2 animate-pulse" />
                  <p>In-game CompetitionCloudSync.cs listener active.</p>
                  <p className="text-[9px] text-slate-605 mt-1 text-slate-600">Pending initial 4s checkpoint flush snapshot upload...</p>
                </div>
              ) : (
                logTrigger.map((log, index) => (
                  <div key={index} className="border-b border-slate-900 pb-2.5 last:border-0 last:pb-0">
                    <div className="flex items-center justify-between mb-1">
                      <span className="text-[9px] text-slate-500">{log.timestamp}</span>
                      <span className={`text-[9px] font-bold px-1.5 py-0.5 rounded ${
                        log.status === 'SUCCESS' 
                          ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/15'
                          : 'bg-yellow-500/10 text-amber-400 border border-yellow-500/15'
                      }`}>
                        {log.type}
                      </span>
                    </div>
                    <div className="text-slate-300 leading-relaxed font-sans text-xs">
                      {log.status === 'WARNING' 
                        ? '⚠️ Invariant Check: Client Level is lower than database! Reverted values back to Server-safe state.' 
                        : '✅ Snapshot fully synchronized. Firestore database profiles updated instantly.'}
                    </div>
                    {/* Collapsible/small payload code view */}
                    <div className="mt-1 bg-slate-900 p-1.5 rounded text-[9px] text-slate-400 border border-slate-850/50 max-h-[120px] overflow-y-auto">
                      <code className="whitespace-pre">{log.payload}</code>
                    </div>
                  </div>
                ))
              )}
            </div>
            <div className="text-[9.5px] font-mono text-slate-500 border-t border-slate-900 pt-2 flex items-center justify-between">
              <span>Sync Endpoint: /api/syncSnapshot</span>
              <span>REST Client active</span>
            </div>
          </div>
        </div>

      </div>
    </div>
  );
}
