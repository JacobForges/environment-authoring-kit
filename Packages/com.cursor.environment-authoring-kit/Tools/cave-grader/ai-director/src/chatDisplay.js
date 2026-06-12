/** Match Build Planner chat display — strip JSON metadata from assistant bubbles. */

export function displayChatContent(content) {
  const text = String(content || "").trim();
  if (!text.startsWith("{")) {
    return content;
  }
  if (
    !text.includes('"checklist"') &&
    !text.includes('"assistantMessage"') &&
    !text.includes('"qnaComplete"') &&
    !text.includes('"teachingMoment"')
  ) {
    return content;
  }
  try {
    const parsed = JSON.parse(text);
    if (typeof parsed.assistantMessage === "string" && parsed.assistantMessage.trim()) {
      return parsed.assistantMessage.trim();
    }
  } catch {
    const match = text.match(/"assistantMessage"\s*:\s*"((?:[^"\\]|\\.)*)"/s);
    if (match) {
      try {
        return JSON.parse(`"${match[1]}"`);
      } catch {
        return match[1].replace(/\\n/g, "\n");
      }
    }
  }
  return "";
}

export function streamingDuplicatesAssistant(session) {
  const stream = (session?.streamingText || "").trim();
  if (!stream) return false;
  const msgs = session?.messages || [];
  for (let i = msgs.length - 1; i >= 0; i -= 1) {
    if (msgs[i].role !== "assistant") continue;
    const committed = displayChatContent(msgs[i].content || "").trim();
    if (!committed) return false;
    return (
      committed === stream ||
      committed.startsWith(stream.slice(0, Math.min(stream.length, 160))) ||
      stream.startsWith(committed.slice(0, Math.min(committed.length, 160)))
    );
  }
  return false;
}
