import { useCallback, useEffect, useState } from "react";

const KEYS = ["auto", "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
const SCALES = ["minor", "major"];

async function api(path, opts = {}) {
  const isForm = opts.body instanceof FormData;
  const res = await fetch(path, {
    ...opts,
    headers: isForm ? opts.headers : { "Content-Type": "application/json", ...(opts.headers || {}) },
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || res.statusText);
  return data;
}

export default function MusicSetupView({
  project,
  onProjectChange,
  onNext,
  onBack,
  fromBriefing = false,
  autoGenerate = false,
}) {
  const [name, setName] = useState("");
  const [key, setKey] = useState("auto");
  const [scale, setScale] = useState("minor");
  const [bpm, setBpm] = useState("");
  const [prompt, setPrompt] = useState("dark pop trap 140 bpm, moody 808s, catchy hook");
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState(null);
  const [err, setErr] = useState("");
  const [sunoConfigured, setSunoConfigured] = useState(false);

  const [autoTried, setAutoTried] = useState(false);

  const refresh = useCallback(async () => {
    if (!project) return;
    const st = await api(`/api/music/status?path=${encodeURIComponent(project)}`);
    setStatus(st);
    if (st.key) setKey(st.key);
    if (st.scale) setScale(st.scale);
    if (st.bpm) setBpm(String(st.bpm));
    if (st.instrumentalPrompt) setPrompt(st.instrumentalPrompt);
    setSunoConfigured(Boolean(st.status?.sunoConfigured));
  }, [project]);

  useEffect(() => {
    api("/api/director/health")
      .then((h) => setSunoConfigured(Boolean(h.sunoConfigured)))
      .catch(() => {});
  }, []);

  useEffect(() => {
    refresh().catch(() => {});
  }, [refresh]);

  useEffect(() => {
    if (!autoGenerate || autoTried || !project || !sunoConfigured || !prompt.trim()) return;
    if (status?.status?.instrumentalReady) return;
    setAutoTried(true);
    (async () => {
      setErr("");
      setBusy(true);
      try {
        await api("/api/music/instrumental/generate", {
          method: "POST",
          body: JSON.stringify({ project, prompt }),
        });
        await refresh();
      } catch (e) {
        setErr(String(e.message || e));
      } finally {
        setBusy(false);
      }
    })();
  }, [autoGenerate, autoTried, project, sunoConfigured, prompt, status?.status?.instrumentalReady, refresh]);

  const createProject = async () => {
    setErr("");
    setBusy(true);
    try {
      const data = await api("/api/music/project/create", {
        method: "POST",
        body: JSON.stringify({
          name: name || "track",
          key,
          scale,
          bpm: bpm ? parseInt(bpm, 10) : null,
          instrumentalPrompt: prompt,
        }),
      });
      onProjectChange?.(data.project);
      await refresh();
    } catch (e) {
      setErr(String(e.message || e));
    } finally {
      setBusy(false);
    }
  };

  const generateInstrumental = async () => {
    if (!project) return;
    setErr("");
    setBusy(true);
    try {
      const data = await api("/api/music/instrumental/generate", {
        method: "POST",
        body: JSON.stringify({ project, prompt }),
      });
      if (!data.ok && data.needsUpload) {
        setErr(data.error || "Add SUNO_API_KEY or upload instrumental below.");
      }
      await refresh();
    } catch (e) {
      setErr(String(e.message || e));
    } finally {
      setBusy(false);
    }
  };

  const uploadInstrumental = async (file) => {
    if (!project || !file) return;
    setErr("");
    setBusy(true);
    try {
      const form = new FormData();
      form.append("project", project);
      form.append("file", file);
      const res = await fetch("/api/music/instrumental/upload", { method: "POST", body: form });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.error || res.statusText);
      await refresh();
    } catch (e) {
      setErr(String(e.message || e));
    } finally {
      setBusy(false);
    }
  };

  const instrumentalReady = Boolean(status?.status?.instrumentalReady);

  const handleNext = () => {
    if (!project) return;
    onNext?.();
  };

  return (
    <div className="music-flow">
      <header className="music-header">
        {onBack && !fromBriefing ? (
          <button type="button" className="secondary" onClick={onBack} disabled={busy}>
            ← Back
          </button>
        ) : (
          <span />
        )}
        <div>
          <h1>{fromBriefing ? "Instrumental" : "Music setup"}</h1>
          <p className="muted">
            {fromBriefing
              ? "Generate or upload the beat from your brief — then karaoke record."
              : "Sing bad, sound pro — pop/trap polish chain."}
          </p>
        </div>
      </header>

      <div className="music-card card">
        {!project && !fromBriefing ? (
          <>
            <label>
              Project name
              <input value={name} onChange={(e) => setName(e.target.value)} placeholder="my-track" disabled={busy} />
            </label>
            <div className="music-row">
              <label>
                Key
                <select value={key} onChange={(e) => setKey(e.target.value)} disabled={busy}>
                  {KEYS.map((k) => (
                    <option key={k} value={k}>
                      {k === "auto" ? "Auto detect" : k}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Scale
                <select value={scale} onChange={(e) => setScale(e.target.value)} disabled={busy}>
                  {SCALES.map((s) => (
                    <option key={s} value={s}>
                      {s}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                BPM <span className="muted">(optional)</span>
                <input
                  type="number"
                  min={60}
                  max={200}
                  value={bpm}
                  onChange={(e) => setBpm(e.target.value)}
                  placeholder="140"
                  disabled={busy}
                />
              </label>
            </div>
            <button type="button" onClick={createProject} disabled={busy}>
              Create music project
            </button>
          </>
        ) : !project ? (
          <p className="muted">Waiting for music project from briefing…</p>
        ) : (
          <>
            <p className="muted">
              Project: <code>{project}</code>
            </p>
            <div className="music-row">
              <label>
                Key
                <select value={key} onChange={(e) => setKey(e.target.value)} disabled={busy}>
                  {KEYS.map((k) => (
                    <option key={k} value={k}>
                      {k}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Scale
                <select value={scale} onChange={(e) => setScale(e.target.value)} disabled={busy}>
                  {SCALES.map((s) => (
                    <option key={s} value={s}>
                      {s}
                    </option>
                  ))}
                </select>
              </label>
            </div>

            <label>
              Instrumental prompt
              <textarea
                className="script-editor"
                style={{ minHeight: 72 }}
                value={prompt}
                onChange={(e) => setPrompt(e.target.value)}
                disabled={busy}
              />
            </label>

            <div className="gate-row">
              <button type="button" onClick={generateInstrumental} disabled={busy || !sunoConfigured}>
                {sunoConfigured ? "Generate AI instrumental" : "AI instrumental (needs API key)"}
              </button>
              <label className="file-btn secondary">
                Upload instrumental MP3
                <input
                  type="file"
                  accept="audio/mpeg,audio/mp3,audio/wav,audio/*"
                  disabled={busy}
                  onChange={(e) => uploadInstrumental(e.target.files?.[0])}
                />
              </label>
            </div>

            {!sunoConfigured ? (
              <p className="muted music-hint">
                No <code>SUNO_API_KEY</code> — upload an instrumental MP3 to continue (required for MVP).
              </p>
            ) : null}

            {instrumentalReady ? (
              <div className="music-ready">
                <span className="ok">✓ Instrumental ready ({status?.status?.instrumentalSource || "upload"})</span>
                <audio controls src={`/api/music/file?path=${encodeURIComponent(project)}&name=instrumental.mp3`} />
              </div>
            ) : null}

            <button type="button" className="landing-cta" onClick={handleNext} disabled={busy || !instrumentalReady}>
              Continue to record →
            </button>
          </>
        )}
        {err ? <p className="err">{err}</p> : null}
      </div>
    </div>
  );
}
