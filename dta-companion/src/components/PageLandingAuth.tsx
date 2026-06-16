import React, { useState } from 'react';
import { Cpu, Key, Play, ShieldAlert, Lock, Gamepad2, Layers } from 'lucide-react';
import { PlayerProfile } from '../types';
import { signInWithGoogle, signInWithEmail, initFirebase } from '../lib/firebase';

interface PageLandingAuthProps {
  playerProfile: PlayerProfile;
  onSignIn: (role: 'student' | 'teacher') => void;
  onAuthSuccess: (user: { email: string; role: 'student' | 'teacher' }) => void;
  currentUser: { email: string; role: 'student' | 'teacher' } | null;
  onSignOut: () => void;
  onGenerateCode: () => void;
}

export default function PageLandingAuth({
  playerProfile,
  onSignIn,
  onAuthSuccess,
  currentUser,
  onSignOut,
  onGenerateCode
}: PageLandingAuthProps) {
  const [emailInput, setEmailInput] = useState('');
  const [passwordInput, setPasswordInput] = useState('');
  const [authError, setAuthError] = useState<string | null>(null);
  const [firebaseReady, setFirebaseReady] = useState<boolean | null>(null);

  React.useEffect(() => {
    initFirebase().then((fb) => setFirebaseReady(!!fb)).catch(() => setFirebaseReady(false));
  }, []);

  const tryFirebaseEmail = async (role: 'student' | 'teacher') => {
    setAuthError(null);
    if (!emailInput.trim() || !passwordInput) {
      setAuthError('Enter email and password, or use sandbox sign-in.');
      return;
    }
    try {
      const user = await signInWithEmail(emailInput.trim(), passwordInput, role);
      onAuthSuccess({ email: user.email || emailInput, role });
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Auth failed';
      if (msg.includes('not configured')) {
        onSignIn(role);
        return;
      }
      setAuthError(msg);
    }
  };

  const tryGoogle = async () => {
    setAuthError(null);
    try {
      const user = await signInWithGoogle();
      onAuthSuccess({ email: user.email || 'google-user', role: 'student' });
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Google sign-in failed';
      if (msg.includes('not configured')) {
        onSignIn('student');
        return;
      }
      setAuthError(msg);
    }
  };

  return (
    <div className="space-y-12 py-4">
      {/* Hero Strip */}
      <div className="relative rounded-3xl overflow-hidden bg-gradient-to-br from-[#0F1629] via-[#1E1B4B] to-[#0C4A6E] p-8 md:p-16 border border-border-subtle shadow-glow">
        {/* Subtle dot grid and noise overlay */}
        <div className="absolute inset-0 dot-grid opacity-30 mix-blend-overlay pointer-events-none" />
        <div className="absolute top-0 right-0 w-96 h-96 bg-accent-cyan/10 rounded-full blur-3xl pointer-events-none" />

        <div className="relative z-10 grid md:grid-cols-2 gap-8 items-center">
          <div className="space-y-6">
            <div className="inline-flex items-center gap-2 px-3 py-1 bg-accent-cyan/10 text-accent-cyan rounded-full border border-accent-cyan/20 text-xs font-mono">
              <Cpu className="w-3.5 h-3.5" />
              <span>DTA SENSIS ENGINE v6.0 READY</span>
            </div>
            
            <h1 className="font-display text-4xl md:text-5xl lg:text-6xl text-white font-bold leading-tight tracking-tight">
              DTA Training <span className="text-transparent bg-clip-text bg-gradient-to-r from-accent-cyan to-accent-violet">Companion</span>
            </h1>
            
            <p className="text-text-secondary text-base md:text-lg leading-relaxed font-sans max-w-xl">
              Train smarter outside the game. Behavior clone telemetry datasets, analyze adapter models with weights dashboards, and sync optimized brains directly to Unity.
            </p>
            
            <div className="flex flex-wrap gap-4 pt-2">
              {!currentUser ? (
                <button
                  onClick={() => onSignIn('student')}
                  className="px-6 py-3 bg-accent-cyan hover:bg-accent-cyan/90 text-[#070B14] font-semibold text-sm rounded-xl transition cursor-pointer flex items-center gap-2 shadow-lg shadow-accent-cyan/20"
                >
                  <Play className="w-4 h-4 fill-current" />
                  Initialize Laboratory Pack
                </button>
              ) : (
                <div className="text-sm font-mono text-accent-emerald bg-accent-emerald/10 border border-accent-emerald/20 px-4 py-2.5 rounded-xl flex items-center gap-2">
                  <span className="w-2 h-2 rounded-full bg-accent-emerald animate-pulse" />
                  Authenticated: {currentUser.role.toUpperCase()} SESSION ACTIVE
                </div>
              )}
              <a 
                href="#link"
                className="px-6 py-3 bg-bg-elevated hover:bg-bg-elevated/80 text-text-primary font-medium text-sm rounded-xl transition border border-border-subtle flex items-center gap-2"
              >
                Learn More
              </a>
            </div>
          </div>

          {/* Screenshot mockup / Graphic */}
          <div className="relative flex justify-center">
            <div className="w-full max-w-md p-6 bg-bg-panel/90 rounded-2xl border border-accent-cyan/20 backdrop-blur-md shadow-2xl relative overflow-hidden">
              <div className="flex items-center justify-between border-b border-border-subtle pb-3 mb-4">
                <div className="flex gap-1.5">
                  <span className="w-2.5 h-2.5 rounded-full bg-accent-rose/80" />
                  <span className="w-2.5 h-2.5 rounded-full bg-accent-amber/80" />
                  <span className="w-2.5 h-2.5 rounded-full bg-accent-emerald/80" />
                </div>
                <span className="text-[10px] font-mono text-text-muted">LAB VIEWPORT: ONNX_LINEAGE</span>
              </div>
              
              <div className="space-y-3 font-mono text-xs">
                <div className="p-2.5 bg-bg-deep rounded border border-border-subtle flex justify-between">
                  <span className="text-accent-cyan">adapter_v7.onnx</span>
                  <span className="text-accent-emerald">READY TO DEPLOY</span>
                </div>
                <div className="p-2.5 bg-bg-deep rounded border border-border-subtle flex justify-between">
                  <span className="text-text-secondary">obs_dimension</span>
                  <span className="text-text-primary">96-Channel Vector</span>
                </div>
                <div className="p-2.5 bg-bg-deep rounded border border-border-subtle flex justify-between">
                  <span className="text-text-secondary">cloning_method</span>
                  <span className="text-accent-violet">bc_adapter</span>
                </div>
                <div className="p-3 bg-accent-cyan/5 rounded border border-accent-cyan/10 space-y-1.5">
                  <div className="flex justify-between text-[10px] text-accent-cyan font-bold">
                    <span>COGNITIVE CONFIDENCES</span>
                    <span>HIGH (94%)</span>
                  </div>
                  <div className="w-full bg-bg-deep h-1.5 rounded-full overflow-hidden">
                    <div className="bg-accent-cyan h-full rounded-full" style={{ width: '94%' }} />
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Grid: Auth triggers & Linking portal */}
      <div id="link" className="grid lg:grid-cols-3 gap-8">
        
        {/* Auth Section */}
        <div className="lg:col-span-2 bg-[#0F1629] p-8 rounded-2xl border border-border-subtle flex flex-col justify-between">
          <div>
            <h2 className="text-xl font-display font-bold text-white mb-2 flex items-center gap-2">
              <Lock className="w-5 h-5 text-accent-cyan" />
              Laboratory Authentication
            </h2>
            <p className="text-text-secondary text-sm mb-6 leading-relaxed">
              Register or verify your training companion identity. Connect via Firebase Secure Auth or use local sandbox bypass when Firebase is not configured.
            </p>
            {firebaseReady === false && (
              <p className="text-[11px] text-accent-amber mb-4 font-mono">
                Firebase not configured — add firebase-applet-config.json (see firebase-applet-config.example.json). Sandbox sign-in still works.
              </p>
            )}
            {authError && (
              <p className="text-[11px] text-accent-rose mb-4 font-mono">{authError}</p>
            )}

            {!currentUser ? (
              <div className="space-y-4">
                <div className="grid grid-cols-2 gap-4">
                  <div className="space-y-1.5">
                    <label className="text-xs font-mono text-text-secondary">EMAIL</label>
                    <input
                      type="email"
                      value={emailInput}
                      onChange={(e) => setEmailInput(e.target.value)}
                      placeholder="e.g. student@your-cohort.local"
                      className="w-full bg-bg-deep border border-border-subtle focus:border-accent-cyan/40 px-3.5 py-2.5 rounded-lg text-xs font-mono outline-none transition"
                    />
                  </div>
                  <div className="space-y-1.5">
                    <label className="text-xs font-mono text-text-secondary">PASSWORD</label>
                    <input
                      type="password"
                      value={passwordInput}
                      onChange={(e) => setPasswordInput(e.target.value)}
                      placeholder="••••••••"
                      className="w-full bg-bg-deep border border-border-subtle focus:border-accent-cyan/40 px-3.5 py-2.5 rounded-lg text-xs font-mono outline-none transition"
                    />
                  </div>
                </div>

                <div className="flex flex-col gap-3 pt-2">
                  <button
                    type="button"
                    onClick={tryGoogle}
                    className="w-full py-3 bg-white hover:bg-white/90 text-[#070B14] border border-border-subtle rounded-xl text-xs font-semibold cursor-pointer transition"
                  >
                    Sign in with Google
                  </button>
                  <button
                    type="button"
                    onClick={() => tryFirebaseEmail('student')}
                    className="flex-1 py-3 bg-bg-elevated hover:bg-bg-elevated/80 text-text-primary border border-border-subtle rounded-xl text-xs font-semibold cursor-pointer transition"
                  >
                    Email sign-in (Student)
                  </button>
                  <button
                    type="button"
                    onClick={() => tryFirebaseEmail('teacher')}
                    className="flex-1 py-3 bg-bg-elevated hover:bg-bg-elevated/80 text-text-primary border border-border-subtle rounded-xl text-xs font-semibold cursor-pointer transition"
                  >
                    Email sign-in (Teacher)
                  </button>
                  <div className="flex gap-3">
                    <button
                      type="button"
                      onClick={() => onSignIn('student')}
                      className="flex-1 py-2.5 bg-bg-deep text-text-muted border border-border-subtle rounded-xl text-[10px] font-mono cursor-pointer transition"
                    >
                      Sandbox student
                    </button>
                    <button
                      type="button"
                      onClick={() => onSignIn('teacher')}
                      className="flex-1 py-2.5 bg-bg-deep text-text-muted border border-border-subtle rounded-xl text-[10px] font-mono cursor-pointer transition"
                    >
                      Sandbox teacher
                    </button>
                  </div>
                </div>
              </div>
            ) : (
              <div className="space-y-4 p-5 bg-bg-deep rounded-xl border border-border-subtle">
                <div className="flex justify-between items-center text-xs font-mono">
                  <span className="text-text-muted">AUTHENTICATED ROLE</span>
                  <span className="text-accent-cyan px-2 py-0.5 bg-accent-cyan/10 rounded border border-accent-cyan/15">{currentUser.role.toUpperCase()}</span>
                </div>
                <div className="flex justify-between items-center text-xs font-mono">
                  <span className="text-text-muted">EMAIL IDENTITY</span>
                  <span className="text-text-primary truncate">{currentUser.email}</span>
                </div>
                <button
                  onClick={onSignOut}
                  className="w-full mt-3 py-2 bg-accent-rose/10 hover:bg-accent-rose/25 text-accent-rose border border-accent-rose/15 rounded-lg text-xs font-semibold cursor-pointer transition"
                >
                  Terminate Active Session
                </button>
              </div>
            )}
          </div>
          
          <div className="mt-6 pt-4 border-t border-border-subtle text-[11px] text-text-muted font-sans flex items-center gap-2 leading-none">
            <span className={`w-1.5 h-1.5 rounded-full ${firebaseReady ? 'bg-accent-emerald animate-pulse' : 'bg-accent-amber'}`} />
            <span>
              {firebaseReady
                ? 'Firebase Secure Auth active. Direct tokens routed safely.'
                : 'Local sandbox mode — configure firebase-applet-config.json for cloud auth.'}
            </span>
          </div>
        </div>

        {/* Account Linking displays code */}
        <div className="bg-[#0F1629] p-8 rounded-2xl border border-border-subtle flex flex-col justify-between">
          <div>
            <h2 className="text-xl font-display font-bold text-white mb-2 flex items-center gap-2">
              <Gamepad2 className="w-5 h-5 text-accent-violet" />
              Link Unity 6 Client
            </h2>
            <p className="text-text-secondary text-sm mb-6 leading-relaxed">
              Connect your physical game client. Paste the 6-character clean base32 code into settings to load checkpoints wirelessly.
            </p>

            <div className="space-y-4 text-center">
              {playerProfile.linkCode ? (
                <div className="space-y-2">
                  <div className="text-[10px] font-mono text-text-muted uppercase tracking-wider">TEMP HYPERHUB CONNECTION KEY</div>
                  <div className="bg-bg-deep border border-accent-violet/30 py-4 px-6 rounded-xl select-all font-mono font-bold text-2xl tracking-widest text-[#22D3EE] shadow-lg shadow-accent-cyan/5">
                    {playerProfile.linkCode}
                  </div>
                  <p className="text-[10px] font-sans text-text-muted leading-tight">
                    Valid for 10 minutes. Go to Space Options → Companion Link inside Unity.
                  </p>
                </div>
              ) : (
                <button
                  onClick={onGenerateCode}
                  className="w-full py-4 bg-accent-violet hover:bg-accent-violet/90 text-[#070B14] font-semibold text-xs rounded-xl transition cursor-pointer shadow-lg shadow-accent-violet/20"
                >
                  Generate New Link Code
                </button>
              )}

              <div className="p-3.5 bg-bg-deep rounded-xl border border-border-subtle text-left">
                <div className="flex justify-between items-center text-xs font-mono">
                  <span className="text-text-secondary">UNITY SYNC STATE</span>
                  {playerProfile.isLinked ? (
                    <span className="text-accent-emerald font-semibold">● LINKED (ACTIVE)</span>
                  ) : (
                    <span className="text-accent-amber font-semibold">● NOT SYNCHRONIZED</span>
                  )}
                </div>
              </div>
            </div>
          </div>

          <div className="mt-6 pt-4 border-t border-border-subtle text-[11px] text-text-muted font-mono flex justify-between items-center">
            <span>Schema v{playerProfile.isLinked ? "6.2" : "6.0"}</span>
            <span className="text-accent-cyan">Sync code base32</span>
          </div>
        </div>

      </div>
    </div>
  );
}
