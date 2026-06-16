import { useCallback, useEffect, useRef, useState } from "react";
import { api, displayChatContent, hubFromUrl, streamingDuplicatesAssistant, tabApi, tabPulse } from "./plannerApi.js";
import { TAB_CONFIGS } from "./tabConfigs.js";

function ChecklistPanel({ items, phase, cursorActive, meta }) {
  const [expanded, setExpanded] = useState({});
  if (!items?.length) return null;
  const doneCount = items.filter((i) => i.done).length;
  const pct = Math.round((doneCount / items.length) * 100);
  const nextPending = items.find((i) => !i.done);
  const allDone = doneCount === items.length;

  return (
    <aside className="checklist-panel card" aria-label="Checklist">
      <div className="checklist-header">
        <h2 className="section">Your decisions</h2>
        <span className={`checklist-badge ${allDone ? "complete" : ""}`}>{doneCount}/{items.length}</span>
      </div>
      <div className="progress-track" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100}>
        <div className="progress-fill" style={{ width: `${pct}%` }} />
      </div>
      {phase === "qna" && nextPending && !cursorActive && (
        <p className="checklist-next">
          <span className="checklist-next-tag">Up next</span>
          {nextPending.label}
        </p>
      )}
      {phase === "qna" && cursorActive && (
        <p className="checklist-next checklist-next-busy">
          <span className="spinner spinner-sm" />
          Updating checklist…
        </p>
      )}
      <ul className="checklist">
        {items.map((item, idx) => {
          const m = meta[item.id] || { icon: "·" };
          const decision = (item.decision || "").trim();
          const long = decision.length > 90;
          const isOpen = expanded[item.id];
          const isNext = !item.done && item.id === nextPending?.id;
          return (
            <li key={item.id} className={`check-item ${item.done ? "done" : "pending"}${isNext ? " next" : ""}`}>
              <span className={`check-icon ${item.done ? "on" : ""}`}>{m.icon}</span>
              <div className="check-body">
                <div className="check-row">
                  <span className="check-label">
                    <span className="check-index">{item.index ?? idx + 1}.</span> {item.label}
                  </span>
                  <span className={`check-state ${item.done ? "on" : ""}`}>{item.done ? "✓" : "○"}</span>
                </div>
                {item.done && decision ? (
                  <>
                    <p className={`check-decision ${long && !isOpen ? "clamped" : ""}`}>{decision}</p>
                    {long && (
                      <button type="button" className="check-expand" onClick={() => setExpanded((p) => ({ ...p, [item.id]: !p[item.id] }))}>
                        {isOpen ? "Show less" : "Show more"}
                      </button>
                    )}
                  </>
                ) : (
                  !item.done && phase === "qna" && <p className="check-waiting">Waiting for your answer</p>
                )}
              </div>
            </li>
          );
        })}
      </ul>
    </aside>
  );
}

function HelpIntro({ text, onStart }) {
  return (
    <div className="card help-intro-card">
      <div className="help-intro-body">{text}</div>
      <button type="button" className="primary" onClick={onStart}>
        Start planning
      </button>
    </div>
  );
}

