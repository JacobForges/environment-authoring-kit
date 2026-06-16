import React from 'react';
import { Play, Download, ShieldAlert, Cpu, Award, Users, BookOpen } from 'lucide-react';

export default function HomeView() {
  return (
    <div className="space-y-12 animate-fade-in" id="home-view">
      {/* Hero Banner Grid */}
      <div className="relative rounded-2xl overflow-hidden bg-gradient-to-b from-slate-900 to-slate-950 border border-slate-800 p-8 md:p-12">
        <div className="absolute top-0 right-0 w-96 h-96 bg-emerald-500/5 blur-3xl rounded-full"></div>
        <div className="absolute bottom-0 left-0 w-80 h-80 bg-blue-500/5 blur-3xl rounded-full"></div>
        
        <div className="max-w-2xl space-y-6 relative">
          <div className="inline-flex items-center gap-2 px-3 py-1 bg-emerald-500/10 rounded-full border border-emerald-500/20 text-xs text-emerald-400 font-mono">
            <Cpu className="w-3.5 h-3.5" />
            <span>Unity 6 + URP Standalone Release</span>
          </div>
          
          <h1 className="font-sans font-extrabold tracking-tight text-white text-3xl md:text-5xl leading-tight">
            Deep Train Academy <br />
            <span className="text-transparent bg-clip-text bg-gradient-to-r from-emerald-400 to-sky-400">
              Companion Hub
            </span>
          </h1>
          
          <p className="text-slate-400 font-sans text-sm md:text-base leading-relaxed">
            Welcome to the centralized spectator and student control center for Deep Train Academy. Connect your web account to synchronize stats, inspect active agent progression sheets, and track live classroom milestones.
          </p>

          <div className="flex flex-wrap gap-4 pt-2">
            <a 
              href="#download" 
              className="bg-emerald-600 hover:bg-emerald-500 text-white font-medium text-xs md:text-sm px-6 py-3 rounded-lg flex items-center gap-2 shadow-lg shadow-emerald-950/20 transition cursor-pointer select-none"
            >
              <Download className="w-4 h-4" />
              Download Standalone Client
            </a>
            <a 
              href="#dossier-root" 
              className="bg-slate-900 hover:bg-slate-800 text-slate-300 font-medium text-xs md:text-sm px-6 py-3 rounded-lg flex items-center gap-2 border border-slate-800 hover:border-slate-700 transition cursor-pointer select-none animate-pulse-slow"
              onClick={(e) => {
                e.preventDefault();
                const spec = document.getElementById('dossier-root');
                spec?.scrollIntoView({ behavior: 'smooth' });
              }}
            >
              <BookOpen className="w-4 h-4" />
              View Architectural Dossier
            </a>
          </div>
        </div>
      </div>

      {/* Feature Bento Grid */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        
        <div className="bg-slate-900 border border-slate-800 p-6 rounded-xl hover:border-slate-700/80 transition group">
          <div className="p-3 bg-emerald-550/10 bg-emerald-500/5 text-emerald-400 w-max rounded-lg border border-emerald-500/10 mb-4">
            <Cpu className="w-5 h-5" />
          </div>
          <h3 className="font-sans font-bold text-slate-200 text-sm tracking-wide mb-2">Targeted Agent Sync</h3>
          <p className="text-xs text-slate-400 leading-relaxed font-sans">
            Your Unity client flushes progress logs every 4 seconds to persistent folders on local hardware, auto-uploading crucial attributes to Cloud Firestore securely.
          </p>
        </div>

        <div className="bg-slate-900 border border-slate-800 p-6 rounded-xl hover:border-slate-700/80 transition group">
          <div className="p-3 bg-blue-500/5 text-blue-400 w-max rounded-lg border border-blue-500/10 mb-4">
            <Award className="w-5 h-5" />
          </div>
          <h3 className="font-sans font-bold text-slate-200 text-sm tracking-wide mb-2">Segregated Leaderboards</h3>
          <p className="text-xs text-slate-400 leading-relaxed font-sans">
            Privacy first. Separated leaderboards verify human progression ranks vs AI Training neural milestone targets to prevent competitive interference.
          </p>
        </div>

        <div className="bg-slate-900 border border-slate-800 p-6 rounded-xl hover:border-slate-700/80 transition group">
          <div className="p-3 bg-purple-500/5 text-purple-400 w-max rounded-lg border border-purple-500/10 mb-4">
            <Users className="w-5 h-5" />
          </div>
          <h3 className="font-sans font-bold text-slate-200 text-sm tracking-wide mb-2">Classroom Cohorts</h3>
          <p className="text-xs text-slate-400 leading-relaxed font-sans">
            Join customized regional teacher cohorts in-game and on web. Create high-visibility scopes for student groups dynamically using isolated codes.
          </p>
        </div>

      </div>

      {/* Game Context Specs */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-8 space-y-6">
        <h2 className="font-sans font-bold text-lg text-slate-100 tracking-tight">Unity 6 Real-time Environment Context</h2>
        <div className="grid grid-cols-2 md:grid-cols-4 gap-6">
          <div className="space-y-1">
            <span className="text-[10px] text-slate-500 font-mono uppercase tracking-wider block">Total Multiplayer</span>
            <span className="font-sans text-xl font-bold text-slate-200">24 Max Players</span>
            <span className="text-[9px] text-slate-400 block">Relay + Vivox Voice</span>
          </div>
          <div className="space-y-1">
            <span className="text-[10px] text-slate-500 font-mono uppercase tracking-wider block">World Invariant</span>
            <span className="font-sans text-xl font-bold text-slate-200">World Seed Code</span>
            <span className="text-[9px] text-slate-400 block">Version matching active</span>
          </div>
          <div className="space-y-1">
            <span className="text-[10px] text-slate-500 font-mono uppercase tracking-wider block">Persistence Paths</span>
            <span className="font-sans text-lg font-mono font-semibold text-slate-200">persistentDataPath</span>
            <span className="text-[9px] text-slate-400 block font-mono">/Competition/save.json</span>
          </div>
          <div className="space-y-1">
            <span className="text-[10px] text-slate-500 font-mono uppercase tracking-wider block">Active Pipelines</span>
            <span className="font-sans text-xl font-bold text-slate-200">Unity 6 URP</span>
            <span className="text-[9px] text-slate-400 block">Windows/macOs Desktop</span>
          </div>
        </div>
      </div>
    </div>
  );
}
