import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";

export type PlaybookEntry = {
  id: string;
  title: string;
  triggers: string[];
  fixRecipe: string;
  deepRepair?: string;
};

const PLAYBOOKS: PlaybookEntry[] = [
  {
    id: "MISSING_SPLINE",
    title: "Missing CaveSplinePathAuthoring",
    triggers: ["Missing CaveSplinePathAuthoring", "too few knots", "path:10"],
    fixRecipe: "FixPath → TryBootstrapSplinePathFromMetadata; adventure → TryBootstrapMissingTrue3DShell.",
    deepRepair: "Full true-3D shell bootstrap from CaveBuildMetadata seed.",
  },
  {
    id: "MOUTH_DEPTH_50M",
    title: "Cave mouth seal depth error",
    triggers: ["50.1m", "cave_mouth_seal", "mouth seal", "ground_placement"],
    fixRecipe: "CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly (XZ locked).",
  },
  {
    id: "SPARSE_BLOCK_TUNNEL",
    title: "Block tunnel sparse (0 blocks)",
    triggers: ["Block tunnel sparse", "block_tunnel:35", "NeedsCompactRouteDensityRepair"],
    fixRecipe: "CaveCompactRouteUtility.RebuildCompactBlockRingsOnly from metadata layout.",
  },
  {
    id: "GEOMETRY_VOID",
    title: "True3D shell insufficient",
    triggers: ["geometry_integrity", "maze walls 0", "open to void"],
    fixRecipe: "TryBootstrapMissingTrue3DShell + maze volume regen.",
  },
  {
    id: "COMPILE_GATE",
    title: "Verified CS compile errors",
    triggers: ["error CS", "compile_gate", "verifiedOnDisk"],
    fixRecipe: "Fix only verifiedOnDisk errors; run ExportCompileDiagnosticsForAgent.",
  },
  {
    id: "ROUTE_PROBE_FAIL",
    title: "Route probe not traversable",
    triggers: ["mouth_unreachable", "route probe", "jump gap"],
    fixRecipe: "Surface trail connector + Fix Cave Playability walkways.",
  },
  {
    id: "PERF_TRI_BUDGET",
    title: "Triangle budget exceeded",
    triggers: ["mesh budget high", "performance:65", "triangles"],
    fixRecipe: "Prefer compact block tunnel; purge layered shell pieces.",
  },
  {
    id: "LAYOUT_AUDIT_SEAMS",
    title: "World layout audit — terrain seams",
    triggers: ["layout audit", "blocksSurfaceContinue", "seam gap", "WorldLayoutAudit"],
    fixRecipe: "Planner fresh build → PurgeFullWorldTerrainForRebuild; restitch play disk before cave queue.",
    deepRepair: "Surface Only rebuild or CAVE_LAYOUT_PLAN_FORCE=1 headless only.",
  },
  {
    id: "STALE_CHECKPOINT",
    title: "Stale paced-step resume",
    triggers: ["stale checkpoint", "pacedStep", "exceeds 2× planned", "8400"],
    fixRecipe: "CaveBuildPersistedSessionReset.ClearForNewBuild on planner approve; delete CaveBuildPacedStepCheckpoint.json.",
  },
  {
    id: "PLANNER_FAST_DEMO",
    title: "Planner session — fast demo flags",
    triggers: ["session_config", "agentInvokes", "use3DCaveSystem: false", "Waiting Room"],
    fixRecipe: "SkipTerrainHelperScripts + skip crater repair; bind CaveBuildActiveSessionConfig.json only.",
    deepRepair: "Never resume old checkpoints; tile count from planner not FullWorld 289.",
  },
  {
    id: "POST_BUILD_PLAYTHROUGH",
    title: "Post-build Play Mode + recap",
    triggers: ["post-build", "playthrough", "DemoRecapPresentation", "prefab export"],
    fixRecipe: "CaveBuildPostBuildFinalizeGate — Play Mode record → prefab → compose; do not finalize timelapse early.",
  },
  {
    id: "TERRAIN_SEAM_NINETILE",
    title: "Nine-tile play disk seams",
    triggers: ["nine-tile seams", "lock play disk", "SurfaceTerrainTile_"],
    fixRecipe: "Let paced seam queue finish; avoid sync StitchAll on broken grid — purge first if gaps >0.5m.",
  },
  {
    id: "NAVMESH_PARTIAL",
    title: "NavMesh path partial",
    triggers: ["NavMesh", "PathPartial", "terrain_integration", "navmesh"],
    fixRecipe: "SurfaceTerrainBuildLadder surface_navmesh rung; rebake after props locked to ground.",
  },
  {
    id: "PROP_FLOATERS",
    title: "Prop floaters / layout audit props",
    triggers: ["prop floater", "propFloaterCount", "blocksCaveQueue"],
    fixRecipe: "SurfacePropGroundLock.Apply after vegetation; CaveBuildPropSnapRepair for outliers.",
  },
  {
    id: "PREBUILD_GATE_BLOCK",
    title: "Pre-build gate blocked",
    triggers: ["pre-build", "PreBuildReloop", "compile wait", "prebuild gate"],
    fixRecipe: "Fix compile_gate first; preBuildReloop:false on planner skips reloop — fix layout audit instead.",
  },
  {
    id: "DEMO_RECAP_COMPOSE",
    title: "Demo recap video compose",
    triggers: ["DemoRecorder", "ffmpeg", "orphaned timelapse", "compose"],
    fixRecipe: "TryComposeOrphanedCaptureIfIdle; ensure playthrough MP4 in uploads/playthroughs/ before compose.",
  },
  {
    id: "DISK_FULL_PACED",
    title: "Disk full during paced checkpoints",
    triggers: ["Disk full", "IOException", "PacedStepCheckpoint"],
    fixRecipe: "Migrate to Lexar via Hub Storage; raise JsonSaveIntervalSteps; ClearForNewBuild.",
  },
  {
    id: "GAMEPLAY_MILESTONE",
    title: "Phase 2 gameplay milestone",
    triggers: ["HubGameProgress", "demoReady", "gameplay", "G1", "G4", "G8"],
    fixRecipe: "hub-gameplay-completion skill — one milestone per session; wire MainScene not package editor.",
  },
  {
    id: "STEP_COUNTER_ETC",
    title: "Hub step counter / ETC wrong",
    triggers: ["EffectivePlannedTotal", "step counter", "ETC", "planned budget"],
    fixRecipe: "CaveBuildStepCounter.SyncLiveTotals; ConfigureForRequest from active planner session.",
  },
  {
    id: "EXTERNAL_STORAGE",
    title: "External storage / Lexar symlink",
    triggers: ["Lexar", "EnvironmentKit-Hub", "symlink", "Generated"],
    fixRecipe: "EnvironmentKitDataRoot on Lexar; never commit Generated/; JSON on disk is truth.",
  },
];

