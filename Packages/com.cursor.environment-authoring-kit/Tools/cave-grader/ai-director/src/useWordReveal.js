import { useEffect, useMemo, useState } from "react";

/** Split text into word + whitespace tokens for natural pacing. */
export function tokenizeWords(text) {
  const raw = String(text || "");
  if (!raw) return [];
  return raw.match(/\S+\s*/g) || [];
}

/**
 * Reveal text word-by-word. When `active`, catches up to `fullText` at `wps` words/sec.
 * When inactive, shows full text immediately.
 */
export function useWordReveal(fullText, active, wps = 11) {
  const tokens = useMemo(() => tokenizeWords(fullText), [fullText]);
  const [tokenIndex, setTokenIndex] = useState(0);

  useEffect(() => {
    if (!active) {
      setTokenIndex(tokens.length);
      return undefined;
    }
    if (tokenIndex > tokens.length) {
      setTokenIndex(tokens.length);
    }
    const ms = Math.max(28, 1000 / wps);
    const id = setInterval(() => {
      setTokenIndex((i) => {
        if (i >= tokens.length) return i;
        return i + 1;
      });
    }, ms);
    return () => clearInterval(id);
  }, [active, tokens.length, wps, tokenIndex, tokens.length]);

  useEffect(() => {
    if (!active) setTokenIndex(tokens.length);
  }, [fullText, active, tokens.length]);

  const display = tokens.slice(0, tokenIndex).join("");
  const done = tokenIndex >= tokens.length && tokens.length > 0;
  const idle = !active || (done && tokenIndex >= tokens.length);

  return { display, done, idle, tokenCount: tokens.length, tokenIndex };
}
