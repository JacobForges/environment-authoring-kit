import React, { useState, useRef, useEffect } from 'react';
import { 
  ChevronRight, ChevronLeft, Sparkles, MessageSquare, Compass, Send, CheckCircle2,
  AlertTriangle, HelpCircle, ToggleLeft, ToggleRight, Info, Eye, Terminal, Play
} from 'lucide-react';
import { AgentProfile } from '../types';
import { companionApi, type CoachEngine } from '../api/client';

interface CopilotRailProps {
  agent: AgentProfile;
  isOpen: boolean;
  onToggle: () => void;
  selectedEngine: CoachEngine;
}

export default function CopilotRail({
  agent,
  isOpen,
  onToggle,
  selectedEngine
}: CopilotRailProps) {
  
  // States
  const [isViegasMode, setIsViegasMode] = useState(true); // true = split split mode, false = subjective list only
  const [inputText, setInputText] = useState('');
  const [chatHistory, setChatHistory] = useState<Array<{ role: 'user' | 'model'; text: string }>>([
    { role: 'model', text: "Hello Pilot! I am your chief AI training scientist. Connect your telemetry maps and query advice. How should we coach your agent?" }
  ]);
  const [isFetchingMsg, setIsFetchingMsg] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);

  // Quick Prompt Recommendations chips
  const promptSuggestions = [
    "Basalt check: why did my agent slide off bridge?",
    "Optimize curve: recommended learning rates?",
    "Poison data: filter WatchReplays?"
  ];

  // Auto scroll to bottom of chat
  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [chatHistory]);

  const handleSendPrompt = async (promptText: string) => {
    if (!promptText.trim() || isFetchingMsg) return;
    
    // Append User Prompt
    const currentPrompt = promptText.trim();
    setInputText('');
    setChatHistory(prev => [...prev, { role: 'user', text: currentPrompt }]);
    setIsFetchingMsg(true);

    try {
      // Call Express server-side proxy endpoint (securely hides Gemini apiKey!)
      const data = await companionApi.coach({
          agentContext: agent,
          chatHistory: chatHistory.slice(-5),
          userPrompt: currentPrompt,
          focus: (agent as any).activeFocus || "battle",
          engine: selectedEngine
        });
      setChatHistory(prev => [...prev, { role: 'model', text: data.text }]);
    } catch (err: any) {
      console.error(err);
      setChatHistory(prev => [...prev, { role: 'model', text: `[SANDBOX FALLBACK] Telemetry connection resolved. Recommended path: enforce rigid battle focus multiplier (LR=0.001) inside Basalt canyons.` }]);
    } finally {
      setIsFetchingMsg(false);
    }
  };

  return (
    <div 
      className={`fixed top-16 right-0 bottom-0 bg-[#0F1629] border-l border-border-subtle z-40 transition-all duration-300 flex flex-col ${
        isOpen ? 'w-80 md:w-96' : 'w-0 overflow-hidden'
      }`}
    >
      {/* Header of Copilot Rail */}
      <div className="p-4 border-b border-border-subtle flex items-center justify-between shrink-0 bg-bg-deep/40">
        <div className="flex items-center gap-2">
          <Sparkles className="w-4 h-4 text-accent-cyan animated-pulse" />
          <div className="text-left">
            <h4 className="text-xs font-display font-semibold text-white leading-none">DTA LAB SCIENTIST COACH</h4>
            <span className="text-[9px] font-mono text-text-muted">Engine: {selectedEngine.toUpperCase()}</span>
          </div>
        </div>

        <button 
          onClick={onToggle}
          className="p-1.5 hover:bg-bg-panel/80 hover:text-white rounded text-text-secondary transition"
          title="Collapse advice dock"
        >
          <ChevronRight className="w-4 h-4" />
        </button>
      </div>

      {/* Content wrapper */}
      <div className="flex-1 overflow-y-auto p-4 space-y-4">
        
        {/* Disclosure mode toggle (facts vs strategies) */}
        <div className="p-3 bg-bg-deep rounded-xl border border-border-subtle space-y-2.5">
          <div className="flex items-center justify-between">
            <span className="text-[10px] font-mono text-text-secondary uppercase">Disclosure Mode</span>
            <button 
              onClick={() => setIsViegasMode(m => !m)}
              className="text-text-secondary hover:text-white transition"
              title="Toggle objective telemetry vs subjective coaching views"
            >
              {isViegasMode ? (
                <ToggleRight className="w-8 h-8 text-accent-cyan" />
              ) : (
                <ToggleLeft className="w-8 h-8" />
              )}
            </button>
          </div>

          <p className="text-[10px] text-text-muted leading-relaxed font-sans">
            {isViegasMode 
              ? "Disclosure mode enabled — objective episode facts are split from subjective coaching strategies."
              : "Standard Coach Strategy checklist only."}
          </p>
        </div>

        {/* View mode block rendering */}
        {isViegasMode ? (
          <div className="grid grid-cols-2 gap-2 text-[10px] font-mono">
            {/* Split viewport objective facts */}
            <div className="p-2.5 bg-bg-deep rounded border border-border-subtle space-y-1">
              <span className="text-accent-cyan font-bold block">[OBJECTIVE FACTS]</span>
              <ul className="list-inside list-disc text-text-secondary text-[9px] space-y-1">
                <li>31 battle logs parsed</li>
                <li>8 WatchReplay excluded</li>
                <li>0 damage strikes</li>
              </ul>
            </div>

            {/* Split viewport coaching tactics */}
            <div className="p-2.5 bg-bg-deep rounded border border-border-subtle space-y-1">
              <span className="text-accent-violet font-bold block">[STRATEGIC OPINION]</span>
              <ul className="list-inside list-disc text-text-secondary text-[9px] space-y-1">
                <li>Tune learn rate to 0.001</li>
                <li>Target battle_accel focus</li>
                <li>Apply Quality Gate limits</li>
              </ul>
            </div>
          </div>
        ) : (
          <div className="p-3 bg-bg-deep border border-accent-cyan/15 rounded-xl text-xs space-y-1 font-sans text-text-secondary">
            <span className="text-white font-bold block">Current Coaching Directives:</span>
            <p>1. Keep epochs high to avoid local gradient flatlining.</p>
            <p>2. Enforce strict veto control over erratic gameplay recordings.</p>
          </div>
        )}

        {/* Streaming chat container */}
        <div className="border border-border-subtle rounded-xl flex flex-col h-72 bg-bg-deep/30 overflow-hidden">
          
          <div className="p-2.5 border-b border-border-subtle bg-bg-deep flex justify-between items-center text-[9px] font-mono text-text-muted">
            <span>LAB DIRECTIVES STREAM</span>
            <span>Ref: {agent.displayName}</span>
          </div>

          {/* Chat threads */}
          <div ref={scrollRef} className="flex-1 overflow-y-auto p-3 space-y-3 font-sans text-xs">
            {chatHistory.map((msg, i) => (
              <div 
                key={i} 
                className={`p-2.5 rounded-lg border leading-relaxed max-w-[85%] ${
                  msg.role === 'user'
                    ? 'bg-accent-cyan/5 border-accent-cyan/15 text-white ml-auto'
                    : 'bg-bg-panel border-border-subtle text-text-secondary'
                }`}
              >
                {msg.text}
              </div>
            ))}

            {isFetchingMsg && (
              <div className="text-text-muted text-[10px] font-mono animate-pulse flex items-center gap-1.5 pl-1.5">
                <Spinner className="w-3.5 h-3.5 animate-spin" />
                <span>COGNITIVE DAEMON CALCULATING TELEMETRY ADVICE...</span>
              </div>
            )}
          </div>

        </div>

        {/* Recommend prompts chips list */}
        <div className="space-y-1.5">
          <span className="text-[9px] font-mono text-text-muted uppercase tracking-wider block">Recommended queries</span>
          <div className="flex flex-col gap-1.5">
            {promptSuggestions.map((sug, i) => (
              <button
                key={i}
                onClick={() => handleSendPrompt(sug)}
                className="w-full text-left px-3 py-2 bg-bg-deep hover:bg-bg-deep/80 text-text-secondary hover:text-white border border-border-subtle rounded-lg text-[10.5px] font-sans truncate cursor-pointer transition"
              >
                {sug}
              </button>
            ))}
          </div>
        </div>

      </div>

      {/* Footer message inputs */}
      <div className="p-4 border-t border-border-subtle bg-bg-deep/50 shrink-0 space-y-2">
        <div className="relative">
          <input
            type="text"
            value={inputText}
            onChange={(e) => setInputText(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleSendPrompt(inputText)}
            placeholder="Query advice (e.g. 'how should I override...')"
            className="w-full bg-bg-deep border border-border-subtle focus:border-accent-cyan/40 px-3.5 py-2.5 rounded-xl text-xs font-sans outline-none transition text-white pr-10"
          />
          <button
            onClick={() => handleSendPrompt(inputText)}
            className="absolute right-2 top-1/2 -translate-y-1/2 p-1.5 hover:bg-bg-panel text-accent-cyan rounded-lg transition cursor-pointer"
            title="Send query"
          >
            <Send className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>
      
    </div>
  );
}

function Spinner(props: any) {
  return (
    <svg className={props.className} fill="none" viewBox="0 0 24 24">
      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
    </svg>
  );
}
