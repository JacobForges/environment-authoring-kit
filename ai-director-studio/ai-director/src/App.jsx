import { useCallback, useEffect, useRef, useState } from "react";
import BriefingView from "./BriefingView.jsx";
import ScriptStudioView from "./ScriptStudioView.jsx";
import MediaIngestView from "./MediaIngestView.jsx";
import LandingView from "./LandingView.jsx";
import MusicSetupView from "./MusicSetupView.jsx";
import MusicBriefingView from "./MusicBriefingView.jsx";
import MusicStepper from "./MusicStepper.jsx";
import MusicRecordView from "./MusicRecordView.jsx";
import MusicProduceView from "./MusicProduceView.jsx";

const PACING_KEYS = [
  { key: "milestoneHoldSec", label: "Milestone hold (sec)" },
  { key: "subbeatHoldSec", label: "Subbeat hold (sec)" },
  { key: "introSec", label: "Intro (sec)" },
  { key: "outroSec", label: "Outro (sec)" },
  { key: "timelapseSecPerFrame", label: "Timelapse sec/frame" },
  { key: "captionReadPauseSec", label: "Caption pause (sec)" },
  { key: "segmentXfadeSec", label: "Crossfade (sec)" },
];

const TABS = ["Media", "Preview", "Segments", "Pacing", "Voice", "Script", "Cards", "Export"];

const VOICE_TONES = [
  "warm-documentary",
  "energetic-host",
  "calm-mentor",
  "cinematic-narrator",
  "peer-devlog",
];

const VOICE_CHARACTERS = ["mentor-host", "adventure-guide", "technical-peer", "storyteller"];

const DELIVERY_PRESETS = ["ladderDocumentary", "raw", "tvhost"];

const APPROVED_CARD_KEYS = [
  { key: "ApprovedIntro.png", label: "Intro" },
  { key: "_approval_intro_c.png", label: "Intro (canonical)" },
  { key: "ApprovedOutro.png", label: "Outro" },
  { key: "_approval_outro_b.png", label: "Outro (canonical)" },
  { key: "_approval_portrait.png", label: "Portrait" },
];

function captureFromUrl() {
  return new URLSearchParams(window.location.search).get("capture") || "";
}

function musicModeFromUrl() {
  const p = new URLSearchParams(window.location.search);
  return p.get("music") === "1" || p.get("mode") === "music";
}

function capQ(capture) {
  return capture ? `?path=${encodeURIComponent(capture)}` : "";
}

