import { useCallback, useEffect, useState } from "react";
import { api, hubFromUrl } from "./plannerApi.js";

export default function PipelineBar({ activeTab, onSelectTab }) {
  const [pipeline, setPipeline] = useState(null);
  const [apply, setApply] = useState(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");

  const refresh = useCallback(() => {
    if (!hubFromUrl()) return;
    api("/api/wizard/pipeline/status")
      .then((d) => setPipeline(d.pipeline))
      .catch(() => setPipeline(null));
    api("/api/wizard/pipeline/apply/status")
      .then((d) => setApply(d.apply))
      .catch(() => setApply(null));
  }, []);

  useEffect(() => {
    refresh();
    const id = setInterval(refresh, 2000);
    return () => clearInterval(id);
  }, [refresh, activeTab]);

  useEffect(() => {
    if (!pipeline?.active) return undefined;
    let cancelled = false;
    const tick = async () => {
      if (cancelled) return;
      try {
        const d = await api("/api/wizard/pipeline/step", { method: "POST", body: "{}" });
        if (cancelled) return;
        if (d?.status) setPipeline(d.status);
        if (d?.apply) setApply(d.apply);
        const focus = d?.focusTab || d?.tabId || d?.status?.focusTab || d?.status?.currentTab;
        if (focus && onSelectTab) onSelectTab(focus);
        if (d?.pipelineDone || d?.status?.status === "done") {
          setPipeline((p) => ({ ...p, active: false, status: "done" }));
          refresh();
        }
        if (d?.error) setErr(d.error);
        const wait = d?.waiting || d?.waitingApply;
        if (!cancelled && pipeline?.active && !wait) {
          setTimeout(tick, 800);
        } else if (!cancelled && wait) {
          setTimeout(tick, 400);
        }
      } catch (e) {
        if (!cancelled) setErr(String(e));
      }
    };
    tick();
    return () => {
      cancelled = true;
    };
  }, [pipeline?.active, onSelectTab, refresh]);

  const start = async () => {
    setErr("");
    setBusy(true);
    try {
      const d = await api("/api/wizard/pipeline/start", {
        method: "POST",
        body: JSON.stringify({ autoApprove: true }),
      });
      setPipeline(d.pipeline);
      onSelectTab?.("terrain");
    } catch (e) {
      setErr(String(e));
    } finally {
      setBusy(false);
    }
  };

  const cancel = async () => {
    setBusy(true);
    try {
      const d = await api("/api/wizard/pipeline/cancel", { method: "POST", body: "{}" });
      setPipeline(d.pipeline);
    } finally {
      setBusy(false);
    }
  };

  const running = pipeline?.active;
  const focus = pipeline?.focusTab || pipeline?.currentTab;
  const applyRunning = apply?.active;
  const applyTab = apply?.tabId;

  return (
    <div className={`pipeline-bar card${running || applyRunning ? " pipeline-bar-active" : ""}`}>
      <div className="pipeline-bar-row">
        <strong>Full pipeline (tabs 1–6)</strong>
        {running ? (
          <>
            <span className="pipeline-status">
              Planning <strong>{focus || "…"}</strong>
              {" · "}
              done: {(pipeline.completedTabs || []).join(", ") || "none"}
            </span>
            <button type="button" className="ghost" disabled={busy} onClick={cancel}>
              Cancel pipeline
            </button>
          </>
        ) : (
          <button type="button" className="primary" disabled={busy || !hubFromUrl()} onClick={start}>
            Run full pipeline (auto)
          </button>
        )}
      </div>
      {applyRunning && (
        <p className="hint pipeline-apply-status">
          Unity applying <strong>{applyTab || "…"}</strong>
          {apply.phase ? ` (${apply.phase})` : ""}
          {apply.queueLength > 0 ? ` · ${apply.queueLength} queued` : ""}
        </p>
      )}
      {!applyRunning && (apply?.completedTabs || []).length > 0 && (
        <p className="hint pipeline-apply-status">
          Applied in Unity: {(apply.completedTabs || []).join(", ")}
        </p>
      )}
      {apply?.error && <p className="err">Unity apply failed: {apply.error}</p>}
      {err && <p className="err">{err}</p>}
      <p className="hint">
        Each tab auto-applies in Unity and saves the scene when you approve (manual or pipeline). Full pipeline
        waits for apply before the next tab. Keep Unity open. Video tab is manual.
      </p>
    </div>
  );
}
