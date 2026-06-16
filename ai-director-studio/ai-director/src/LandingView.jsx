import { useCallback, useEffect, useState } from "react";

async function api(path, opts = {}) {
  const res = await fetch(path, {
    ...opts,
    headers: { ...(opts.headers || {}), ...(opts.body instanceof FormData ? {} : { "Content-Type": "application/json" }) },
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || res.statusText);
  return data;
}

function formatDuration(sec) {
  if (sec == null || Number.isNaN(sec)) return null;
  const s = Math.round(Number(sec));
  if (s < 60) return `${s}s`;
  const m = Math.floor(s / 60);
  const r = s % 60;
  return r ? `${m}m ${r}s` : `${m}m`;
}

function VideoLanding({ capture, onProjectChange, onContinue, initialStatus }) {
  const [projects, setProjects] = useState([]);
  const [name, setName] = useState("");
  const [dragOver, setDragOver] = useState(false);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState(null);
  const [err, setErr] = useState("");
  const [projectStatus, setProjectStatus] = useState(initialStatus || null);

  const refreshProjects = useCallback(async () => {
    const data = await api("/api/project/list");
    setProjects(data.projects || []);
  }, []);

  const refreshStatus = useCallback(async (path) => {
    if (!path) {
      setProjectStatus(null);
      return;
    }
    const data = await api(`/api/project/status?path=${encodeURIComponent(path)}`);
    setProjectStatus(data);
  }, []);

  useEffect(() => {
    refreshProjects().catch(() => {});
  }, [refreshProjects]);

  useEffect(() => {
    refreshStatus(capture).catch(() => {});
  }, [capture, refreshStatus]);

  const ensureProject = async () => {
    if (capture) return capture;
    const data = await api("/api/project/create", {
      method: "POST",
      body: JSON.stringify({ name: name || "project" }),
    });
    onProjectChange?.(data.project);
    await refreshProjects();
    return data.project;
  };

  const createProject = async () => {
    setErr("");
    setBusy(true);
    try {
      const path = await ensureProject();
      setStatus(`Created project — drop media or choose files to continue.`);
      await refreshStatus(path);
    } catch (e) {
      setErr(String(e.message || e));
    } finally {
      setBusy(false);
    }
  };

  const uploadFiles = async (fileList) => {
    const files = Array.from(fileList || []).filter(Boolean);
    if (!files.length) return;

    setErr("");
    setBusy(true);
    setStatus(`Uploading ${files.length} file(s)…`);
    try {
      let projectPath = capture;
      if (!projectPath) {
        projectPath = await ensureProject();
      }
      const form = new FormData();
      form.append("project", projectPath);
      form.append("title", name || "");
      for (const file of files) {
        form.append("files", file);
      }
      const res = await fetch("/api/project/ingest", { method: "POST", body: form });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.error || res.statusText);
      onProjectChange?.(projectPath);
      setStatus(
        `Ingested ${data.frameCount ?? 0} frames` +
          (data.hasVideo ? " from video" : "") +
          (data.hasImages ? (data.hasVideo ? " + images" : " from images") : "") +
          "."
      );
      await refreshProjects();
      await refreshStatus(projectPath);
    } catch (e) {
      setErr(String(e.message || e));
    } finally {
      setBusy(false);
    }
  };

  const onDrop = (e) => {
    e.preventDefault();
    setDragOver(false);
    uploadFiles(e.dataTransfer?.files);
  };

  const mediaReady = Boolean(projectStatus?.mediaIngested && projectStatus?.frameCount > 0);
  const dur = formatDuration(projectStatus?.durationSec);

  const handleContinue = async () => {
    if (!capture || !mediaReady) return;
    setErr("");
    setBusy(true);
    try {
      const sess = await api("/api/director/session/start", {
        method: "POST",
        body: JSON.stringify({ capture }),
      });
      onContinue?.(sess);
    } catch (e) {
      setErr(String(e.message || e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <div className="ingest-row">
        <label>
          Project name
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="my-recap"
            disabled={busy}
          />
        </label>
        <button type="button" onClick={createProject} disabled={busy}>
          New project
        </button>
      </div>

      {projects.length > 0 && (
        <div className="ingest-row">
          <label>
            Open existing
            <select
              value={capture || ""}
              onChange={(e) => onProjectChange?.(e.target.value)}
              disabled={busy}
            >
              <option value="">— select —</option>
              {projects.map((p) => (
                <option key={p.path} value={p.path}>
                  {p.name} ({p.frames} frames{p.mediaIngested ? " ✓" : ""})
                </option>
              ))}
            </select>
          </label>
        </div>
      )}

      <div
        className={`drop-zone landing-drop${dragOver ? " drag-over" : ""}`}
        onDragOver={(e) => {
          e.preventDefault();
          setDragOver(true);
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={onDrop}
      >
        <div className="drop-icon" aria-hidden="true">
          ⬆
        </div>
        <p className="drop-title">Drag & drop video or images</p>
        <p className="muted drop-hint">.mp4, .mov, .png, .jpg, .webp — or pick files below</p>
        <label className="file-btn">
          Choose files
          <input
            type="file"
            multiple
            accept="video/mp4,video/quicktime,video/*,image/*"
            disabled={busy}
            onChange={(e) => uploadFiles(e.target.files)}
          />
        </label>
      </div>

      {mediaReady && (
        <div className="landing-summary">
          <h3>Footage ready</h3>
          <ul className="landing-stats">
            <li>
              <strong>{projectStatus.frameCount}</strong> frames extracted
            </li>
            {projectStatus.hasVideo ? <li>Includes video</li> : null}
            {projectStatus.hasImages ? <li>Includes images</li> : null}
            {dur ? (
              <li>
                ~<strong>{dur}</strong> source duration
              </li>
            ) : null}
          </ul>
          <button type="button" className="landing-cta" onClick={handleContinue} disabled={busy}>
            Continue to Director →
          </button>
        </div>
      )}

      {capture && !mediaReady ? (
        <p className="muted landing-hint">
          Active project: <code>{capture}</code> — upload media to continue.
        </p>
      ) : null}
      {status && !err ? <p className="ok">{status}</p> : null}
      {err ? <p className="err">{err}</p> : null}
    </>
  );
}

export default function LandingView({ capture, onProjectChange, onContinue, initialStatus, onSelectMusic }) {
  const [pathChoice, setPathChoice] = useState(null);

  if (pathChoice === "video") {
    return (
      <div className="landing-page">
        <div className="landing-hero">
          <div className="landing-brand">
            <span className="landing-logo">AI Director</span>
            <h1>Start with your footage</h1>
            <p className="muted landing-lead">
              Upload video or stills first. The Director uses your media to ask smarter pre-production
              questions before compose.
            </p>
          </div>
          <button type="button" className="secondary path-back" onClick={() => setPathChoice(null)}>
            ← Choose different path
          </button>
          <div className="landing-card card">
            <VideoLanding
              capture={capture}
              onProjectChange={onProjectChange}
              onContinue={onContinue}
              initialStatus={initialStatus}
            />
          </div>
        </div>
        <footer className="landing-footer muted">Flow: upload → ingest → Director Q&amp;A → Studio Editor</footer>
      </div>
    );
  }

  return (
    <div className="landing-page">
      <div className="landing-hero landing-hero-wide">
        <div className="landing-brand">
          <span className="landing-logo">AI Director Studio</span>
          <h1>What are you making?</h1>
          <p className="muted landing-lead">
            Record a polished track with AI instrumentals and karaoke, or direct a video from your footage.
          </p>
        </div>

        <div className="path-cards">
          <button
            type="button"
            className="path-card card music-card"
            onClick={() => {
              onSelectMusic?.();
            }}
          >
            <span className="path-icon" aria-hidden="true">
              🎤
            </span>
            <h2>Music</h2>
            <p className="muted">
              Pop/trap vocal polish — chat with the Music Director first, then instrumental, karaoke, and mix.
            </p>
            <span className="path-cta">Music Director Q&amp;A →</span>
          </button>

          <button type="button" className="path-card card video-card" onClick={() => setPathChoice("video")}>
            <span className="path-icon" aria-hidden="true">
              🎬
            </span>
            <h2>Video</h2>
            <p className="muted">
              Upload footage or stills, brief with the Director, compose narrated recap or reel.
            </p>
            <span className="path-cta">Upload footage →</span>
          </button>
        </div>
      </div>

      <footer className="landing-footer muted">
        Music: Director Q&amp;A → instrumental → record → produce · Video: upload → Director → Editor
      </footer>
    </div>
  );
}
