import { useCallback, useEffect, useMemo, useRef, useState } from "react";

const PHASE_LABELS = {
  qna: "Q&A",
  awaiting_concept_approval: "Concept approval",
  research: "Gathering research…",
  awaiting_research_approval: "Research approval",
  awaiting_plan_approval: "Plan approval",
  finalized: "Starting build in Unity…",
  cancelled: "Cancelled",
};

const TYPEWRITER_CPS = 52;

function hubFromUrl() {
  return new URLSearchParams(window.location.search).get("hub") || "";
}

async function api(path, opts = {}) {
  const hub = hubFromUrl();
  const q = hub ? `?hub=${encodeURIComponent(hub)}` : "";
  const res = await fetch(`${path}${q}`, {
    ...opts,
    headers: { "Content-Type": "application/json", ...(opts.headers || {}) },
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || res.statusText);
  return data;
}

function useTypewriter(text, enabled, cps = TYPEWRITER_CPS) {
  const [display, setDisplay] = useState(enabled ? "" : text || "");
  const [done, setDone] = useState(!enabled);

  useEffect(() => {
    if (!enabled || !text) {
      setDisplay(text || "");
      setDone(true);
      return undefined;
    }
    setDisplay("");
    setDone(false);
    let i = 0;
    const ms = 1000 / cps;
    let timer;
    const tick = () => {
      i += 1;
      setDisplay(text.slice(0, i));
      if (i >= text.length) {
        setDone(true);
      } else {
        timer = setTimeout(tick, ms);
      }
    };
    timer = setTimeout(tick, ms);
    return () => clearTimeout(timer);
  }, [text, enabled, cps]);

  return { display, done };
}

function AssistantBubble({ content, animate, onTypingDone }) {
  const { display, done } = useTypewriter(content, animate);
  useEffect(() => {
    if (animate && done) onTypingDone?.();
  }, [animate, done, onTypingDone]);
  return (
    <>
      {display}
      {animate && !done && <span className="caret" aria-hidden />}
    </>
  );
}

const CHECKLIST_META = {
  play_disk: { icon: "▦", group: "Core" },
  scope: { icon: "◎", group: "Core" },
  props: { icon: "▪", group: "Content" },
  npcs: { icon: "●", group: "Content" },
  enemies: { icon: "▲", group: "Content" },
  puzzles: { icon: "★", group: "Content" },
  terrain: { icon: "⛰", group: "World" },
  speed: { icon: "⚡", group: "Build" },
};

function ChecklistPanel({ items, phase, cursorActive }) {
  const [expanded, setExpanded] = useState({});

  if (!items?.length) return null;

  const doneCount = items.filter((i) => i.done).length;
  const pct = Math.round((doneCount / items.length) * 100);
  const nextPending = items.find((i) => !i.done);
  const allDone = doneCount === items.length;

  const toggleExpand = (id) => setExpanded((prev) => ({ ...prev, [id]: !prev[id] }));

  return (
    <aside className="checklist-panel card" aria-label="Planning decisions checklist">
      <div className="checklist-header">
        <h2 className="section">Your decisions</h2>
        <span className={`checklist-badge ${allDone ? "complete" : ""}`}>
          {doneCount}/{items.length}
        </span>
      </div>

      <div className="progress-track" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100}>
        <div className="progress-fill" style={{ width: `${pct}%` }} />
      </div>
      <p className="checklist-progress-label">
        {allDone
          ? "All topics decided"
          : `${doneCount} decided · ${items.length - doneCount} still open`}
      </p>

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
        {items.map((item) => {
          const meta = CHECKLIST_META[item.id] || { icon: "·", group: "" };
          const decision = (item.decision || "").trim();
          const long = decision.length > 90;
          const isOpen = expanded[item.id];
          const isNext = !item.done && item.id === nextPending?.id;

          return (
            <li
              key={item.id}
              className={`check-item ${item.done ? "done" : "pending"}${isNext ? " next" : ""}`}
            >
              <span className={`check-icon ${item.done ? "on" : ""}`} aria-hidden>
                {meta.icon}
              </span>
              <div className="check-body">
                <div className="check-row">
                  <span className="check-label">{item.label}</span>
                  <span className={`check-state ${item.done ? "on" : ""}`} aria-label={item.done ? "Decided" : "Pending"}>
                    {item.done ? "✓" : "○"}
                  </span>
                </div>

                {item.done && decision ? (
                  <>
                    <p className={`check-decision ${long && !isOpen ? "clamped" : ""}`}>{decision}</p>
                    {long && (
                      <button type="button" className="check-expand" onClick={() => toggleExpand(item.id)}>
                        {isOpen ? "Show less" : "Show more"}
                      </button>
                    )}
                  </>
                ) : (
                  !item.done &&
                  phase === "qna" && <p className="check-waiting">Waiting for your answer</p>
                )}
              </div>
            </li>
          );
        })}
      </ul>
    </aside>
  );
}

