import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import CardMatrixRain, { useCardWorkProgress } from "./CardMatrixRain";
import { useWizardRecorder } from "./useWizardRecorder";

const PHASE_LABELS = {
  qna: "Q&A",
  awaiting_concept_approval: "Concept review",
  research: "Gathering research…",
  awaiting_research_approval: "Research approval",
  awaiting_plan_approval: "Plan approval",
  finalized: "Starting build in Unity…",
  cancelled: "Cancelled",
};

const AI_PRESETS = [
  {
    id: "small",
    label: "Small world",
    hint: "9-tile play disk + floating islands · fast demo",
  },
  {
    id: "medium",
    label: "Medium world",
    hint: "81-tile FullWorld · standard playable demo",
  },
  {
    id: "large",
    label: "Large world",
    hint: "289-tile extended open world",
  },
];

const TYPEWRITER_CPS = 68;
const STREAM_POLL_MS = 100;
const PRESENTATION_IDLE_MS = 80;

function displayChatContent(content) {
  const text = String(content || "").trim();
  if (!text.startsWith("{")) {
    return content;
  }
  if (
    !text.includes('"checklist"') &&
    !text.includes('"assistantMessage"') &&
    !text.includes('"qnaComplete"') &&
    !text.includes('"teachingMoment"')
  ) {
    return content;
  }
  try {
    const parsed = JSON.parse(text);
    if (typeof parsed.assistantMessage === "string" && parsed.assistantMessage.trim()) {
      return parsed.assistantMessage.trim();
    }
  } catch {
    const match = text.match(/"assistantMessage"\s*:\s*"((?:[^"\\]|\\.)*)"/s);
    if (match) {
      try {
        return JSON.parse(`"${match[1]}"`);
      } catch {
        return match[1].replace(/\\n/g, "\n");
      }
    }
  }
  return "";
}

function streamingDuplicatesAssistant(session) {
  const stream = (session?.streamingText || "").trim();
  if (!stream) return false;
  const msgs = session?.messages || [];
  for (let i = msgs.length - 1; i >= 0; i -= 1) {
    if (msgs[i].role !== "assistant") continue;
    const committed = displayChatContent(msgs[i].content || "").trim();
    if (!committed) return false;
    return (
      committed === stream ||
      committed.startsWith(stream.slice(0, Math.min(stream.length, 160))) ||
      stream.startsWith(committed.slice(0, Math.min(committed.length, 160)))
    );
  }
  return false;
}

function hubFromUrl() {
  return new URLSearchParams(window.location.search).get("hub") || "";
}

function buildAssetUrl(imageRel, cacheKey = "") {
  const hub = hubFromUrl();
  if (!hub || !imageRel) return "";
  const ts = cacheKey === "" || cacheKey == null ? "0" : String(cacheKey);
  return `/api/planner/asset?hub=${encodeURIComponent(hub)}&rel=${encodeURIComponent(imageRel)}&v=${encodeURIComponent(ts)}`;
}

function mapPreviewUrl(card, conceptImageTs) {
  const hub = hubFromUrl();
  if (!hub) return "";
  const rel = card.imageRel;
  if (!rel) return "";
  const bust = `${card.reviseCount ?? 0}-${card.imageTs ?? conceptImageTs ?? 0}`;
  if (card.imageUrl) {
    try {
      const u = new URL(card.imageUrl, window.location.origin);
      u.searchParams.set("v", bust);
      return `${u.pathname}${u.search}`;
    } catch {
      /* fall through */
    }
  }
  return buildAssetUrl(rel, bust);
}

