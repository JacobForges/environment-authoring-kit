/**
 * Copyright (c) 2026 JacobForges — DTA Training Companion™
 */

import React, { useState, useEffect } from 'react';
import { 
  Trophy, BookOpen, Layers, Key, RefreshCw, Cpu, Award, Download, LogOut, 
  Code, User, Menu, X, CheckSquare, Sparkles, Sliders, Settings, HelpCircle, 
  Inbox, Activity, Compass, Users
} from 'lucide-react';

// Import our beautiful modular pages
import PageLandingAuth from './components/PageLandingAuth';
import PageAgentRoster from './components/PageAgentRoster';
import PageHomeLab from './components/PageHomeLab';
import PageBrainLab from './components/PageBrainLab';
import PageTrainingStudio from './components/PageTrainingStudio';
import PageCheckpointDeploy from './components/PageCheckpointDeploy';
import PageImportExport from './components/PageImportExport';
import PageSettings from './components/PageSettings';
import PageClassroom from './components/PageClassroom';
import PageModelMarket from './components/PageModelMarket';
import PageAgentStudio from './components/PageAgentStudio';
import AppFooter from './components/AppFooter';
import OnboardingWizard from './components/OnboardingWizard';
import CopilotRail from './components/CopilotRail';
import { companionApi, type CoachEngine } from './api/client';
import { signOut as firebaseSignOut } from 'firebase/auth';
import { initFirebase } from './lib/firebase';

import { PlayerProfile, AgentProfile, LeaderboardEntry, CheckpointManifest } from './types';

// Seed Initial Profiles mimicking local file outputs
const initialPlayerProfile: PlayerProfile = {
  playerId: "usr_dta9943x",
  ugsPlayerId: "ugs-7253-x99f-asda",
  displayName: "Jacob Adkins",
  level: 4,
  xp: 3240,
  activeAgentId: "agent_train_0c1",
  cohortIds: ["DTA-SPRING26-A"],
  updatedAt: new Date().toISOString(),
  isLinked: false,
  rpgStats: {
    level: 4,
    xp: 3240,
    health: 95,
    stamina: 80,
    currencyGold: 450,
    currencyGems: 15,
    skills: [
      { name: "Dynamic Piloting", level: 2, description: "Increases rail stability in biomes." },
      { name: "Signals Routing", level: 1, description: "Decreases route calculation drag." }
    ],
    inventory: [
      { id: "inv_01", name: "Heavy Rail Shielding", quantity: 1, rarity: "Epic" },
      { id: "inv_02", name: "Glow Signal Lens", quantity: 2, rarity: "Common" },
      { id: "inv_03", name: "AI Core Optimizer", quantity: 1, rarity: "Legendary" },
      { id: "inv_04", name: "High-grade Coal Alloy", quantity: 12, rarity: "Common" }
    ],
    equipment: [
      { slot: "Chassis Armor", itemId: "eq_01", name: "Tungsten Plated Hull", bonus: "+30 Max HP" },
      { slot: "Rail Engine", itemId: "eq_02", name: "Hyper-induction Furnace", bonus: "+15% Velocity" },
      { slot: "Signal Matrix", itemId: "eq_03", name: "Omni-directional Scanner", bonus: "Reveal Nodes" }
    ],
    biomesDiscovered: ["Slate Canyons", "Basalt Bridges", "Verdant Overlook"],
    bossFlags: { "goliath_rail": false }
  }
};

const initialAgents: AgentProfile[] = [
  {
    agentId: "agent_train_0c1",
    playerId: "usr_dta9943x",
    displayName: "SensiScout Active Core",
    xp: 3100,
    level: 5,
    avatarId: "av_blue_orb",
    mood: "Attentive / Hyper-Focused",
    strikes: 0,
    rewardsCount: 7,
    trainingMilestones: ["Mapped Basalt curves with perfect velocity", "Cleared Slate signal nodes with 94% precision"],
    updatedAt: new Date().toISOString(),
    activeFocus: "battle"
  } as any,
  {
    agentId: "agent_train_0c2",
    playerId: "usr_dta9943x",
    displayName: "Slate-A4 Optimizer",
    xp: 1400,
    level: 2,
    avatarId: "av_emerald_sph",
    mood: "Learning / Passive",
    strikes: 1,
    rewardsCount: 4,
    trainingMilestones: ["Follow queue alignment calibrated"],
    updatedAt: new Date().toISOString(),
    activeFocus: "follow"
  } as any,
  {
    agentId: "agent_train_0c3",
    playerId: "usr_dta9943x",
    displayName: "Vance_AutoPilot Core",
    xp: 850,
    level: 1,
    avatarId: "av_amber_orb",
    mood: "Basal Calibrations Needed",
    strikes: 3,
    rewardsCount: 1,
    trainingMilestones: ["Base signals routing diagnostic run failed"],
    updatedAt: new Date().toISOString(),
    activeFocus: "explore"
  } as any
];

