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
];

export function pickPlaybook(issueText: string): PlaybookEntry | null {
  const lower = issueText.toLowerCase();
  for (const pb of PLAYBOOKS) {
    if (pb.triggers.some((t) => lower.includes(t.toLowerCase()))) return pb;
  }
  return null;
}

export function formatPlaybookBlock(hubRoot: string, issueText: string): string {
  const pb = pickPlaybook(issueText);
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