async function api(path, opts = {}, timeoutMs = 0) {
  const hub = hubFromUrl();
  const q = hub ? `?hub=${encodeURIComponent(hub)}` : "";
  const controller = timeoutMs > 0 ? new AbortController() : null;
  let timer;
  if (controller && timeoutMs > 0) {
    timer = setTimeout(() => controller.abort(), timeoutMs);
  }
  try {
    const res = await fetch(`${path}${q}`, {
      cache: "no-store",
      ...opts,
      signal: controller?.signal,
      headers: { "Content-Type": "application/json", ...(opts.headers || {}) },
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) throw new Error(data.error || res.statusText);
    return data;
  } catch (e) {
    if (controller?.signal?.aborted) {
      throw new Error(`Request timed out after ${Math.round(timeoutMs / 1000)}s — refresh and retry.`);
    }
    throw e;
  } finally {
    if (timer) clearTimeout(timer);
  }
}

const CARD_IDLE_MS = {
  map: 180000,
  mesh: 90000,
  sculpt: 90000,
  asset: 60000,
};
const CARD_CURSOR_IDLE_MS = 240000;
const CARD_TOTAL_MS = 600000;
const CARD_POLL_MS = 650;

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function cardProgressSnapshot(session, cardId) {
  const card = (session?.conceptCards || []).find((c) => c.id === cardId);
  const active =
    session?.cardPacedActive?.cardId === cardId ? session.cardPacedActive : null;
  const job = card?.jobProgress || active;
  return {
    imageTs: card?.imageTs ?? null,
    imageRel: card?.imageRel ?? null,
    prefabPath: card?.prefabPath ?? null,
    jobPhase: job?.phase ?? null,
    jobProgress: job?.progress ?? null,
    jobMessage: job?.message ?? null,
    jobType: job?.jobType ?? null,
    queueLen: (session?.cardPacedQueue || []).filter((e) => e.cardId === cardId).length,
    activeCardId: session?.cardPacedActive?.cardId ?? null,
    updatedUtc: session?.updatedUtc ?? null,
    layoutReviseCount: session?.layoutReviseCount ?? null,
    densityReviseCount: session?.densityReviseCount ?? null,
    lastMapRevisionSummary: session?.lastMapRevisionSummary ?? null,
  };
}

function snapSignature(snap) {
  return JSON.stringify(snap);
}

function mapReviseComplete(baseline, snap) {
  return snap.imageTs != null && snap.imageTs !== baseline.imageTs;
}

function mapReviseJobComplete(baseline, snap, session, cardId) {
  if (snap.jobPhase === "error") return true;
  if (snap.jobPhase === "done" || snap.jobProgress >= 100) return true;
  if (mapReviseComplete(baseline, snap)) return true;
  const revField =
    cardId === "layout" ? session?.layoutReviseCount : session?.densityReviseCount;
  const baseRev =
    cardId === "layout" ? baseline.layoutReviseCount : baseline.densityReviseCount;
  if (revField != null && baseRev != null && revField !== baseRev) return true;
  if (
    session?.lastMapRevisionCard === cardId &&
    session?.lastMapRevisionSummary &&
    session.lastMapRevisionSummary !== baseline.lastMapRevisionSummary
  ) {
    return true;
  }
  return meshJobComplete(baseline, snap, session, cardId);
}

function meshJobComplete(baseline, snap, session, cardId) {
  if (snap.jobPhase === "done" || snap.jobProgress >= 100) return true;
  if (snap.imageTs != null && snap.imageTs !== baseline.imageTs && snap.jobPhase !== "queued") {
    return true;
  }
  const hadJob =
    baseline.jobPhase ||
    baseline.jobProgress > 0 ||
    baseline.queueLen > 0 ||
    baseline.activeCardId === cardId;
  const stillQueued = snap.queueLen > 0;
  const stillActive = snap.activeCardId === cardId;
  if (hadJob && !stillQueued && !stillActive && snap.jobPhase !== "queued") return true;
  const card = (session?.conceptCards || []).find((c) => c.id === cardId);
  if (hadJob && card && !card.jobProgress && !stillActive && !stillQueued) return true;
  return false;
}

function assetReviseComplete(baseline, snap, session, cardId) {
  if (mapReviseComplete(baseline, snap)) return true;
  if (snap.prefabPath && snap.prefabPath !== baseline.prefabPath) return true;
  return meshJobComplete(baseline, snap, session, cardId);
}

async function waitForCardOperation({
  cardId,
  baselineSnap,
  onSession,
  idleMs,
  totalMs = CARD_TOTAL_MS,
  pollMs = CARD_POLL_MS,
  isComplete,
  postPromise,
}) {
  const t0 = Date.now();
  let lastProgressAt = Date.now();
  let lastSig = snapSignature(baselineSnap);
  let postDone = false;
  let postError = null;
  let postPayload = null;

  if (postPromise) {
    postPromise.then(
      (payload) => {
        postDone = true;
        postPayload = payload;
      },
      (err) => {
        postDone = true;
        postError = err;
      }
    );
  }

  while (Date.now() - t0 < totalMs) {
    await sleep(pollMs);

    if (postError) throw postError;

    let session = postPayload?.session;
    if (!session) {
      try {
        const d = await api("/api/planner/session");
        session = d?.session;
      } catch {
        if (postDone && postPayload?.session) {
          session = postPayload.session;
        } else {
          continue;
        }
      }
    } else if (postDone) {
      postPayload = null;
    }

    if (session) onSession?.(session);

    const snap = cardProgressSnapshot(session, cardId);
    const sig = snapSignature(snap);
    if (sig !== lastSig) {
      lastSig = sig;
      lastProgressAt = Date.now();
    }

    if (snap.jobPhase === "error") {
      throw new Error(snap.jobMessage || "Generation failed — keep Unity open and retry.");
    }

    if (isComplete(session, snap, baselineSnap)) {
      return session;
    }

    if (postDone && postPayload?.session && isComplete(postPayload.session, cardProgressSnapshot(postPayload.session, cardId), baselineSnap)) {
      return postPayload.session;
    }

    const cursorWait =
      snap.jobType === "revise_map" ||
      snap.jobPhase === "cursor" ||
      (snap.jobMessage || "").includes("Cursor API");
    const stallLimit = cursorWait ? Math.max(idleMs, CARD_CURSOR_IDLE_MS) : idleMs;
    if (Date.now() - lastProgressAt >= stallLimit) {
      throw new Error(
        `No progress on this card for ${Math.round(stallLimit / 1000)}s — it may be stuck. ` +
          (cursorWait
            ? "Cursor layout revisions often take 1–2 min — refresh to check if it finished, then retry if needed."
            : "Keep Unity open, refresh, then retry.")
      );
    }
  }

  throw new Error(
    "Generation is taking longer than expected. Refresh to check the latest state — Unity may still be working."
  );
}

function useTypewriter(text, enabled, cps = TYPEWRITER_CPS) {
  const [display, setDisplay] = useState(enabled ? "" : text || "");
  const [done, setDone] = useState(!enabled);

  useEffect(() => {
    if (!enabled || !text) {
      setDisplay(text || "");
      setDone(true);
      return undefined;
    }
    setDisplay("");
    setDone(false);
    let i = 0;
    const ms = 1000 / cps;
    let timer;
    const tick = () => {
      i += 1;
      setDisplay(text.slice(0, i));
      if (i >= text.length) {
        setDone(true);
      } else {
        timer = setTimeout(tick, ms);
      }
    };
    timer = setTimeout(tick, ms);
    return () => clearTimeout(timer);
  }, [text, enabled, cps]);

  return { display, done };
}

function StreamingBubble({ content, role }) {
  const waiting = !content;
  return (
    <div className={`bubble streaming ${role === "user" ? "user" : "assistant"}`}>
      <div className="role">{role === "user" ? "AI Responder" : "Planner"}</div>
      <div className="text">
        {waiting ? <span className="thinking-dots" aria-hidden>…</span> : content}
        <span className="caret" aria-hidden />
      </div>
    </div>
  );
}

function TypedBubble({ content, animate, onTypingDone }) {
  const { display, done } = useTypewriter(content, animate);
  useEffect(() => {
    if (animate && done) onTypingDone?.();
  }, [animate, done, onTypingDone]);
  return (
    <>
      {display}
      {animate && !done && <span className="caret" aria-hidden />}
    </>
  );
}

const CHECKLIST_META = {
  play_disk: { icon: "▦", group: "Core" },
  scope: { icon: "◎", group: "Core" },
  props: { icon: "▪", group: "Content" },
  npcs: { icon: "●", group: "Content" },
  enemies: { icon: "▲", group: "Content" },
  puzzles: { icon: "★", group: "Content" },
  terrain: { icon: "⛰", group: "World" },
  speed: { icon: "⚡", group: "Build" },
  tech_jump: { icon: "↗", group: "Learn" },
  tech_seams: { icon: "▤", group: "Learn" },
  tech_navmesh: { icon: "⌁", group: "Learn" },
  tech_spawn: { icon: "⌂", group: "Learn" },
  tech_seed: { icon: "⚄", group: "Learn" },
  tech_collision: { icon: "⛝", group: "Learn" },
  tech_perf: { icon: "◷", group: "Learn" },
};

function CardJobProgress({ job }) {
  if (!job) return null;
  if (job.phase === "error") {
    return (
      <div className="concept-card-progress concept-card-progress-error" aria-live="polite">
        <p className="concept-card-progress-label">{job.message || "Preview failed"}</p>
      </div>
    );
  }
  if (job.progress >= 100) return null;
  const pct = Math.max(0, Math.min(100, Number(job.progress) || 0));
  return (
    <div className="concept-card-progress" aria-live="polite">
      <div className="concept-card-progress-track">
        <div className="concept-card-progress-fill" style={{ width: `${pct}%` }} />
      </div>
      <p className="concept-card-progress-label">
        {job.message || "Working…"} <span className="concept-card-progress-pct">{pct}%</span>
      </p>
    </div>
  );
}

function ConceptImageCard({
  card,
  busy,
  revising,
  accepting,
  generatingMesh,
  sculptingCharacter,
  cardJob,
  justRevised,
  onRevise,
  onAccept,
  onUnaccept,
  onSetCount,
  onGenerateMesh,
  onSculptCharacter,
  imgError,
  onImgError,
  onImgLoad,
  conceptImageTs,
}) {
  const [sculptHint, setSculptHint] = useState(card.lastSculptHint || "");
  const [reviseNote, setReviseNote] = useState(card.lastRevisionNote || "");
  const [mapUseFull, setMapUseFull] = useState(false);
  useEffect(() => {
    setSculptHint(card.lastSculptHint || "");
  }, [card.id, card.lastSculptHint]);
  useEffect(() => {
    setReviseNote(card.lastRevisionNote || "");
  }, [card.id, card.lastRevisionNote]);
  useEffect(() => {
    setMapUseFull(false);
  }, [card.id, card.imageTs, card.imageRel, card.reviseCount]);

  const accepted = card.status === "accepted";
  const isMap = card.cardType === "layout" || card.cardType === "density";
  const canAdjustCount =
    !isMap &&
    String(card.id || "").startsWith("asset:") &&
    (card.allowCountAdjust || card.cardType === "prop" || card.cardType === "enemy");
  const displayCount = card.targetInstanceCount ?? card.instanceCount ?? 1;
  const mapRel =
    isMap && mapUseFull && card.imageRelFull ? card.imageRelFull : card.imageRel;
  const imgSrc = isMap
    ? mapPreviewUrl(card, conceptImageTs)
    : buildAssetUrl(
        card.imageRel,
        `${card.reviseCount ?? 0}-${card.imageTs ?? conceptImageTs ?? 0}-${card.candidateIndex ?? 0}`
      );
  const showPath = card.prefabPath && (imgError || !imgSrc);
  const displayJob =
    cardJob ||
    (generatingMesh
      ? {
          jobType: "generate_mesh",
          phase: "starting",
          progress: 4,
          message: "AI Generator — starting mesh job…",
        }
      : null);
  const jobBusy = Boolean(
    displayJob && displayJob.progress < 100 && displayJob.phase !== "error"
  );
  const isMeshJob = displayJob?.jobType === "generate_mesh";
  const isSculptJob = displayJob?.jobType === "sculpt_character";
  const isPreviewJob = displayJob?.jobType === "load_preview";
  const isMapReviseJob = displayJob?.jobType === "revise_map";
  const poolHint =
    !card.needsAiMesh &&
    card.poolSize > 1 &&
    String(card.id || "").startsWith("asset:")
      ? `Kit option ${((card.candidateIndex ?? 0) % card.poolSize) + 1} of ${card.poolSize}`
      : null;
  const meshReady =
    Boolean(card.meshPreviewReady) ||
    Boolean(card.prefabPath && !card.isPlaceholderPreview);
  const aiMeshHint =
    card.needsAiMesh && !accepted
      ? meshReady
        ? "3D mesh ready — Accept or Regenerate 3D (AI) for a new shape"
        : "Click Generate 3D (AI) or Revise when you want a new shape"
      : null;
  const sculptWords = sculptHint.trim().split(/\s+/).filter(Boolean).slice(0, 2).join(" ");
  const cardWorking =
    revising || accepting || jobBusy || generatingMesh || sculptingCharacter;
  const previewBusy = cardWorking;
  const { progress: workProgress, indeterminate: workIndeterminate } = useCardWorkProgress(
    cardWorking,
    displayJob && displayJob.phase !== "error" ? displayJob : null
  );
  const workMessage =
    displayJob?.message ||
    (accepting
      ? "Locking card…"
      : isMapReviseJob || (isMap && revising)
        ? displayJob?.message ||
          (card.lastRevisionNote
            ? `Cursor — ${card.lastRevisionNote.slice(0, 90)}`
            : "Cursor API — revising layout from your note…")
      : isSculptJob || sculptingCharacter
        ? `AI morph — ${sculptWords || card.lastSculptHint || "sculpting"}…`
        : isMeshJob || generatingMesh
          ? "AI Generator — building 3D mesh…"
          : isPreviewJob
            ? "Loading 3D preview…"
            : revising
              ? "Revising…"
              : "Working…");
  const previewVisible = Boolean(imgSrc && !imgError);
  const displayTitle =
    card.needsAiMesh && card.slotLabel
      ? card.slotLabel.split(" ").slice(-1)[0] || card.label
      : card.label || card.id;
  const handleImgError = () => {
    if (isMap && !mapUseFull && card.imageRelFull) {
      setMapUseFull(true);
      return;
    }
    onImgError();
  };
  return (
    <div
      id={`concept-card-${card.id}`}
      className={`concept-card ${accepted ? "accepted" : "pending"}${revising ? " revising" : ""}${
        justRevised ? " revised-flash" : ""
      }`}
      data-type={card.cardType || "asset"}
    >
      <div className="concept-card-head">
        <div className="concept-card-titles">
          <h4 className="concept-card-title">{displayTitle}</h4>
          {card.slotLabel && card.slotLabel !== displayTitle && (
            <p className="concept-slot-label">{card.slotLabel}</p>
          )}
        </div>
        {accepted && <span className="concept-card-lock">Locked</span>}
        {canAdjustCount && accepted && (
          <span className="concept-card-count">×{displayCount}</span>
        )}
        {(card.cardType === "npc" || card.cardType === "player") && accepted && (
          <span className="concept-card-count">1 slot</span>
        )}
      </div>
      <div className="concept-card-frame">
        {previewVisible ? (
          <img
            key={`${card.id}-${card.reviseCount ?? 0}-${card.imageTs || conceptImageTs || 0}`}
            className={`concept-img${previewBusy ? " concept-img-busy" : ""}`}
            src={imgSrc}
            alt={card.label || card.id}
            decoding="async"
            onLoad={onImgLoad}
            onError={handleImgError}
          />
        ) : (
          <div className="concept-fallback">
            {previewBusy
              ? isMap
                ? "Regenerating map…"
                : isMeshJob || generatingMesh
                  ? "AI Generator — building 3D mesh…"
                  : "Working…"
              : imgError && imgSrc
                ? isMap
                  ? "Map preview failed to load — click Revise again"
                  : "Preview failed to load — try Regenerate 3D (AI)"
                : card.needsAiMesh
                  ? meshReady
                    ? "Loading 3D preview…"
                    : "No 3D mesh yet — click Generate 3D (AI)"
                  : card.isPlaceholderPreview
                    ? "Queued for 3D preview…"
                    : "Preview unavailable"}
          </div>
        )}
        <CardMatrixRain
          active={cardWorking}
          message={workMessage}
          progress={workProgress}
          indeterminate={workIndeterminate}
        />
        {displayJob?.phase === "error" && <CardJobProgress job={displayJob} />}
      </div>
      {card.canGenerateMesh && !accepted && !revising && !generatingMesh && !jobBusy && (
        <button
          type="button"
          className="ghost small concept-generate-mesh"
          disabled={generatingMesh || jobBusy}
          onClick={() => onGenerateMesh(card.id)}
        >
          {card.needsAiMesh
            ? card.isPlaceholderPreview || !meshReady
              ? "Generate 3D (AI)"
              : "Regenerate 3D (AI)"
            : card.isPlaceholderPreview
              ? "Generate mesh"
              : "Regenerate mesh (new shape)"}
        </button>
      )}
      {card.canSculptCharacter &&
        !accepted &&
        !revising &&
        !sculptingCharacter &&
        !generatingMesh &&
        !jobBusy && (
          <div className="concept-sculpt-row">
            <input
              className="concept-sculpt-input"
              type="text"
              maxLength={28}
              placeholder="e.g. tall lanky"
              aria-label={`Sculpt hint for ${card.label || card.id}`}
              value={sculptHint}
              onChange={(e) => {
                const cleaned = e.target.value.replace(/[^\w\s-]/g, "");
                setSculptHint(cleaned.split(/\s+/).filter(Boolean).slice(0, 2).join(" "));
              }}
            />
            <button
              type="button"
              className="primary small concept-sculpt-btn"
              disabled={sculptingCharacter || jobBusy || !sculptWords}
              onClick={() => onSculptCharacter(card.id, sculptWords)}
            >
              Resculpt
            </button>
          </div>
        )}
      {poolHint && !revising && !generatingMesh && (
        <p className="hint concept-pool-hint">{poolHint}</p>
      )}
      {aiMeshHint && !revising && !generatingMesh && !jobBusy && (
        <p className="hint concept-pool-hint concept-ai-mesh-hint">{aiMeshHint}</p>
      )}
      {canAdjustCount && !accepted && (
        <div className="concept-card-count-row">
          <span className="concept-card-count-label">Instances</span>
          <button
            type="button"
            className="ghost small count-btn"
            disabled={busy || displayCount <= 0}
            onClick={() => onSetCount(card.id, Math.max(0, displayCount - 1))}
            aria-label="Decrease instance count"
          >
            −
          </button>
          <input
            className="concept-count-input"
            type="number"
            min={0}
            max={500}
            value={displayCount}
            disabled={busy}
            onChange={(e) => {
              const next = Number.parseInt(e.target.value, 10);
              if (!Number.isNaN(next)) onSetCount(card.id, next);
            }}
          />
          <button
            type="button"
            className="ghost small count-btn"
            disabled={busy || displayCount >= 500}
            onClick={() => onSetCount(card.id, displayCount + 1)}
            aria-label="Increase instance count"
          >
            +
          </button>
        </div>
      )}
      {showPath && <p className="hint concept-card-path">{card.prefabPath}</p>}
      {isMap && !accepted && (
        <div className="concept-revise-row">
          <textarea
            className="concept-revise-input"
            rows={2}
            maxLength={240}
            aria-label={`Revision note for ${card.label || card.id}`}
            placeholder={
              card.id === "layout"
                ? "What to change on the layout? e.g. shift scatter north, open center hub, corner maze"
                : "What to change on trails/density? e.g. fewer props on maze walls, stronger trail east"
            }
            value={reviseNote}
            disabled={revising || jobBusy}
            onChange={(e) => setReviseNote(e.target.value)}
          />
        </div>
      )}
      <div className="concept-card-actions">
        <button
          type="button"
          className="ghost small"
          disabled={revising || accepting || jobBusy}
          onClick={() => onRevise(card.id, reviseNote.trim())}
        >
          {revising ? "Revising…" : "Revise"}
        </button>
        {accepted ? (
          <button
            type="button"
            className="ghost small"
            disabled={busy || accepting}
            onClick={() => onUnaccept(card.id)}
          >
            Un-accept
          </button>
        ) : (
          <button
            type="button"
            className="primary small"
            disabled={accepting || revising}
            onClick={() => onAccept(card.id)}
          >
            {accepting ? "Locking…" : "Accept"}
          </button>
        )}
      </div>
    </div>
  );
}

function ChecklistPanel({ items, phase, cursorActive }) {
  const [expanded, setExpanded] = useState({});

  if (!items?.length) return null;

  const doneCount = items.filter((i) => i.done).length;
  const pct = Math.round((doneCount / items.length) * 100);
  const nextPending = items.find((i) => !i.done);
  const allDone = doneCount === items.length;

  const toggleExpand = (id) => setExpanded((prev) => ({ ...prev, [id]: !prev[id] }));

  return (
    <aside className="checklist-panel card" aria-label="Planning decisions checklist">
      <div className="checklist-header">
        <h2 className="section">Your decisions</h2>
        <span className={`checklist-badge ${allDone ? "complete" : ""}`}>
          {doneCount}/{items.length}
        </span>
      </div>

      <div className="progress-track" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100}>
        <div className="progress-fill" style={{ width: `${pct}%` }} />
      </div>
      <p className="checklist-progress-label">
        {phase === "awaiting_concept_approval"
          ? allDone
            ? "Q&A complete — review concept cards below (nothing accepted until you click Accept)"
            : `${doneCount} decided · ${items.length - doneCount} still open`
          : allDone
            ? "All topics decided — generating concept cards next"
            : `${doneCount} decided · ${items.length - doneCount} still open`}
      </p>
      <p className="hint checklist-hint">
        Answer each question in chat. Say <strong>move on</strong> to skip remaining topics. To revise a
        locked decision: <strong>change checklist option 3</strong> (use the option number).
      </p>

      {phase === "qna" && nextPending && !cursorActive && (
        <p className="checklist-next">
          <span className="checklist-next-tag">Up next</span>
          {nextPending.label}
        </p>
      )}

      {phase === "qna" && cursorActive && (
        <p className="checklist-next checklist-next-busy">
          <span className="spinner spinner-sm" />
          Updating checklist…
        </p>
      )}

      <ul className="checklist">
        {items.map((item) => {
          const meta = CHECKLIST_META[item.id] || { icon: "·", group: "" };
          const decision = (item.decision || "").trim();
          const long = decision.length > 90;
          const isOpen = expanded[item.id];
          const isNext = !item.done && item.id === nextPending?.id;

          return (
            <li
              key={item.id}
              className={`check-item ${item.done ? "done" : "pending"}${isNext ? " next" : ""}`}
            >
              <span className={`check-icon ${item.done ? "on" : ""}`} aria-hidden>
                {meta.icon}
              </span>
              <div className="check-body">
                <div className="check-row">
                  <span className="check-label">
                    <span className="check-index">{item.index ?? "?"}.</span> {item.label}
                    {item.locked && item.done && (
                      <span className="check-lock" title="Locked — say change checklist option N to revise">
                        {" "}
                        🔒
                      </span>
                    )}
                  </span>
                  <span className={`check-state ${item.done ? "on" : ""}`} aria-label={item.done ? "Decided" : "Pending"}>
                    {item.done ? "✓" : "○"}
                  </span>
                </div>

                {item.done && decision ? (
                  <>
                    <p className={`check-decision ${long && !isOpen ? "clamped" : ""}`}>{decision}</p>
                    {long && (
                      <button type="button" className="check-expand" onClick={() => toggleExpand(item.id)}>
                        {isOpen ? "Show less" : "Show more"}
                      </button>
                    )}
                  </>
                ) : (
                  !item.done &&
                  phase === "qna" && <p className="check-waiting">Waiting for your answer</p>
                )}
              </div>
            </li>
          );
        })}
      </ul>
    </aside>
  );
}

