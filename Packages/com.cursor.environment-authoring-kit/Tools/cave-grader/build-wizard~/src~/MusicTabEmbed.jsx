import { useEffect, useState } from "react";
import { api, hubFromUrl } from "./plannerApi.js";

export default function MusicTabEmbed() {
  const hub = hubFromUrl();
  const [state, setState] = useState("loading");
  const [musicUrl, setMusicUrl] = useState("");
  const [err, setErr] = useState("");

  useEffect(() => {
    if (!hub) {
      setState("error");
      setErr("Missing ?hub= query — reopen from Unity Hub.");
      return undefined;
    }

    let cancelled = false;
    (async () => {
      try {
        const status = await api("/api/wizard/music/status");
        if (cancelled) return;
        if (status.music?.ready && status.music?.musicUrl) {
          setMusicUrl(status.music.musicUrl);
          setState("ready");
          return;
        }
        const ensured = await api("/api/wizard/music/ensure", { method: "POST", body: "{}" });
        if (cancelled) return;
        if (ensured.ok && ensured.musicUrl) {
          setMusicUrl(ensured.musicUrl);
          setState("ready");
        } else {
          setState("error");
          setErr(ensured.error || "AI Director Studio (music) could not start.");
        }
      } catch (e) {
        if (!cancelled) {
          setState("error");
          const msg = String(e);
          setErr(
            msg === "Error: not found" || msg === "not found"
              ? "Build wizard server is outdated — restart it (Unity: reopen wizard, or run ensure-build-wizard.py --restart)."
              : msg
          );
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [hub]);

  if (state === "loading") {
    return (
      <div className="video-director-shell">
        <div className="video-director-loading card">
          <span className="spinner" />
          <p>Starting AI Director music studio…</p>
        </div>
      </div>
    );
  }

  if (state === "error") {
    return (
      <div className="video-director-shell">
        <div className="card video-director-error">
          <h2 className="section">Music studio</h2>
          <p className="err">{err}</p>
          <p className="hint">
            The Music tab embeds <strong>AI Director Studio</strong> — briefing, instrumental, vocal record, and produce
            (instrumental → lyrics → vocal → master → optional music video handoff).
          </p>
          <button
            type="button"
            className="primary"
            onClick={() => {
              setState("loading");
              setErr("");
              api("/api/wizard/music/ensure", { method: "POST", body: "{}" })
                .then((d) => {
                  if (d.ok && d.musicUrl) {
                    setMusicUrl(d.musicUrl);
                    setState("ready");
                  } else {
                    setErr(d.error || "Could not start music studio");
                    setState("error");
                  }
                })
                .catch((e) => {
                  const msg = String(e);
                  setErr(
                    msg === "Error: not found" || msg === "not found"
                      ? "Build wizard server is outdated — restart it (Unity: reopen wizard, or run ensure-build-wizard.py --restart)."
                      : msg
                  );
                  setState("error");
                });
            }}
          >
            Retry
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="video-director-shell video-director-embedded">
      <div className="video-director-chrome">
        <span className="video-director-label">Music studio</span>
        <a className="ghost video-director-popout" href={musicUrl} target="_blank" rel="noreferrer">
          Open in new tab
        </a>
      </div>
      <iframe title="AI Director Music" className="video-embed-frame video-embed-frame-full" src={musicUrl} />
    </div>
  );
}
