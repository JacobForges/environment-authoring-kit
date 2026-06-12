import { useEffect, useMemo, useRef } from "react";
import ThinkingIndicator from "./ThinkingIndicator.jsx";
import { displayChatContent, streamingDuplicatesAssistant } from "./chatDisplay.js";

function StaticBubble({ message, label }) {
  return (
    <div className={`brief-bubble ${message.role}`}>
      <div className="bubble-role">{label}</div>
      <div className="bubble-body">{displayChatContent(message.content)}</div>
    </div>
  );
}

function StreamingBubble({ content, label }) {
  const waiting = !content;
  return (
    <div className="brief-bubble assistant streaming">
      <div className="bubble-role">{label}</div>
      <div className="bubble-body">
        {waiting ? <span className="thinking-dots" aria-hidden>…</span> : content}
        <span className="caret" aria-hidden />
      </div>
    </div>
  );
}

export default function ConversationMessages({
  session,
  autoRunning,
  onPresentationBusy,
}) {
  const bottomRef = useRef(null);
  const prevCountRef = useRef(0);

  const messages = session?.messages || [];
  const streamingIsStale = useMemo(
    () => streamingDuplicatesAssistant(session),
    [session?.streamingText, messages]
  );
  const liveStreamingText = streamingIsStale ? "" : session?.streamingText || "";
  const directorLive = Boolean(session?.assistantWorking || liveStreamingText);
  const responderWaiting = Boolean(
    session?.responderWorking || autoRunning
  ) && !directorLive;

  useEffect(() => {
    const count = messages.length;
    const grew = count > prevCountRef.current;
    prevCountRef.current = count;
    if (!grew && !liveStreamingText) return;
    bottomRef.current?.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }, [messages.length, liveStreamingText, messages]);

  const presentationBusy = directorLive || responderWaiting;

  useEffect(() => {
    onPresentationBusy?.(presentationBusy);
  }, [presentationBusy, onPresentationBusy]);

  const labelFor = (m) => {
    if (m.role === "assistant") return "AI Director";
    if (m.fromResponder) return "AI Responder";
    return "You";
  };

  return (
    <>
      {messages.map((m, i) => (
        <StaticBubble key={`m-${i}`} message={m} label={labelFor(m)} />
      ))}
      {directorLive ? (
        <StreamingBubble
          content={displayChatContent(liveStreamingText || "")}
          label="AI Director"
        />
      ) : null}
      {responderWaiting ? (
        <ThinkingIndicator label="AI Responder is thinking…" variant="responder" />
      ) : null}
      <div ref={bottomRef} className="chat-scroll-anchor" aria-hidden />
    </>
  );
}
