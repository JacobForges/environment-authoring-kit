import { useEffect, useMemo, useRef } from "react";
import { displayChatContent, streamingDuplicatesAssistant } from "./chatDisplay.js";

export default function ScriptStudioView({
  capture,
  session,
  script,
  setScript,
  setScriptDirty,
  previewVideoUrl,
  silentReady,
  presentationReady,
  composeRunning,
  busy,
  err,
  scriptChatInput,
  setScriptChatInput,
  onSendScriptChat,
  onBootstrapScriptChat,
  scriptDirty,
  onSave,
  onRegen,
  onApprove,
}) {
  const messagesRef = useRef(null);
  const bottomRef = useRef(null);
  const bootstrappedRef = useRef(false);
  const prevCountRef = useRef(0);
  const messages = session?.scriptMessages || [];
  const scriptSession = useMemo(
    () => ({ ...session, messages: session?.scriptMessages || [] }),
    [session]
  );
  const streamingIsStale = useMemo(
    () => streamingDuplicatesAssistant(scriptSession),
    [scriptSession, session?.streamingText]
  );
  const liveStreamingText =
    session?.streamingRole === "script" && !streamingIsStale ? session?.streamingText || "" : "";
  const scriptLive = Boolean(session?.scriptAssistantWorking || liveStreamingText);
  const locked = busy || session?.scriptAssistantWorking;

  useEffect(() => {
    if (!capture || bootstrappedRef.current) return;
    if (messages.length > 0) {
      bootstrappedRef.current = true;
      return;
    }
    bootstrappedRef.current = true;
    onBootstrapScriptChat?.();
  }, [capture, messages.length, onBootstrapScriptChat]);

  useEffect(() => {
    const count = messages.length;
    const grew = count > prevCountRef.current;
    prevCountRef.current = count;
    if (!grew && !liveStreamingText) return;
    bottomRef.current?.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }, [messages.length, liveStreamingText, messages]);

  return (
    <section className="panel script-studio">
      <header className="script-studio-head">
        <div>
          <h2>Narration script</h2>
          <p className="muted">
            Watch the silent master, edit lines, and work the script with the Director in chat.
          </p>
        </div>
      </header>

      <div className="script-studio-grid">
        <div className="script-studio-main">
          {previewVideoUrl ? (
            <video
              key={previewVideoUrl}
              className="script-preview-video"
              controls
              src={previewVideoUrl}
            />
          ) : (
            <div className="script-preview-placeholder">
              <p className="muted">
                {composeRunning
                  ? "Compose in progress — preview appears when the silent shell is ready."
                  : "Run silent compose to preview picture before locking narration."}
              </p>
            </div>
          )}

          <label className="script-editor-label" htmlFor="narration-script">
            NarrationVoiceGuide.md
          </label>
          <textarea
            id="narration-script"
            className="script-editor script-editor-tall"
            value={script}
            onChange={(e) => {
              setScript(e.target.value);
              setScriptDirty(true);
            }}
            placeholder="Run silent compose to generate, or draft with Script Director chat…"
            disabled={locked}
          />

          <div className="gate-row">
            <button type="button" onClick={onSave} disabled={locked || !scriptDirty}>
              Save script
            </button>
            <button type="button" className="secondary" onClick={onRegen} disabled={locked || !silentReady}>
              Regenerate from capture
            </button>
            <button type="button" onClick={onApprove} disabled={locked || composeRunning || !silentReady}>
              Approve &amp; record voice
            </button>
          </div>
          {!silentReady && !presentationReady ? (
            <p className="muted script-studio-hint">
              Script chat can draft intent now; apply timed lines after silent video exists.
            </p>
          ) : null}
        </div>

        <aside className="script-chat card">
          <h3 className="section-title">Script Director</h3>
          <div className="script-chat-messages" ref={messagesRef}>
            {messages.map((m, i) => (
              <div key={i} className={`brief-bubble ${m.role}`}>
                <div className="bubble-role">{m.role === "assistant" ? "Script Director" : "You"}</div>
                <div className="bubble-body">{displayChatContent(m.content)}</div>
              </div>
            ))}
            {scriptLive ? (
              <div className="brief-bubble assistant streaming">
                <div className="bubble-role">Script Director</div>
                <div className="bubble-body">
                  {liveStreamingText ? (
                    displayChatContent(liveStreamingText)
                  ) : (
                    <span className="thinking-dots" aria-hidden>…</span>
                  )}
                  <span className="caret" aria-hidden />
                </div>
              </div>
            ) : null}
            <div ref={bottomRef} className="chat-scroll-anchor" aria-hidden />
          </div>
          {err ? <div className="banner-warn script-chat-err">{err}</div> : null}
          <textarea
            className="brief-input script-chat-input"
            value={scriptChatInput}
            onChange={(e) => setScriptChatInput(e.target.value)}
            placeholder={
              locked
                ? "Processing…"
                : "e.g. Open with a question hook, tighten act two, make CTA warmer…"
            }
            rows={3}
            disabled={locked}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey && !locked && scriptChatInput.trim()) {
                e.preventDefault();
                onSendScriptChat();
              }
            }}
          />
          <button
            type="button"
            onClick={onSendScriptChat}
            disabled={locked || !scriptChatInput.trim()}
          >
            {scriptLive ? "Director…" : "Send"}
          </button>
        </aside>
      </div>
    </section>
  );
}
