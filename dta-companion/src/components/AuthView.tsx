import React, { useState } from 'react';
import { User, LogIn, Mail, Lock, ShieldCheck, Award } from 'lucide-react';

interface AuthViewProps {
  onSignIn: (role: 'student' | 'teacher') => void;
  currentUser: { email: string | null; role: 'student' | 'teacher' } | null;
  onSignOut: () => void;
}

export default function AuthView({ onSignIn, currentUser, onSignOut }: AuthViewProps) {
  const [emailInput, setEmailInput] = useState<string>('');
  const [passwordInput, setPasswordInput] = useState<string>('');
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);

  const handleSubmit = (e: React.FormEvent, role: 'student' | 'teacher') => {
    e.preventDefault();
    setIsSubmitting(true);
    setTimeout(() => {
      onSignIn(role);
      setIsSubmitting(false);
    }, 800);
  };

  return (
    <div className="max-w-md mx-auto space-y-6 animate-fade-in" id="auth-view">
      
      {/* Title */}
      <div className="text-center space-y-2">
        <h2 className="font-sans font-extrabold text-2xl tracking-tight text-white">Sign In to Companion Hub</h2>
        <p className="text-xs text-slate-400 font-sans">Spectator login, student character tracking, and cohort moderators.</p>
      </div>

      {currentUser ? (
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-6 text-center space-y-4">
          <ShieldCheck className="w-12 h-12 text-emerald-400 mx-auto" />
          <div className="space-y-1">
            <h3 className="font-sans font-bold text-slate-205 text-slate-200">You are Signed In!</h3>
            <p className="text-xs text-slate-400 font-mono">{currentUser.email}</p>
            <span className={`inline-block text-[10px] uppercase font-mono font-bold px-2 py-0.5 rounded ${
              currentUser.role === 'teacher' 
                ? 'bg-purple-500/10 text-purple-400 border border-purple-500/15' 
                : 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/15'
            }`}>
              {currentUser.role === 'teacher' ? '💼 Classroom Moderator' : '🎓 Student Cohort'}
            </span>
          </div>

          <button
            onClick={onSignOut}
            className="w-full bg-slate-950 border border-slate-800 hover:border-slate-700 text-slate-350 text-slate-300 font-medium py-2 rounded-lg text-xs hover:text-white transition cursor-pointer select-none"
          >
            SIGN OUT SESSION
          </button>
        </div>
      ) : (
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-6 space-y-4">
          <div className="space-y-3">
            <button
              onClick={() => onSignIn('student')}
              className="w-full bg-slate-950 hover:bg-slate-850 text-slate-250 text-slate-200 border border-slate-800 hover:border-slate-755 hover:border-slate-700 font-semibold font-sans py-2.5 px-4 rounded-lg text-xs flex items-center justify-center gap-2 transition cursor-pointer select-none"
            >
              {/* Simple stylized google icon */}
              <svg className="w-4 h-4" viewBox="0 0 24 24">
                <path fill="#ea4335" d="M12.2 5c1.2 0 2.3.4 3.1 1.2l2.3-2.3C16.1 2.5 14.2 1.8 12.2 1.8c-4.1 0-7.7 2.4-9.3 5.9l2.7 2.1c.7-2 2.6-4.8 6.6-4.8z"/>
                <path fill="#4285f4" d="M22.2 12.2c0-.7-.1-1.3-.2-1.9H12.2v3.7h5.7c-.2 1.3-1 2.4-2.1 3.1v2.6h3.4c2-1.8 3-4.5 3-7.5z"/>
                <path fill="#fbbc05" d="M5.6 14.8c-.2-.7-.3-1.4-.3-2.1a6.6 6.6 0 0 1 .3-2.2L2.9 8.4C2.1 10 1.7 11.8 1.7 12.7c0 .9.4 2.7 1.2 4.3l2.7-2.2z"/>
                <path fill="#34a853" d="M12.2 22.2c2.7 0 5-.9 6.7-2.5l-3.4-2.6c-.9.6-2.1 1-3.3 1-4 0-5.9-2.8-6.6-4.8l-2.7 2.1c1.6 3.5 5.2 6.8 9.3 6.8z"/>
              </svg>
              Sign In with Google Account
            </button>
          </div>

          <div className="relative flex py-2 items-center">
            <div className="flex-grow border-t border-slate-800"></div>
            <span className="flex-shrink mx-4 text-[10px] font-mono text-slate-550 text-slate-500 uppercase">Or Email Credentials</span>
            <div className="flex-grow border-t border-slate-800"></div>
          </div>

          {/* Form */}
          <form onSubmit={(e) => handleSubmit(e, 'student')} className="space-y-3.5">
            <div className="space-y-1">
              <label className="block text-[10px] text-slate-405 text-slate-400 font-medium font-sans">Email Address</label>
              <div className="relative">
                <span className="absolute inset-y-0 left-0 pl-3 flex items-center text-slate-500">
                  <Mail className="w-3.5 h-3.5" />
                </span>
                <input 
                  type="email" 
                  required
                  placeholder="name@cohort.local"
                  value={emailInput}
                  onChange={(e) => setEmailInput(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-800 rounded px-9 py-1.5 text-xs text-white focus:outline-none focus:border-emerald-500"
                />
              </div>
            </div>

            <div className="space-y-1">
              <label className="block text-[10px] text-slate-405 text-slate-400 font-medium font-sans">Account Password</label>
              <div className="relative">
                <span className="absolute inset-y-0 left-0 pl-3 flex items-center text-slate-500">
                  <Lock className="w-3.5 h-3.5" />
                </span>
                <input 
                  type="password" 
                  required
                  placeholder="••••••••"
                  value={passwordInput}
                  onChange={(e) => setPasswordInput(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-800 rounded px-9 py-1.5 text-xs text-white focus:outline-none focus:border-emerald-500"
                />
              </div>
            </div>

            <div className="grid grid-cols-2 gap-3 pt-2">
              <button
                type="submit"
                onClick={() => setEmailInput('jacob.student@dta.local')}
                className="bg-emerald-650 bg-emerald-600 hover:bg-emerald-550 text-white font-semibold py-2 rounded-lg text-xs transition cursor-pointer flex items-center justify-center gap-1 shrink-0"
              >
                <LogIn className="w-3.5 h-3.5" />
                Student Login
              </button>
              <button
                type="button"
                onClick={(e) => {
                  setEmailInput('professor.adkins@dta.local');
                  handleSubmit(e as any, 'teacher');
                }}
                className="bg-purple-650 bg-purple-600 hover:bg-purple-550 text-white font-semibold py-2 rounded-lg text-xs transition cursor-pointer flex items-center justify-center gap-1 shrink-0"
              >
                <Award className="w-3.5 h-3.5" />
                Teacher Login
              </button>
            </div>
          </form>

          {/* Guidelines notes */}
          <p className="text-[10.5px] text-slate-505 text-slate-500 text-center font-sans leading-relaxed pt-2">
            Teachers login as moderators to configure unique, isolated class Cohorts and access mod tools in v2.
          </p>
        </div>
      )}

    </div>
  );
}
