import ConversationMessages from "./ConversationMessages.jsx";
import ThinkingIndicator from "./ThinkingIndicator.jsx";

export default function BriefingView({
  capture,
  session,
  chatInput,
  setChatInput,
  busy,
  busyKind,
  autoRunning,
  inputLocked,
  err,
  onSend,
  onResponder,
  onEnterEditor,
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
  const canEnter = allDone || session?.phase === "briefing_ready";
  const locked = inputLocked || busy || session?.assistantWorking || session?.responderWorking;
  const footagePhase = session?.footage?.phase;
  const plannerFootageOnly = footagePhase === "planner_only" || footagePhase === "pre_capture";
  const userMediaFootage = footagePhase === "user_media" || session?.footage?.source === "user_media";

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
          <h1>AI Director — Pre-production</h1>
          <p className="muted">
            {checklist.length} broadcast-grade decisions — Q&amp;A before the studio editor. Capture:{" "}
            <code className="capture-code">{capture}</code>
            {userMediaFootage ? (
              <>
                <br />
                <span className="footage-hint footage-user">
                  Briefing uses your uploaded footage — {session?.footage?.timelapseFrames ?? 0} frames
                  {session?.footage?.hasVideo ? " (video)" : ""}
                  {session?.footage?.hasImages ? " (images)" : ""}.
                </span>
              </>
            ) : plannerFootageOnly ? (
              <>
                <br />
                <span className="footage-hint">
                  Footage so far is planner UI only — describe film intent, not built terrain yet.
                </span>
              </>
            ) : null}
          </p>
        </div>
        <button type="button" className="secondary" onClick={onRefresh} disabled={busy}>
          Refresh
        </button>
      </header>

      {err ? <div className="banner-warn">{err}</div> : null}

      <div className="briefing-grid">
        <main className="briefing-chat card">
          <h2 className="section-title">Director conversation</h2>
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
                      ? "AI Director is speaking…"
                      : "Processing your last message…"
                  : "Answer the Director's question… (Enter to send, Shift+Enter for newline)"
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
            <h2 className="section-title">Production checklist</h2>
            <span className={`checklist-badge ${allDone ? "complete" : ""}`}>
              {doneCount}/{checklist.length}
            </span>
          </div>
          <div className="progress-track" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100}>
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
            {canEnter ? (
              <>
                <p className="muted">All decisions captured — open the studio to preview and approve generation.</p>
                <button type="button" className="proceed" onClick={onEnterEditor} disabled={locked}>
                  Enter Studio Editor
                </button>
              </>
            ) : (
              <p className="muted">Complete each checklist item in chat, then enter the studio editor.</p>
            )}
          </div>
        </aside>
      </div>
    </div>
  );
}
