/**
 * Single source of truth for Cursor agent prompts — stable instructions + live JSON on disk.
 * Replaces dynamic web-search blocks that caused repetitive layouts and map-icon mimicry.
 */
import { MANDATORY_BUILD_RULES_MD } from "./mandatory-build-rules.js";
import {
  JSON_MANIFEST_REL,
  RESEARCH_AGENT_PROMPT_REL,
  UNIFIED_AGENT_PROMPT_REL,
  ACTIVE_PHASE_PROMPT_REL,
  PHASE_DATA_DIGEST_REL,
} from "./agent-artifact-paths.js";
import { formatResearchBlockForFixPrompt } from "./research-agent-prompt.js";
import { PROMPT_HARMONY_RULES_MD } from "./prompt-coherence.js";

export const HARDCODED_PROMPTS_VERSION = "2026-05-26";

/** Always read these after Unity grades or bots run (paths relative to Hub root). */
export const REQUIRED_JSON_REPORTS = [
  "Assets/EnvironmentKit/Generated/CaveBuildGeneratedJsonManifest.json",
  "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json",
  "Assets/EnvironmentKit/Generated/CaveBuildFailingStages.json",
  "Assets/EnvironmentKit/Generated/CaveBuildLadderContext.json",
  "Assets/EnvironmentKit/Generated/CaveBuildVisualShellAudit.json",
  "Assets/EnvironmentKit/Generated/CaveBuildPhaseBotReport.json",
  "Assets/EnvironmentKit/Generated/CaveBuildRouteProbe.json",
  "Assets/EnvironmentKit/Generated/CaveBuildSurfaceRouteProbe.json",
  "Assets/EnvironmentKit/Generated/CaveBuildSurfaceProbe.json",
  "Assets/EnvironmentKit/Generated/SurfaceTerrainBuildLadderReport.json",
  "Assets/EnvironmentKit/Generated/SurfaceTerrainBuildLadderContext.json",
  "Assets/EnvironmentKit/Generated/SurfaceTerrainQualityReport.json",
  "Assets/EnvironmentKit/Generated/SurfaceTerrainPhaseLog.json",
  "Assets/EnvironmentKit/Generated/CaveBuildResearchActionPlan.json",
  "Assets/EnvironmentKit/Generated/CaveBuildNextStepsPrompt.md",
  "Assets/EnvironmentKit/Generated/CaveBuildDoNotPrompt.md",
  "Assets/EnvironmentKit/Generated/CaveBuildMeatLoopHistory.json",
  "Assets/EnvironmentKit/Generated/CaveBuildCompileDiagnostics.json",
  "Assets/EnvironmentKit/Generated/SurfaceWorldManifest.json",
  "Assets/EnvironmentKit/Generated/CaveLayoutBlueprint.json",
] as const;

export const LAND_MASS_REFERENCE_RULES_MD = `## Land-mass reference only (no map chrome, no DEM photocopy)

When using Florida LiDAR hillshade PNGs or elevation grids:

- **Terrain is procedural first** — seed-locked FBM sculpt is the playable landscape. LiDAR is a **macro guide only** (≤28% structural bias): basin vs ridge trend, not a 1:1 heightmap stamp.
- **Do not** paste county DEM pixels into Unity terrain or match hillshade silhouettes literally. Warp/simplify: smoothed elev grid, domain-warped UV, creative passes after guide.
- **Ignore** map legends, scale bars, north arrows, titles, watermarks, attribution stamps, borders, and **any map icons** (POI pins, highway shields, city labels).
- **Ignore** blue water polygons, bathymetry, inundation, and spring/discharge symbology — they are not terrain height.
- Prefer \`elevation-grid.json\` + \`manifest.json\` bbox for **slope/basin hints** — not decorative pixels in \`hillshade.png\`.
- Underground cave layout must **not** copy the 2D map footprint literally — use structure-only inspiration (subsidence, fracture trends), then vary spline/chambers per seed.`;

