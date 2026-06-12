import { useCallback, useEffect, useRef, useState } from "react";
import { toBlob } from "html-to-image";

const CAPTURE_MS = 900;

async function captureRootPng(rootEl) {
  if (!rootEl) return null;
  const scale = Math.min(2, window.devicePixelRatio || 1.5);
  try {
    return await toBlob(rootEl, {
      type: "image/png",
      pixelRatio: scale,
      backgroundColor: "#12141a",
      cacheBust: true,
      skipAutoScale: false,
      filter: (node) => {
        const tag = node?.tagName || "";
        if (tag === "VIDEO" || tag === "IFRAME") return false;
        const cls = node?.classList;
        if (cls?.contains("capture-exclude")) return false;
        if (cls?.contains("recording-bar")) return false;
        if (cls?.contains("chapter-toast")) return false;
        return true;
      },
    });
  } catch {
    return null;
  }
}

export function useWizardRecorder({ hub, api, started, session, rootRef }) {
  const [armed, setArmed] = useState(false);
  const [recording, setRecording] = useState(false);
  const [chapterTitle, setChapterTitle] = useState("");
  const [chapterVisible, setChapterVisible] = useState(false);
  const shownChaptersRef = useRef(new Set());
  const activeJobChapterRef = useRef("");
  const frameTimerRef = useRef(null);
  const chapterTimerRef = useRef(null);
  const captureBusyRef = useRef(false);
  const wasCapturingRef = useRef(false);
  const finalizeSentRef = useRef(false);

  const finalizeWizardCapture = useCallback(async () => {
    if (finalizeSentRef.current) return;
    finalizeSentRef.current = true;
    wasCapturingRef.current = false;
    setArmed(false);
    setRecording(false);
    if (frameTimerRef.current) {
      clearInterval(frameTimerRef.current);
      frameTimerRef.current = null;
    }
    try {
      await api("/api/recording/wizard-finalize", { method: "POST", body: "{}" });
    } catch {
      /* ignore */
    }
    shownChaptersRef.current.clear();
    activeJobChapterRef.current = "";
  }, [api]);

  const showChapterToast = useCallback((title) => {
    setChapterTitle(title);
    setChapterVisible(true);
    if (chapterTimerRef.current) clearTimeout(chapterTimerRef.current);
    chapterTimerRef.current = setTimeout(() => setChapterVisible(false), 2200);
  }, []);

  const markChapterOnce = useCallback(
    async (chapterId, title, phase = "") => {
      if (!armed) return;
      if (shownChaptersRef.current.has(chapterId)) return;
      shownChaptersRef.current.add(chapterId);
      showChapterToast(title);
      try {
        await api("/api/recording/chapter", {
          method: "POST",
          body: JSON.stringify({ chapterId, title, phase }),
        });
      } catch {
        /* ignore */
      }
    },
    [api, armed, showChapterToast]
  );

  const uploadFrame = useCallback(async () => {
    const root = rootRef?.current;
    if (!root || !armed || captureBusyRef.current) return;
    captureBusyRef.current = true;
    try {
      const blob = await captureRootPng(root);
      if (!blob) return;
      const buf = await blob.arrayBuffer();
      const bytes = new Uint8Array(buf);
      let binary = "";
      for (let i = 0; i < bytes.length; i += 1) binary += String.fromCharCode(bytes[i]);
      const b64 = btoa(binary);
      const d = await api("/api/recording/wizard-frame", {
        method: "POST",
        body: JSON.stringify({ pngBase64: b64 }),
      });
      if (d?.ok === false && /finalized/i.test(d?.message || "")) {
        await finalizeWizardCapture();
      }
    } catch {
      /* ignore capture errors */
    } finally {
      captureBusyRef.current = false;
    }
  }, [api, armed, rootRef, finalizeWizardCapture]);

  useEffect(() => {
    if (!started || !hub) return undefined;
    let cancelled = false;
    const poll = async () => {
      try {
        const d = await api("/api/recording/session");
        if (cancelled) return;
        const rec = d?.recording || {};
        const uiCapture =
          rec.wizardUiCapture != null ? Boolean(rec.wizardUiCapture) : Boolean(rec.recording);
        setArmed(uiCapture);
        setRecording(uiCapture);
        if (uiCapture) {
          wasCapturingRef.current = true;
          finalizeSentRef.current = false;
        }
      } catch {
        if (!cancelled) {
          setArmed(false);
          setRecording(false);
        }
      }
    };
    poll();
    const id = setInterval(poll, 900);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, [started, hub, api]);

  useEffect(() => {
    if (!recording) {
      if (frameTimerRef.current) clearInterval(frameTimerRef.current);
      return undefined;
    }
    uploadFrame();
    frameTimerRef.current = setInterval(uploadFrame, CAPTURE_MS);
    markChapterOnce("wizard_open", "AI Planning Session", "wizard_open");
    return () => {
      if (frameTimerRef.current) clearInterval(frameTimerRef.current);
    };
  }, [recording, uploadFrame, markChapterOnce]);

  const phase = session?.phase || "";
  const kitExporting = Boolean(session?.kitCatalogExporting);
  const jobType = session?.cardPacedActive?.jobType || "";
  const jobCardId = session?.cardPacedActive?.cardId || "";

  useEffect(() => {
    if (!recording) return;
    if (phase === "qna") {
      markChapterOnce("planning_qna", "Planning & Q&A", phase);
    } else if (phase === "awaiting_concept_approval") {
      markChapterOnce("concept_review", "Concept Review", phase);
    } else if (phase === "research") {
      markChapterOnce("research", "Research & Sources", phase);
    } else if (phase === "plan") {
      markChapterOnce("build_plan", "Build Plan Review", phase);
    }
  }, [recording, phase, markChapterOnce]);

  useEffect(() => {
    if (!recording || !kitExporting) return;
    markChapterOnce("asset_previews", "3D Asset Previews", "kit_catalog");
  }, [recording, kitExporting, markChapterOnce]);

  useEffect(() => {
    if (!recording || !jobType) return;
    const jobKey = `${jobType}:${jobCardId}`;
    if (activeJobChapterRef.current === jobKey) return;
    activeJobChapterRef.current = jobKey;
    if (jobType === "generate_mesh") {
      markChapterOnce(`mesh:${jobCardId}`, "AI Mesh Generation", "mesh");
    } else if (jobType === "sculpt_character") {
      markChapterOnce(`sculpt:${jobCardId}`, "AI Character Morph", "sculpt");
    } else if (jobType === "load_preview") {
      markChapterOnce(`preview:${jobCardId}`, "3D Preview Pipeline", "preview");
    }
  }, [recording, jobType, jobCardId, markChapterOnce]);

  useEffect(() => {
    if (phase !== "finalized") return;
    finalizeWizardCapture();
  }, [phase, finalizeWizardCapture]);

  useEffect(() => {
    if (!hub) return undefined;
    const finalizeOnLeave = () => {
      if (!wasCapturingRef.current && !recording) return;
      const q = `?hub=${encodeURIComponent(hub)}`;
      try {
        navigator.sendBeacon(
          `/api/recording/wizard-finalize${q}`,
          new Blob(["{}"], { type: "application/json" })
        );
      } catch {
        /* ignore */
      }
      wasCapturingRef.current = false;
      finalizeSentRef.current = true;
    };
    window.addEventListener("pagehide", finalizeOnLeave);
    return () => window.removeEventListener("pagehide", finalizeOnLeave);
  }, [hub, recording]);

  return {
    recording,
    chapterTitle,
    chapterVisible,
    markChapter: markChapterOnce,
    finalizeWizardCapture,
  };
}
