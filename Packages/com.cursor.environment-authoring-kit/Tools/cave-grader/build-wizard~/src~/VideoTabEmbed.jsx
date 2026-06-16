import { useEffect, useState } from "react";
import { api, hubFromUrl } from "./plannerApi.js";

export default function VideoTabEmbed() {
  const hub = hubFromUrl();
  const [state, setState] = useState("loading");
  const [directorUrl, setDirectorUrl] = useState("");
  const [capture, setCapture] = useState("");
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
        const status = await api("/api/wizard/director/status");
        if (cancelled) return;
        if (status.director?.ready && status.director?.directorUrl) {
          setDirectorUrl(status.director.directorUrl);
          setCapture(status.director.capture || "");
          setState("ready");
          return;
        }
        const ensured = await api("/api/wizard/director/ensure", { method: "POST", body: "{}" });
        if (cancelled) return;
        if (ensured.ok && ensured.directorUrl) {
          setDirectorUrl(ensured.directorUrl);
          setCapture(ensured.capture || "");
          setState("ready");
        } else {
          setState("error");
          setErr(ensured.error || "AI Director could not start.");
        }
      } catch (e) {
        if (!cancelled) {
          setState("error");
          setErr(String(e));
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
          <p>Starting AI Director video editor…</p>
        </div>
      </div>
    );
  }

  if (state === "error") {
    return (
      <div className="video-director-shell">
        <div className="card video-director-error">
          <h2 className="section">AI Director</h2>
          <p className="err">{err}</p>
          <p className="hint">
            The Video tab embeds the full <strong>AI Director</strong> studio (Preview, Segments, Pacing, Voice, Script, Export).
            You need a <code>DemoCapture</code> run with timelapse frames under{" "}
            <code>Library/EnvironmentKit/DemoCapture/</code>.
          </p>
          <button
            type="button"
            className="primary"
            onClick={() => {
              setState("loading");
              setErr("");
              api("/api/wizard/director/ensure", { method: "POST", body: "{}" })
                .then((d) => {
                  if (d.ok && d.directorUrl) {
                    setDirectorUrl(d.directorUrl);
                    setCapture(d.capture || "");
                    setState("ready");
                  } else {
                    setErr(d.error || "Could not start director");
                    setState("error");
                  }
                })
                .catch((e) => {
                  setErr(String(e));
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
        <span className="video-director-label">AI Director</span>
        {capture && <span className="video-director-capture" title={capture}>{capture.split("/").pop()}</span>}
        <a className="ghost video-director-popout" href={directorUrl} target="_blank" rel="noreferrer">
          Open in new tab
        </a>
      </div>
      <iframe title="AI Director" className="video-embed-frame video-embed-frame-full" src={directorUrl} />
    </div>
  );
}