async function api(path, opts = {}) {
  const res = await fetch(path, {
    ...opts,
    headers: { "Content-Type": "application/json", ...(opts.headers || {}) },
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || res.statusText);
  return data;
}

export default function App() {
  const [capture, setCapture] = useState(captureFromUrl());
  const [musicMode, setMusicMode] = useState(musicModeFromUrl);
  const [musicStep, setMusicStep] = useState("briefing");
  const [musicProject, setMusicProject] = useState("");
  const [musicSession, setMusicSession] = useState(null);
  const [musicChatInput, setMusicChatInput] = useState("");
  const [musicAutoRunning, setMusicAutoRunning] = useState(false);
  const [musicInternetResearch, setMusicInternetResearch] = useState(true);
  const musicAutoProceedRef = useRef(false);
  const musicSessionStartRef = useRef(false);
  const [briefingEntered, setBriefingEntered] = useState(false);
  const [projectStatus, setProjectStatus] = useState(null);
  const [tab, setTab] = useState("Preview");
  const [status, setStatus] = useState(null);
  const [session, setSession] = useState(null);
  const [segments, setSegments] = useState([]);
  const [timeline, setTimeline] = useState(null);
  const [persona, setPersona] = useState(null);
  const [voiceProfile, setVoiceProfile] = useState(null);
  const [testPhrase, setTestPhrase] = useState("");
  const [voiceTestKey, setVoiceTestKey] = useState(0);
  const [script, setScript] = useState("");
  const [scriptDirty, setScriptDirty] = useState(false);
  const [approvedAssets, setApprovedAssets] = useState(null);
  const [composeLive, setComposeLive] = useState("");
  const [chatInput, setChatInput] = useState("");
  const [scriptChatInput, setScriptChatInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [busyKind, setBusyKind] = useState(null);
  const [autoRunning, setAutoRunning] = useState(false);
  const [err, setErr] = useState("");
  const [assetsVerified, setAssetsVerified] = useState(false);
  const phraseLoadedRef = useRef(false);
  const actionGuard = useRef(false);
  const presentationBusyRef = useRef(false);
  const sessionRef = useRef(null);

  useEffect(() => {
    sessionRef.current = session;
  }, [session]);

  const waitForPresentationIdle = useCallback(async () => {
    for (let i = 0; i < 800; i += 1) {
      const s = sessionRef.current;
      const serverBusy = Boolean(
        s?.assistantWorking || s?.responderWorking || s?.streamingText
      );
      if (!presentationBusyRef.current && !serverBusy) {
        await new Promise((r) => setTimeout(r, 150));
        if (!presentationBusyRef.current && !sessionRef.current?.assistantWorking) return;
      }
      await new Promise((r) => setTimeout(r, 70));
    }
  }, []);

  const handlePresentationBusy = useCallback((busyNow) => {
    presentationBusyRef.current = busyNow;
  }, []);

  const withGuard = async (fn, kind = "other") => {
    if (actionGuard.current) return null;
    actionGuard.current = true;
    setBusyKind(kind);
    setBusy(true);
    try {
      return await fn();
    } catch (e) {
      setErr(String(e.message || e));
      return null;
    } finally {
      actionGuard.current = false;
      setBusy(false);
      setBusyKind(null);
    }
  };

  const composeRunning = status?.composeJob?.running;
  const presentationReady = status?.presentationHasAudio;
  const silentReady = Boolean(status?.silentVideo);
  const gate = status?.composeGate;
  const phase = session?.phase || "landing";
  const inBriefing =
    briefingEntered &&
    Boolean(session?.ok !== false && session?.checklist?.length) &&
    (session?.inBriefing ?? (phase === "qna" || phase === "briefing_ready"));
  const mediaReady = Boolean(projectStatus?.mediaIngested && projectStatus?.frameCount > 0);
  const showLanding = !briefingEntered || !capture || !mediaReady;

  const setCapturePath = useCallback((path) => {
    setCapture(path || "");
    const url = new URL(window.location.href);
    if (path) url.searchParams.set("capture", path);
    else url.searchParams.delete("capture");
    window.history.replaceState({}, "", url);
  }, []);

  const refreshProjectStatus = useCallback(async (cap) => {
    if (!cap) {
      setProjectStatus(null);
      return;
    }
    try {
      const st = await api(`/api/project/status?path=${encodeURIComponent(cap)}`);
      setProjectStatus(st);
      if (st.hasSession && st.mediaIngested) {
        setBriefingEntered(true);
      }
    } catch {
      setProjectStatus(null);
    }
  }, []);

  const refresh = useCallback(async () => {
    if (!capture) {
      setProjectStatus(null);
      return;
    }
    await refreshProjectStatus(capture);
    if (!briefingEntered) return;

    const q = capQ(capture);
    const [st, sess, segs, tl, profile, live, narr, assets] = await Promise.all([
      api(`/api/capture/status${q}`),
      api(`/api/director/session${q}`).catch(() => null),
      api(`/api/director/segments${q}`).catch(() => ({ segments: [] })),
      api(`/api/capture/timeline${q}`).catch(() => null),
      api(`/api/director/voice-profile${q}`).catch(() => null),
      api(`/api/capture/compose/live${q}`).catch(() => ({ text: "" })),
      api(`/api/capture/narration/review${q}`).catch(() => null),
      api("/api/approved/assets").catch(() => null),
    ]);
    setStatus(st);
    if (sess?.ok !== false) {
      setSession(sess);
      if (sess?.checklist?.length) setBriefingEntered(true);
    } else if (sess?.noSession) {
      setSession(null);
    }
    setSegments(segs.segments || []);
    if (tl) setTimeline(tl);
    if (profile) {
      setVoiceProfile(profile);
      setPersona(profile.persona || {});
      if (!phraseLoadedRef.current) {
        setTestPhrase(profile.testPhrase || "");
        phraseLoadedRef.current = true;
      }
    }
    setApprovedAssets(assets);
    setComposeLive(live.markdown || live.text || "");
    setAssetsVerified(Boolean(st?.composeGate?.assetsVerified));
    if (!scriptDirty) {
      if (narr?.guideMarkdown) setScript(narr.guideMarkdown);
      else if (narr?.scriptText) setScript(narr.scriptText);
    }
  }, [capture, scriptDirty, briefingEntered, refreshProjectStatus]);

  useEffect(() => {
    refresh().catch((e) => setErr(String(e.message || e)));
  }, [capture, briefingEntered]);

  useEffect(() => {
    if (capture) refreshProjectStatus(capture).catch(() => {});
  }, [capture, refreshProjectStatus]);

  useEffect(() => {
    if (!capture || inBriefing) return;
    api(`/api/director/voice-profile/sync${capQ(capture)}`, {
      method: "POST",
      body: JSON.stringify({ capture }),
    }).catch(() => {});
  }, [capture, inBriefing]);

  useEffect(() => {
    let ms = 10000;
    if (inBriefing) {
      ms =
        autoRunning ||
        session?.assistantWorking ||
        session?.responderWorking ||
        session?.streamingText
          ? 350
          : 5000;
    } else if (tab === "Script" && session?.scriptAssistantWorking) ms = 1500;
    else if (composeRunning) ms = 2500;
    const t = setInterval(() => {
      refresh().catch(() => {});
    }, ms);
    return () => clearInterval(t);
  }, [
    refresh,
    composeRunning,
    autoRunning,
    session?.assistantWorking,
    session?.responderWorking,
    session?.streamingText,
    session?.scriptAssistantWorking,
    inBriefing,
    tab,
  ]);

  const inputLocked =
    busy ||
    autoRunning ||
    session?.assistantWorking ||
    session?.responderWorking ||
    session?.inputBlocked;

  const sendChat = async () => {
    if (!capture || !chatInput.trim() || inputLocked) return;
    await waitForPresentationIdle();
    const message = chatInput.trim();
    await withGuard(async () => {
      const sess = await api(`/api/director/chat${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture, message }),
      });
      if (sess.duplicateRequest) {
        setErr(sess.blockReason || "Please wait — your last message is still processing.");
        return;
      }
      setSession(sess);
      setChatInput("");
    }, "director");
  };

  const enterEditor = async () =>
    withGuard(async () => {
      const sess = await api(`/api/director/enter-editor${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture }),
      });
      setSession(sess);
      await refresh();
    });

  const returnToBriefing = async () =>
    withGuard(async () => {
      const sess = await api(`/api/director/return-briefing${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture }),
      });
      setSession(sess);
    });

  const runResponder = async () => {
    if (!capture || inputLocked) return;
    setErr("");
    await waitForPresentationIdle();
    setAutoRunning(true);
    const pollMs = 350;
    const pollId = window.setInterval(() => {
      refresh().catch(() => {});
    }, pollMs);
    try {
      const r = await api(`/api/director/responder/run-until-done${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture, preset: "student-portfolio" }),
      });
      if (!r.ok && r.error) setErr(r.error);
      if (r.session) setSession(r.session);
    } catch (e) {
      setErr(String(e.message || e));
    } finally {
      window.clearInterval(pollId);
      await refresh().catch(() => {});
      setAutoRunning(false);
      await waitForPresentationIdle();
    }
  };

  const verifyCards = async () =>
    withGuard(async () => {
      await api(`/api/capture/compose/gate/verify${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture }),
      });
      await refresh();
    });

  const proceedCompose = async () => {
    if (composeRunning || !assetsVerified) return;
    await withGuard(async () => {
      await api(`/api/director/apply-brief${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture }),
      });
      await api(`/api/capture/compose/start${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture }),
      });
      await api(`/api/director/phase${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture, phase: "awaiting_compose" }),
      });
      await refresh();
    });
  };

  const retryCompose = async () => {
    if (composeRunning) return;
    await withGuard(async () => {
      await api(`/api/capture/compose/start${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture }),
      });
      await refresh();
    });
  };

  const saveTimeline = async () =>
    withGuard(async () => {
      await api(`/api/capture/timeline${capQ(capture)}`, {
        method: "PUT",
        body: JSON.stringify(timeline),
      });
    });

  const savePersona = async (patch) => {
    const merged = { ...persona, ...patch, testPhrase: patch.testPhrase ?? testPhrase };
    setPersona(merged);
    await withGuard(async () => {
      const r = await api(`/api/director/voice-persona${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture, persona: merged, testPhrase: merged.testPhrase }),
      });
      setPersona(r.persona);
      if (r.profile) setVoiceProfile(r.profile);
    });
  };

  const testVoice = async () =>
    withGuard(async () => {
      const r = await api(`/api/director/voice-test${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture, text: testPhrase, openPlayer: true }),
      });
      if (!r.ok) throw new Error(r.error || "Voice test failed");
      setVoiceTestKey((k) => k + 1);
      await refresh();
    });

  const syncVoiceProfile = async () =>
    withGuard(async () => {
      const p = await api(`/api/director/voice-profile/sync${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture }),
      });
      setVoiceProfile(p);
      setPersona(p.persona || {});
      setTestPhrase(p.testPhrase || "");
    });

  const saveScript = async () =>
    withGuard(async () => {
      await api(`/api/director/script/save${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture, markdown: script }),
      });
      setScriptDirty(false);
    });

  const assistantVoice = async () =>
    withGuard(async () => {
      const r = await api(`/api/director/voice-persona/assistant-adjust${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({
          capture,
          instruction: persona?.personaNotes?.trim() || "Tune for warm documentary devlog mentor tone.",
        }),
      });
      setPersona(r.persona);
      if (r.assistantMessage) {
        setSession((s) => ({
          ...s,
          messages: [...(s?.messages || []), { role: "assistant", content: r.assistantMessage }],
        }));
      }
    });

  const regenScript = async () =>
    withGuard(async () => {
      await api(`/api/capture/narration/review/regenerate${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture, useCursor: true }),
      });
      await refresh();
    });

  const bootstrapScriptChat = useCallback(async () => {
    if (!capture) return;
    try {
      const r = await api(`/api/director/script/chat${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture, bootstrap: true }),
      });
      if (r.session) setSession(r.session);
    } catch (e) {
      setErr(String(e.message || e));
    }
  }, [capture]);

  const sendScriptChat = async () => {
    if (!capture || !scriptChatInput.trim() || busy || session?.scriptAssistantWorking) return;
    const message = scriptChatInput.trim();
    await withGuard(async () => {
      const r = await api(`/api/director/script/chat${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture, message }),
      });
      if (r.session) setSession(r.session);
      if (r.scriptMarkdown) {
        setScript(r.scriptMarkdown);
        setScriptDirty(false);
      }
      if (!r.duplicateRequest) setScriptChatInput("");
    });
  };

  const approveVoice = async () => {
    if (composeRunning) return;
    await withGuard(async () => {
      await api(`/api/capture/narration/review/approve${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture, startPersonalVoice: true }),
      });
      await api(`/api/director/phase${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture, phase: "awaiting_final" }),
      });
      await refresh();
    });
  };

  const exportYoutube = async () =>
    withGuard(async () => {
      const r = await api(`/api/director/export/youtube${capQ(capture)}`, {
        method: "POST",
        body: JSON.stringify({ capture }),
      });
      alert(r.message || "Opened YouTube Studio — title & description copied.");
      await refresh();
    });

  const previewVideoUrl = presentationReady
    ? `/api/director/presentation/video${capQ(capture)}&kind=final`
    : status?.silentVideo
      ? `/api/director/presentation/video${capQ(capture)}&kind=silent`
      : null;

  const segmentVideoUrl = (seg) =>
    `/api/director/segment/video${capQ(capture)}&name=${encodeURIComponent(seg.name)}&work=${encodeURIComponent(seg.workDir)}`;

  const musicPath = musicProject || capture;
  const musicInputLocked =
    busy ||
    musicAutoRunning ||
    musicSession?.assistantWorking ||
    musicSession?.responderWorking ||
    musicSession?.inputBlocked;

  const refreshMusicSession = useCallback(async () => {
    if (!musicPath) return;
    try {
      const sess = await api(`/api/music/session?path=${encodeURIComponent(musicPath)}`);
      if (sess?.ok !== false) setMusicSession(sess);
      else if (sess?.noSession) setMusicSession(null);
    } catch {
      setMusicSession(null);
    }
  }, [musicPath]);

  const startMusicSession = useCallback(async () => {
    setErr("");
    setBusy(true);
    try {
      const body = musicPath
        ? { project: musicPath, internetResearch: musicInternetResearch }
        : { name: "track", internetResearch: musicInternetResearch };
      const sess = await api("/api/music/session/start", {
        method: "POST",
        body: JSON.stringify(body),
      });
      if (sess.project) {
        setMusicProject(sess.project);
        setCapturePath(sess.project);
      }
      setMusicSession(sess);
    } catch (e) {
      setErr(String(e.message || e));
    } finally {
      setBusy(false);
    }
  }, [musicPath, setCapturePath, musicInternetResearch]);

  const enterMusicProduction = useCallback(
    async () => {
      if (!musicPath) return;
      await withGuard(async () => {
        const sess = await api("/api/music/enter-production", {
          method: "POST",
          body: JSON.stringify({ project: musicPath, step: "instrumental" }),
        });
        setMusicSession(sess);
        setMusicStep("instrumental");
      });
    },
    [musicPath]
  );

  const sendMusicChat = async () => {
    if (!musicPath || !musicChatInput.trim() || musicInputLocked) return;
    const message = musicChatInput.trim();
    await withGuard(async () => {
      const sess = await api("/api/music/chat", {
        method: "POST",
        body: JSON.stringify({ project: musicPath, message }),
      });
      if (sess.duplicateRequest) {
        setErr(sess.blockReason || "Please wait — your last message is still processing.");
        return;
      }
      setMusicSession(sess);
      setMusicChatInput("");
    }, "director");
  };

  const runMusicResponder = async () => {
    if (!musicPath || musicInputLocked) return;
    setErr("");
    setMusicAutoRunning(true);
    const pollId = window.setInterval(() => refreshMusicSession().catch(() => {}), 350);
    try {
      const r = await api("/api/music/responder/run-until-done", {
        method: "POST",
        body: JSON.stringify({ project: musicPath, preset: "bedroom-trap" }),
      });
      if (!r.ok && r.error) setErr(r.error);
      if (r.session) setMusicSession(r.session);
    } catch (e) {
      setErr(String(e.message || e));
    } finally {
      window.clearInterval(pollId);
      await refreshMusicSession().catch(() => {});
      setMusicAutoRunning(false);
    }
  };

  useEffect(() => {
    if (!musicMode || musicStep !== "briefing") return;
    if (musicSession?.ok || musicSessionStartRef.current) return;
    musicSessionStartRef.current = true;
    startMusicSession().catch(() => {
      musicSessionStartRef.current = false;
    });
  }, [musicMode, musicStep, musicSession?.ok, startMusicSession]);

  useEffect(() => {
    if (!musicMode || musicStep !== "briefing") return;
    if (!musicSession?.checklistComplete) return;
    if (musicAutoProceedRef.current) return;
    musicAutoProceedRef.current = true;
    enterMusicProduction().catch(() => {
      musicAutoProceedRef.current = false;
    });
  }, [musicMode, musicStep, musicSession?.checklistComplete, enterMusicProduction]);

  useEffect(() => {
    if (!musicMode || musicStep !== "briefing") return;
    let ms = 5000;
    if (
      musicAutoRunning ||
      musicSession?.assistantWorking ||
      musicSession?.responderWorking ||
      musicSession?.streamingText
    ) {
      ms = 350;
    }
    const t = setInterval(() => refreshMusicSession().catch(() => {}), ms);
    return () => clearInterval(t);
  }, [
    musicMode,
    musicStep,
    musicAutoRunning,
    musicSession?.assistantWorking,
    musicSession?.responderWorking,
    musicSession?.streamingText,
    refreshMusicSession,
  ]);

  if (musicMode) {
    const stepper = (
      <MusicStepper
        current={musicStep}
        onBack={() => {
          setMusicMode(false);
          setMusicStep("briefing");
          setMusicSession(null);
          musicAutoProceedRef.current = false;
          musicSessionStartRef.current = false;
        }}
      />
    );

    if (musicStep === "briefing") {
      return (
        <>
          {stepper}
          <MusicBriefingView
            project={musicPath}
            session={musicSession}
            chatInput={musicChatInput}
            setChatInput={setMusicChatInput}
            busy={busy || musicAutoRunning}
            busyKind={musicAutoRunning ? "responder" : busyKind}
            inputLocked={musicInputLocked}
            err={err}
            internetResearch={musicInternetResearch}
            onInternetResearchChange={setMusicInternetResearch}
            onSend={sendMusicChat}
            onResponder={runMusicResponder}
            onStartProduction={() => enterMusicProduction()}
            onRefresh={() => refreshMusicSession().catch((e) => setErr(String(e.message || e)))}
            onPresentationBusy={handlePresentationBusy}
            autoRunning={musicAutoRunning}
          />
        </>
      );
    }
    if (musicStep === "instrumental") {
      return (
        <>
          {stepper}
          <MusicSetupView
            project={musicPath}
            fromBriefing
            autoGenerate
            onProjectChange={(path) => {
              setMusicProject(path);
              setCapturePath(path);
            }}
            onNext={() => setMusicStep("record")}
            onBack={() => setMusicStep("briefing")}
          />
        </>
      );
    }
    if (musicStep === "record") {
      return (
        <>
          {stepper}
          <MusicRecordView
            project={musicPath}
            onNext={() => setMusicStep("produce")}
            onBack={() => setMusicStep("instrumental")}
          />
        </>
      );
    }
    if (musicStep === "produce") {
      return (
        <>
          {stepper}
          <MusicProduceView
            project={musicPath}
            onBack={() => setMusicStep("record")}
            onMusicVideo={(videoProject, data) => {
              setMusicMode(false);
              setMusicStep("briefing");
              setCapturePath(videoProject);
              setBriefingEntered(true);
              if (data?.session) setSession(data.session);
              refresh().catch(() => {});
            }}
          />
        </>
      );
    }
  }

  if (showLanding) {
    return (
      <LandingView
        capture={capture}
        initialStatus={projectStatus}
        onSelectMusic={() => {
          setErr("");
          setMusicMode(true);
          setMusicStep("briefing");
          setMusicProject("");
          setMusicSession(null);
          musicAutoProceedRef.current = false;
          musicSessionStartRef.current = true;
          startMusicSession().catch(() => {
            musicSessionStartRef.current = false;
          });
        }}
        onProjectChange={(path) => {
          setCapturePath(path);
          refreshProjectStatus(path).catch(() => {});
        }}
        onContinue={(sess) => {
          setBriefingEntered(true);
          if (sess?.checklist?.length) setSession(sess);
          refresh().catch((e) => setErr(String(e.message || e)));
        }}
      />
    );
  }

  if (inBriefing) {
    return (
      <BriefingView
        capture={capture}
        session={session}
        chatInput={chatInput}
        setChatInput={setChatInput}
        busy={busy || autoRunning}
        busyKind={autoRunning ? "responder" : busyKind}
        inputLocked={inputLocked}
        err={err}
        onSend={sendChat}
        onResponder={runResponder}
        onEnterEditor={enterEditor}
        onRefresh={() => refresh().catch((e) => setErr(String(e.message || e)))}
        onPresentationBusy={handlePresentationBusy}
        autoRunning={autoRunning}
      />
    );
  }

  return (
    <div className="studio-full">
      <header className="studio-topbar">
        <div>
          <strong>AI Director — Studio Editor</strong>
          <div className="muted" style={{ fontSize: "0.75rem", marginTop: "0.2rem" }}>
            {capture || "No capture"}
          </div>
        </div>
        <span className="phase-pill">Phase: {phase.replace(/_/g, " ")}</span>
        <button type="button" className="secondary" onClick={returnToBriefing} disabled={busy}>
          Review brief
        </button>
        <button type="button" className="secondary" onClick={() => refresh()} disabled={busy}>
          Refresh
        </button>
      </header>

        {err ? (
          <div className="panel" style={{ margin: "0.5rem 1rem", borderColor: "#b45309" }}>
            <span className="muted">{err}</span>
          </div>
        ) : null}

        <nav className="tab-bar">
          {TABS.map((t) => (
            <button key={t} type="button" className={tab === t ? "active" : ""} onClick={() => setTab(t)}>
              {t}
            </button>
          ))}
        </nav>

        <div className="studio-panel">
          {tab === "Media" && (
            <MediaIngestView
              capture={capture}
              onProjectChange={(path) => {
                setCapturePath(path);
                refresh().catch(() => {});
              }}
              onIngested={() => refresh().catch(() => {})}
            />
          )}
          {tab === "Preview" && (
            <section className="panel">
              <h2>Final preview</h2>
              <p className="muted">
                {composeRunning
                  ? "Compose in progress — segments appear in the Segments tab."
                  : presentationReady
                    ? "Narrated master ready for export."
                    : silentReady
                      ? "Silent shell ready — approve script & apply Personal Voice."
                      : "Start compose after card verification."}
              </p>
              {previewVideoUrl ? (
                <video key={previewVideoUrl} className="preview-video" controls src={previewVideoUrl} />
              ) : null}
              <div className="compose-live">{composeLive || "Waiting for compose…"}</div>
              <div className="gate-row">
                {!assetsVerified ? (
                  <button type="button" onClick={verifyCards} disabled={busy}>
                    Verify cards
                  </button>
                ) : (
                  <span className="muted">Cards verified</span>
                )}
                <button type="button" onClick={proceedCompose} disabled={busy || !assetsVerified || composeRunning}>
                  Start silent compose
                </button>
                {gate?.proceeded && !presentationReady && !composeRunning ? (
                  <button type="button" className="secondary" onClick={retryCompose} disabled={busy}>
                    Retry compose
                  </button>
                ) : null}
                {silentReady && !presentationReady ? (
                  <button type="button" onClick={approveVoice} disabled={busy || composeRunning}>
                    Approve script &amp; record voice
                  </button>
                ) : null}
              </div>
            </section>
          )}

          {tab === "Segments" && (
            <section className="panel">
              <h2>Segments ({segments.length})</h2>
              <p className="muted">Encoded pieces appear here as compose runs.</p>
              <div className="segment-grid">
                {segments.map((seg) => (
                  <div key={seg.path} className="segment-card">
                    <video src={segmentVideoUrl(seg)} controls muted preload="metadata" />
                    <div className="seg-label">{seg.name}</div>
                  </div>
                ))}
              </div>
              {!segments.length ? <p className="muted">No segments yet.</p> : null}
            </section>
          )}

          {tab === "Pacing" && timeline && (
            <section className="panel">
              <h2>Pacing &amp; length</h2>
              <p className="muted">Studio sliders — saved to DemoRecapTimeline.json on this capture.</p>
              {PACING_KEYS.map(({ key, label }) => (
                <div key={key} className="slider-row">
                  <label>{label}</label>
                  <input
                    type="range"
                    min={key.includes("SecPerFrame") ? 0.5 : 1}
                    max={key.includes("Hold") ? 30 : key.includes("SecPerFrame") ? 4 : 20}
                    step={0.1}
                    value={timeline[key] ?? 0}
                    onChange={(e) => setTimeline({ ...timeline, [key]: parseFloat(e.target.value) })}
                  />
                  <span>{timeline[key]}</span>
                </div>
              ))}
              <button type="button" onClick={saveTimeline} disabled={busy}>
                Save pacing
              </button>
              <button
                type="button"
                className="secondary"
                onClick={async () => {
                  setBusy(true);
                  try {
                    await api(`/api/director/apply-brief${capQ(capture)}`, {
                      method: "POST",
                      body: JSON.stringify({ capture }),
                    });
                    await refresh();
                  } catch (e) {
                    setErr(String(e.message));
                  } finally {
                    setBusy(false);
                  }
                }}
                disabled={busy}
              >
                Apply brief from chat
              </button>
            </section>
          )}

          {tab === "Voice" && persona && (
            <section className="panel">
              <h2>Voice persona</h2>
              <p className="muted">
                Test your Personal Voice here before a full compose — settings save to ApprovedCards.json.
              </p>
              {voiceProfile ? (
                <div>
                  <span className="voice-badge">Personal Voice: {voiceProfile.voiceName}</span>
                  <span className="muted" style={{ marginLeft: "0.5rem" }}>
                    {voiceProfile.sayRate} wpm · {voiceProfile.readyReason}
                  </span>
                </div>
              ) : null}
              <label className="muted">Test phrase</label>
              <textarea
                className="script-editor"
                style={{ minHeight: 64 }}
                value={testPhrase}
                onChange={(e) => setTestPhrase(e.target.value)}
                onBlur={() => savePersona({ testPhrase })}
                placeholder="Words to speak for voice test…"
              />
              {voiceProfile?.hasLastTest ? (
                <audio
                  key={voiceTestKey}
                  className="voice-test-audio"
                  controls
                  src={`/api/director/voice-test/audio${capQ(capture)}&t=${voiceTestKey}`}
                />
              ) : null}
              <div className="gate-row" style={{ marginBottom: "0.75rem" }}>
                <button type="button" onClick={testVoice} disabled={busy}>
                  {busy ? "Testing voice…" : "Test Personal Voice"}
                </button>
                <button type="button" className="secondary" onClick={syncVoiceProfile} disabled={busy}>
                  Reload from studio profile
                </button>
              </div>
              <label className="muted">Delivery preset</label>
              <select
                value={persona.delivery || DELIVERY_PRESETS[0]}
                onChange={(e) => savePersona({ delivery: e.target.value })}
                style={{ width: "100%", marginBottom: "0.5rem" }}
              >
                {DELIVERY_PRESETS.map((d) => (
                  <option key={d} value={d}>
                    {d}
                  </option>
                ))}
              </select>
              <label className="muted">Tone</label>
              <select
                value={persona.tone || VOICE_TONES[0]}
                onChange={(e) => savePersona({ tone: e.target.value })}
                style={{ width: "100%", marginBottom: "0.5rem" }}
              >
                {VOICE_TONES.map((t) => (
                  <option key={t} value={t}>
                    {t}
                  </option>
                ))}
              </select>
              <label className="muted">Character</label>
              <select
                value={persona.character || VOICE_CHARACTERS[0]}
                onChange={(e) => savePersona({ character: e.target.value })}
                style={{ width: "100%", marginBottom: "0.5rem" }}
              >
                {VOICE_CHARACTERS.map((c) => (
                  <option key={c} value={c}>
                    {c}
                  </option>
                ))}
              </select>
              {[
                { key: "energy", label: "Energy", min: 0, max: 100 },
                { key: "formality", label: "Formality", min: 0, max: 100 },
                { key: "sayRate", label: "Pace (WPM)", min: 140, max: 220 },
              ].map(({ key, label, min, max }) => (
                <div key={key} className="slider-row">
                  <span>{label}</span>
                  <input
                    type="range"
                    min={min}
                    max={max}
                    value={persona[key] ?? min}
                    onChange={(e) => savePersona({ [key]: parseInt(e.target.value, 10) })}
                  />
                  <span>{persona[key]}</span>
                </div>
              ))}
              <textarea
                className="script-editor"
                style={{ minHeight: 80 }}
                placeholder="Persona notes for the narrator…"
                value={persona.personaNotes || ""}
                onChange={(e) => setPersona({ ...persona, personaNotes: e.target.value })}
                onBlur={() => savePersona({ personaNotes: persona.personaNotes })}
              />
              <div className="gate-row">
                <button type="button" onClick={() => savePersona(persona)} disabled={busy}>
                  Save voice
                </button>
                <button type="button" className="secondary" onClick={assistantVoice} disabled={busy}>
                  Assistant adjust voice
                </button>
              </div>
            </section>
          )}

          {tab === "Script" && (
            <ScriptStudioView
              capture={capture}
              session={session}
              script={script}
              setScript={setScript}
              setScriptDirty={setScriptDirty}
              previewVideoUrl={previewVideoUrl}
              silentReady={silentReady}
              presentationReady={presentationReady}
              composeRunning={composeRunning}
              busy={busy}
              err={err}
              scriptChatInput={scriptChatInput}
              setScriptChatInput={setScriptChatInput}
              scriptDirty={scriptDirty}
              onSendScriptChat={sendScriptChat}
              onBootstrapScriptChat={bootstrapScriptChat}
              onSave={saveScript}
              onRegen={regenScript}
              onApprove={approveVoice}
            />
          )}

          {tab === "Cards" && (
            <section className="panel">
              <h2>Intro / outro cards</h2>
              <p className="muted">Your approved studio cards — verify before compose.</p>
              <div className="card-grid">
                {APPROVED_CARD_KEYS.map(({ key, label }) => {
                  const found = approvedAssets?.assets?.[key];
                  return (
                    <div key={key} className="card-thumb">
                      {found?.exists ? (
                        <img src={`/api/approved/image?name=${encodeURIComponent(key)}`} alt={label} />
                      ) : (
                        <div className="muted" style={{ padding: "1rem", textAlign: "center" }}>
                          missing
                        </div>
                      )}
                      <div className="card-label">{label}</div>
                    </div>
                  );
                })}
              </div>
              <button type="button" onClick={verifyCards} disabled={busy || assetsVerified}>
                {assetsVerified ? "Cards verified ✓" : "I verified these cards"}
              </button>
            </section>
          )}

          {tab === "Export" && (
            <section className="panel">
              <h2>Share &amp; export</h2>
              <p className="muted">
                When the narrated master is approved, export opens the video and YouTube Studio with title &
                description from your brief.
              </p>
              <button type="button" onClick={exportYoutube} disabled={busy || !presentationReady}>
                Share to YouTube
              </button>
              {!presentationReady ? (
                <p className="muted" style={{ marginTop: "0.75rem" }}>
                  Complete silent compose → script approval → Personal Voice first.
                </p>
              ) : null}
            </section>
          )}
        </div>
    </div>
  );
}
