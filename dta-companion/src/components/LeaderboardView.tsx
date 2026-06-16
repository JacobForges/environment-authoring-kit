import React, { useState, useEffect } from 'react';
import { Award, Users, RefreshCw, Trophy, Cpu, Clock, Filter } from 'lucide-react';
import { LeaderboardEntry } from '../types';

interface LeaderboardViewProps {
  humanEntries: LeaderboardEntry[];
  agentEntries: LeaderboardEntry[];
  cohortIds: string[];
}

export default function LeaderboardView({ humanEntries, agentEntries, cohortIds }: LeaderboardViewProps) {
  const [boardType, setBoardType] = useState<'human' | 'agent'>('human');
  const [selectedCohort, setSelectedCohort] = useState<string>('all');
  const [countdown, setCountdown] = useState<number>(30);
  const [isRefreshing, setIsRefreshing] = useState<boolean>(false);

  // Poll simulation every 30 seconds
  useEffect(() => {
    const timer = setInterval(() => {
      setCountdown((prev) => {
        if (prev <= 1) {
          triggerPoll();
          return 30;
        }
        return prev - 1;
      });
    }, 1000);
    return () => clearInterval(timer);
  }, []);

  const triggerPoll = () => {
    setIsRefreshing(true);
    setTimeout(() => {
      setIsRefreshing(false);
      setCountdown(30);
    }, 1000);
  };

  const getFilteredEntries = () => {
    const targetSet = boardType === 'human' ? humanEntries : agentEntries;
    if (selectedCohort === 'all') {
      return targetSet.sort((a,b) => b.xp - a.xp).map((entry, idx) => ({ ...entry, rank: idx + 1 }));
    }
    return targetSet
      .filter(entry => entry.cohortId === selectedCohort)
      .sort((a,b) => b.xp - a.xp)
      .map((entry, idx) => ({ ...entry, rank: idx + 1 }));
  };

  const activeRows = getFilteredEntries();

  return (
    <div className="space-y-6 animate-fade-in" id="leaderboard-view">
      {/* Controls Card */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-5 flex flex-col md:flex-row md:items-center justify-between gap-4">
        
        {/* Toggle Human / Agent boards */}
        <div className="flex bg-slate-950 p-1 border border-slate-800 rounded-lg shrink-0">
          <button
            onClick={() => setBoardType('human')}
            className={`flex items-center gap-2 px-4 py-2 font-mono text-xs font-bold rounded-md transition-all cursor-pointer ${
              boardType === 'human' 
                ? 'bg-emerald-600 text-white shadow-md' 
                : 'text-slate-400 hover:text-slate-200'
            }`}
          >
            <Trophy className="w-3.5 h-3.5" />
            Human Board
          </button>
          <button
            onClick={() => setBoardType('agent')}
            className={`flex items-center gap-2 px-4 py-2 font-mono text-xs font-bold rounded-md transition-all cursor-pointer ${
              boardType === 'agent' 
                ? 'bg-blue-650 bg-blue-600 text-white shadow-md' 
                : 'text-slate-400 hover:text-slate-200'
            }`}
          >
            <Cpu className="w-3.5 h-3.5" />
            Agent Board
          </button>
        </div>

        {/* Polling bar and poll controls */}
        <div className="flex flex-wrap items-center gap-4 text-xs">
          
          {/* Cohort Scoped selector */}
          <div className="flex items-center gap-2">
            <Filter className="w-3.5 h-3.5 text-slate-500" />
            <select
              value={selectedCohort}
              onChange={(e) => setSelectedCohort(e.target.value)}
              className="bg-slate-950 border border-slate-850 border-slate-800 text-slate-300 font-sans text-xs px-3 py-1.5 rounded-lg focus:outline-none focus:border-emerald-500"
            >
              <option value="all">Global (All cohorts)</option>
              {cohortIds.map(cohortId => (
                <option key={cohortId} value={cohortId}>{cohortId} Cohort</option>
              ))}
            </select>
          </div>

          {/* Polling progress indicators */}
          <div className="flex items-center gap-3 bg-slate-950 px-4 py-2 border border-slate-800/80 rounded-lg">
            <Clock className="w-3.5 h-3.5 text-slate-400" />
            <div className="w-20 bg-slate-800 h-1.5 rounded-full overflow-hidden">
              <div 
                className="bg-sky-500 h-full transition-all duration-1000" 
                style={{ width: `${(countdown / 30) * 100}%` }}
              ></div>
            </div>
            <span className="font-mono text-slate-400 text-[10.5px]">Refresh: {countdown}s</span>
            <button
              onClick={triggerPoll}
              disabled={isRefreshing}
              className="text-slate-500 hover:text-slate-350 transition transition cursor-pointer"
            >
              <RefreshCw className={`w-3.5 h-3.5 ${isRefreshing ? 'animate-spin text-emerald-400' : ''}`} />
            </button>
          </div>

        </div>

      </div>

      {/* Leaderboard Table */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl overflow-hidden shadow-xl">
        <div className="overflow-x-auto">
          <table className="w-full text-left border-collapse font-sans">
            <thead>
              <tr className="bg-slate-950 border-b border-slate-805 border-slate-800/80 text-xs font-mono font-bold text-slate-400">
                <th className="px-6 py-4 w-20 text-center">Rank</th>
                <th className="px-6 py-4">Identity Moniker</th>
                <th className="px-6 py-4 text-center">Level</th>
                <th className="px-6 py-4 text-right">Accumulated XP</th>
                <th className="px-6 py-4 text-center text-slate-500 font-normal">Classification</th>
              </tr>
            </thead>
            <tbody>
              {activeRows.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-6 py-12 text-center text-slate-500 text-xs font-sans">
                    No active leaderboard snapshot records registered for this cohort scope.
                  </td>
                </tr>
              ) : (
                activeRows.map((entry) => (
                  <tr 
                    key={entry.id} 
                    className="border-b last:border-0 border-slate-850 border-slate-800/40 hover:bg-slate-800/10 transition text-slate-300"
                  >
                    {/* Rank */}
                    <td className="px-6 py-4.5 text-center font-mono font-bold">
                      {entry.rank === 1 && <span className="bg-amber-500/10 text-amber-500 border border-amber-500/20 px-2 py-0.5 rounded-md text-xs">🥇 1</span>}
                      {entry.rank === 2 && <span className="text-slate-400">🥈 2</span>}
                      {entry.rank === 3 && <span className="text-amber-700">🥉 3</span>}
                      {entry.rank && entry.rank > 3 && <span className="text-slate-500">{entry.rank}</span>}
                    </td>

                    {/* Moniker Identity Details */}
                    <td className="px-6 py-4.5 min-w-[200px]">
                      <div className="flex items-center gap-3">
                        <div className={`w-8 h-8 rounded-full border flex items-center justify-center font-mono font-bold text-xs ${
                          boardType === 'human' 
                            ? 'bg-emerald-500/5 text-emerald-400 border-emerald-500/25' 
                            : 'bg-blue-500/5 text-blue-400 border-blue-500/25'
                        }`}>
                          {entry.name.slice(0, 2).toUpperCase()}
                        </div>
                        <div>
                          <p className="font-semibold text-slate-200 text-sm">{entry.name}</p>
                          <p className="text-[10px] text-slate-500 font-mono">{entry.id}</p>
                        </div>
                      </div>
                    </td>

                    {/* Level */}
                    <td className="px-6 py-4.5 text-center font-semibold font-mono text-xs text-white">
                      Lvl {entry.level}
                    </td>

                    {/* XP */}
                    <td className="px-6 py-4.5 text-right font-mono text-xs font-semibold text-sky-400">
                      {entry.xp.toLocaleString()} XP
                    </td>

                    {/* Scope details */}
                    <td className="px-6 py-4.5 text-center text-slate-500 text-[10.5px] font-mono">
                      {entry.detail}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