export default function App() {
  const [session, setSession] = useState(null);
  const [input, setInput] = useState("");
  const [internetResearch, setInternetResearch] = useState(false);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");
  const [started, setStarted] = useState(false);
  const [typeLastAssistant, setTypeLastAssistant] = useState(false);
  const [cardImgErrors, setCardImgErrors] = useState({});
  const [revisingCardId, setRevisingCardId] = useState(null);
  const [acceptingCardId, setAcceptingCardId] = useState(null);
  const [generatingMeshCardId, setGeneratingMeshCardId] = useState(null);
  const [sculptingCharacterCardId, setSculptingCharacterCardId] = useState(null);
  const [cardActionNote, setCardActionNote] = useState(null);
  const [flashedCardId, setFlashedCardId] = useState(null);
  const animatedMsgRef = useRef(new Set());
  const bottomRef = useRef(null);
  const conceptSectionRef = useRef(null);
  const flashTimerRef = useRef(null);
  const noteTimerRef = useRef(null);
  const prevPhaseRef = useRef(null);
  const prevMessageCountRef = useRef(0);
  const prevCardUrlsRef = useRef({});
  const wrapRef = useRef(null);
  const sessionRef = useRef(null);
  const typeLastAssistantRef = useRef(false);

  useEffect(() => {
    sessionRef.current = session;
  }, [session]);

  useEffect(() => {
    typeLastAssistantRef.current = typeLastAssistant;
  }, [typeLastAssistant]);

  const waitForPresentationIdle = useCallback(async () => {
    for (let i = 0; i < 900; i += 1) {
      const s = sessionRef.current;
      if (s?.cursorWorking || (s?.streamingText && !streamingDuplicatesAssistant(s))) {
        await new Promise((r) => setTimeout(r, STREAM_POLL_MS));
        continue;
      }
      if (typeLastAssistantRef.current) {
        await new Promise((r) => setTimeout(r, PRESENTATION_IDLE_MS));
        continue;
      }
      return;
    }
  }, []);

  const applySession = useCallback((s, animateReply = false, { keepCardErrors = false } = {}) => {
    setSession(s);
    if (animateReply) setTypeLastAssistant(true);
    if (!keepCardErrors) setCardImgErrors({});
  }, []);

  const resumePlanner = useCallback(async () => {
    const d = await api("/api/planner/resume", { method: "POST", body: "{}" });
    applySession(d.session, true);
    setStarted(true);
  }, [applySession]);

  const loadSession = useCallback(async () => {
    const hub = hubFromUrl();
    if (!hub) return;
    try {
      const d = await api("/api/planner/session");
      if (d.session?.phase && d.session.phase !== "cancelled") {
        applySession(d.session, false);
        setStarted(true);
        if (d.session.awaitingAssistantReply && d.session.cursorWorking) {
          setBusy(true);
          try {
            await resumePlanner();
          } catch (e) {
            setErr(String(e));
          } finally {
            setBusy(false);
          }
        }
      }
    } catch {
      /* no session yet */
    }
  }, [applySession, resumePlanner]);

  useEffect(() => {
    loadSession();
  }, [loadSession]);

  const hubRoot = hubFromUrl();
  const hubMissing = !hubRoot;

  const phase = session?.phase;
  const messages = session?.messages || [];
  const checklist = session?.checklist || [];

  const requireHub = () => {
    if (hubRoot) return hubRoot;
    setErr(
      "Missing ?hub= in the URL — reopen from Unity (Window → Environment Kit → Hub), not a bare 127.0.0.1 tab."
    );
    return "";
  };

  const streamingIsStale = useMemo(
    () => streamingDuplicatesAssistant(session),
    [session?.streamingText, messages]
  );

  const liveStreamingText = streamingIsStale ? "" : session?.streamingText || "";

  const assistantWorking =
    busy ||
    session?.cursorWorking ||
    (session?.autoRespondActive && phase === "qna") ||
    phase === "research" ||
    Boolean(liveStreamingText);

  const aiActive =
    assistantWorking || session?.awaitingAssistantReply;

  const needsPlannerRetry =
    phase === "qna" &&
    Boolean(session?.awaitingAssistantReply) &&
    !assistantWorking &&
    !busy;

  const checklistBusy =
    busy || session?.cursorWorking || Boolean(session?.autoRespondActive);

  const { recording: wizardRecording, chapterTitle, chapterVisible } = useWizardRecorder({
    hub: hubFromUrl(),
    api,
    started,
    session,
    rootRef: wrapRef,
  });

  const autoRespondLabel = useMemo(() => {
    if (!session?.autoRespondActive) return "";
    const preset = AI_PRESETS.find((p) => p.id === session.autoRespondPreset);
    const turn = session.autoRespondTurn ? ` · turn ${session.autoRespondTurn}` : "";
    return `${preset?.label || "AI"} auto-fill${turn}`;
  }, [session?.autoRespondActive, session?.autoRespondPreset, session?.autoRespondTurn]);

  useEffect(() => {
    const cards = session?.conceptCards || [];
    const changedIds = [];
    for (const card of cards) {
      const sig = `${card.imageUrl || ""}|${card.imageTs || ""}|${card.imageRel || ""}|${card.reviseCount ?? 0}`;
      if (sig && prevCardUrlsRef.current[card.id] !== sig) {
        prevCardUrlsRef.current[card.id] = sig;
        changedIds.push(card.id);
      }
    }
    if (!changedIds.length) return;
    setCardImgErrors((prev) => {
      const next = { ...prev };
      for (const id of changedIds) delete next[id];
      return next;
    });
  }, [session?.conceptCards]);

  const conceptCardActionBusy = Boolean(
    revisingCardId || acceptingCardId || generatingMeshCardId || sculptingCharacterCardId
  );
  const cardPacedBusy = Boolean(session?.cardPacedBusy);
  const prevCardPacedBusyRef = useRef(false);
  useEffect(() => {
    if (prevCardPacedBusyRef.current && !cardPacedBusy && started) {
      api("/api/planner/session")
        .then((d) => {
          if (d.session) {
            setCardImgErrors({});
            setSession(d.session);
          }
        })
        .catch(() => {});
    }
    prevCardPacedBusyRef.current = cardPacedBusy;
  }, [cardPacedBusy, started]);

  const conceptReviewPolling = phase === "awaiting_concept_approval";

  const shouldPoll =
    started &&
    (aiActive ||
      Boolean(liveStreamingText) ||
      session?.awaitingAssistantReply ||
      session?.kitCatalogExporting ||
      cardPacedBusy ||
      conceptReviewPolling);

  useEffect(() => {
    if (!shouldPoll) return undefined;
    const poll = async () => {
      try {
        const usePulse =
          aiActive &&
          phase !== "awaiting_concept_approval" &&
          !session?.kitCatalogExporting;
        if (usePulse) {
          const d = await api("/api/planner/pulse");
          if (d.pulse) {
            setSession((prev) => (prev ? { ...prev, ...d.pulse } : d.pulse));
          }
        } else {
          const d = await api("/api/planner/session");
          if (d.session) setSession(d.session);
        }
      } catch {
        /* ignore poll errors */
      }
    };
    poll();
    const ms =
      liveStreamingText || aiActive
        ? STREAM_POLL_MS
        : cardPacedBusy || generatingMeshCardId || revisingCardId
          ? 320
          : conceptReviewPolling
            ? 500
            : session?.autoRespondActive
              ? 180
              : 450;
    const id = setInterval(poll, ms);
    return () => clearInterval(id);
  }, [
    shouldPoll,
    liveStreamingText,
    session?.autoRespondActive,
    session?.kitCatalogExporting,
    cardPacedBusy,
    conceptReviewPolling,
    aiActive,
    phase,
    busy,
    conceptCardActionBusy,
    generatingMeshCardId,
    revisingCardId,
  ]);

  useEffect(() => {
    const count = messages.length;
    const grew = count > prevMessageCountRef.current;
    prevMessageCountRef.current = count;
    if (phase === "awaiting_concept_approval") return;
    if (!grew && !liveStreamingText && !typeLastAssistant) return;
    bottomRef.current?.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }, [messages.length, liveStreamingText, typeLastAssistant, phase, messages]);

  useEffect(() => {
    if (
      phase === "awaiting_concept_approval" &&
      prevPhaseRef.current !== "awaiting_concept_approval"
    ) {
      requestAnimationFrame(() => {
        conceptSectionRef.current?.scrollIntoView({ behavior: "smooth", block: "start" });
      });
    }
    prevPhaseRef.current = phase;
  }, [phase]);

  const run = async (fn, animateReply = false) => {
    setErr("");
    setBusy(true);
    try {
      const result = await fn();
      if (result?.session) {
        applySession(result.session, animateReply, { keepCardErrors: !animateReply });
      }
      return result;
    } catch (e) {
      setErr(String(e));
      try {
        const d = await api("/api/planner/session");
        if (d.session) applySession(d.session, false);
      } catch {
        /* ignore refresh errors */
      }
      throw e;
    } finally {
      setBusy(false);
    }
  };

  const retryPlannerReply = () =>
    run(async () => {
      const d = await api("/api/planner/resume", { method: "POST", body: "{}" });
      return d;
    }, true);

  const start = () => {
    if (!requireHub()) return undefined;
    return run(async () => {
      const msg = input.trim();
      if (!msg) return null;
      const d = await api("/api/planner/start", {
        method: "POST",
        body: JSON.stringify({ message: msg, internetResearch }),
      });
      setStarted(true);
      setInput("");
      return d;
    }, true);
  };

  const sendChat = () => {
    if (!requireHub()) return undefined;
    return run(async () => {
      const msg = input.trim();
      if (!msg) return null;
      const d = await api("/api/planner/chat", {
        method: "POST",
        body: JSON.stringify({ message: msg }),
      });
      setInput("");
      return d;
    }, true);
  };

  const autoRespond = (presetId) => {
    if (!requireHub()) return undefined;
    setStarted(true);
    setErr("");
    setBusy(true);
    animatedMsgRef.current = new Set();
    return (async () => {
      try {
        await waitForPresentationIdle();
        let done = false;
        let guard = 0;
        let waitSpins = 0;
        while (!done && guard < 32) {
          guard += 1;
          await waitForPresentationIdle();
          const d = await api("/api/planner/auto-respond/step", {
            method: "POST",
            body: JSON.stringify({ preset: presetId, internetResearch }),
          });
          setInput("");
          if (d?.session) {
            setSession(d.session);
            setTypeLastAssistant(true);
          }
          if (d?.session?.phase === "awaiting_concept_approval" || d?.done) {
            done = true;
            break;
          }
          if (d?.waiting) {
            waitSpins += 1;
            if (waitSpins > 120) {
              throw new Error(
                "AI Responder stalled waiting for the planner — refresh and continue from concept review."
              );
            }
            await sleep(500);
            continue;
          }
          waitSpins = 0;
          if (!done) {
            await waitForPresentationIdle();
          }
        }
      } catch (e) {
        setErr(String(e));
      } finally {
        setBusy(false);
      }
    })();
  };

  const applyConceptCardNote = (d, cardId, { flashOnDone = false } = {}) => {
    setCardImgErrors((prev) => {
      const next = { ...prev };
      delete next[cardId];
      return next;
    });
    const note = d?.session?.lastConceptCardAction;
    if (note?.message) {
      setCardActionNote(note);
      if (noteTimerRef.current) clearTimeout(noteTimerRef.current);
      noteTimerRef.current = setTimeout(() => setCardActionNote(null), 6000);
    }
    if (flashOnDone) {
      setFlashedCardId(cardId);
      if (flashTimerRef.current) clearTimeout(flashTimerRef.current);
      flashTimerRef.current = setTimeout(() => setFlashedCardId(null), 1400);
      requestAnimationFrame(() => {
        document
          .getElementById(`concept-card-${cardId}`)
          ?.scrollIntoView({ behavior: "smooth", block: "nearest" });
      });
    }
  };

  const reviseConceptCard = async (cardId, revisionNote = "") => {
    if (revisingCardId || acceptingCardId) return;
    if (!requireHub()) return;
    const label =
      (session?.conceptCards || []).find((c) => c.id === cardId)?.label || cardId;
    const notePreview = revisionNote.trim();
    const isMap = cardId === "layout" || cardId === "density";
    const baseline = cardProgressSnapshot(session, cardId);
    setRevisingCardId(cardId);
    setCardActionNote({
      cardId,
      message: notePreview
        ? `Cursor — revising ${label} — “${notePreview.slice(0, 80)}${notePreview.length > 80 ? "…" : ""}”`
        : `Cursor — revising ${label}…`,
    });
    setErr("");
    setCardImgErrors((prev) => {
      const next = { ...prev };
      if (isMap) {
        delete next.layout;
        delete next.density;
      } else {
        delete next[cardId];
      }
      return next;
    });

    const postPromise = api("/api/planner/concept/card/revise", {
      method: "POST",
      body: JSON.stringify({ cardId, revisionNote: notePreview }),
    });

    try {
      if (isMap) {
        const finalSession = await waitForCardOperation({
          cardId,
          baselineSnap: baseline,
          onSession: (s) => applySession(s, false),
          idleMs: CARD_IDLE_MS.map,
          postPromise,
          isComplete: (s, snap, base) => mapReviseJobComplete(base, snap, s, cardId),
        });
        applySession(finalSession, false);
        setCardImgErrors((prev) => {
          const next = { ...prev };
          delete next.layout;
          delete next.density;
          return next;
        });
        applyConceptCardNote({ session: finalSession }, cardId, { flashOnDone: true });
      } else {
        const d = await postPromise;
        if (d?.session) applySession(d.session, false);
        applyConceptCardNote(d, cardId, { flashOnDone: true });
      }
    } catch (e) {
      setErr(String(e));
      try {
        const d = await api("/api/planner/session");
        if (d.session) applySession(d.session, false);
      } catch {
        /* ignore refresh errors */
      }
    } finally {
      setRevisingCardId(null);
    }
  };

  const acceptConceptCard = (cardId) => {
    if (revisingCardId || acceptingCardId) return;
    setAcceptingCardId(cardId);
    return run(async () => {
      const d = await api("/api/planner/concept/card/accept", {
        method: "POST",
        body: JSON.stringify({ cardId }),
      });
      return d;
    }, false).finally(() => setAcceptingCardId(null));
  };

  const unacceptConceptCard = (cardId) => {
    if (revisingCardId || acceptingCardId) return;
    setAcceptingCardId(cardId);
    return run(async () => {
      const d = await api("/api/planner/concept/card/unaccept", {
        method: "POST",
        body: JSON.stringify({ cardId }),
      });
      return d;
    }, false).finally(() => setAcceptingCardId(null));
  };
  const generateConceptCardMesh = async (cardId) => {
    if (generatingMeshCardId) return;
    if (!requireHub()) return;
    const label =
      (session?.conceptCards || []).find((c) => c.id === cardId)?.label || cardId;
    setGeneratingMeshCardId(cardId);
    setCardActionNote({
      cardId,
      message: `AI Generator — building 3D mesh for ${label}…`,
    });
    setErr("");
    setCardImgErrors((prev) => {
      const next = { ...prev };
      delete next[cardId];
      return next;
    });
    try {
      const d = await api("/api/planner/concept/card/generate-mesh", {
        method: "POST",
        body: JSON.stringify({ cardId }),
      });
      if (d?.session) applySession(d.session, false);
      applyConceptCardNote(d, cardId, { flashOnDone: true });
    } catch (e) {
      setErr(String(e));
      try {
        const d = await api("/api/planner/session");
        if (d.session) applySession(d.session, false);
      } catch {
        /* ignore refresh errors */
      }
    } finally {
      setGeneratingMeshCardId(null);
    }
  };
  const sculptConceptCharacter = async (cardId, sculptHint) => {
    if (sculptingCharacterCardId) return;
    if (!requireHub()) return;
    const baseline = cardProgressSnapshot(session, cardId);
    setSculptingCharacterCardId(cardId);
    setCardActionNote(null);
    setErr("");

    const postPromise = api("/api/planner/concept/card/sculpt-character", {
      method: "POST",
      body: JSON.stringify({ cardId, sculptHint }),
    });

    try {
      const finalSession = await waitForCardOperation({
        cardId,
        baselineSnap: baseline,
        onSession: (s) => applySession(s, false),
        idleMs: CARD_IDLE_MS.sculpt,
        postPromise,
        isComplete: (s, snap, base) => meshJobComplete(base, snap, s, cardId),
      });
      applySession(finalSession, false);
      applyConceptCardNote({ session: finalSession }, cardId, { flashOnDone: true });
    } catch (e) {
      setErr(String(e));
      try {
        const d = await api("/api/planner/session");
        if (d.session) applySession(d.session, false);
      } catch {
        /* ignore refresh errors */
      }
    } finally {
      setSculptingCharacterCardId(null);
    }
  };
  const setConceptCardCount = (cardId, count) =>
    run(async () => {
      const d = await api("/api/planner/concept/card/count", {
        method: "POST",
        body: JSON.stringify({ cardId, count }),
      });
      setCardImgErrors((prev) => {
        const next = { ...prev };
        delete next.density;
        return next;
      });
      return d;
    });

  const approveConcept = (approved) =>
    run(async () => {
      const d = await api("/api/planner/concept/approve", {
        method: "POST",
        body: JSON.stringify({
          approved,
          feedback: approved ? "" : input.trim() || "Please revise the concept.",
        }),
      });
      if (!approved) setInput("");
      return d;
    });

  const approveResearch = (approved) =>
    run(async () => {
      const items = session?.researchBundle?.items || [];
      const d = await api("/api/planner/research/approve", {
        method: "POST",
        body: JSON.stringify({
          approved,
          selectedIds: items.map((i) => i.id),
        }),
      });
      return d;
    });

  const approvePlan = (approved) =>
    run(async () => {
      const d = await api("/api/planner/plan/approve", {
        method: "POST",
        body: JSON.stringify({ approved }),
      });
      return d;
    });

  const cancel = () =>
    run(async () => {
      await api("/api/build/cancel", { method: "POST" });
      setSession({ phase: "cancelled", messages: [] });
      return null;
    });

  const resetPlanner = (mode) =>
    run(async () => {
      const d = await api("/api/planner/reset", {
        method: "POST",
        body: JSON.stringify({ mode }),
      });
      if (mode === "full") {
        setStarted(false);
        setSession(null);
        setInput("");
        return null;
      }
      return d;
    });

  const lastAssistantIdx = useMemo(() => {
    for (let i = messages.length - 1; i >= 0; i -= 1) {
      if (messages[i].role === "assistant") return i;
    }
    return -1;
  }, [messages]);

  const showChecklist =
    started && checklist.length > 0 && phase !== "cancelled";

  const conceptApprovalPanel =
    phase === "awaiting_concept_approval" ? (
      <div className="card concept-approval" ref={conceptSectionRef}>
        <h2 className="section">Concept preview</h2>
        <p className="sub">
          Every image below is a generation contract for Unity. <strong>Revise</strong> regenerates
          that card only — layout/density maps redraw; kit cards cycle to the next prefab in the
          pool. <strong>Accept</strong> locks a card; global buttons approve or reject the whole
          set.
        </p>
        {session?.cardPacedBusy && (
          <p className="concept-action-note kit-export-note" role="status">
            <span className="spinner inline-spinner" aria-hidden />
            One card at a time — paced 3D preview / mesh queue (
            {(session.cardPacedQueue || []).length} waiting)
          </p>
        )}
        {session?.kitCatalogExporting && (
          <p className="concept-action-note kit-export-note" role="status">
            <span className="spinner inline-spinner" aria-hidden />
            {session.kitCatalogExportMessage ||
              "Unity is rendering 3D kit previews for prop cards…"}
          </p>
        )}
        {!session?.kitCatalogExporting && session?.kitCatalogExportError && (
          <p className="err concept-kit-export-err" role="status">
            {session.kitCatalogExportError}
          </p>
        )}
        {cardActionNote?.message && (
          <p className="concept-action-note" role="status">
            {cardActionNote.message}
          </p>
        )}
        {err && phase === "awaiting_concept_approval" && (
          <p className="err concept-kit-export-err" role="alert">
            {err}
          </p>
        )}
        {(session?.conceptCardsAccepted != null || session?.conceptCardsTotal > 0) && (
          <p className="hint concept-progress">
            {(session.conceptCardsAccepted ?? 0) === 0
              ? `None accepted yet — review each card, then click Accept (${session.conceptCardsTotal ?? 0} total)`
              : `${session.conceptCardsAccepted ?? 0} of ${session.conceptCardsTotal ?? 0} cards accepted`}
          </p>
        )}
        <div className="concept-card-grid">
          {(session?.conceptCards || []).map((card) => (
            <ConceptImageCard
              key={card.id}
              card={card}
              busy={busy}
              revising={revisingCardId === card.id}
              accepting={acceptingCardId === card.id}
              generatingMesh={generatingMeshCardId === card.id}
              sculptingCharacter={sculptingCharacterCardId === card.id}
              cardJob={
                card.jobProgress ||
                (session?.cardPacedActive?.cardId === card.id ? session.cardPacedActive : null)
              }
              justRevised={flashedCardId === card.id}
              onRevise={reviseConceptCard}
              onAccept={acceptConceptCard}
              onUnaccept={unacceptConceptCard}
              onSetCount={setConceptCardCount}
              onGenerateMesh={generateConceptCardMesh}
              onSculptCharacter={sculptConceptCharacter}
              imgError={Boolean(cardImgErrors[card.id])}
              onImgError={() => setCardImgErrors((prev) => ({ ...prev, [card.id]: true }))}
              onImgLoad={() =>
                setCardImgErrors((prev) => {
                  if (!prev[card.id]) return prev;
                  const next = { ...prev };
                  delete next[card.id];
                  return next;
                })
              }
              conceptImageTs={session?.conceptImageTs}
            />
          ))}
        </div>
        {!session?.conceptCards?.length && (
          <div className="concept-fallback">Generating concept cards…</div>
        )}
        <div className="actions concept-global-actions">
          <button
            type="button"
            className="primary"
            disabled={busy}
            onClick={() => approveConcept(true)}
          >
            Accept all cards & continue
          </button>
          <button
            type="button"
            className="ghost"
            disabled={busy}
            onClick={() => approveConcept(false)}
          >
            Reject &amp; revise all
          </button>
        </div>
        <div className="row">
          <label>Revision notes (if rejecting all)</label>
          <input
            type="text"
            value={input}
            onChange={(e) => setInput(e.target.value)}
            placeholder="e.g. more water, less labyrinth"
          />
        </div>
      </div>
    ) : null;

  return (
    <>
      {wizardRecording ? (
        <div className="recording-bar capture-exclude" aria-live="polite">
          <span className="recording-dot" />
          <span className="recording-label">Recording</span>
          <span className="recording-phase">Pre-production</span>
          <span className="recording-hint">
            Planner UI capture — stops when you finalize the plan; Unity Scene timelapse continues in the Editor
          </span>
        </div>
      ) : null}
      {chapterVisible && chapterTitle ? (
        <div className="chapter-toast capture-exclude" aria-live="polite">
          <span className="chapter-toast-kicker">Chapter</span>
          <span className="chapter-toast-title">{chapterTitle}</span>
        </div>
      ) : null}
      <div
        className={`wrap ${started ? "wrap-wide" : ""} ${wizardRecording ? "wrap-recording" : ""}`}
        ref={wrapRef}
      >
      <header className="planner-hero">
        <p className="planner-kicker">Environment Kit</p>
        <h1>AI Build Planner</h1>
        <p className="sub">
          Pre-production briefing — checklist Q&amp;A, concept approval
          {internetResearch ? ", research" : ""}, and plan sign-off before world generation begins in Unity.
        </p>
      </header>

      {hubMissing && (
        <div className="card hub-missing-card" role="alert">
          <p className="err">
            This tab is missing <code>?hub=</code> — the planner cannot talk to your Unity project.
          </p>
          <p className="hint">
            Reopen from Unity: <strong>Window → Environment Kit → Hub</strong>, or paste this URL:
          </p>
          <p className="hub-missing-url">
            <code>{`http://127.0.0.1:8766/?hub=/Users/jacob/Hub`}</code>
          </p>
          <button
            type="button"
            className="primary"
            onClick={() => {
              window.location.href = `/?hub=${encodeURIComponent("/Users/jacob/Hub")}`;
            }}
          >
            Open with /Users/jacob/Hub
          </button>
        </div>
      )}

      {!started && (
        <div className="card">
          <label className="row check">
            <input
              type="checkbox"
              checked={internetResearch}
              onChange={(e) => setInternetResearch(e.target.checked)}
            />
            <span>
              Internet research (after concept approval — sources saved to ResearchCache)
            </span>
          </label>
          <div className="row">
            <label>What do you want to build?</label>
            <textarea
              rows={4}
              value={input}
              onChange={(e) => setInput(e.target.value)}
              placeholder="e.g. 9-tile playable demo with props, no mountains, fast to record today"
            />
          </div>
          <button type="button" className="primary" disabled={busy || !input.trim() || hubMissing} onClick={start}>
            Start planning
          </button>

          <div className="ai-responder-block">
            <h2 className="section">AI Responder</h2>
            <p className="hint">
              AI Responder fills the full checklist via live chat — AI Planner and AI Responder
              stream word-by-word, one turn at a time — then stops at concept approval for you.
            </p>
            <div className="ai-preset-row">
              {AI_PRESETS.map((p) => (
                <button
                  key={p.id}
                  type="button"
                  className={`ai-preset-btn ai-preset-${p.id}`}
                  disabled={busy || hubMissing}
                  onClick={() => autoRespond(p.id)}
                >
                  <span className="ai-preset-title">{p.label}</span>
                  <span className="ai-preset-hint">{p.hint}</span>
                </button>
              ))}
            </div>
          </div>
        </div>
      )}

      {started && (
        <div className={`planner-grid ${showChecklist ? "with-checklist" : ""}`}>
          <div className="planner-main">
            <div className="phase-bar">
              Phase:{" "}
              <strong>
                {PHASE_LABELS[phase] || phase || (aiActive ? "Working…" : "—")}
              </strong>
              {session?.internetResearch && <span className="pill active">Research ON</span>}
            </div>

            {conceptApprovalPanel}

            <div className={`chat${phase === "awaiting_concept_approval" ? " chat-transcript" : ""}`}>
              {(aiActive || liveStreamingText) && phase !== "awaiting_concept_approval" && (
                <h3 className="chat-transcript-title live-conversation">Live conversation</h3>
              )}
              {phase === "awaiting_concept_approval" && (
                <h3 className="chat-transcript-title">Planning transcript</h3>
              )}
              {messages.map((m, i) => {
                const isAutoUser =
                  m.role === "user" && (session?.autoRespondActive || session?.autoRespondPreset);
                const roleLabel =
                  m.role === "user" ? (isAutoUser ? "AI Responder" : "You") : "Planner";
                const body = displayChatContent(m.content);
                const shouldAnimate =
                  m.role === "assistant" &&
                  !animatedMsgRef.current.has(i) &&
                  (typeLastAssistant || session?.autoRespondActive) &&
                  i === lastAssistantIdx;
                return (
                  <div key={i} className={`bubble ${m.role}`}>
                    <div className="role">{roleLabel}</div>
                    <div className="text">
                      {shouldAnimate ? (
                        <TypedBubble
                          content={body}
                          animate
                          onTypingDone={() => {
                            animatedMsgRef.current.add(i);
                            if (i === lastAssistantIdx) setTypeLastAssistant(false);
                          }}
                        />
                      ) : (
                        body
                      )}
                    </div>
                  </div>
                );
              })}
              {(liveStreamingText || assistantWorking) && (
                <StreamingBubble
                  content={displayChatContent(liveStreamingText || "")}
                  role={session?.streamingRole || "assistant"}
                />
              )}
              <div ref={bottomRef} />
            </div>

            {assistantWorking && (
              <div className="working-bar" role="status" aria-live="polite">
                <span className="spinner" />
                <span>
                  {autoRespondLabel
                    ? `AI Responder — ${autoRespondLabel}…`
                    : liveStreamingText
                      ? "Streaming reply…"
                      : "AI Assistant is working…"}
                </span>
              </div>
            )}

            {needsPlannerRetry && (
              <div className="composer card planner-retry-card">
                <p className="hint">
                  The planner did not finish its reply (often a temporary Cursor API hiccup).
                </p>
                <button type="button" className="primary" disabled={busy} onClick={retryPlannerReply}>
                  Retry reply
                </button>
              </div>
            )}

            {phase === "qna" && !session?.awaitingAssistantReply && !assistantWorking && (
              <div className="composer card">
                <div className="ai-responder-inline">
                  <span className="ai-responder-label">AI Responder</span>
                  <div className="ai-preset-row ai-preset-row-compact">
                    {AI_PRESETS.map((p) => (
                      <button
                        key={p.id}
                        type="button"
                        className={`ai-preset-btn ai-preset-${p.id}`}
                        disabled={busy}
                        onClick={() => autoRespond(p.id)}
                        title={p.hint}
                      >
                        {p.label}
                      </button>
                    ))}
                  </div>
                </div>
                <textarea
                  rows={2}
                  value={input}
                  onChange={(e) => setInput(e.target.value)}
                  placeholder={
                    messages.some((m) => m.role === "assistant")
                      ? "Answer the planner's question… (or move on / change checklist option 6)"
                      : "Add more detail about your build…"
                  }
                  onKeyDown={(e) => {
                    if (e.key === "Enter" && !e.shiftKey) {
                      e.preventDefault();
                      sendChat();
                    }
                  }}
                />
                <button
                  type="button"
                  className="primary"
                  disabled={busy || !input.trim()}
                  onClick={sendChat}
                >
                  Send
                </button>
              </div>
            )}

            {phase === "research" && (
              <div className="card">
                <h2 className="section">Web research</h2>
                <p className="sub">AI Assistant is searching the web for sources relevant to your build…</p>
              </div>
            )}

            {phase === "awaiting_research_approval" && session.researchBundle && (
              <div className="card">
                <h2 className="section">Web research — review sources</h2>
                <p className="sub">
                  Gathered by <strong>AI Assistant</strong> (web search). Approved items save to ResearchCache,
                  then planning continues.
                </p>
                {session.researchError && (
                  <p className="err">Research failed: {session.researchError}</p>
                )}
                {!(session.researchBundle.items || []).length && !session.researchError && (
                  <p className="sub">No sources returned — try Re-gather research.</p>
                )}
                <ul className="research-list">
                  {(session.researchBundle.items || []).map((item) => (
                    <li key={item.id}>
                      <a href={item.url} target="_blank" rel="noreferrer">
                        {item.title}
                      </a>
                      <span className="hint"> — {item.category}</span>
                      {item.summary && <p className="hint research-summary">{item.summary}</p>}
                    </li>
                  ))}
                </ul>
                <div className="actions">
                  <button
                    type="button"
                    className="primary"
                    disabled={busy}
                    onClick={() => approveResearch(true)}
                  >
                    Approve research &amp; continue
                  </button>
                  <button
                    type="button"
                    className="ghost"
                    disabled={busy}
                    onClick={() => approveResearch(false)}
                  >
                    Re-gather research
                  </button>
                </div>
              </div>
            )}

            {phase === "awaiting_plan_approval" && (
              <div className="card">
                <h2 className="section">Final plan — confirm before build</h2>
                <pre className="plan">{session.planSummary}</pre>
                <div className="actions">
                  <button
                    type="button"
                    className="primary"
                    disabled={busy}
                    onClick={() => approvePlan(true)}
                  >
                    Approve plan &amp; start build in Unity
                  </button>
                  <button
                    type="button"
                    className="ghost"
                    disabled={busy}
                    onClick={() => approvePlan(false)}
                  >
                    Back to Q&A
                  </button>
                </div>
              </div>
            )}

            {phase === "finalized" && (
              <div className="card status-box">
                <p className="status">
                  Plan finalized — switching to Unity, opening the Hub, and starting the build
                  with demo recording.
                </p>
                <p className="hint">
                  To test approve again: reset below, then in Unity click{" "}
                  <strong>Build Complete Cave</strong> once to re-open the planner gate.
                </p>
                <div className="actions">
                  <button
                    type="button"
                    className="primary"
                    disabled={busy}
                    onClick={() => resetPlanner("plan")}
                  >
                    Reset to plan approval
                  </button>
                  <button
                    type="button"
                    className="ghost"
                    disabled={busy}
                    onClick={() => resetPlanner("full")}
                  >
                    Start over
                  </button>
                </div>
              </div>
            )}

            {phase === "cancelled" && (
              <div className="card status-box">
                <p className="status">Build cancelled.</p>
                <div className="actions">
                  <button
                    type="button"
                    className="primary"
                    disabled={busy}
                    onClick={() => resetPlanner("full")}
                  >
                    Start over
                  </button>
                </div>
              </div>
            )}

            {phase !== "finalized" && phase !== "cancelled" && (
              <button type="button" className="ghost cancel-btn" disabled={busy} onClick={cancel}>
                Cancel build
              </button>
            )}
          </div>

          {showChecklist && (
            <ChecklistPanel items={checklist} phase={phase} cursorActive={checklistBusy} />
          )}
        </div>
      )}

      {err && <p className="err">{err}</p>}
      </div>
    </>
  );
}
