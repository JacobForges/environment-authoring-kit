import { useCallback, useEffect, useState } from "react";

const PACING_KEYS = [
  "milestoneHoldSec",
  "subbeatHoldSec",
  "introSec",
  "outroSec",
  "timelapseSecPerFrame",
  "captionReadPauseSec",
  "segmentXfadeSec",
];

const APPROVED_CARD_KEYS = [
  { key: "ApprovedOutro.png", label: "Outro (ApprovedOutro.png)" },
  { key: "_approval_outro_b.png", label: "Outro canonical (_approval_outro_b.png)" },
  { key: "ApprovedIntro.png", label: "Intro (ApprovedIntro.png)" },
  { key: "_approval_intro_c.png", label: "Intro canonical (_approval_intro_c.png)" },
  { key: "_approval_portrait.png", label: "Portrait (_approval_portrait.png)" },
];

const SETTING_FIELDS = [
  {
    key: "cursorFullNarration",
    label: "AI full-script narration",
    help: "Generate full Personal Voice script from captions (cursorFullNarration).",
    type: "checkbox",
  },
  {
    key: "fullNarrationFromCaptions",
    label: "Narration from milestone captions",
    help: "Build narration text from timeline captions.",
    type: "checkbox",
  },
  {
    key: "cursorVision",
    label: "AI vision callouts (Cursor director)",
    help: "Use Cursor API to place green annotation boxes on milestones.",
    type: "checkbox",
  },
  {
    key: "videoEnhance",
    label: "Video enhance (upscale + grade)",
    help: "Scene upscale and master-grade on final mux.",
    type: "checkbox",
  },
  {
    key: "narrationEmphasisCapture",
    label: "Emphasis moments in narration",
    help: "Apply configured emphasis phrases during Personal Voice.",
    type: "checkbox",
  },
  {
    key: "showAnnotations",
    label: "On-screen annotations",
    help: "Draw OpenCV / director boxes on holds.",
    type: "checkbox",
  },
  {
    key: "narrationMode",
    label: "Narration mode",
    type: "select",
    options: ["fullScript", "captionsOnly"],
  },
  {
    key: "annotationSource",
    label: "Annotation source",
    type: "select",
    options: ["opencv", "cursor", "fixed"],
  },
];

function captureFromUrl() {
  const params = new URLSearchParams(window.location.search);
  return params.get("capture") || "";
}

async function api(path, opts) {
  const res = await fetch(path, opts);
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || res.statusText);
  return data;
}

