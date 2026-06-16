import { useCallback, useEffect, useMemo, useRef, useState } from "react";

const CONTENT_PHASE_LABELS = {
  idle: "Not started",
  qna: "Q&A",
  awaiting_approval: "Layout brief review",
  finalized: "Brief approved",
  cancelled: "Cancelled",
};

const CONTENT_PRESETS = [
  {
    id: "town_hub",
    label: "Town hub",
    hint: "Polyvania town — quest giver at gate, 2 ambient NPCs, trainer by depot, gate blocked until intro quest.",
  },
  {
    id: "arena_trainers",
    label: "Arena trainers",
    hint: "Battle arena trainer NPC, shop unlocks at agent training grade 70%, patrol enemies on arena perimeter.",
  },
  {
    id: "full_mainscene",
    label: "Full MainScene",
    hint: "Full MainScene pass: town quest NPCs, arena trainers, cave gate blocker, east patrol enemies, playmode smoke on 3 NPCs.",
  },
];

const CONTENT_CHECKLIST_META = {
  scene_scope: { icon: "◎", group: "Scene" },
  zones: { icon: "▦", group: "Scene" },
  quest_npcs: { icon: "●", group: "NPCs" },
  trainer_npcs: { icon: "◆", group: "NPCs" },
  ambient_npcs: { icon: "○", group: "NPCs" },
  blocker_gates: { icon: "⛨", group: "NPCs" },
  enemy_patrols: { icon: "▲", group: "Combat" },
  props_layout: { icon: "▪", group: "Props" },
  textures: { icon: "▤", group: "Props" },
  dialog_hybrid: { icon: "◈", group: "Dialog" },
  blockers_training: { icon: "↗", group: "Blockers" },
  blockers_quest: { icon: "★", group: "Blockers" },
  placement: { icon: "⌂", group: "Layout" },
  playmode_smoke: { icon: "✓", group: "Verify" },
};

const SESSION_POLL_MS = 400;

function hubFromUrl() {
  return new URLSearchParams(window.location.search).get("hub") || "";
}

function api(path, init = {}) {
  const hub = hubFromUrl();
  const sep = path.includes("?") ? "&" : "?";
  const url = hub ? `${path}${sep}hub=${encodeURIComponent(hub)}` : path;
  return fetch(url, {
    headers: { "Content-Type": "application/json", ...(init.headers || {}) },
    ...init,
  }).then(async (res) => {
    const data = await res.json().catch(() => ({}));
    if (!res.ok) {
      if (res.status === 404) {
        throw new Error(
          "Content API not found — restart: python3 ensure-build-wizard.py --restart (in Tools/cave-grader)",
        );
      }
      throw new Error(data.error || res.statusText);
    }
    return data;
  });
}

function displayChatContent(content) {
  const raw = String(content || "").trim();
  if (!raw.startsWith("{")) return raw;
  try {
    const p = JSON.parse(raw);
    if (typeof p.assistantMessage === "string") return p.assistantMessage;
  } catch {
    const m = raw.match(/"assistantMessage"\s*:\s*"((?:[^"\\]|\\.)*)"/);
    if (m) return m[1].replace(/\\n/g, "\n");
  }
  return raw;
}