export default function App() {
  const [session, setSession] = useState(null);
  const [input, setInput] = useState("");
  const [internetResearch, setInternetResearch] = useState(false);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");
  const [started, setStarted] = useState(false);
  const [typeLastAssistant, setTypeLastAssistant] = useState(false);
  const [conceptImgError, setConceptImgError] = useState(false);
  const bottomRef = useRef(null);

  const applySession = useCallback((s, animateReply = false) => {
    setSession(s);
    if (animateReply) setTypeLastAssistant(true);
    setConceptImgError(false);
  }, []);

  const resumePlanner = useCallback(async () => {
    const d = await api("/api/planner/resume", { method: "POST", body: "{}" });
    applySession(d.session, true);
    setStarted(true);
  }, [applySession]);

  const loadSession = useCallback(async () => {
    const hub = hubFromUrl();
    if (!hub) return;
    try {
      const d = await api("/api/planner/session");
      if (d.session?.phase && d.session.phase !== "cancelled") {
        applySession(d.session, false);
        setStarted(true);
        if (d.session.awaitingAssistantReply) {
          setBusy(true);
          try {
            await resumePlanner();
          } catch (e) {
            setErr(String(e));
          } finally {
            setBusy(false);
          }
        }
      }
    } catch {
      /* no session yet */
    }
  }, [applySession, resumePlanner]);

  useEffect(() => {
    loadSession();
  }, [loadSession]);

  const phase = session?.phase;
  const messages = session?.messages || [];
  const checklist = session?.checklist || [];

  const cursorActive =
    busy ||
    session?.cursorWorking ||
    session?.awaitingAssistantReply ||
    phase === "research";

  useEffect(() => {
    if (!cursorActive || !started) return undefined;
    const id = setInterval(async () => {
      try {
        const d = await api("/api/planner/session");
        if (d.session) setSession(d.session);
      } catch {
        /* ignore poll errors */
      }
    }, 700);
    return () => clearInterval(id);
  }, [cursorActive, started]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [session?.messages, typeLastAssistant]);

  const run = async (fn, animateReply = false) => {
    setErr("");
    setBusy(true);
    try {
      const result = await fn();
      if (result?.session) applySession(result.session, animateReply);
    } catch (e) {
      setErr(String(e));
    } finally {
      setBusy(false);
    }
  };

  const start = () =>
    run(async () => {
      const msg = input.trim();
      if (!msg) return null;
      const d = await api("/api/planner/start", {
        method: "POST",
        body: JSON.stringify({ message: msg, internetResearch }),
      });
      setStarted(true);
      setInput("");
      return d;
    }, true);

  const sendChat = () =>
    run(async () => {
      const msg = input.trim();
      if (!msg) return null;
      const d = await api("/api/planner/chat", {
        method: "POST",
        body: JSON.stringify({ message: msg }),
      });
      setInput("");
      return d;
    }, true);

  const approveConcept = (approved) =>
    run(async () => {
      const d = await api("/api/planner/concept/approve", {
        method: "POST",
        body: JSON.stringify({
          approved,
          feedback: approved ? "" : input.trim() || "Please revise the concept.",
        }),
      });
      if (!approved) setInput("");
      return d;
    });

  const approveResearch = (approved) =>
    run(async () => {
      const items = session?.researchBundle?.items || [];
      const d = await api("/api/planner/research/approve", {
        method: "POST",
        body: JSON.stringify({
          approved,
          selectedIds: items.map((i) => i.id),
        }),
      });
      return d;
    });

  const approvePlan = (approved) =>
    run(async () => {
      const d = await api("/api/planner/plan/approve", {
        method: "POST",
        body: JSON.stringify({ approved }),
      });
      return d;
    });

  const cancel = () =>
    run(async () => {
      await api("/api/build/cancel", { method: "POST" });
      setSession({ phase: "cancelled", messages: [] });
      return null;
    });

  const resetPlanner = (mode) =>
    run(async () => {
      const d = await api("/api/planner/reset", {
        method: "POST",
        body: JSON.stringify({ mode }),
      });
      if (mode === "full") {
        setStarted(false);
        setSession(null);
        setInput("");
        return null;
      }
      return d;
    });

  const lastAssistantIdx = useMemo(() => {
    for (let i = messages.length - 1; i >= 0; i -= 1) {
      if (messages[i].role === "assistant") return i;
    }
    return -1;
  }, [messages]);

  const showChecklist =
    started && checklist.length > 0 && phase !== "cancelled";

  return (
    <div className={`wrap ${started ? "wrap-wide" : ""}`}>
      <h1>Environment Kit — AI Build Planner</h1>
      <p className="sub">
        Powered by <strong>Cursor</strong> (CURSOR_API_KEY in cave-grader/.env).
        Describe your build → Q&A → concept image →
        {internetResearch ? " web research →" : ""} plan approval → Unity starts.
        <br />
        Hub: {hubFromUrl() || "(add ?hub= path)"}
      </p>

      {!started && (
        <div className="card">
          <label className="row check">
            <input
              type="checkbox"
              checked={internetResearch}
              onChange={(e) => setInternetResearch(e.target.checked)}
            />
            <span>
              Internet research (after concept approval — sources saved to ResearchCache)
            </span>
          </label>
          <div className="row">
            <label>What do you want to build?</label>
            <textarea
              rows={4}
              value={input}
              onChange={(e) => setInput(e.target.value)}
              placeholder="e.g. 9-tile playable demo with props, no mountains, fast to record today"
            />
          </div>
          <button type="button" className="primary" disabled={busy || !input.trim()} onClick={start}>
            Start planning
          </button>
        </div>
      )}

      {started && (
        <div className={`planner-grid ${showChecklist ? "with-checklist" : ""}`}>
          <div className="planner-main">
            <div className="phase-bar">
              Phase: <strong>{PHASE_LABELS[phase] || phase}</strong>
              {session?.internetResearch && <span className="pill active">Research ON</span>}
            </div>

            {cursorActive && (
              <div className="working-bar" role="status" aria-live="polite">
                <span className="spinner" />
                <span>Cursor is working…</span>
              </div>
            )}

            <div className="chat">
              {messages.map((m, i) => (
                <div key={i} className={`bubble ${m.role}`}>
                  <div className="role">{m.role === "user" ? "You" : "Planner"}</div>
                  <div className="text">
                    {m.role === "assistant" && i === lastAssistantIdx ? (
                      <AssistantBubble
                        content={m.content}
                        animate={typeLastAssistant}
                        onTypingDone={() => setTypeLastAssistant(false)}
                      />
                    ) : (
                      m.content
                    )}
                  </div>
                </div>
              ))}
              <div ref={bottomRef} />
            </div>

            {phase === "qna" && !session?.awaitingAssistantReply && !cursorActive && (
              <div className="composer card">
                <textarea
                  rows={2}
                  value={input}
                  onChange={(e) => setInput(e.target.value)}
                  placeholder={
                    messages.some((m) => m.role === "assistant")
                      ? "Answer the planner's question…"
                      : "Add more detail about your build…"
                  }
                  onKeyDown={(e) => {
                    if (e.key === "Enter" && !e.shiftKey) {
                      e.preventDefault();
                      sendChat();
                    }
                  }}
                />
                <button
                  type="button"
                  className="primary"
                  disabled={busy || !input.trim()}
                  onClick={sendChat}
                >
                  Send
                </button>
              </div>
            )}

            {phase === "awaiting_concept_approval" && (
              <div className="card">
                <h2 className="section">Concept preview</h2>
                <p className="sub">
                Top-down layout map — spawn, props, NPCs, enemies, collectibles, trails, and islands
                labeled. Approve when it matches what you expect in-game.
              </p>
                <div className="concept-frame">
                  {session.conceptImageUrl && !conceptImgError ? (
                    <img
                      className="concept-img"
                      src={session.conceptImageUrl}
                      alt="Build concept map"
                      onError={() => setConceptImgError(true)}
                    />
                  ) : (
                    <div className="concept-fallback">
                      {conceptImgError
                        ? "Could not load concept image — check server logs and ResearchCache path."
                        : "Generating concept preview…"}
                    </div>
                  )}
                </div>
                {session.conceptImageRel && (
                  <p className="hint">Saved: {session.conceptImageRel}</p>
                )}
                <div className="actions">
                  <button
                    type="button"
                    className="primary"
                    disabled={busy}
                    onClick={() => approveConcept(true)}
                  >
                    Approve concept
                  </button>
                  <button
                    type="button"
                    className="ghost"
                    disabled={busy}
                    onClick={() => approveConcept(false)}
                  >
                    Reject &amp; revise
                  </button>
                </div>
                <div className="row">
                  <label>Revision notes (if rejecting)</label>
                  <input
                    type="text"
                    value={input}
                    onChange={(e) => setInput(e.target.value)}
                    placeholder="e.g. more water, less labyrinth"
                  />
                </div>
              </div>
            )}

            {phase === "research" && (
              <div className="card">
                <h2 className="section">Web research</h2>
                <p className="sub">Cursor is searching the web for sources relevant to your build…</p>
              </div>
            )}

            {phase === "awaiting_research_approval" && session.researchBundle && (
              <div className="card">
                <h2 className="section">Web research — review sources</h2>
                <p className="sub">
                  Gathered by <strong>Cursor</strong> (web search). Approved items save to ResearchCache,
                  then planning continues.
                </p>
                {session.researchError && (
                  <p className="err">Research failed: {session.researchError}</p>
                )}
                {!(session.researchBundle.items || []).length && !session.researchError && (
                  <p className="sub">No sources returned — try Re-gather research.</p>
                )}
                <ul className="research-list">
                  {(session.researchBundle.items || []).map((item) => (
                    <li key={item.id}>
                      <a href={item.url} target="_blank" rel="noreferrer">
                        {item.title}
                      </a>
                      <span className="hint"> — {item.category}</span>
                      {item.summary && <p className="hint research-summary">{item.summary}</p>}
                    </li>
                  ))}
                </ul>
                <div className="actions">
                  <button
                    type="button"
                    className="primary"
                    disabled={busy}
                    onClick={() => approveResearch(true)}
                  >
                    Approve research &amp; continue
                  </button>
                  <button
                    type="button"
                    className="ghost"
                    disabled={busy}
                    onClick={() => approveResearch(false)}
                  >
                    Re-gather research
                  </button>
                </div>
              </div>
            )}

            {phase === "awaiting_plan_approval" && (
              <div className="card">
                <h2 className="section">Final plan — confirm before build</h2>
                <pre className="plan">{session.planSummary}</pre>
                <div className="actions">
                  <button
                    type="button"
                    className="primary"
                    disabled={busy}
                    onClick={() => approvePlan(true)}
                  >
                    Approve plan &amp; start build in Unity
                  </button>
                  <button
                    type="button"
                    className="ghost"
                    disabled={busy}
                    onClick={() => approvePlan(false)}
                  >
                    Back to Q&A
                  </button>
                </div>
              </div>
            )}

            {phase === "finalized" && (
              <div className="card status-box">
                <p className="status">
                  Plan finalized — switching to Unity, opening the Hub, and starting the build
                  with demo recording.
                </p>
                <p className="hint">
                  To test approve again: reset below, then in Unity click{" "}
                  <strong>Build Complete Cave</strong> once to re-open the planner gate.
                </p>
                <div className="actions">
                  <button
                    type="button"
                    className="primary"
                    disabled={busy}
                    onClick={() => resetPlanner("plan")}
                  >
                    Reset to plan approval
                  </button>
                  <button
                    type="button"
                    className="ghost"
                    disabled={busy}
                    onClick={() => resetPlanner("full")}
                  >
                    Start over
                  </button>
                </div>
              </div>
            )}

            {phase === "cancelled" && (
              <div className="card status-box">
                <p className="status">Build cancelled.</p>
                <div className="actions">
                  <button
                    type="button"
                    className="primary"
                    disabled={busy}
                    onClick={() => resetPlanner("full")}
                  >
                    Start over
                  </button>
                </div>
              </div>
            )}

            {phase !== "finalized" && phase !== "cancelled" && (
              <button type="button" className="ghost cancel-btn" disabled={busy} onClick={cancel}>
                Cancel build
              </button>
            )}
          </div>

          {showChecklist && (
            <ChecklistPanel items={checklist} phase={phase} cursorActive={cursorActive} />
          )}
        </div>
      )}

      {err && <p className="err">{err}</p>}
    </div>
  );
}
