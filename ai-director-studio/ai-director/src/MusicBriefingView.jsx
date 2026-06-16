import ConversationMessages from "./ConversationMessages.jsx";
import ThinkingIndicator from "./ThinkingIndicator.jsx";

export default function MusicBriefingView({
  project,
  session,
  chatInput,
  setChatInput,
  busy,
  busyKind,
  autoRunning,
  inputLocked,
  err,
  internetResearch = true,
  onInternetResearchChange,
  onSend,
  onResponder,
  onStartProduction,
  onRefresh,
  onPresentationBusy,
}) {
  const messages = session?.messages || [];
  const directorThinking =
    Boolean(session?.assistantWorking || (busy && busyKind === "director")) &&
    !session?.streamingText;
  const responderThinking = Boolean(
    session?.responderWorking || autoRunning || (busy && busyKind === "responder")
  );
  const checklist = session?.checklist || [];
  const doneCount = checklist.filter((c) => c.done).length;
  const allDone = checklist.length > 0 && doneCount === checklist.length;
  const pct = checklist.length ? Math.round((doneCount / checklist.length) * 100) : 0;
  const canProceed = allDone || session?.phase === "briefing_ready" || session?.musicQnaComplete;
  const locked = inputLocked || busy || session?.assistantWorking || session?.responderWorking;

  const grouped = checklist.reduce((acc, item) => {
    const cat = item.category || "General";
    if (!acc[cat]) acc[cat] = [];
    acc[cat].push(item);
    return acc;
  }, {});
  const categoryOrder = session?.categories?.length
    ? session.categories
    : Object.keys(grouped);

  return (
    <div className="briefing-page">
      <header className="briefing-header">
        <div>
          <h1>Music Director — Pre-production</h1>
          <p className="muted">
            {checklist.length} track decisions — Q&amp;A before instrumental, karaoke, and polish.
            {session?.internetResearch ? (
              <span className="pill active" style={{ marginLeft: "0.5rem" }}>
                Research ON
              </span>
            ) : null}
            {project ? (
              <>
                {" "}
                Project: <code className="capture-code">{project}</code>
              </>
            ) : null}
          </p>
        </div>
        <button type="button" className="secondary" onClick={onRefresh} disabled={busy}>
          Refresh
        </button>
      </header>

      {err ? <div className="banner-warn">{err}</div> : null}

      {!session?.messages?.some((m) => m.role === "user") && onInternetResearchChange ? (
        <label className="card row check" style={{ marginBottom: "0.75rem" }}>
          <input
            type="checkbox"
            checked={internetResearch}
            onChange={(e) => onInternetResearchChange(e.target.checked)}
            disabled={busy}
          />
          <span>
            Internet research (pop/trap/game OST 2025–2026 — injected before first Q&amp;A reply)
          </span>
        </label>
      ) : null}

      <div className="briefing-grid">
        <main className="briefing-chat card">
          <h2 className="section-title">Music Director conversation</h2>
          <div className="briefing-messages">
            <ConversationMessages
              session={session}
              autoRunning={autoRunning}
              onPresentationBusy={onPresentationBusy}
            />
          </div>
          <div className="briefing-compose">
            <textarea
              className="brief-input"
              value={chatInput}
              onChange={(e) => setChatInput(e.target.value)}
              placeholder={
                locked
                  ? responderThinking
                    ? "AI Responder is working…"
                    : directorThinking || session?.streamingText
                      ? "Music Director is speaking…"
                      : "Processing your last message…"
                  : "Answer the Music Director's question… (Enter to send, Shift+Enter for newline)"
              }
              rows={3}
              disabled={locked}
              onKeyDown={(e) => {
                if (e.key === "Enter" && !e.shiftKey && !locked) {
                  e.preventDefault();
                  onSend();
                }
              }}
            />
            <div className="gate-row">
              <button type="button" onClick={onSend} disabled={locked || !chatInput.trim()}>
                {directorThinking || session?.streamingText ? "Director…" : "Send"}
              </button>
              <button type="button" className="secondary" onClick={onResponder} disabled={locked}>
                {responderThinking ? "Running Q&A…" : "AI Responder — run all Q&A"}
              </button>
            </div>
          </div>
        </main>

        <aside className="briefing-checklist card">
          <div className="checklist-header">
            <h2 className="section-title">Track checklist</h2>
            <span className={`checklist-badge ${allDone ? "complete" : ""}`}>
              {doneCount}/{checklist.length}
            </span>
          </div>
          <div
            className="progress-track"
            role="progressbar"
            aria-valuenow={pct}
            aria-valuemin={0}
            aria-valuemax={100}
          >
            <div className="progress-fill" style={{ width: `${pct}%` }} />
          </div>
          {directorThinking ? (
            <ThinkingIndicator label="Updating checklist…" variant="director" />
          ) : null}
          <div className="checklist-items">
            {categoryOrder.map((cat) => {
              const items = grouped[cat];
              if (!items?.length) return null;
              const catDone = items.filter((i) => i.done).length;
              return (
                <section key={cat} className="checklist-category">
                  <h3 className="checklist-cat-title">
                    {cat}{" "}
                    <span className="muted">
                      ({catDone}/{items.length})
                    </span>
                  </h3>
                  <ul className="checklist-cat-list">
                    {items.map((c) => (
                      <li key={c.id} className={c.done ? "done" : "pending"}>
                        <div className="checklist-row">
                          <span className="check-icon">{c.done ? "✓" : "○"}</span>
                          <div>
                            <strong>{c.label}</strong>
                            {c.value ? <div className="check-value">{c.value}</div> : null}
                            {c.teach ? <p className="check-teach">{c.teach}</p> : null}
                          </div>
                        </div>
                      </li>
                    ))}
                  </ul>
                </section>
              );
            })}
          </div>
          <div className="briefing-enter">
            {canProceed ? (
              <>
                <p className="muted">
                  All track decisions captured — start the production pipeline (instrumental → record →
                  produce).
                </p>
                <button type="button" className="proceed" onClick={onStartProduction} disabled={locked}>
                  Start production →
                </button>
              </>
            ) : (
              <p className="muted">Complete each checklist item in chat, then start production.</p>
            )}
          </div>
        </aside>
      </div>
    </div>
  );
}