export const LAYOUT_CREATIVITY_RULES_MD = `## Layout variety (avoid clone builds)

Each seed must feel like a **new** expedition, not a reskin of the last run:

- Change **tunnel segment count**, chamber count, yaw variance, entrance azimuth, and maze flavor within code limits.
- Vary **prop emphasis** and scatter density; do not reuse the same platform ring layout when the rubric already passed once.
- Surface: respect the rolled \`SurfaceDirectionCount\`, time/weather, and extent — do not force the same 3×3 grid story every time.
- You may improve C# parameters and authoring logic, but **do not** paste a fixed reference level layout from prior chats or images.
- Creative freedom is encouraged for **gameplay readability** (platforms, trail approach, mouth alignment) — not for copying satellite UI or prior seed geometry.`;

export const PIPELINE_AND_PACING_RULES_MD = `## Pipeline order & editor safety (MacBook / Unity)

Unity runs these in order — **do not** replace the pipeline with one giant rebuild:

1. **Grade** — read JSON reports listed above; update failing stage fields in code/scene.
2. **Fix one ladder rung** — smallest C# or scene change that addresses the active rung only.
3. **Bots** (editor, paced) — surface route probe → cave route probe; read \`CaveBuildPhaseBotReport.json\` after each phase.
4. **Re-grade** — Unity writes fresh JSON; read files again before the next agent pass.

**Never:** sync \`WaitForExit\` node scripts during an active build; never pull full heightmaps in one frame; never spawn 8 neighbor tiles + full smooth in one editor tick.
**One rung per agent invoke.** After edits, tell the user to let Unity re-grade — do not assume Play Mode unless the rung requires it.`;

export function formatJsonContractBlock(hubRoot: string): string {
  const hub = hubRoot.replace(/\/$/, "");
  const lines = [
    "## JSON contract (read & update on disk)",
    "",
    "Unity regenerates these each run. **Read the files** — do not invent grades or probe results.",
    "",
    `- Manifest (all paths): \`${hub}/${JSON_MANIFEST_REL}\``,
    `- Unified index: \`${hub}/${UNIFIED_AGENT_PROMPT_REL}\``,
    `- Active phase focus: \`${hub}/${ACTIVE_PHASE_PROMPT_REL}\``,
    `- Phase digest: \`${hub}/${PHASE_DATA_DIGEST_REL}\``,
    `- Research (land-mass only): \`${hub}/${RESEARCH_AGENT_PROMPT_REL}\``,
    "",
    "Required reports:",
  ];
  for (const rel of REQUIRED_JSON_REPORTS) {
    lines.push(`- \`${hub}/${rel}\``);
  }
  lines.push(
    "",
    "After your pass, ensure Unity can re-export quality reports (no breaking compile). Do not delete `Generated/` or `ResearchCache/`."
  );
  return lines.join("\n");
}

export function buildHardcodedCorePrompt(hubRoot: string): string {
  return [
    `# Environment Kit — hardcoded agent contract v${HARDCODED_PROMPTS_VERSION}`,
    "",
    PROMPT_HARMONY_RULES_MD,
    "",
    MANDATORY_BUILD_RULES_MD,
    "",
    LAND_MASS_REFERENCE_RULES_MD,
    "",
    LAYOUT_CREATIVITY_RULES_MD,
    "",
    PIPELINE_AND_PACING_RULES_MD,
    "",
    formatJsonContractBlock(hubRoot),
  ].join("\n");
}

export type CaveLadderPromptInput = {
  hubRoot: string;
  activeRung: string;
  letterGrade: string;
  overallScore: number;
  buildAcceptable: boolean;
  isDud?: boolean;
  recommendedAction?: string;
  reportPath: string;
  failingStageLines: string[];
  dudReasonLines: string[];
  rungTaskMarkdown: string;
  tailoredBrief: string;
  executionBrief: string;
};

