import React, { useState } from 'react';
import { Key, Copy, Check, ShieldCheck, HelpCircle, Monitor, ArrowRight, CheckCircle2 } from 'lucide-react';
import { PlayerProfile } from '../types';

interface LinkingViewProps {
  playerProfile: PlayerProfile;
  onGenerateCode: () => void;
  onMockUnityRedeemCode: (code: string) => void;
}

export default function LinkingView({ playerProfile, onGenerateCode, onMockUnityRedeemCode }: LinkingViewProps) {
  const [copied, setCopied] = useState<boolean>(false);
  const [unityInputCode, setUnityInputCode] = useState<string>('');
  const [isRedeeming, setIsRedeeming] = useState<boolean>(false);
  const [redeemedSuccess, setRedeemedSuccess] = useState<boolean>(false);

  const handleCopy = () => {
    if (playerProfile.linkCode) {
      navigator.clipboard.writeText(playerProfile.linkCode);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  };

  const handleMockRedeem = (e: React.FormEvent) => {
    e.preventDefault();
    if (!unityInputCode.trim()) return;
    setIsRedeeming(true);
    setTimeout(() => {
      onMockUnityRedeemCode(unityInputCode.trim().toUpperCase());
      setIsRedeeming(false);
      setRedeemedSuccess(true);
      setUnityInputCode('');
      setTimeout(() => setRedeemedSuccess(false), 4000);
    }, 1500);
  };

  return (
    <div className="space-y-6 animate-fade-in" id="linking-view">
      
      {/* Title block */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-6 relative overflow-hidden">
        <div className="absolute right-0 top-0 w-64 h-64 bg-emerald-500/5 rounded-full blur-2xl"></div>
        <div className="max-w-xl space-y-2">
          <h2 className="font-sans font-bold text-lg text-slate-100 flex items-center gap-2">
            <Key className="w-5 h-5 text-emerald-400" />
            Unity Account Linker Panel
          </h2>
          <p className="text-xs text-slate-400 leading-relaxed font-sans">
            Your web profile handles high-visibility classrooms and static spectator boards, but your gameplay details originate on standalone local hardware. Generate and redeem a one-time cryptographic link code to unify your accounts.
          </p>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        
        {/* Step 1: Generate short Code */}
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-5 space-y-4">
          <div className="flex items-center gap-2 border-b border-slate-800 pb-2.5">
            <span className="w-5 h-5 rounded-full bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 flex items-center justify-center font-mono font-bold text-xs">1</span>
            <h3 className="font-sans font-bold text-sm text-slate-205 text-slate-200">Web Portal: Generate Link Code</h3>
          </div>

          <p className="text-xs text-slate-400 leading-relaxed font-sans">
            Click below to contact our matching cloud relay and produce a secure short link code mapping back to your active Firebase user session ID.
          </p>

          <div className="bg-slate-950 p-4 rounded-lg border border-slate-800/80 flex flex-col items-center justify-center text-center space-y-3 min-h-[160px]">
            {playerProfile.isLinked ? (
              <div className="space-y-2">
                <ShieldCheck className="w-10 h-10 text-emerald-400 mx-auto" />
                <h4 className="font-sans font-bold text-slate-200 text-xs">Accounts Linked!</h4>
                <p className="text-[10px] text-slate-500 font-mono">Linked UGS ID: {playerProfile.ugsPlayerId}</p>
              </div>
            ) : playerProfile.linkCode ? (
              <div className="space-y-3 w-full">
                <span className="text-[10px] font-mono text-slate-500 block uppercase">One-Time Link Code (valid 10m)</span>
                <div className="flex items-center justify-center gap-2">
                  <span className="font-mono text-2xl font-extrabold tracking-widest text-amber-400 bg-slate-900 px-6 py-2 rounded-lg border border-slate-800 select-all">
                    {playerProfile.linkCode}
                  </span>
                  <button 
                    onClick={handleCopy}
                    className="p-2 bg-slate-900 hover:bg-slate-850 rounded-lg border border-slate-800 hover:border-slate-700 transition cursor-pointer"
                  >
                    {copied ? <Check className="w-4 h-4 text-emerald-400" /> : <Copy className="w-4 h-4 text-slate-400" />}
                  </button>
                </div>
                <p className="text-[10px] text-slate-500">Enter this code inside Unity settings panel.</p>
              </div>
            ) : (
              <div className="space-y-3">
                <button
                  onClick={onGenerateCode}
                  className="bg-emerald-600 hover:bg-emerald-500 text-white font-mono text-xs font-bold px-5 py-2.5 rounded-lg border border-emerald-700 transition cursor-pointer select-none"
                >
                  GENERATE ONE-TIME CODE
                </button>
                <p className="text-[10px] text-slate-500">Contacting cloud relay...</p>
              </div>
            )}
          </div>
        </div>

        {/* Step 2: Unity redeem simulator */}
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-5 space-y-4">
          <div className="flex items-center gap-2 border-b border-slate-800 pb-2.5">
            <span className="w-5 h-5 rounded-full bg-sky-500/10 border border-sky-500/20 text-sky-450 text-sky-400 flex items-center justify-center font-mono font-bold text-xs">2</span>
            <h3 className="font-sans font-bold text-sm text-slate-205 text-slate-200">Unity Console: Redeem link code</h3>
          </div>

          <p className="text-xs text-slate-400 leading-relaxed font-sans">
            In-game, the player enters the 6-character code into settings. Below is a simulation of the Unity Game Engine sending the code back via the REST client.
          </p>

          <form onSubmit={handleMockRedeem} className="space-y-3 bg-slate-950 p-4 rounded-lg border border-slate-800/80 min-h-[160px] flex flex-col justify-between">
            <div className="space-y-2">
              <label className="block text-[10px] uppercase font-mono text-slate-500">Unity Settings Input Code</label>
              <div className="flex gap-2">
                <input 
                  type="text" 
                  placeholder="e.g. X7-R2Y"
                  value={unityInputCode}
                  onChange={(e) => setUnityInputCode(e.target.value)}
                  disabled={playerProfile.isLinked || isRedeeming}
                  className="bg-slate-900 border border-slate-800 hover:border-slate-700 rounded px-3 py-1.5 text-xs font-mono tracking-widest uppercase focus:outline-none focus:border-sky-500 text-slate-200 flex-1"
                />
                <button
                  type="submit"
                  disabled={playerProfile.isLinked || isRedeeming}
                  className="bg-sky-650 bg-sky-600 hover:bg-sky-550 text-white font-mono text-xs font-bold px-4 py-1.5 rounded transition cursor-pointer flex items-center gap-1.5 disabled:opacity-50 select-none disabled:cursor-not-allowed"
                >
                  REDEEM
                </button>
              </div>
            </div>

            {isRedeeming && (
              <p className="text-[10px] font-mono text-sky-400 animate-pulse">
                📲 posting code to endpoint /api/redeemLinkCode...
              </p>
            )}

            {redeemedSuccess && (
              <div className="flex items-center gap-1.5 text-[10px] font-mono text-emerald-400 bg-emerald-500/5 px-2.5 py-1.5 rounded border border-emerald-500/10">
                <CheckCircle2 className="w-3.5 h-3.5 text-emerald-400 shrink-0" />
                <span>Code verified! Unity local files linked to Web UID.</span>
              </div>
            )}

            {!isRedeeming && !redeemedSuccess && (
              <div className="text-[9.5px] font-mono text-slate-500 leading-normal flex items-start gap-1.5">
                <Monitor className="w-3.5 h-3.5 text-slate-600 shrink-0" />
                <span>Simulate Unity client request payload to link <code className="text-slate-400">ugsPlayerId</code> of game standalones.</span>
              </div>
            )}
          </form>
        </div>

      </div>
    </div>
  );
}
