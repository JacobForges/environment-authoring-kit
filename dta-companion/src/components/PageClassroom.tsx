import React, { useState } from 'react';
import { 
  Award, Shield, HelpCircle, Users, Trophy, Search, Cpu, CheckCircle2
} from 'lucide-react';
import { LeaderboardEntry } from '../types';

interface PageClassroomProps {
  leaderboard: LeaderboardEntry[];
  currentUserEmail: string | null;
}

export default function PageClassroom({ leaderboard, currentUserEmail }: PageClassroomProps) {
  const [filterCohort, setFilterCohort] = useState<string>('all');
  const [searchQuery, setSearchQuery] = useState('');

  const cohorts = [
    { id: 'all', name: 'Global Syndicate Ranks' },
    { id: 'DTA-SPRING26-A', name: 'Classroom Syndicate A (Basalt Biome)' },
    { id: 'DTA-SPRING26-B', name: 'Classroom Syndicate B (Slate Canyon)' }
  ];

  const processedLeaderboard = leaderboard.filter(entry => {
    // Filter by cohort association
    if (filterCohort !== 'all' && entry.cohortId !== filterCohort) {
      return false;
    }
    // Search query matches player display name or agent name
    if (searchQuery) {
      const query = searchQuery.toLowerCase();
      const nameMatch = entry.name.toLowerCase().includes(query);
      const detailMatch = entry.detail.toLowerCase().includes(query);
      return nameMatch || detailMatch;
    }
    return true;
  });

  return (
    <div className="space-y-8">
      {/* Title */}
      <div className="border-b border-border-subtle pb-6 flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <span className="text-xs font-mono text-accent-cyan font-bold uppercase tracking-wider">tactical classroom syndicates level</span>
          <h2 className="text-3xl font-display font-semibold text-white mt-1">Classroom Syndicate Leaders</h2>
          <p className="text-text-secondary text-sm font-sans mt-1">
            Track student milestones, inspect cohort average neural levels, and evaluate peer behaviors inside global training leaderboards.
          </p>
        </div>
      </div>

      <div className="grid lg:grid-cols-4 gap-8">
        
        {/* Left Side Filter block (Span 1) */}
        <div className="lg:col-span-1 space-y-4">
          <span className="text-[10px] font-mono text-text-muted uppercase tracking-wider block">Cohort Syndicates</span>
          
          <div className="space-y-2">
            {cohorts.map((coh) => (
              <button
                key={coh.id}
                onClick={() => setFilterCohort(coh.id)}
                className={`w-full text-left p-3.5 rounded-xl border text-xs font-medium cursor-pointer transition ${
                  filterCohort === coh.id
                    ? 'bg-accent-cyan/10 border-accent-cyan text-accent-cyan font-bold'
                    : 'bg-bg-panel border-border-subtle text-text-secondary hover:text-white'
                }`}
              >
                {coh.name}
              </button>
            ))}
          </div>

          <div className="p-4 bg-bg-panel rounded-xl border border-border-subtle text-[11px] text-text-secondary leading-relaxed">
            <Users className="w-4 h-4 text-accent-cyan mb-1.5" />
            <strong className="text-white block mb-0.5">Syndicate Objectives:</strong>
            Active cohort training task: <strong className="text-accent-violet">Optimize basalt curves & signal brakes</strong>. Average team level: L7.2.
          </div>
        </div>

        {/* Right Side Table spreadsheet (Span 3) */}
        <div className="lg:col-span-3 space-y-4">
          
          <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-4">
            {/* Search Input bar */}
            <div className="relative flex-1">
              <Search className="w-4 h-4 text-text-muted absolute left-3.5 top-1/2 -translate-y-1/2" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="Search pilot name or agent codenames..."
                className="w-full bg-bg-panel border border-border-subtle focus:border-accent-cyan/40 pl-10 pr-4 py-2.5 rounded-xl text-xs font-sans outline-none transition text-white"
              />
            </div>
          </div>

          {/* Leaderboard Table Spreadsheet */}
          <div className="bg-bg-panel border border-border-subtle rounded-2xl overflow-hidden">
            <table className="w-full text-left border-collapse text-xs">
              <thead>
                <tr className="border-b border-border-subtle text-[10px] font-mono text-text-muted uppercase bg-bg-deep/50">
                  <th className="py-3 px-6 text-center w-12">RANK</th>
                  <th className="py-3 px-4">PILOT STUDENT DISPLAY</th>
                  <th className="py-3 px-4">ACTIVE SQUAD AGENT</th>
                  <th className="py-3 px-4 text-center">LEVEL</th>
                  <th className="py-3 px-6 text-right">ACCUMULATED COGNITIVE XP</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border-subtle font-mono text-[11.5px] tabular-nums text-text-secondary">
                {processedLeaderboard.map((row, i) => {
                  const isCurrent = (row.email && currentUserEmail && row.email.toLowerCase() === currentUserEmail.toLowerCase()) || (i === 2 && !currentUserEmail); // Default highlighted Jacob index

                  return (
                    <tr 
                      key={row.id} 
                      className={`hover:bg-bg-deep/40 transition-colors ${
                        isCurrent 
                          ? 'bg-accent-cyan/5 border-l-2 border-l-accent-cyan' 
                          : ''
                      }`}
                    >
                      {/* Rank Cell */}
                      <td className="py-4 px-6 text-center">
                        <div className="flex items-center justify-center">
                          {i === 0 ? (
                            <Trophy className="w-4 h-4 text-accent-amber" />
                          ) : i === 1 ? (
                            <Trophy className="w-4 h-4 text-accent-cyan" />
                          ) : (
                            <span className="text-text-muted font-bold font-mono">#{i + 1}</span>
                          )}
                        </div>
                      </td>

                      {/* Name & Highlight check */}
                      <td className="py-4 px-4 font-sans font-medium text-white">
                        <span className="flex items-center gap-2">
                          {row.name}
                          {isCurrent && (
                            <span className="px-1.5 py-0.2 bg-accent-cyan/10 border border-accent-cyan/20 text-accent-cyan text-[8px] rounded uppercase font-mono tracking-wider font-bold">
                              YOU
                            </span>
                          )}
                        </span>
                      </td>

                      {/* Squad Detail */}
                      <td className="py-4 px-4 text-text-secondary flex items-center gap-1.5 pt-4.5">
                        <Cpu className="w-3.5 h-3.5 text-accent-violet shrink-0" />
                        <span className="truncate max-w-[150px]">{row.detail}</span>
                      </td>

                      {/* Companion Level */}
                      <td className="py-4 px-4 text-center font-bold text-white">L{row.level}</td>

                      {/* Accumulated XP */}
                      <td className="py-4 px-6 text-right text-accent-cyan font-bold tabular-nums">
                        {row.xp.toLocaleString()} XP
                      </td>

                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <div className="p-4 bg-bg-panel rounded-xl border border-border-subtle flex items-start gap-3">
            <Award className="w-4 h-4 text-accent-violet shrink-0 mt-0.5" />
            <div className="text-xs text-text-secondary leading-relaxed font-sans">
              <strong className="text-white">Syndicate Standing ledger check:</strong> Points are refreshed every 10 minutes from cloud-hosted Firestore logs. Level milestones contribute directly to team overall rank.
            </div>
          </div>

        </div>

      </div>
    </div>
  );
}
