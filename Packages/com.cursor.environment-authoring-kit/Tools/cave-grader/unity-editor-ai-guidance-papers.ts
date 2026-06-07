/**
 * Unity Editor + AI pipeline UX — prompt preview, non-blocking notifications.
 * Category: unity_editor_ai_guidance. Read during research phases and agent prompt export.
 */
import type { ResearchEntry } from "./research-catalog.js";

const C = "unity_editor_ai_guidance";

export const UNITY_EDITOR_AI_GUIDANCE_PAPERS: ResearchEntry[] = [
  {
    lab: "Unity Technologies",
    title: "Unity 6 — EditorWindow and non-blocking progress",
    year: 2026,
    venue: "Unity 6 Manual",
    url: "https://docs.unity3d.com/6000.0/Documentation/Manual/editor-EditorWindows.html",
    topics: `${C}, EditorWindow, progress, non-blocking, pipeline`,
    provenInProduction: true,
    notes:
      "AI prompt preview should use EditorWindow.Notify / custom overlay — never modal EditorUtility.DisplayProgressBar during paced queue.",
  },
  {
    lab: "Unity Technologies",
    title: "Unity 6 — ShowNotification in Scene view",
    year: 2026,
    venue: "Unity 6 Scripting",
    url: "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneView.ShowNotification.html",
    topics: `${C}, ShowNotification, scene view, AI prompt, visible banner`,
    provenInProduction: true,
    notes:
      "Kit target: large Scene-view notification when agent prompt is dispatched — pipeline continues via CaveBuildActionPacing.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "Kit — CaveBuildRunStatusPublisher pulse (Hub + Scene)",
    year: 2026,
    venue: "CaveBuildRunStatusPublisher",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/Blockout/CaveBuildRunStatusPublisher.cs",
    topics: `${C}, CaveBuildRunStatusPublisher, PulseSubOperation, Hub status, no freeze`,
    provenInProduction: true,
    notes: "Extend with ShowAgentPromptPreview(title, bodyPreview) — 3–5 line scroll, auto-dismiss optional.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "Kit — Agent prompt export without stopping queue",
    year: 2026,
    venue: "CaveBuildResearchPhase",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/Blockout/CaveBuildResearchPhase.cs",
    topics: `${C}, research phase, prompt, queue idle, ScheduleLight`,
    provenInProduction: true,
    notes:
      "Research pull runs on ScheduleLight; prompt text written to Generated/ + notification — never block main thread > 200ms.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "HOW TO: use ResearchCache images per tool/phase",
    year: 2026,
    venue: "research-store buildAgentGuidance",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Tools/cave-grader/research-store.ts",
    topics: `${C}, ResearchCache, localImages, category index, agent guidance`,
    provenInProduction: true,
    notes:
      "For each phase: lookupForCategories(hub, [surface_props, prop_do_not]). Open entries/{id}/images/ref-0.png in Unity before editing matching C# tool.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "HOW TO: map Unity tools to research categories",
    year: 2026,
    venue: "Agent tool map",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_AGENT_TOOL_MAP.md",
    topics: `${C}, SurfaceIntelligentPropPlacer, SurfaceTerrainTileExpansion, tool tag, category`,
    provenInProduction: true,
    notes:
      "SurfaceIntelligentPropPlacer → surface_props. SurfaceMountainTerrainPhases.QueueCliffAccent → mountain_terrain + prop_do_not. SurfaceTerrainSeamWelds → terrain_tiling.",
  },
];
