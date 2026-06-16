const PLANNER_JSON_MARKERS = [
  '"checklist"',
  '"assistantMessage"',
  '"qnaComplete"',
  '"teachingMoment"',
  '"brief"',
  '"sessionConfig"',
];

function stripMarkdownFence(text) {
  const t = String(text || "").trim();
  if (!t.startsWith("```")) return t;
  return t.replace(/^```(?:json)?\s*/i, "").replace(/\s*```$/i, "").trim();
}

function isolateJsonObject(text) {
  const t = String(text || "").trim();
  if (t.startsWith("{")) return t;
  const fenced = t.match(/```(?:json)?\s*(\{[\s\S]*?\})\s*```/i);
  if (fenced) return fenced[1];
  const start = t.indexOf("{");
  if (start < 0) return null;
  let depth = 0;
  for (let i = start; i < t.length; i += 1) {
    const ch = t[i];
    if (ch === "{") depth += 1;
    else if (ch === "}") {
      depth -= 1;
      if (depth === 0) return t.slice(start, i + 1);
    }
  }
  const tail = t.slice(start);
  return PLANNER_JSON_MARKERS.some((m) => tail.includes(m)) ? tail : null;
}

function extractAssistantMessage(text) {
  const blob = isolateJsonObject(text) || stripMarkdownFence(text).trim();
  if (!blob) return "";
  try {
    const p = JSON.parse(blob);
    if (typeof p.assistantMessage === "string") return p.assistantMessage.trim();
  } catch {
    const m = blob.match(/"assistantMessage"\s*:\s*"((?:[^"\\]|\\.)*)"/);
    if (m) return m[1].replace(/\\n/g, "\n").replace(/\\"/g, '"').trim();
  }
  return "";
}

export function displayChatContent(content) {
  const raw = String(content || "").trim();
  if (!raw) return "";
  if (!raw.startsWith("{") && !raw.includes('"assistantMessage"')) return raw;
  const extracted = extractAssistantMessage(raw);
  if (extracted) return extracted;
  if (PLANNER_JSON_MARKERS.some((m) => raw.includes(m))) return "";
  return raw;
}

export function streamingDuplicatesAssistant(session) {
  const stream = (session?.streamingText || "").trim();
  if (!stream) return false;
  const msgs = session?.messages || [];
  for (let i = msgs.length - 1; i >= 0; i -= 1) {
    if (msgs[i].role === "assistant") {
      const committed = displayChatContent(msgs[i].content).trim();
      return committed && (committed === stream || committed.startsWith(stream.slice(0, 80)));
    }
  }
  return false;
}