export default function App() {
  const [capture, setCapture] = useState(captureFromUrl());
  const [status, setStatus] = useState(null);
  const [timeline, setTimeline] = useState(null);
  const [gate, setGate] = useState(null);
  const [approvedAssets, setApprovedAssets] = useState(null);
  const [settings, setSettings] = useState(null);
  const [agent, setAgent] = useState(null);
  const [composeLive, setComposeLive] = useState("");
  const [narrationReview, setNarrationReview] = useState(null);
  const [showSpokenScript, setShowSpokenScript] = useState(false);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const refresh = useCallback(async () => {
    setError("");
    try {
      const latest = await api("/api/capture/latest");
      const cap = capture || latest.capture || "";
      if (cap && cap !== capture) setCapture(cap);
      if (!cap) return;
      const [st, tl, composeFeed, agentSt, gateSt, approvedSt, settingsSt] = await Promise.all([
        api(`/api/capture/status?path=${encodeURIComponent(cap)}`),
        api(`/api/capture/timeline?path=${encodeURIComponent(cap)}`),
        api(`/api/capture/compose/live?path=${encodeURIComponent(cap)}`),
        api("/api/agent/status"),
        api(`/api/capture/compose/gate?path=${encodeURIComponent(cap)}`),
        api("/api/approved/assets"),
        api("/api/approved/settings"),
      ]);
      setStatus(st);
      setTimeline(tl);
      setComposeLive(composeFeed.markdown || "");
      setAgent(agentSt);
      setGate(gateSt);
      setApprovedAssets(approvedSt);
      setSettings(settingsSt.settings || {});
      if (st?.silentVideo || st?.narrationReview?.ready || st?.narrationReview?.generating) {
        try {
          const review = await api(`/api/capture/narration/review?path=${encodeURIComponent(cap)}`);
          setNarrationReview(review);
        } catch {
          setNarrationReview(st?.narrationReview || null);
        }
      } else {
        setNarrationReview(st?.narrationReview || null);
      }
    } catch (e) {
      setError(String(e.message || e));
    }
  }, [capture]);

  useEffect(() => {
    refresh();
    const generating = narrationReview?.generating || status?.narrationReview?.generating;
    const ms = generating ? 1500 : status?.composeJob?.running ? 2000 : 5000;
    const id = setInterval(refresh, ms);
    return () => clearInterval(id);
  }, [refresh, status?.composeJob?.running, narrationReview?.generating, status?.narrationReview?.generating]);

  async function saveTimeline() {
    if (!capture || !timeline) return;
    setBusy(true);
    setError("");
    try {
      await api(`/api/capture/timeline?path=${encodeURIComponent(capture)}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(timeline),
      });
      await refresh();
    } catch (e) {
      setError(String(e.message || e));
    } finally {
      setBusy(false);
    }
  }

  async function saveSettings() {
    if (!settings) return;
    setBusy(true);
    setError("");
    try {
      await api("/api/approved/settings", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ settings }),
      });
      await refresh();
    } catch (e) {
      setError(String(e.message || e));
    } finally {
      setBusy(false);
    }
  }

  function patchSetting(key, value) {
    setSettings((s) => ({ ...s, [key]: value }));
  }

  async function verifyApprovedAssets() {
    if (!capture) return;
    setBusy(true);
    setError("");
    try {
      await api(
        `/api/capture/compose/gate/verify?path=${encodeURIComponent(capture)}`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ capture }),
        },
      );
      await refresh();
    } catch (e) {
      setError(String(e.message || e));
    } finally {
      setBusy(false);
    }
  }

  async function proceedToCompose() {
    if (!capture) return;
    setBusy(true);
    setError("");
    try {
      if (settings) {
        await api("/api/approved/settings", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ settings }),
        });
      }
      const result = await api(
        `/api/capture/compose/gate/proceed?path=${encodeURIComponent(capture)}`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ capture }),
        },
      );
      if (result.error) throw new Error(result.error);
      await refresh();
    } catch (e) {
      setError(String(e.message || e));
    } finally {
      setBusy(false);
    }
  }

  async function regenerateNarrationScript(useCursor = false) {
    if (!capture) return;
    setBusy(true);
    setError("");
    try {
      const result = await api(
        `/api/capture/narration/review/regenerate?path=${encodeURIComponent(capture)}`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ capture, useCursor }),
        },
      );
      if (result.error) throw new Error(result.error);
      await refresh();
    } catch (e) {
      setError(String(e.message || e));
    } finally {
      setBusy(false);
    }
  }

  async function approveNarrationScript(startTerminal = true) {
    if (!capture) return;
    setBusy(true);
    setError("");
    try {
      const result = await api(
        `/api/capture/narration/review/approve?path=${encodeURIComponent(capture)}`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ capture, startPersonalVoice: startTerminal }),
        },
      );
      if (result.error) throw new Error(result.error);
      await refresh();
    } catch (e) {
      setError(String(e.message || e));
    } finally {
      setBusy(false);
    }
  }

  async function retryCompose() {
    if (!capture) return;
    setBusy(true);
    setError("");
    try {
      const result = await api(
        `/api/capture/compose/start?path=${encodeURIComponent(capture)}`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ capture }),
        },
      );
      if (result.error) throw new Error(result.error);
      await refresh();
    } catch (e) {
      setError(String(e.message || e));
    } finally {
      setBusy(false);
    }
  }

  function patchPacing(key, value) {
    setTimeline((tl) => ({ ...tl, [key]: value }));
  }

  const gateWaiting = Boolean(gate?.waiting && !gate?.proceed);
  const assetsVerified = Boolean(gate?.assetsVerified);
  const gateProceeded = Boolean(gate?.proceed);
  const composeAllowed = assetsVerified && !gateProceeded;
  const personalVoicePending = Boolean(status?.personalVoicePending);
  const presentationReady = Boolean(status?.presentationMp4 && status?.presentationHasAudio);
  const narrationReviewState = narrationReview || status?.narrationReview || {};
  const showNarrationReview =
    Boolean(status?.silentVideo || narrationReviewState.ready) && !presentationReady;
  const narrationGenerating = Boolean(narrationReviewState.generating);
  const narrationApproved = Boolean(narrationReviewState.approved);
  const narrationNeedsReview = Boolean(narrationReviewState.needsReview || (showNarrationReview && narrationReviewState.ready && !narrationApproved));
  const job = status?.composeJob;
  const composeRunning = Boolean(job?.running);
  const composePhase = job?.phase || (job?.terminalNarration ? "narration" : composeRunning ? "video" : "idle");

  let composeBadge = "Compose idle";
  let composeBadgeClass = "";
  if (composeRunning) {
    composeBadge = composePhase === "narration" ? "Narration starting…" : "Building silent video…";
    composeBadgeClass = "connected";
  } else if (personalVoicePending) {
    composeBadge = "Personal Voice pending";
    composeBadgeClass = "warn";
  } else if (presentationReady) {
    composeBadge = "Recap ready (with audio)";
    composeBadgeClass = "connected";
  } else if (job?.exitCode != null && job.exitCode !== 0) {
    composeBadge = "Compose failed";
    composeBadgeClass = "failed";
  }

  let gateStatus = "Loading gate…";
  let gateAction = "Refresh or wait for API.";
  if (gate) {
    if (gateProceeded) {
      if (job?.running) {
        gateStatus = "Compose running — building video from timelapse PNGs…";
        gateAction = "Headless Personal Voice runs after you approve Step 4.";
      } else if (personalVoicePending) {
        gateStatus = narrationNeedsReview
          ? "Silent video ready — review narration script in Step 4."
          : narrationApproved
            ? "Script approved — Personal Voice recording in background."
            : "Silent video ready — approve Step 4 to start headless Personal Voice.";
        gateAction = narrationNeedsReview
          ? "Read NarrationVoiceGuide below, then Approve or Regenerate."
          : 'Click "Approve & apply Personal Voice" in Step 4.';
      } else if (presentationReady) {
        gateStatus = "Done — DemoRecapPresentation.mp4 includes your Personal Voice.";
        gateAction = "Open the capture folder or check your video player.";
      } else if (status?.presentationMp4) {
        gateStatus = "Video file exists but has no narration audio.";
        gateAction = 'Click "Approve & apply Personal Voice" in Step 4 to finish the recap.';
      } else if (job?.exitCode != null && job.exitCode !== 0) {
        gateStatus = "Compose failed — see log below.";
        gateAction = 'Click "Retry compose" or check ~/Library/EnvironmentKit/recap-dashboard-server.log';
      } else {
        gateStatus = "Proceed clicked — compose starting (or waiting for Python).";
        gateAction = "If nothing happens in 30s, click Retry compose.";
      }
    } else if (gateWaiting) {
      gateStatus = assetsVerified
        ? "Ready — click Proceed to start compose."
        : "Waiting — verify your approved cards first (Step 1).";
      gateAction = assetsVerified ? "Click Proceed to compose below." : "Click verify in Step 1.";
    } else {
      gateStatus = "Gate not armed for this capture.";
      gateAction =
        "Run a Unity build (recording ends) or: bash start-recap-dashboard.sh <capture>";
    }
  }

  const cardsToShow = APPROVED_CARD_KEYS.filter(({ key }) => {
    if (key.startsWith("_approval_") && key !== "_approval_portrait.png") {
      return approvedAssets?.assets?.[key]?.exists;
    }
    return true;
  });

  return (
    <main>
      <header className="header-row">
        <h1>Recap Dashboard</h1>
        <span className={`agent-badge ${composeBadgeClass}`} title="Video compose worker (not Cursor IDE agent)">
          {composeBadge}
        </span>
      </header>

      <p className="muted">
        Proceed builds the silent video, generates a narration script for Step 4 review,
        then records Personal Voice headless in the background (no Terminal window).
      </p>

      {error ? <div className="panel panel-error">{error}</div> : null}

      <section className="panel panel-status-bar">
        <strong>Status</strong>
        <ul className="status-list muted">
          <li>
            <span className="status-label">API</span> {error ? "error" : "connected"}
          </li>
          <li>
            <span className="status-label">Capture</span>{" "}
            <code>{capture || "(none — add ?capture= to URL)"}</code>
          </li>
          <li>
            <span className="status-label">Gate</span> {gateStatus}
          </li>
          <li>
            <span className="status-label">Next</span> {gateAction}
          </li>
          {status?.presentationMp4 ? (
            <li>
              <span className="status-label">Video</span> DemoRecapPresentation.mp4 exists
            </li>
          ) : null}
        </ul>
      </section>

      <section className="panel panel-verify">
        <strong>Step 1 — Verify approved cards</strong>
        <p className="muted">
          These images are copied into every recap. Nothing generates until you confirm they are
          correct.
        </p>

        {approvedAssets ? (
          <div className="asset-grid">
            {cardsToShow.map(({ key, label }) => {
              const asset = approvedAssets.assets?.[key];
              return (
                <figure key={key} className="asset-card">
                  <figcaption>{label}</figcaption>
                  {asset?.exists && asset.url ? (
                    <img className="asset-thumb" src={asset.url} alt={label} />
                  ) : (
                    <div className="asset-missing">missing</div>
                  )}
                </figure>
              );
            })}
          </div>
        ) : (
          <p className="muted">Loading approved assets…</p>
        )}

        {assetsVerified ? (
          <p className="verified-ok">Cards verified.</p>
        ) : (
          <button type="button" onClick={verifyApprovedAssets} disabled={busy || !capture}>
            I verified these cards are correct
          </button>
        )}
      </section>

      {settings ? (
        <section className="panel">
          <strong>Step 2 — Recap settings</strong>
          <p className="muted">Saved to ApprovedCards.json when you proceed.</p>
          <div className="settings-grid">
            {SETTING_FIELDS.map((field) => (
              <label key={field.key} className="setting-row">
                <span className="setting-label">{field.label}</span>
                {field.type === "checkbox" ? (
                  <input
                    type="checkbox"
                    checked={Boolean(settings[field.key])}
                    onChange={(e) => patchSetting(field.key, e.target.checked)}
                  />
                ) : (
                  <select
                    value={settings[field.key] || field.options[0]}
                    onChange={(e) => patchSetting(field.key, e.target.value)}
                  >
                    {field.options.map((opt) => (
                      <option key={opt} value={opt}>
                        {opt}
                      </option>
                    ))}
                  </select>
                )}
                {field.help ? <span className="muted setting-help">{field.help}</span> : null}
              </label>
            ))}
          </div>
          <button type="button" className="secondary" onClick={saveSettings} disabled={busy}>
            Save settings
          </button>
        </section>
      ) : null}

      <section className="panel panel-gate">
        <strong>Step 3 — Proceed to compose</strong>
        <p className="muted">
          Unity will not encode until you click Proceed. Compose runs once per capture run.
        </p>
        {!assetsVerified ? (
          <p className="panel-warn inline-warn">Verify cards in Step 1 first.</p>
        ) : null}
        {gateProceeded ? (
          <p className={personalVoicePending ? "panel-warn inline-warn" : "verified-ok"}>
            {composeRunning
              ? "Compose in progress…"
              : personalVoicePending
                ? "Silent shell ready — Personal Voice still required."
                : presentationReady
                  ? "Presentation video ready (with narration)."
                  : status?.presentationMp4
                    ? "Video on disk but missing narration audio."
                    : "Proceed sent — compose should start automatically."}
          </p>
        ) : null}
        <button
          type="button"
          className="proceed"
          onClick={proceedToCompose}
          disabled={busy || !assetsVerified || gateProceeded || !capture}
        >
          Proceed to compose
        </button>
        {gateProceeded && !presentationReady && !composeRunning ? (
          <button type="button" className="secondary" onClick={retryCompose} disabled={busy}>
            Retry compose
          </button>
        ) : null}
        {personalVoicePending && !composeRunning ? (
          <p className="panel-warn inline-warn">
            Silent shell ready — complete Step 4 (review narration) before Personal Voice.
          </p>
        ) : null}
        {status?.composeJob?.stderrTail ? (
          <pre className="muted">{status.composeJob.stderrTail.slice(-800)}</pre>
        ) : null}
      </section>

      {showNarrationReview ? (
        <section className="panel panel-narration-review">
          <strong>Step 4 — Review narration script</strong>
          <p className="muted">
            This is <code>NarrationVoiceGuide.md</code> — voice direction, AI prompt, and spoken script
            preview. Approve when it sounds like you, or regenerate before Personal Voice records.
          </p>

          {narrationGenerating ? (
            <p className="panel-warn inline-warn">Generating narration script…</p>
          ) : null}

          {narrationReviewState.error ? (
            <p className="panel-warn inline-warn">{narrationReviewState.error}</p>
          ) : null}

          {narrationReviewState.wordCount != null ? (
            <ul className="muted narration-stats">
              <li>
                Words: {narrationReviewState.wordCount}
                {narrationReviewState.targetWordCount != null
                  ? ` / ~${narrationReviewState.targetWordCount} target`
                  : ""}
                {narrationReviewState.targetDurationSec != null
                  ? ` (${Math.round(narrationReviewState.targetDurationSec)}s video)`
                  : ""}
              </li>
              {narrationReviewState.source ? (
                <li>Source: {narrationReviewState.source}</li>
              ) : null}
              {narrationApproved ? (
                <li className="verified-ok">Approved — headless Personal Voice will mux when complete.</li>
              ) : narrationReviewState.ready ? (
                <li className="panel-warn inline-warn">Pending your approval.</li>
              ) : null}
            </ul>
          ) : null}

          {narrationReview?.guideMarkdown ? (
            <pre className="narration-review-pre">{narrationReview.guideMarkdown}</pre>
          ) : narrationReviewState.ready ? (
            <p className="muted">Loading guide…</p>
          ) : (
            <p className="muted">Waiting for silent video and script generation…</p>
          )}

          {narrationReview?.scriptText ? (
            <>
              <button
                type="button"
                className="secondary"
                onClick={() => setShowSpokenScript((v) => !v)}
              >
                {showSpokenScript ? "Hide spoken script" : "Show spoken script only"}
              </button>
              {showSpokenScript ? (
                <pre className="narration-script-pre">{narrationReview.scriptText}</pre>
              ) : null}
            </>
          ) : null}

          <div className="narration-review-actions">
            <button
              type="button"
              className="secondary"
              onClick={() => regenerateNarrationScript(false)}
              disabled={busy || narrationGenerating || !capture}
            >
              Regenerate script
            </button>
            {settings?.cursorFullNarration ? (
              <button
                type="button"
                className="secondary"
                onClick={() => regenerateNarrationScript(true)}
                disabled={busy || narrationGenerating || !capture}
              >
                Regenerate with AI Assistant
              </button>
            ) : null}
            <button
              type="button"
              className="proceed"
              onClick={() => approveNarrationScript(true)}
              disabled={busy || narrationGenerating || !capture || !narrationReviewState.ready}
            >
              {narrationApproved ? "Re-apply Personal Voice (headless)" : "Approve & apply Personal Voice"}
            </button>
          </div>
        </section>
      ) : null}

      <section className="panel">
        <div className="muted">Capture</div>
        <code>{capture || "(none)"}</code>
        {status ? (
          <ul className="muted">
            <li>{status.timelapseFrames} timelapse frames</li>
            <li>{status.milestones} milestones</li>
            <li>
              Presentation:{" "}
              {status.presentationHasAudio
                ? "yes (with Personal Voice)"
                : status.presentationMp4
                  ? "silent shell only"
                  : "no"}
            </li>
            <li>
              Personal Voice: {status.narrator?.engine} (requirePersonal=
              {String(status.narrator?.requirePersonal)})
            </li>
          </ul>
        ) : null}
        <button type="button" className="secondary" onClick={refresh} disabled={busy}>
          Refresh
        </button>
      </section>

      {timeline ? (
        <section className="panel">
          <strong>Pacing (DemoRecapTimeline.json)</strong>
          <div className="grid">
            {PACING_KEYS.map((key) => (
              <label key={key}>
                {key}
                <input
                  type="number"
                  step="0.1"
                  value={timeline[key] ?? ""}
                  onChange={(e) => patchPacing(key, parseFloat(e.target.value))}
                />
              </label>
            ))}
          </div>
          <p className="muted">videoPlaybackFactor is locked at 1.0 (Personal Voice sync).</p>
          <button type="button" onClick={saveTimeline} disabled={busy}>
            Save timeline
          </button>
        </section>
      ) : null}

      <section className="panel panel-compose-live">
        <strong>Video editor live feed</strong>
        <p className="muted">
          Producer recap pipeline only (timelapse PNGs → segments → narration). Not Unity.
        </p>
        <pre className="compose-live-pre">{composeLive || "Waiting for compose activity…"}</pre>
      </section>
    </main>
  );
}