export function buildHardcodedCaveLadderPrompt(input: CaveLadderPromptInput): string {
  const {
    hubRoot,
    activeRung,
    letterGrade,
    overallScore,
    buildAcceptable,
    isDud,
    recommendedAction,
    reportPath,
    failingStageLines,
    dudReasonLines,
    rungTaskMarkdown,
    tailoredBrief,
    executionBrief,
  } = input;

  const lines = [
    buildHardcodedCorePrompt(hubRoot),
    "",
    "---",
    "",
    `## Active pass — cave ladder rung: **${activeRung}**`,
    "",
    `Hub: \`${hubRoot}\``,
    `Grade: ${letterGrade} (${overallScore}/100) acceptable=${buildAcceptable} dud=${isDud ?? false}`,
    `Recommended: ${recommendedAction ?? "RunMeatLoop"}`,
    `Primary report: \`${reportPath}\``,
    "",
    executionBrief,
    "",
    tailoredBrief,
    "",
  ];

  if (dudReasonLines.length) {
    lines.push("### Dud reasons", ...dudReasonLines.map((r) => `- ${r}`), "");
  }
  if (failingStageLines.length) {
    lines.push("### Failing stages (this rung)", ...failingStageLines, "");
  }

  lines.push(
    "---",
    "",
    formatResearchBlockForFixPrompt(hubRoot, "cave"),
    "---",
    "",
    "## Rung task checklist",
    "",
    rungTaskMarkdown || `_No rung markdown for ${activeRung} — fix using JSON issues/fixes only._`,
    "",
    "---",
    "",
    "## Output",
    "",
    "1. Read manifest + reports for this rung.",
    "2. Apply **one** minimal Hub code/scene fix.",
    "3. Note which JSON files Unity should refresh on re-grade.",
    ""
  );

  return lines.join("\n");
}

export type TerrainLadderPromptInput = {
  hubRoot: string;
  activeRung: string;
  scene?: string;
  seed?: number;
  letterGrade: string;
  overallScore: number;
  targetScore?: number;
  reportPath: string;
  failingSummary: string;
  issueLines: string;
  fixLines: string;
  rungTaskMarkdown: string;
  contextBlock: string;
};

export function buildHardcodedTerrainLadderPrompt(input: TerrainLadderPromptInput): string {
  const {
    hubRoot,
    activeRung,
    scene,
    seed,
    letterGrade,
    overallScore,
    targetScore,
    reportPath,
    failingSummary,
    issueLines,
    fixLines,
    rungTaskMarkdown,
    contextBlock,
  } = input;

  return [
    buildHardcodedCorePrompt(hubRoot),
    "",
    "---",
    "",
    `## Active pass — terrain ladder rung: **${activeRung}**`,
    "",
    `Scene: ${scene ?? "unknown"} | Seed: ${seed ?? 0}`,
    `Grade: ${letterGrade} (${overallScore}/100) | Target: ${targetScore ?? 85}+`,
    `Report: \`${reportPath}\``,
    "",
    "### Failing stages",
    failingSummary || "none",
    "",
    "### This rung — issues",
    issueLines || "- (see report JSON)",
    "",
    "### Suggested fixes",
    fixLines || "- SurfaceTerrainLadderFixer / crater repair / NavMesh / props",
    "",
    contextBlock ? `### Ladder context\n${contextBlock}\n` : "",
    "---",
    "",
    formatResearchBlockForFixPrompt(hubRoot, "terrain"),
    "---",
    "",
    "## Rung task checklist",
    "",
    rungTaskMarkdown || `_Improve rung ${activeRung} using SurfaceTerrainBuildLadderReport.json._`,
    "",
    "## Rules",
    "1. Hub package only — additive terrain/cave fixes.",
    "2. Read `SurfaceTerrainSculptAgentPrompt.md` — Ground center XZ is the geo anchor; never move cave/layout off it.",
    "3. LiDAR/DEM ≤28% guide — procedural FBM is playable height (no photocopy, no flat inner disk + quilted ring).",
    "4. One prop category per prop_* rung.",
    "5. Multi-tile NavMesh must include **all** surface terrains.",
    "6. Do not re-stamp DEM on every neighbor tile (seam stitch only when LiDAR already authoritative).",
    "",
  ].join("\n");
}

export function buildHardcodedUnifiedPromptHeader(
  hubRoot: string,
  activePhase?: string
): string {
  const phaseNote = activePhase
    ? `\n**Active Unity phase:** \`${activePhase}\` — read \`${hubRoot}/${ACTIVE_PHASE_PROMPT_REL}\` for task focus.\n`
    : "";
  return `${buildHardcodedCorePrompt(hubRoot)}${phaseNote}`;
}
