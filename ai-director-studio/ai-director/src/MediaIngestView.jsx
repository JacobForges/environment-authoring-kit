import { useCallback, useEffect, useState } from "react";

async function api(path, opts = {}) {
  const res = await fetch(path, {
    ...opts,
    headers: { ...(opts.headers || {}) },
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || res.statusText);
  return data;
}

export default function MediaIngestView({ capture, onProjectChange, onIngested }) {
  const [projects, setProjects] = useState([]);
  const [name, setName] = useState("");
  const [dragOver, setDragOver] = useState(false);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState("");
  const [err, setErr] = useState("");

  const refreshProjects = useCallback(async () => {
    const data = await api("/api/project/list");
    setProjects(data.projects || []);
  }, []);

  useEffect(() => {
    refreshProjects().catch(() => {});
  }, [refreshProjects]);

  const createProject = async () => {
    setErr("");
    setBusy(true);
    try {
      const data = await api("/api/project/create", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name: name || "project" }),
      });
      setStatus(`Created ${data.name}`);
      onProjectChange?.(data.project);
      await refreshProjects();
    } catch (e) {
      setErr(String(e.message || e));
    } finally {
      setBusy(false);
    }
  };

  const uploadFiles = async (fileList) => {
    if (!capture) {
      setErr("Create or select a project first");
      return;
    }
    const files = Array.from(fileList || []);
    if (!files.length) return;

    setErr("");
    setBusy(true);
    setStatus(`Uploading ${files.length} file(s)…`);
    try {
      const form = new FormData();
      form.append("project", capture);
      form.append("title", name || "");
      for (const file of files) {
        form.append("files", file);
      }
      const res = await fetch("/api/project/ingest", { method: "POST", body: form });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.error || res.statusText);
      setStatus(`Ingested ${data.frameCount ?? 0} frames into timelapse/`);
      onIngested?.(data);
      await refreshProjects();
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

  return (
    <div className="media-ingest">
      <h2>Media ingest</h2>
      <p className="muted">
        Drop video or images to build <code>timelapse/tl_*.png</code> frames and a starter timeline.
      </p>

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
            Open project
            <select
              value={capture || ""}
              onChange={(e) => onProjectChange?.(e.target.value)}
              disabled={busy}
            >
              <option value="">— select —</option>
              {projects.map((p) => (
                <option key={p.path} value={p.path}>
                  {p.name} ({p.frames} frames)
                </option>
              ))}
            </select>
          </label>
        </div>
      )}

      <div
        className={`drop-zone${dragOver ? " drag-over" : ""}`}
        onDragOver={(e) => {
          e.preventDefault();
          setDragOver(true);
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={onDrop}
      >
        <p>Drag & drop video (.mp4, .mov) or images here</p>
        <label className="file-btn">
          Choose files
          <input
            type="file"
            multiple
            accept="video/*,image/*"
            disabled={busy || !capture}
            onChange={(e) => uploadFiles(e.target.files)}
          />
        </label>
      </div>

      {capture && <p className="muted">Active project: <code>{capture}</code></p>}
      {status && <p className="ok">{status}</p>}
      {err && <p className="err">{err}</p>}
    </div>
  );
}
