import { useCallback, useEffect, useState } from "react";

async function api(path, opts = {}) {
  const res = await fetch(path, {
    ...opts,
    headers: { "Content-Type": "application/json", ...(opts.headers || {}) },
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || res.statusText);
  return data;
}

export default function MusicProduceView({ project, onBack, onMusicVideo }) {
  const [status, setStatus] = useState(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");
  const [preview, setPreview] = useState("produced");

  const refresh = useCallback(async () => {
    if (!project) return;
    const st = await api(`/api/music/status?path=${encodeURIComponent(project)}`);
    setStatus(st);
  }, [project]);

  useEffect(() => {
    refresh().catch(() => {});
    const id = setInterval(() => refresh().catch(() => {}), status?.status?.produceRunning ? 800 : 4000);
    return () => clearInterval(id);
  }, [refresh, status?.status?.produceRunning]);

  const produce = async () => {
    setErr("");
    setBusy(true);
    try {
      await api("/api/music/produce", {
        method: "POST",
        body: JSON.stringify({ project }),
      });
      await refresh();
    } catch (e) {
      setErr(String(e.message || e));
    } finally {
      setBusy(false);
    }
  };

  const makeMusicVideo = async () => {
    setErr("");
    setBusy(true);
    try {
      const data = await api("/api/music/to-music-video", {
        method: "POST",
        body: JSON.stringify({ project }),
      });
      onMusicVideo?.(data.videoProject, data);
    } catch (e) {
      setErr(String(e.message || e));
    } finally {
      setBusy(false);
    }
  };

  const st = status?.status || {};
  const produced = Boolean(st.produced);
  const running = Boolean(st.produceRunning);
  const progress = st.produceProgress ?? 0;
  const message = st.produceMessage || "";

  const dryUrl = project
    ? `/api/music/file?path=${encodeURIComponent(project)}&name=vocal_dry.wav`
    : null;
  const producedUrl = project
    ? `/api/music/file?path=${encodeURIComponent(project)}&name=vocal_produced.wav`
    : null;
  const masterWav = project
    ? `/api/music/file?path=${encodeURIComponent(project)}&name=SongMaster.wav`
    : null;
  const masterMp3 = project
    ? `/api/music/file?path=${encodeURIComponent(project)}&name=SongMaster.mp3`
    : null;

  return (
    <div className="music-flow">
      <header className="music-header">
        <button type="button" className="secondary" onClick={onBack} disabled={busy || running}>
          ← Record
        </button>
        <div>
          <h1>Produce</h1>
          <p className="muted">Pop/trap polish — tune, stack, compress, loud.</p>
        </div>
      </header>

      <div className="music-card card">
        <button type="button" className="landing-cta" onClick={produce} disabled={busy || running}>
          {running ? `Producing… ${progress}%` : "Produce track"}
        </button>
        {running || message ? (
          <div className="produce-progress">
            <div className="produce-bar" style={{ width: `${progress}%` }} />
            <p className="muted">{message}</p>
          </div>
        ) : null}

        {produced ? (
          <>
            <div className="ab-preview">
              <div className="gate-row">
                <button
                  type="button"
                  className={preview === "dry" ? "active" : "secondary"}
                  onClick={() => setPreview("dry")}
                >
                  A — Dry vocal
                </button>
                <button
                  type="button"
                  className={preview === "produced" ? "active" : "secondary"}
                  onClick={() => setPreview("produced")}
                >
                  B — Produced vocal
                </button>
              </div>
              <audio
                key={preview}
                controls
                src={preview === "dry" ? dryUrl : producedUrl}
                className="music-inst-audio"
              />
            </div>

            <h3 className="section-title">Master export</h3>
            <audio controls src={masterWav} className="music-inst-audio" />
            <div className="gate-row">
              <a className="file-btn secondary" href={masterWav} download="SongMaster.wav">
                Download WAV
              </a>
              <a className="file-btn secondary" href={masterMp3} download="SongMaster.mp3">
                Download MP3
              </a>
            </div>

            {status?.hasPerformance ? (
              <div style={{ marginTop: "1.25rem" }}>
                <h3 className="section-title">Music video handoff</h3>
                <p className="muted">
                  Send performance footage + master to the Director for a music-video brief.
                </p>
                <button type="button" onClick={makeMusicVideo} disabled={busy}>
                  Make music video →
                </button>
              </div>
            ) : (
              <p className="muted music-hint">
                Record with webcam enabled to unlock music video handoff.
              </p>
            )}
          </>
        ) : (
          <p className="muted" style={{ marginTop: "0.75rem" }}>
            Pitch-correct to scale, stack a detuned double, compress + limit for competitive loudness.
          </p>
        )}
        {err ? <p className="err">{err}</p> : null}
      </div>
    </div>
  );
}