function ContentChecklistPanel({ items, phase, cursorActive }) {
  const [expanded, setExpanded] = useState({});

  if (!items?.length) return null;

  const doneCount = items.filter((i) => i.done).length;
  const pct = Math.round((doneCount / items.length) * 100);
  const nextPending = items.find((i) => !i.done);
  const allDone = doneCount === items.length;

  const toggleExpand = (id) => setExpanded((prev) => ({ ...prev, [id]: !prev[id] }));

  return (
    <aside className="checklist-panel card" aria-label="Content layout decisions checklist">
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
        {phase === "awaiting_approval"
          ? allDone
            ? "Q&A complete — review the layout brief below"
            : `${doneCount} decided · ${items.length - doneCount} still open`
          : allDone
            ? "All topics decided — brief review next"
            : `${doneCount} decided · ${items.length - doneCount} still open`}
      </p>
      <p className="hint checklist-hint">
        Answer each question in chat. Say <strong>move on</strong> to skip remaining topics.
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
        {items.map((item, idx) => {
          const meta = CONTENT_CHECKLIST_META[item.id] || { icon: "·", group: "" };
          const decision = (item.decision || "").trim();
          const long = decision.length > 90;
          const isOpen = expanded[item.id];
          const isNext = !item.done && item.id === nextPending?.id;
          const displayIndex = item.index ?? idx + 1;

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
                  <span className="check-label">
                    <span className="check-index">{displayIndex}.</span> {item.label}
                  </span>
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

export default function ContentLayoutApp() {
  const hub = hubFromUrl();
  const hubMissing = !hub;
  const [session, setSession] = useState(null);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");
  const [serverOk, setServerOk] = useState(null);
  const [started, setStarted] = useState(false);
  const bottomRef = useRef(null);

  const phase = session?.phase || "idle";
  const messages = session?.messages || [];
  const checklist = session?.checklist || [];
  const assistantWorking = busy || session?.cursorWorking;
  const checklistBusy = assistantWorking;
  const autoRespondLabel = CONTENT_PRESETS.find((p) => p.id === session?.autoRespondPreset)?.label;

  const showChecklist = started && checklist.length > 0 && phase !== "cancelled";

  const loadSession = useCallback(async () => {
    if (hubMissing) return;
    try {
      const d = await api("/api/content/session");
      if (d.session?.phase && d.session.phase !== "cancelled" && d.session.messages?.length) {
        setSession(d.session);
        setStarted(true);
      }
    } catch {
      /* no session yet */
    }
  }, [hubMissing]);

  useEffect(() => {
    fetch("/api/health")
      .then((r) => r.json())
      .then((h) => setServerOk(!!h.contentApi))
      .catch(() => setServerOk(false));
    loadSession();
  }, [loadSession]);

  useEffect(() => {
    if (!started || !session?.cursorWorking) return undefined;
    const poll = () => {
      api("/api/content/session")
        .then((d) => {
          if (d.session) setSession(d.session);
        })
        .catch(() => {});
    };
    poll();
    const id = setInterval(poll, SESSION_POLL_MS);
    return () => clearInterval(id);
  }, [started, session?.cursorWorking]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }, [messages.length, assistantWorking]);

  const applySession = (s) => {
    setSession(s);
    if (s?.messages?.length) setStarted(true);
  };

  const run = async (fn) => {
    setErr("");
    setBusy(true);
    try {
      const result = await fn();
      if (result?.session) applySession(result.session);
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
      const d = await api("/api/content/start", {
        method: "POST",
        body: JSON.stringify({ message: msg }),
      });
      setInput("");
      setStarted(true);
      return d;
    });

  const sendChat = () =>
    run(async () => {
      const msg = input.trim();
      if (!msg) return null;
      const d = await api("/api/content/chat", {
        method: "POST",
        body: JSON.stringify({ message: msg }),
      });
      setInput("");
      return d;
    });

  const approve = (approved) =>
    run(async () => {
      const d = await api("/api/content/approve", {
        method: "POST",
        body: JSON.stringify({
          approved,
          feedback: approved ? "" : input.trim() || "Revise the layout.",
        }),
      });
      if (!approved) setInput("");
      return d;
    });

  const reset = () =>
    run(async () => {
      const d = await api("/api/content/reset", { method: "POST", body: "{}" });
      setSession(null);
      setStarted(false);
      setInput("");
      return d;
    });

  const autoRespond = (presetId) => {
    const preset = CONTENT_PRESETS.find((p) => p.id === presetId);
    const kickoff = preset?.hint || presetId;
    setErr("");
    setBusy(true);
    return (async () => {
      try {
        if (!started) {
          const startResult = await api("/api/content/start", {
            method: "POST",
            body: JSON.stringify({ message: kickoff }),
          });
          if (startResult?.session) applySession(startResult.session);
          setInput("");
        }
        let done = false;
        let guard = 0;
        let waitSpins = 0;
        while (!done && guard < 28) {
          guard += 1;
          const d = await api("/api/content/auto-respond/step", {
            method: "POST",
            body: JSON.stringify({ preset: presetId }),
          });
          setInput("");
          if (d?.session) setSession(d.session);
          if (d?.done || d?.session?.phase === "awaiting_approval") {
            done = true;
            break;
          }
          if (d?.waiting) {
            waitSpins += 1;
            if (waitSpins > 80) throw new Error("AI Responder stalled — refresh and continue manually.");
            await new Promise((r) => setTimeout(r, 400));
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

  return (
    <>
      <div className={`wrap planner-pane planner-pane-content ${started ? "wrap-wide" : ""}`}>
        <header className="planner-hero">
          <p className="planner-kicker">Environment Kit · Content layout</p>
          <h1>Content Layout Planner</h1>
          <p className="sub">
            Pre-production briefing for MainScene — NPCs, enemies, props, hybrid dialog (Talk + Shop),
            training/quest blockers, and Play Mode smoke verification.
          </p>
        </header>

        {hubMissing && (
          <div className="card hub-missing-card" role="alert">
            <p className="err">
              This tab is missing <code>?hub=</code> — the planner cannot talk to your Unity project.
            </p>
            <p className="hint">
              Reopen from Unity: <strong>Window → Environment Kit → Hub</strong>, or use the{" "}
              <strong>Content layout</strong> tab link.
            </p>
            <p className="hub-missing-url">
              <code>{`http://127.0.0.1:8766/?hub=/Users/jacob/Hub#content`}</code>
            </p>
            <button
              type="button"
              className="primary"
              onClick={() => {
                window.location.href = `/?hub=${encodeURIComponent("/Users/jacob/Hub")}#content`;
              }}
            >
              Open with /Users/jacob/Hub
            </button>
          </div>
        )}

        {serverOk === false && (
          <div className="card hub-missing-card" role="alert">
            <p className="err">Wizard server is outdated (no Content API).</p>
            <pre className="brief-preview">{`cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
python3 ensure-build-wizard.py --restart`}</pre>
          </div>
        )}

        {!started && (
          <div className="card">
            <div className="row">
              <label>What NPCs, enemies, and props do you want in MainScene?</label>
              <textarea
                rows={4}
                value={input}
                onChange={(e) => setInput(e.target.value)}
                placeholder="e.g. Trainer NPC by the arena who unlocks shop after agent reaches 70% training grade..."
              />
            </div>
            <button type="button" className="primary" disabled={busy || !input.trim() || hubMissing} onClick={() => start()}>
              Start planning
            </button>

            <div className="ai-responder-block">
              <h2 className="section">AI Responder</h2>
              <p className="hint">
                AI Responder fills the full checklist via live chat — one turn at a time — then stops at
                brief approval for you. Separate from the World build tab.
              </p>
              <div className="ai-preset-row">
                {CONTENT_PRESETS.map((p) => (
                  <button
                    key={p.id}
                    type="button"
                    className={`ai-preset-btn ai-preset-${p.id}`}
                    disabled={busy || hubMissing}
                    onClick={() => autoRespond(p.id)}
                    title={p.hint}
                  >
                    <span className="ai-preset-title">{p.label}</span>
                    <span className="ai-preset-hint">{p.hint}</span>
                  </button>
                ))}
              </div>
            </div>
          </div>
        )}

        {started && (
          <div className={`planner-grid ${showChecklist ? "with-checklist" : ""}`}>
            <div className="planner-main">
              <div className="phase-bar">
                Phase: <strong>{CONTENT_PHASE_LABELS[phase] || phase || (assistantWorking ? "Working…" : "—")}</strong>
              </div>

              <div className="chat-scope-banner chat-scope-content" role="note">
                <span className="chat-scope-tag">Content layout session</span>
                <span className="chat-scope-detail">
                  This chat does not share messages with World build. For terrain, tiles, and caves, use the World
                  build tab.
                </span>
              </div>

              <div className="chat">
                <h3 className="chat-transcript-title live-conversation">MainScene content conversation</h3>
                {messages.map((m, i) => {
                  const isAutoUser =
                    m.role === "user" && (session?.autoRespondActive || session?.autoRespondPreset);
                  const roleLabel =
                    m.role === "user" ? (isAutoUser ? "AI Responder" : "You") : "Planner";
                  return (
                    <div key={i} className={`bubble ${m.role}`}>
                      <div className="role">{roleLabel}</div>
                      <div className="text">{displayChatContent(m.content)}</div>
                    </div>
                  );
                })}
                <div ref={bottomRef} />
              </div>

              {assistantWorking && (
                <div className="working-bar" role="status" aria-live="polite">
                  <span className="spinner" />
                  <span>
                    {autoRespondLabel
                      ? `AI Responder — ${autoRespondLabel}…`
                      : session?.cursorWorking
                        ? "AI Assistant is working…"
                        : "Working…"}
                  </span>
                </div>
              )}

              {phase === "qna" && !session?.cursorWorking && !busy && (
                <div className="composer card">
                  <div className="ai-responder-inline">
                    <span className="ai-responder-label">AI Responder</span>
                    <div className="ai-preset-row ai-preset-row-compact">
                      {CONTENT_PRESETS.map((p) => (
                        <button
                          key={p.id}
                          type="button"
                          className={`ai-preset-btn ai-preset-${p.id}`}
                          disabled={busy}
                          onClick={() => autoRespond(p.id)}
                          title={p.hint}
                        >
                          {p.label}
                        </button>
                      ))}
                    </div>
                  </div>
                  <textarea
                    rows={2}
                    value={input}
                    onChange={(e) => setInput(e.target.value)}
                    placeholder={
                      messages.some((m) => m.role === "assistant")
                        ? "Answer the planner's question… (or move on when ready to review)"
                        : "Add more detail about your MainScene layout…"
                    }
                    onKeyDown={(e) => {
                      if (e.key === "Enter" && !e.shiftKey) {
                        e.preventDefault();
                        sendChat();
                      }
                    }}
                  />
                  <button type="button" className="primary" disabled={busy || !input.trim()} onClick={sendChat}>
                    Send
                  </button>
                </div>
              )}

              {phase === "awaiting_approval" && (
                <div className="card">
                  <h2 className="section">Layout brief — confirm before apply</h2>
                  <pre className="plan">{JSON.stringify(session?.brief || {}, null, 2)}</pre>
                  <div className="actions">
                    <button type="button" className="primary" disabled={busy} onClick={() => approve(true)}>
                      Approve &amp; write brief
                    </button>
                    <button type="button" className="ghost" disabled={busy} onClick={() => approve(false)}>
                      Back to Q&amp;A
                    </button>
                  </div>
                </div>
              )}

              {phase === "finalized" && (
                <div className="card status-box">
                  <p className="status">Content layout brief approved.</p>
                  <p className="hint">
                    In Unity: <strong>Window → Environment Kit → Open MainScene &amp;&amp; Apply Content Layout</strong>,
                    then verify in Play Mode.
                  </p>
                  <div className="actions">
                    <button type="button" className="primary" disabled={busy} onClick={reset}>
                      Start over
                    </button>
                  </div>
                </div>
              )}

              {phase !== "finalized" && phase !== "cancelled" && (
                <button type="button" className="ghost cancel-btn" disabled={busy} onClick={reset}>
                  Reset content chat
                </button>
              )}
            </div>

            {showChecklist && (
              <ContentChecklistPanel items={checklist} phase={phase} cursorActive={checklistBusy} />
            )}
          </div>
        )}

        {err && <p className="err">{err}</p>}
      </div>
    </>
  );
}