export function pickPlaybook(issueText: string): PlaybookEntry | null {
  const lower = issueText.toLowerCase();
  for (const pb of PLAYBOOKS) {
    if (pb.triggers.some((t) => lower.includes(t.toLowerCase()))) return pb;
  }
  return null;
}

export function pickPlaybookFromSession(hubRoot: string, issueText: string): PlaybookEntry | null {
  const fromIssue = pickPlaybook(issueText);
  if (fromIssue) return fromIssue;

  const sessionPath = join(hubRoot, "Assets/EnvironmentKit/Generated/CaveBuildActiveSessionConfig.json");
  if (existsSync(sessionPath)) {
    try {
      const doc = JSON.parse(readFileSync(sessionPath, "utf8")) as { agentInvokes?: boolean; use3DCaveSystem?: boolean };
      if (doc.agentInvokes === false) return pickPlaybook("session_config agentInvokes") ?? null;
      if (doc.use3DCaveSystem === false) return pickPlaybook("use3DCaveSystem: false") ?? null;
    } catch {
      /* ignore */
    }
  }
  return null;
}

export function formatPlaybookBlock(hubRoot: string, issueText: string): string {
  const pb = pickPlaybookFromSession(hubRoot, issueText);
  if (!pb) return "";

  const mdPath = join(hubRoot, "Tools/cursor-bot/playbooks", `${pb.id}.md`);
  let body = "";
  if (existsSync(mdPath)) body = readFileSync(mdPath, "utf8");

  return [
    `## Production playbook: ${pb.id}`,
    `**${pb.title}**`,
    `Fix: ${pb.fixRecipe}`,
    pb.deepRepair ? `Deep repair: ${pb.deepRepair}` : "",
    body ? `\n${body}` : "",
    "Cite this playbook ID in your change comment.",
  ]
    .filter(Boolean)
    .join("\n");
}

export function listPlaybookIds(): string[] {
  return PLAYBOOKS.map((p) => p.id);
}

export function listPlaybooks(): readonly PlaybookEntry[] {
  return PLAYBOOKS;
}
