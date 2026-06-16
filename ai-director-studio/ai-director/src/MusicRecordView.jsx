import { useCallback, useEffect, useRef, useState } from "react";
import { activeLineIndex, parseLyrics } from "./lrcParser.js";

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

export default function MusicRecordView({ project, onNext, onBack }) {
  const [lyrics, setLyrics] = useState("");
  const [lyricsFormat, setLyricsFormat] = useState("lines");
  const [bpm, setBpm] = useState(140);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");
  const [recording, setRecording] = useState(false);
  const [hasVocal, setHasVocal] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [webcamOn, setWebcamOn] = useState(false);

  const audioRef = useRef(null);
  const instRef = useRef(null);
  const mediaRecorderRef = useRef(null);
  const chunksRef = useRef([]);
  const webcamRef = useRef(null);
  const webcamStreamRef = useRef(null);
  const perfRecorderRef = useRef(null);
  const perfChunksRef = useRef([]);
  const rafRef = useRef(null);

  const timedLines = parseLyrics(lyrics, lyricsFormat, bpm);
  const activeIdx = activeLineIndex(timedLines, currentTime);

  const refresh = useCallback(async () => {
    if (!project) return;
    const st = await api(`/api/music/status?path=${encodeURIComponent(project)}`);
    if (st.lyrics) setLyrics(st.lyrics);
    if (st.lyricsFormat) setLyricsFormat(st.lyricsFormat);
    if (st.bpm) setBpm(st.bpm);
    setHasVocal(Boolean(st.status?.vocalRecorded));
  }, [project]);

  useEffect(() => {
    refresh().catch(() => {});
  }, [refresh]);

  useEffect(() => {
    const tick = () => {
      const t = instRef.current?.currentTime ?? audioRef.current?.currentTime ?? 0;
      setCurrentTime(t);
      rafRef.current = requestAnimationFrame(tick);
    };
    rafRef.current = requestAnimationFrame(tick);
    return () => {
      if (rafRef.current) cancelAnimationFrame(rafRef.current);
    };
  }, []);

  const saveLyrics = async () => {
    setErr("");
    setBusy(true);
    try {
      await api("/api/music/lyrics/save", {
        method: "POST",
        body: JSON.stringify({ project, lyrics, format: lyricsFormat, bpm }),
      });
    } catch (e) {
      setErr(String(e.message || e));
    } finally {
      setBusy(false);
    }
  };

  const startWebcam = async () => {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
      webcamStreamRef.current = stream;
      if (webcamRef.current) webcamRef.current.srcObject = stream;
      setWebcamOn(true);
    } catch (e) {
      setErr(`Webcam: ${e.message || e}`);
    }
  };

  const stopWebcam = () => {
    webcamStreamRef.current?.getTracks().forEach((t) => t.stop());
    webcamStreamRef.current = null;
    setWebcamOn(false);
  };

  const startRecording = async () => {
    setErr("");
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      chunksRef.current = [];
      const mr = new MediaRecorder(stream, { mimeType: MediaRecorder.isTypeSupported("audio/webm") ? "audio/webm" : undefined });
      mr.ondataavailable = (e) => {
        if (e.data.size) chunksRef.current.push(e.data);
      };
      mr.start(200);
      mediaRecorderRef.current = mr;

      if (webcamOn && webcamStreamRef.current) {
        const perfStream = new MediaStream([
          ...webcamStreamRef.current.getVideoTracks(),
          ...stream.getAudioTracks(),
        ]);
        perfChunksRef.current = [];
        const pr = new MediaRecorder(perfStream, { mimeType: MediaRecorder.isTypeSupported("video/webm") ? "video/webm" : undefined });
        pr.ondataavailable = (e) => {
          if (e.data.size) perfChunksRef.current.push(e.data);
        };
        pr.start(200);
        perfRecorderRef.current = pr;
      }

      instRef.current?.play().catch(() => {});
      setRecording(true);
    } catch (e) {
      setErr(String(e.message || e));
    }
  };

  const stopRecording = async () => {
    setRecording(false);
    setBusy(true);
    try {
      const mr = mediaRecorderRef.current;
      if (!mr) return;
      await new Promise((resolve) => {
        mr.onstop = resolve;
        mr.stop();
        mr.stream.getTracks().forEach((t) => t.stop());
      });

      const blob = new Blob(chunksRef.current, { type: "audio/webm" });
      const form = new FormData();
      form.append("project", project);
      form.append("vocal", blob, "vocal_raw.webm");

      if (perfRecorderRef.current) {
        await new Promise((resolve) => {
          perfRecorderRef.current.onstop = resolve;
          perfRecorderRef.current.stop();
        });
        const perfBlob = new Blob(perfChunksRef.current, { type: "video/webm" });
        form.append("performance", perfBlob, "performance.webm");
      }

      const res = await fetch("/api/music/record/upload", { method: "POST", body: form });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.error || res.statusText);
      setHasVocal(true);
      await refresh();
    } catch (e) {
      setErr(String(e.message || e));
    } finally {
      setBusy(false);
      mediaRecorderRef.current = null;
      perfRecorderRef.current = null;
    }
  };

  const instUrl = project
    ? `/api/music/file?path=${encodeURIComponent(project)}&name=instrumental.mp3`
    : null;

  return (
    <div className="music-flow">
      <header className="music-header">
        <button type="button" className="secondary" onClick={onBack} disabled={busy || recording}>
          ← Setup
        </button>
        <div>
          <h1>Record vocals</h1>
          <p className="muted">Karaoke lyrics · mic record · optional webcam performance</p>
        </div>
      </header>

      <div className="music-record-grid">
        <div className="music-card card">
          <label>
            Lyrics <span className="muted">(paste LRC or one line per row)</span>
            <textarea
              className="script-editor karaoke-input"
              value={lyrics}
              onChange={(e) => setLyrics(e.target.value)}
              placeholder={"[00:12.00]First line\n[00:18.50]Second line\n\nor plain lines…"}
              disabled={recording}
            />
          </label>
          <div className="music-row">
            <label>
              Format
              <select value={lyricsFormat} onChange={(e) => setLyricsFormat(e.target.value)} disabled={recording}>
                <option value="auto">Auto</option>
                <option value="lrc">LRC</option>
                <option value="lines">Lines + BPM</option>
              </select>
            </label>
            <label>
              BPM
              <input
                type="number"
                value={bpm}
                onChange={(e) => setBpm(parseInt(e.target.value, 10) || 140)}
                disabled={recording}
              />
            </label>
            <button type="button" className="secondary" onClick={saveLyrics} disabled={busy || recording}>
              Save lyrics
            </button>
          </div>

          {instUrl ? (
            <audio ref={instRef} src={instUrl} controls className="music-inst-audio" />
          ) : null}

          <div className="karaoke-display" aria-live="polite">
            {timedLines.length ? (
              timedLines.map((line, i) => (
                <p key={`${line.time}-${i}`} className={`karaoke-line${i === activeIdx ? " active" : ""}`}>
                  {line.text}
                </p>
              ))
            ) : (
              <p className="muted">Add lyrics above — they scroll in sync during playback.</p>
            )}
          </div>
        </div>

        <div className="music-card card">
          <h3 className="section-title">Mic &amp; performance</h3>
          {webcamOn ? (
            <video ref={webcamRef} autoPlay muted playsInline className="webcam-preview" />
          ) : null}
          <div className="gate-row">
            {!webcamOn ? (
              <button type="button" className="secondary" onClick={startWebcam} disabled={busy || recording}>
                Enable webcam
              </button>
            ) : (
              <button type="button" className="secondary" onClick={stopWebcam} disabled={recording}>
                Disable webcam
              </button>
            )}
            {!recording ? (
              <button type="button" onClick={startRecording} disabled={busy}>
                ● Record take
              </button>
            ) : (
              <button type="button" className="record-stop" onClick={stopRecording}>
                ■ Stop &amp; upload
              </button>
            )}
          </div>
          {hasVocal ? (
            <p className="ok">✓ Vocal take saved</p>
          ) : (
            <p className="muted">Record over the instrumental — raw take uploads automatically.</p>
          )}
          {hasVocal ? (
            <audio
              controls
              src={`/api/music/file?path=${encodeURIComponent(project)}&name=vocal_dry.wav`}
            />
          ) : null}
          <button
            type="button"
            className="landing-cta"
            style={{ marginTop: "1rem" }}
            onClick={onNext}
            disabled={!hasVocal || recording}
          >
            Continue to produce →
          </button>
          {err ? <p className="err">{err}</p> : null}
        </div>
      </div>
    </div>
  );
}
