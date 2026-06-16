/** Simple LRC parser and line-based lyrics with BPM timing fallback. */

/**
 * Parse LRC format: [mm:ss.xx]lyric line
 * @returns {{ time: number, text: string }[]}
 */
export function parseLrc(text) {
  const lines = (text || "").split(/\r?\n/);
  const out = [];
  const re = /\[(\d{1,2}):(\d{2})(?:\.(\d{1,3}))?\]\s*(.*)/;
  for (const line of lines) {
    const m = line.match(re);
    if (!m) continue;
    const min = parseInt(m[1], 10);
    const sec = parseInt(m[2], 10);
    const frac = m[3] ? parseInt(m[3].padEnd(3, "0").slice(0, 3), 10) / 1000 : 0;
    const time = min * 60 + sec + frac;
    const lyric = (m[4] || "").trim();
    if (lyric) out.push({ time, text: lyric });
  }
  return out.sort((a, b) => a.time - b.time);
}

/**
 * Convert plain lines to timed lyrics using even spacing from BPM.
 */
export function linesToTimed(text, bpm = 120, beatsPerLine = 4) {
  const lines = (text || "")
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter(Boolean);
  const secPerBeat = 60 / (bpm || 120);
  const secPerLine = secPerBeat * beatsPerLine;
  return lines.map((line, i) => ({ time: i * secPerLine, text: line }));
}

/**
 * @param {string} text
 * @param {"lrc"|"lines"|"auto"} format
 * @param {number|null} bpm
 */
export function parseLyrics(text, format = "auto", bpm = null) {
  const trimmed = (text || "").trim();
  if (!trimmed) return [];
  const looksLrc = /\[\d{1,2}:\d{2}/.test(trimmed);
  if (format === "lrc" || (format === "auto" && looksLrc)) {
    const parsed = parseLrc(trimmed);
    if (parsed.length) return parsed;
  }
  return linesToTimed(trimmed, bpm || 120);
}

/** Find active line index for current playback time. */
export function activeLineIndex(timedLines, currentTime) {
  if (!timedLines?.length) return -1;
  let idx = 0;
  for (let i = 0; i < timedLines.length; i++) {
    if (timedLines[i].time <= currentTime) idx = i;
    else break;
  }
  return idx;
}

/** Export timed lines back to LRC string. */
export function toLrc(timedLines) {
  return timedLines
    .map(({ time, text }) => {
      const min = Math.floor(time / 60);
      const sec = time % 60;
      const ss = sec.toFixed(2).padStart(5, "0");
      return `[${String(min).padStart(2, "0")}:${ss}]${text}`;
    })
    .join("\n");
}