const initialLeaderboard: LeaderboardEntry[] = [
  { id: "usr_01", name: "Prof_Vance_01", type: "human", level: 12, xp: 18450, detail: "Vance_AutoPilot Core", cohortId: "DTA-SPRING26-A", email: "professor.vance@dta.local" },
  { id: "usr_02", name: "Sarah Connor", type: "human", level: 8, xp: 9400, detail: "Skynet Optimizer", cohortId: "DTA-SPRING26-B", email: "sarah@cohort.local" },
  { id: "usr_dta9943x", name: "Jacob Adkins", type: "human", level: 4, xp: 3240, detail: "SensiScout Active Core", cohortId: "DTA-SPRING26-A", email: "jacob.adkins@dta.local" },
  { id: "usr_04", name: "Leo_K_Rider", type: "human", level: 3, xp: 1950, detail: "Rookie Explorer", cohortId: "DTA-SPRING26-A", email: "leo@cohort.local" },
  { id: "usr_05", name: "Spectator_Tom", type: "human", level: 1, xp: 120, detail: "Passive spectator only", cohortId: "DTA-FALL26", email: "tom@cohort.local" }
];

export default function App() {
  const [activePage, setActivePage] = useState<'landing' | 'roster' | 'home' | 'brain' | 'train' | 'deploy' | 'import' | 'market' | 'studio' | 'settings' | 'classroom'>('landing');
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  
  // App States
  const [currentUser, setCurrentUser] = useState<{ email: string; role: 'student' | 'teacher' } | null>(null);

  const [playerProfile, setPlayerProfile] = useState<PlayerProfile>(initialPlayerProfile);
  const [agents, setAgents] = useState<AgentProfile[]>(initialAgents);
  const [activeAgentId, setActiveAgentId] = useState<string>("agent_train_0c1");
  const [activeEngine, setActiveEngine] = useState<CoachEngine>('auto');
  
  // Copilot and compile states
  const [isCopilotOpen, setIsCopilotOpen] = useState(true);
  const [isTrainingComplete, setIsTrainingComplete] = useState(false);
  const [checkpoint, setCheckpoint] = useState<CheckpointManifest | null>(null);
  const [showOnboarding, setShowOnboarding] = useState(false);

  const activeAgent = agents.find(a => a.agentId === activeAgentId) || agents[0];

  useEffect(() => {
    companionApi.onboarding().then((o) => {
      if (o.needsOnboarding) setShowOnboarding(true);
      if (o.displayName) {
        setPlayerProfile((prev) => ({ ...prev, displayName: o.displayName }));
      }
    }).catch(() => null);
    companionApi.health().catch(() => null);
    companionApi.loadSettings().then((s) => {
      if (typeof s.coachEngine === 'string') setActiveEngine(s.coachEngine as CoachEngine);
    }).catch(() => null);
    companionApi
      .syncSnapshot(activeAgentId)
      .then((snap) => {
        if (snap.activeAgentId) setActiveAgentId(snap.activeAgentId);
        setPlayerProfile((prev) => ({
          ...prev,
          displayName: snap.playerName || prev.displayName,
          activeAgentId: snap.activeAgentId || prev.activeAgentId,
        }));
      })
      .catch(() => null);
  }, [activeAgentId]);

  const handleAuthSuccess = (user: { email: string; role: 'student' | 'teacher' }) => {
    setCurrentUser(user);
    setPlayerProfile((prev) => ({
      ...prev,
      displayName: user.role === 'teacher' ? 'Teacher' : prev.displayName,
    }));
  };

  const handleSignIn = (role: 'student' | 'teacher') => {
    handleAuthSuccess({
      email: role === 'teacher' ? 'sandbox.teacher@local' : 'sandbox.student@local',
      role,
    });
  };

  const handleSignOut = async () => {
    try {
      const fb = await initFirebase();
      if (fb) await firebaseSignOut(fb.auth);
    } catch {
      // ignore
    }
    setCurrentUser(null);
  };

  const handleGenerateShortCode = () => {
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    let code = '';
    for (let i = 0; i < 6; i++) {
      if (i === 3) code += '-';
      code += chars.charAt(Math.floor(Math.random() * chars.length));
    }
    setPlayerProfile(prev => ({ ...prev, linkCode: code }));
  };

  // Mock checking redeem code inside Settings in Unity
  const handleImportSuccess = (details: { displayName: string; level: number; xp: number }) => {
    const newAgent: AgentProfile = {
      agentId: `agent_train_${Math.floor(Date.now() / 1000)}`,
      playerId: playerProfile.playerId,
      displayName: details.displayName,
      level: details.level,
      xp: details.xp,
      avatarId: "av_blue_orb",
      mood: "Perfect / Validated",
      strikes: 0,
      rewardsCount: 6,
      trainingMilestones: ["Imported check details verified successfully"],
      updatedAt: new Date().toISOString(),
      activeFocus: "battle"
    } as any;

    setAgents(prev => [newAgent, ...prev]);
    setActiveAgentId(newAgent.agentId);
    setActivePage('roster');
  };

  const handleTrainingFinished = (manifest: CheckpointManifest) => {
    setCheckpoint(manifest);
    setIsTrainingComplete(true);
    // Upgrade agent stats immediately to celebrate compile success!
    setAgents(prev => prev.map(a => a.agentId === activeAgentId ? {
      ...a,
      level: a.level + 1,
      xp: a.xp + 450,
      rewardsCount: (a.rewardsCount || 0) + 1,
      checkpointVersion: 7
    } as any : a));
  };

  const handleCurrencyChange = (gold: number, gems: number) => {
    setPlayerProfile((prev) => ({
      ...prev,
      rpgStats: { ...prev.rpgStats, currencyGold: gold, currencyGems: gems },
    }));
  };

  return (
    <div className="min-h-screen bg-bg-deep text-text-primary flex flex-col font-sans selection:bg-accent-cyan/20 selection:text-accent-cyan">
      
      {/* Top Navigation Panel Frame */}
      <header className="sticky top-0 z-50 bg-bg-deep/90 backdrop-blur-md border-b border-border-subtle shrink-0">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex items-center justify-between h-16">
            
            {/* Logo codename */}
            <div className="flex items-center gap-3">
              <div className="w-9 h-9 rounded-xl bg-gradient-to-tr from-accent-cyan to-accent-violet p-[1.5px] shadow-lg shadow-accent-cyan/15">
                <div className="w-full h-full bg-bg-deep rounded-[10px] flex items-center justify-center">
                  <Cpu className="w-4 h-4 text-accent-cyan animate-pulse" />
                </div>
              </div>
              <div>
                <span className="font-display font-extrabold tracking-tight text-white text-sm">Deep Train Academy</span>
                <span className="text-[10px] bg-bg-panel border border-border-subtle text-accent-cyan px-1.5 py-0.2 rounded ml-2 font-mono">LAB COMPANION</span>
              </div>
            </div>

            {/* Desktop Navigation */}
            <nav className="hidden xl:flex items-center gap-1.5 text-xs font-mono font-medium tracking-wide">
              <button 
                onClick={() => setActivePage('landing')}
                className={`px-3 py-2 rounded-lg transition-all cursor-pointer ${activePage === 'landing' ? 'text-accent-cyan bg-accent-cyan/5 border border-accent-cyan/15 font-bold' : 'text-text-secondary hover:text-white'}`}
              >
                Entrance
              </button>
              
              <button 
                onClick={() => setActivePage('roster')}
                className={`px-3 py-2 rounded-lg transition-all cursor-pointer ${activePage === 'roster' ? 'text-accent-cyan bg-accent-cyan/5 border border-accent-cyan/15 font-bold' : 'text-text-secondary hover:text-white'}`}
              >
                Squad Roster
              </button>

              <button 
                onClick={() => setActivePage('home')}
                className={`px-3 py-2 rounded-lg transition-all cursor-pointer ${activePage === 'home' ? 'text-accent-cyan bg-accent-cyan/5 border border-accent-cyan/15 font-bold' : 'text-text-secondary hover:text-white'}`}
              >
                Home Lab
              </button>

              <button 
                onClick={() => setActivePage('brain')}
                className={`px-3 py-2 rounded-lg transition-all cursor-pointer ${activePage === 'brain' ? 'text-accent-cyan bg-accent-cyan/5 border border-accent-cyan/15 font-bold' : 'text-text-secondary hover:text-white'}`}
              >
                Brain Lab
              </button>

              <button 
                onClick={() => setActivePage('train')}
                className={`px-3 py-2 rounded-lg transition-all cursor-pointer ${activePage === 'train' ? 'text-accent-cyan bg-accent-cyan/5 border border-accent-cyan/15 font-bold' : 'text-text-secondary hover:text-white'}`}
              >
                Training Studio
              </button>

              <button 
                onClick={() => setActivePage('deploy')}
                className={`px-3 py-2 rounded-lg transition-all cursor-pointer ${activePage === 'deploy' ? 'text-accent-cyan bg-accent-cyan/5 border border-accent-cyan/15 font-bold' : 'text-text-secondary hover:text-white'}`}
              >
                One-Click Deploy
              </button>

              <button 
                onClick={() => setActivePage('import')}
                className={`px-3 py-2 rounded-lg transition-all cursor-pointer ${activePage === 'import' ? 'text-accent-cyan bg-accent-cyan/5 border border-accent-cyan/15 font-bold' : 'text-text-secondary hover:text-white'}`}
              >
                Import/Export
              </button>

              <button 
                onClick={() => setActivePage('market')}
                className={`px-3 py-2 rounded-lg transition-all cursor-pointer ${activePage === 'market' ? 'text-accent-violet bg-accent-violet/5 border border-accent-violet/15 font-bold' : 'text-text-secondary hover:text-white'}`}
              >
                Model Market
              </button>

              <button 
                onClick={() => setActivePage('studio')}
                className={`px-3 py-2 rounded-lg transition-all cursor-pointer ${activePage === 'studio' ? 'text-accent-cyan bg-accent-cyan/5 border border-accent-cyan/15 font-bold' : 'text-text-secondary hover:text-white'}`}
              >
                Agent Studio
              </button>

              <button 
                onClick={() => setActivePage('classroom')}
                className={`px-3 py-2 rounded-lg transition-all cursor-pointer ${activePage === 'classroom' ? 'text-accent-cyan bg-accent-cyan/5 border border-accent-cyan/15 font-bold' : 'text-text-secondary hover:text-white'}`}
              >
                Classroom Syndicate
              </button>

              <button 
                onClick={() => setActivePage('settings')}
                className={`px-3 py-2 rounded-lg transition-all cursor-pointer ${activePage === 'settings' ? 'text-accent-cyan bg-accent-cyan/5 border border-accent-cyan/15 font-bold' : 'text-text-secondary hover:text-white'}`}
              >
                Settings
              </button>
            </nav>

            {/* Profile trigger or Coaching toggle */}
            <div className="hidden xl:flex items-center gap-3">
              <button
                onClick={() => setIsCopilotOpen(o => !o)}
                className={`px-3 py-1.5 border rounded-lg text-xs font-mono font-bold transition-all cursor-pointer ${
                  isCopilotOpen 
                    ? 'bg-accent-cyan/10 border-accent-cyan text-accent-cyan' 
                    : 'bg-bg-panel border-border-subtle text-text-secondary hover:text-white'
                }`}
              >
                {isCopilotOpen ? "Close Coach Rail" : "Open Coach Rail"}
              </button>

              {currentUser ? (
                <div className="flex items-center gap-2 border border-border-subtle bg-bg-panel px-3.5 py-1.5 rounded-lg text-xs font-mono">
                  <User className="w-3.5 h-3.5 text-text-secondary" />
                  <span className="text-text-secondary font-medium tracking-tight truncate max-w-[120px]">{currentUser.email}</span>
                  <button onClick={handleSignOut} className="text-text-secondary hover:text-accent-rose transition cursor-pointer" title="Sign out text">
                    <LogOut className="w-3.5 h-3.5" />
                  </button>
                </div>
              ) : (
                <button
                  onClick={() => setActivePage('landing')}
                  className="px-4 py-2 bg-accent-cyan text-[#070B14] text-xs font-bold font-sans rounded-lg hover:bg-accent-cyan/90 cursor-pointer select-none"
                >
                  Sign In
                </button>
              )}
            </div>

            {/* Mobile menu trigger */}
            <div className="xl:hidden flex items-center gap-2">
              <button
                onClick={() => setIsCopilotOpen(o => !o)}
                className="p-2 bg-bg-panel rounded-lg border border-border-subtle text-accent-cyan text-xs font-mono"
              >
                Coach
              </button>
              <button
                onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
                className="text-text-secondary hover:text-white transition cursor-pointer"
              >
                {mobileMenuOpen ? <X className="w-5 h-5" /> : <Menu className="w-5 h-5" />}
              </button>
            </div>

          </div>
        </div>

        {/* Mobile slide nav */}
        {mobileMenuOpen && (
          <div className="xl:hidden bg-bg-deep border-b border-border-subtle px-4 pt-2 pb-4 space-y-1 font-mono text-xs">
            {([
              ['landing', 'Entrance'],
              ['roster', 'Squad Roster'],
              ['home', 'Home Lab'],
              ['brain', 'Brain Lab'],
              ['train', 'Training Studio'],
              ['deploy', 'One-Click Deploy'],
              ['import', 'Import/Export'],
              ['market', 'Model Market'],
              ['studio', 'Agent Studio'],
              ['classroom', 'Classroom Syndicate'],
              ['settings', 'Settings']
            ] as const).map(([page, label]) => (
              <button
                key={page}
                onClick={() => { setActivePage(page); setMobileMenuOpen(false); }}
                className={`block w-full text-left px-3 py-2.5 rounded-lg font-mono ${
                  activePage === page ? 'text-accent-cyan bg-accent-cyan/5 font-bold' : 'text-text-secondary hover:text-white'
                }`}
              >
                {label}
              </button>
            ))}
          </div>
        )}
      </header>

      {/* Main stage with responsive grid layout (Gutter offsets) */}
      <div className="flex-1 flex relative">
        
        {/* Playable content frame block */}
        <div className="flex-1 max-w-7xl w-full mx-auto px-4 sm:px-6 lg:px-8 py-8 md:py-12 space-y-12">
          
          {activePage === 'landing' && (
            <PageLandingAuth
              playerProfile={playerProfile}
              onSignIn={handleSignIn}
              onAuthSuccess={handleAuthSuccess}
              currentUser={currentUser}
              onSignOut={handleSignOut}
              onGenerateCode={handleGenerateShortCode}
            />
          )}

          {activePage === 'roster' && (
            <PageAgentRoster
              agents={agents}
              activeAgentId={activeAgentId}
              onSelectAgent={(id) => { setActiveAgentId(id); setActivePage('home'); }}
              onNavigateToImport={() => setActivePage('import')}
            />
          )}

          {activePage === 'home' && (
            <PageHomeLab
              agent={activeAgent}
              player={playerProfile}
              checkpoint={checkpoint}
              onNavigatePage={(p) => setActivePage(p)}
              onRefreshData={() => alert("Refreshed active telemetry matrices.")}
              isTrainingComplete={isTrainingComplete}
            />
          )}

          {activePage === 'brain' && (
            <PageBrainLab
              agent={activeAgent}
              checkpoint={checkpoint}
            />
          )}

          {activePage === 'train' && (
            <PageTrainingStudio
              agentId={activeAgentId}
              onTrainingFinished={handleTrainingFinished}
              isTrainingComplete={isTrainingComplete}
            />
          )}

          {activePage === 'deploy' && (
            <PageCheckpointDeploy
              checkpoint={checkpoint}
              agentId={activeAgentId}
            />
          )}

          {activePage === 'import' && (
            <PageImportExport
              onImportSuccess={handleImportSuccess}
            />
          )}

          {activePage === 'market' && (
            <PageModelMarket activeAgentId={activeAgentId} />
          )}

          {activePage === 'studio' && (
            <PageAgentStudio
              activeAgentId={activeAgentId}
              playerProfile={playerProfile}
              onCurrencyChange={handleCurrencyChange}
            />
          )}

          {activePage === 'classroom' && (
            <PageClassroom
              leaderboard={initialLeaderboard}
              currentUserEmail={currentUser ? currentUser.email : null}
            />
          )}

          {activePage === 'settings' && (
            <PageSettings
              activeEngine={activeEngine}
              onEngineChange={(eng) => setActiveEngine(eng)}
              activeAgentId={activeAgentId}
              onRestartOnboarding={() => setShowOnboarding(true)}
            />
          )}

        </div>

        {/* Collapsible right hand Copilot Coach side-bar panel */}
        <CopilotRail
          agent={activeAgent}
          isOpen={isCopilotOpen}
          onToggle={() => setIsCopilotOpen(false)}
          selectedEngine={activeEngine}
        />

      </div>

      <AppFooter />

      {showOnboarding && (
        <OnboardingWizard
          onComplete={(name) => {
            setShowOnboarding(false);
            setPlayerProfile((prev) => ({ ...prev, displayName: name }));
            if (typeof name === 'string') setActivePage('roster');
          }}
        />
      )}

    </div>
  );
}