export default function PlannerTabApp({ tabId }) {
  const cfg = TAB_CONFIGS[tabId];
  const hubMissing = !hubFromUrl();
  const [internetResearch, setInternetResearch] = useState(true);
  const [session, setSession] = useState(null);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");
  const [showHelp, setShowHelp] = useState(true);
  const bottomRef = useRef(null);

  const phase = session?.phase || "idle";
  const messages = session?.messages || [];
  const checklist = session?.checklist || [];
  const started = phase !== "idle" && messages.length > 0;
  const liveStream = streamingDuplicatesAssistant(session) ? "" : session?.streamingText || "";
  const assistantWorking = busy || session?.cursorWorking || !!liveStream;
  const helpText = session?.helpIntro || cfg?.helpFallback || "";

  const loadSession = useCallback(async () => {
    if (hubMissing) return;
    try {
      const d = await api(`/api/${tabId}/session`);
      const s = d.session;
      if (s?.messages?.length) {
        setSession(s);
        setShowHelp(false);
      } else {
        setSession(s);
        setShowHelp(!s?.helpSeen);
      }
    } catch {
      /* none */
    }
  }, [hubMissing, tabId]);

  useEffect(() => {
    loadSession();
  }, [loadSession]);

  useEffect(() => {
    if (!started) return undefined;
    const poll = async () => {
      try {
        const usePulse = session?.cursorWorking || liveStream || session?.autoRespondActive;
        if (usePulse) {
          const d = await tabPulse(tabId);
          if (d.pulse) {
            setSession((prev) => (prev ? { ...prev, ...d.pulse } : d.pulse));
          }
        } else {
          const d = await api(`/api/${tabId}/session`);
          if (d.session) setSession(d.session);
        }
      } catch {
        /* ignore */
      }
    };
    poll();
    const ms = liveStream || session?.cursorWorking ? 120 : session?.autoRespondActive ? 200 : 600;
    const id = setInterval(poll, ms);
    return () => clearInterval(id);
  }, [started, tabId, session?.cursorWorking, session?.autoRespondActive, liveStream]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }, [messages.length, assistantWorking, liveStream]);

  const waitForIdle = async () => {
    for (let i = 0; i < 150; i += 1) {
      const d = await tabPulse(tabId);
      const p = d.pulse;
      if (p) setSession((prev) => (prev ? { ...prev, ...p } : p));
      if (!p?.cursorWorking && !p?.streamingText) return;
      await new Promise((r) => setTimeout(r, 150));
    }
  };

  const run = async (fn) => {
    if (assistantWorking) return null;
    setErr("");
    setBusy(true);
    try {
      const result = await fn();
      if (result?.session) {
        setSession(result.session);
        setShowHelp(false);
      }
      return result;
    } catch (e) {
      setErr(String(e));
      loadSession();
      throw e;
    } finally {
      setBusy(false);
    }
  };

  const start = (message) =>
    run(async () => {
      const msg = (message || input).trim();
      if (!msg) return null;
      const d = await tabApi(tabId, "start", {
        method: "POST",
        body: JSON.stringify({ message: msg, internetResearch }),
      });
      setInput("");
      setShowHelp(false);
      return d;
    });

  const sendChat = () =>
    run(async () => {
      const msg = input.trim();
      if (!msg) return null;
      const d = await tabApi(tabId, "chat", { method: "POST", body: JSON.stringify({ message: msg }) });
      setInput("");
      return d;
    });

  const approve = (approved) =>
    run(async () => {
      const d = await tabApi(tabId, "approve", {
        method: "POST",
        body: JSON.stringify({ approved, feedback: approved ? "" : input.trim() || "Revise." }),
      });
      if (!approved) setInput("");
      return d;
    });

  const reset = () =>
    run(async () => {
      const d = await tabApi(tabId, "reset", { method: "POST", body: "{}" });
      setSession(d.session || null);
      setShowHelp(true);
      setInput("");
      return d;
    });

  const exportPlan = () =>
    run(async () => tabApi(tabId, "export", { method: "POST", body: "{}" }));

  const autoRespond = (presetId) => {
    if (assistantWorking) return;
    const preset = cfg.presets.find((p) => p.id === presetId);
    setErr("");
    setBusy(true);
    setShowHelp(false);
    (async () => {
      try {
        if (!started) {
          await start(preset?.hint || presetId);
        }
        let done = false;
        let guard = 0;
        let waits = 0;
        while (!done && guard < 30) {
          guard += 1;
          await waitForIdle();
          const d = await tabApi(tabId, "auto-respond/step", {
            method: "POST",
            body: JSON.stringify({ preset: presetId, internetResearch }),
          });
          if (d?.session) setSession(d.session);
          if (d?.done || d?.session?.phase === "awaiting_approval" || d?.session?.phase === "finalized") {
            done = true;
            break;
          }
          if (d?.waiting) {
            waits += 1;
            if (waits > 100) throw new Error("AI Responder stalled");
            await waitForIdle();
          }
        }
      } catch (e) {
        setErr(String(e));
        loadSession();
      } finally {
        setBusy(false);
      }
    })();
  };

  if (!cfg) return <p className="err">Unknown tab {tabId}</p>;

  return (
    <div className={`wrap planner-pane planner-pane-${tabId} ${started ? "wrap-wide" : ""}`}>
      <header className="planner-hero">
        <p className="planner-kicker">{cfg.kicker}</p>
        <h1>{cfg.title}</h1>
      </header>

      {err && <p className="err">{err}</p>}

      {!started && showHelp && (
        <HelpIntro
          text={helpText}
          onStart={() => {
            setShowHelp(false);
            if (!input.trim()) setInput("I'd like to plan this step.");
          }}
        />
      )}

      {!started && !showHelp && (
        <div className="card">
          <label className="row check">
            <input
              type="checkbox"
              checked={internetResearch}
              onChange={(e) => setInternetResearch(e.target.checked)}
            />
            <span>
              Internet research (before Q&amp;A — 2025–2026 sources injected into planner prompts)
            </span>
          </label>
          <div className="row">
            <label>Describe what you want for this step</label>
            <textarea rows={4} value={input} onChange={(e) => setInput(e.target.value)} placeholder="Your goals…" />
          </div>
          <button type="button" className="primary" disabled={busy || hubMissing || !input.trim()} onClick={() => start()}>
            Start planning
          </button>
          <div className="ai-responder-block">
            <h2 className="section">AI Responder</h2>
            <div className="ai-preset-row">
              {cfg.presets.map((p) => (
                <button key={p.id} type="button" className={`ai-preset-btn ai-preset-${p.id}`} disabled={busy || hubMissing} onClick={() => autoRespond(p.id)} title={p.hint}>
                  <span className="ai-preset-title">{p.label}</span>
                  <span className="ai-preset-hint">{p.hint}</span>
                </button>
              ))}
            </div>
          </div>
        </div>
      )}

      {started && (
        <div className={`planner-grid with-checklist-left ${checklist.length ? "has-checklist" : ""}`}>
          {checklist.length > 0 && (
            <ChecklistPanel items={checklist} phase={phase} cursorActive={assistantWorking} meta={cfg.checklistMeta} />
          )}
          <div className="planner-main">
            <div className="phase-bar">
              Phase: <strong>{cfg.phaseLabels[phase] || phase}</strong>
              {session?.internetResearch && <span className="pill active">Research ON</span>}
            </div>
            <div className={`chat-scope-banner ${cfg.scopeClass}`} role="note">
              <span className="chat-scope-tag">{cfg.title}</span>
              <span className="chat-scope-detail">Isolated session — other tabs are not affected.</span>
            </div>
            <div className="chat">
              {messages.map((m, i) => {
                const isAuto = m.role === "user" && session?.autoRespondPreset;
                const role = m.role === "user" ? (isAuto ? "AI Responder" : "You") : "Planner";
                return (
                  <div key={i} className={`bubble ${m.role}`}>
                    <div className="role">{role}</div>
                    <div className="text">{displayChatContent(m.content)}</div>
                  </div>
                );
              })}
              {liveStream && (
                <div className={`bubble ${session?.streamingRole || "assistant"} streaming`}>
                  <div className="role">{session?.streamingRole === "user" ? "AI Responder" : "Planner"}</div>
                  <div className="text">{displayChatContent(liveStream)}</div>
                </div>
              )}
              <div ref={bottomRef} />
            </div>
            {assistantWorking && !liveStream && (
              <div className="working-bar" role="status">
                <span className="spinner" />
                <span>AI working…</span>
              </div>
            )}
            {phase === "qna" && !assistantWorking && (
              <div className="composer card">
                <div className="ai-responder-inline">
                  <span className="ai-responder-label">AI Responder</span>
                  <div className="ai-preset-row ai-preset-row-compact">
                    {cfg.presets.map((p) => (
                      <button key={p.id} type="button" className="ai-preset-btn" disabled={busy || assistantWorking} onClick={() => autoRespond(p.id)}>
                        {p.label}
                      </button>
                    ))}
                  </div>
                </div>
                <textarea rows={2} value={input} onChange={(e) => setInput(e.target.value)} placeholder="Answer… or type move on" onKeyDown={(e) => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); sendChat(); } }} />
                <button type="button" className="primary" disabled={busy || !input.trim()} onClick={sendChat}>Send</button>
              </div>
            )}
            {phase === "awaiting_approval" && (
              <div className="card">
                <h2 className="section">Review brief</h2>
                <pre className="plan">{JSON.stringify(session?.brief || {}, null, 2)}</pre>
                <div className="actions">
                  <button type="button" className="primary" disabled={busy} onClick={() => approve(true)}>Approve &amp; write brief</button>
                  <button type="button" className="ghost" disabled={busy} onClick={() => approve(false)}>Back to Q&amp;A</button>
                </div>
              </div>
            )}
            {phase === "finalized" && (
              <div className="card status-box">
                <p className="status">Brief approved for this tab.</p>
                <div className="actions">
                  <button type="button" className="primary" disabled={busy} onClick={exportPlan}>Export plan to Desktop</button>
                  <button type="button" className="ghost" disabled={busy} onClick={reset}>Start over</button>
                </div>
              </div>
            )}
            {phase !== "finalized" && (
              <button type="button" className="ghost cancel-btn" disabled={busy} onClick={reset}>Reset this tab</button>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
