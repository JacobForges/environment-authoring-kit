import { useEffect, useState } from "react";
import WorldBuildApp from "./App.jsx";
import PlannerTabApp from "./PlannerTabApp.jsx";
import PipelineBar from "./PipelineBar.jsx";
import VideoTabEmbed from "./VideoTabEmbed.jsx";
import MusicTabEmbed from "./MusicTabEmbed.jsx";
import OnnxBrainsTab from "./OnnxBrainsTab.jsx";
import { api, hubFromUrl } from "./plannerApi.js";
import { tabFromHash, WIZARD_TABS } from "./tabConfigs.js";

const GENERIC_TABS = new Set(["surface-content", "caves", "mazes", "interior-content", "atmosphere"]);

export default function WizardShell() {
  const [tab, setTab] = useState(tabFromHash);
  const [manifest, setManifest] = useState(null);

  useEffect(() => {
    const onHash = () => setTab(tabFromHash());
    window.addEventListener("hashchange", onHash);
    return () => window.removeEventListener("hashchange", onHash);
  }, []);

  useEffect(() => {
    if (!hubFromUrl()) return;
    api("/api/wizard/manifest")
      .then((d) => setManifest(d.manifest))
      .catch(() => {});
  }, [tab]);

  const selectTab = (id) => {
    const t = WIZARD_TABS.find((x) => x.id === id) || WIZARD_TABS[0];
    setTab(t.id);
    window.location.hash = t.hash;
    requestAnimationFrame(() => {
      document.querySelector(`.wizard-tab-${t.css}.active`)?.scrollIntoView({ inline: "center", block: "nearest" });
    });
  };

  const tabMeta = WIZARD_TABS.find((t) => t.id === tab) || WIZARD_TABS[0];

  return (
    <>
      <nav className={`wizard-tabs wizard-tabs-${WIZARD_TABS.length}`} aria-label="Build wizard steps">
        {WIZARD_TABS.map((t) => {
          const chip = manifest?.tabs?.find((m) => m.id === t.id);
          return (
            <button
              key={t.id}
              type="button"
              className={`wizard-tab wizard-tab-${t.css}${tab === t.id ? " active" : ""}`}
              onClick={() => selectTab(t.id)}
              aria-current={tab === t.id ? "page" : undefined}
            >
              {t.label}
              {chip?.complete && <span className="tab-chip-done"> ✓</span>}
            </button>
          );
        })}
      </nav>
      <PipelineBar activeTab={tab} onSelectTab={selectTab} />
      <div className={`wizard-tab-banner wizard-tab-banner-${tabMeta.css}`} role="status">
        <strong>{tabMeta.label}</strong> — isolated session. Optional order; reset one tab without affecting others.
      </div>
      {tab === "terrain" && <WorldBuildApp key="tab-terrain" />}
      {GENERIC_TABS.has(tab) && <PlannerTabApp key={`tab-${tab}`} tabId={tab} />}
      {tab === "video" && <VideoTabEmbed key="tab-video" />}
      {tab === "music" && <MusicTabEmbed key="tab-music" />}
      {tab === "onnx-brains" && <OnnxBrainsTab key="tab-onnx-brains" />}
    </>
  );
}
