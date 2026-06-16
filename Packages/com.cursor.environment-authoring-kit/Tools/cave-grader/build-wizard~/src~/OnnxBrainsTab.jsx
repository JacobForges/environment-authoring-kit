import { useEffect, useMemo, useState } from "react";
import { api, hubFromUrl } from "./plannerApi.js";

const TAB_ORDER = [
  "terrain",
  "surface-content",
  "caves",
  "mazes",
  "interior-content",
  "atmosphere",
  "music",
  "video",
];

export default function OnnxBrainsTab() {
  const hubMissing = !hubFromUrl();
  const [config, setConfig] = useState(null);
  const [models, setModels] = useState([]);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");
  const [note, setNote] = useState("");
  const [diag, setDiag] = useState(null);

  const load = async () => {
    if (hubMissing) return;
    setErr("");
    try {
      const d = await api("/api/onnx/config");
      setConfig(d.config || null);
      setModels(d.models || []);
      setDiag(d.diagnostics || null);
    } catch (e) {
      setErr(String(e));
    }
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const setGlobal = (patch) =>
    setConfig((prev) => ({
      ...(prev || {}),
      global: { ...(prev?.global || {}), ...patch },
    }));

  const setTab = (tabId, patch) =>
    setConfig((prev) => ({
      ...(prev || {}),
      tabs: {
        ...(prev?.tabs || {}),
        [tabId]: { ...(prev?.tabs?.[tabId] || {}), ...patch },
      },
    }));

  const save = async () => {
    if (!config) return;
    setBusy(true);
    setErr("");
    setNote("");
    try {
      const d = await api("/api/onnx/config", {
        method: "POST",
        body: JSON.stringify({ config }),
      });
      setConfig(d.config || config);
      setModels(d.models || models);
      setDiag(d.diagnostics || null);
      setNote("Saved ONNX brain config.");
    } catch (e) {
      setErr(String(e));
    } finally {
      setBusy(false);
    }
  };

  const tabRows = useMemo(() => {
    const tabs = config?.tabs || {};
    return TAB_ORDER.map((tabId) => ({
      id: tabId,
      ...(tabs[tabId] || { id: tabId, enabled: true, threshold: null, modelPath: "" }),
    }));
  }, [config]);

  const applyRecommendedDefaults = () => {
    if (!config) return;
    setConfig((prev) => {
      const next = { ...(prev || {}), tabs: { ...(prev?.tabs || {}) } };
      TAB_ORDER.forEach((tabId) => {
        const t = { ...(next.tabs[tabId] || { id: tabId, enabled: true, threshold: null, modelPath: "" }) };
        t.enabled = true;
        const model = `${tabId}_brain.tabbrain`;
        if (model) t.modelPath = model;
        if (tabId === "music" && (t.threshold == null || t.threshold === "")) t.threshold = 0.62;
        next.tabs[tabId] = t;
      });
      return next;
    });
    setNote("Applied recommended defaults. Click Save ONNX settings.");
  };

  const diagByTab = useMemo(() => {
    const rows = diag?.tabs || [];
    const out = {};
    rows.forEach((r) => { out[r.tabId] = r; });
    return out;
  }, [diag]);

  return (
    <div className="wrap planner-pane planner-pane-onnx">
      <header className="planner-hero">
        <p className="planner-kicker">Environment Kit · ONNX brains</p>
        <h1>ONNX Brain Controls</h1>
      </header>

      {err && <p className="err">{err}</p>}
      {note && <p className="status">{note}</p>}

      {hubMissing && (
        <div className="card">
          <p className="err">Missing `?hub=` query in URL — reopen from Unity Hub window.</p>
        </div>
      )}

      {!hubMissing && config && (
        <>
          <div className="card">
            <h2 className="section">Global ONNX</h2>
            {diag && (
              <p className="hint">
                Active counter: {diag.enabledTabs}/{diag.totalTabs} tabs enabled · {diag.tabsWithModelFile}/{diag.totalTabs} tabs with model files found.
              </p>
            )}
            <label className="row check">
              <input
                type="checkbox"
                checked={Boolean(config.global?.enabled)}
                onChange={(e) => setGlobal({ enabled: e.target.checked })}
              />
              <span>Enable ONNX assist globally</span>
            </label>
            <div className="row">
              <label>Global threshold (0.00 - 1.00)</label>
              <input
                type="number"
                min="0"
                max="1"
                step="0.01"
                value={config.global?.threshold ?? 0.7}
                onChange={(e) => setGlobal({ threshold: Number.parseFloat(e.target.value || "0.7") })}
              />
            </div>
            <div className="actions">
              <button type="button" className="ghost" disabled={busy} onClick={applyRecommendedDefaults}>
                Apply recommended defaults
              </button>
            </div>
          </div>

          <div className="card">
            <h2 className="section">Per-tab brain controls</h2>
            <p className="hint">Set tab-level enable, threshold override, and optional ONNX model override file name/path.</p>
            <div className="onnx-grid">
              {tabRows.map((t) => (
                <div key={t.id} className="onnx-row">
                  <div className="onnx-row-head">
                    <strong>{t.id}</strong>
                    {diagByTab[t.id] && (
                      <p className="hint">
                        model: {diagByTab[t.id].modelExists ? "found" : "missing"}
                        {typeof diagByTab[t.id].testAccuracy === "number"
                          ? ` · test acc ${diagByTab[t.id].testAccuracy.toFixed(3)}`
                          : ""}
                      </p>
                    )}
                  </div>
                  <label className="row check">
                    <input
                      type="checkbox"
                      checked={Boolean(t.enabled)}
                      onChange={(e) => setTab(t.id, { enabled: e.target.checked })}
                    />
                    <span>Enable ONNX for this tab</span>
                  </label>
                  <div className="row">
                    <label>Tab threshold (blank = global)</label>
                    <input
                      type="number"
                      min="0"
                      max="1"
                      step="0.01"
                      value={t.threshold ?? ""}
                      onChange={(e) =>
                        setTab(t.id, {
                          threshold: e.target.value === "" ? null : Number.parseFloat(e.target.value),
                        })
                      }
                      placeholder="global"
                    />
                  </div>
                  <div className="row">
                    <label>Model override path (blank = manifest)</label>
                    <input
                      list={`onnx-models-${t.id}`}
                      value={t.modelPath || ""}
                      onChange={(e) => setTab(t.id, { modelPath: e.target.value })}
                      placeholder="surface-content_brain.tabbrain"
                    />
                    <datalist id={`onnx-models-${t.id}`}>
                      {models.map((m) => (
                        <option key={m} value={m} />
                      ))}
                    </datalist>
                  </div>
                </div>
              ))}
            </div>
            <div className="actions">
              <button type="button" className="primary" disabled={busy} onClick={save}>
                Save ONNX settings
              </button>
              <button type="button" className="ghost" disabled={busy} onClick={load}>
                Reload
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

