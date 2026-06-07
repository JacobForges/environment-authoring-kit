/**
 * Hollow Titan anti-patterns — category hollow_titan_do_not.
 * Agents MUST read before any Hollow Titan meat-phase C# edit.
 */
import type { ResearchEntry } from "./research-catalog.js";

const C = "hollow_titan_do_not";

export const HOLLOW_TITAN_DO_NOT_PAPERS: ResearchEntry[] = [
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: CC0 L01–L04 white cylinders as primary trunk shell",
    year: 2026,
    venue: "HollowTitanExteriorDressing.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/World/HollowTitanExteriorDressing.cs",
    topics: `${C}, CC0 L01, L02, L03, L04, white blocks, LPMagicalForest primary`,
    provenInProduction: true,
    notes:
      "BuildHyperRealTrunkShell uses Tree_Mammoth_Green_01 + curtain rings when BiomePropCatalog.LpMagicalForestRoot resolves. CC0 bark columns are fallback ONLY when LP assets missing.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: world scatter enemy/loot tables on landmark floors",
    year: 2026,
    venue: "HollowTitanLandmarkSpawnCatalog.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Runtime/World/HollowTitanLandmarkSpawnCatalog.cs",
    topics: `${C}, world scatter, landmark spawn, ENEMY_Annex, loot manifest`,
    provenInProduction: true,
    notes:
      "Phases 8–11 use HollowTitanLandmarkSpawnPoint + landmark catalog IDs only. Never mix SurfaceWorld enemy scatter or world pickup tables.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: place Hollow Titan on cave mouth or underground entrance",
    year: 2026,
    venue: "HollowTitanLandmarkSitePicker.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/World/HollowTitanLandmarkSitePicker.cs",
    topics: `${C}, cave mouth, underground, surface only, site picker exclude`,
    provenInProduction: true,
    notes:
      "Landmark is surface terrain tile only. Site picker excludes cave mouth tiles, CarveCliffTunnelMouth sites, and UndergroundCaveSystem anchors.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: site pick on labyrinth bench or annex walkway tiles",
    year: 2026,
    venue: "RESEARCH_MOUNTAIN_LABYRINTH.md",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_LABYRINTH.md",
    topics: `${C}, labyrinth bench, annex walkway, mountain_labyrinth, tile exclude`,
    provenInProduction: true,
    notes:
      "Hollow Titan on peak-ring karst tiles — never south labyrinth annex benches or BuildCarvePolylines walkway footprint tiles.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: rebuild entire FullWorld surface for one landmark phase fix",
    year: 2026,
    venue: "HollowTitanLandmarkMeatPhases.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_DO_NOT.md",
    topics: `${C}, fullworld rebuild, targeted fix, meat phase, single phase`,
    provenInProduction: true,
    notes:
      "Fix failing meat phase only (0–11). Re-run HollowTitanLandmarkMeatPhases for that index — do not purge GeneratedSurfaceWorld or restart mountain pipeline.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: edit C# before opening concept images + ResearchCache",
    year: 2026,
    venue: "HollowTitanPhaseResearchGate.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/World/HollowTitanPhaseResearchGate.cs",
    topics: `${C}, concept image, research gate, mandatory read, content.md`,
    provenInProduction: true,
    notes:
      "Gate exports HollowTitanResearchExecutionBrief + active phase prompt. Open master + phase concept PNG and ≥2 hollow_titan content.md files before any kit edit.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: leave HollowVolume collider enabled",
    year: 2026,
    venue: "HollowTitanLandmarkMeatPhases.cs",
    topics: `${C}, hollow volume, collider, interior void, walkable marker`,
    provenInProduction: true,
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/World/HollowTitanLandmarkMeatPhases.cs",
    notes:
      "Phase 3: Interior/HollowVolume is invisible marker only — renderer off, collider removed. Walkable space comes from floor plates + stairs.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: block entrance arch with dead branch scatter",
    year: 2026,
    venue: "HollowTitanExteriorDressing.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/World/HollowTitanExteriorDressing.cs",
    topics: `${C}, entrance arch, dead branches, scatter mask, FloorPlan.EntranceForward`,
    provenInProduction: true,
    notes:
      "Phase 6 scatter must mask cone along EntranceForward. Phase 7 arch must remain readable — BossStagePortal approach visible.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: BUILD / GENERATE / MAKE hollow_titan research from agent chat",
    year: 2026,
    venue: "ResearchCache policy",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_HOLLOW_TITAN.md",
    topics: `${C}, research cache, sync-hollow-titan-research, no invent papers`,
    provenInProduction: true,
    notes:
      "Agents READ ResearchCache + run npm run sync-hollow-titan-research in Tools/cave-grader. Do NOT fabricate URLs or DO NOT lists in chat as authoritative.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: use cube arch when Log_Hollow_01 LP prefab loads",
    year: 2026,
    venue: "HollowTitanExteriorDressing.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/World/HollowTitanExteriorDressing.cs",
    topics: `${C}, cube arch, Log_Hollow_01, entrance framing, LP primary`,
    provenInProduction: true,
    notes:
      "TryPlaceEntranceArch prefers Log_Hollow_01 hyper-real hollow log frame. Cube + darker opening cube is fallback when LP asset missing from project.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "DO NOT: pick play-disk center tile if it blocks karst vista readability",
    year: 2026,
    venue: "HollowTitanLandmarkSitePicker.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/World/HollowTitanLandmarkSitePicker.cs",
    topics: `${C}, play disk center, vista POI, peak ring, site pick`,
    provenInProduction: true,
    notes:
      "Prefer peak-ring tiles with horizon sightlines. Avoid inner play-disk tiles that hide landmark behind foothill mass or labyrinth framing.",
  },
];

/** Appended to Hollow Titan action plans and active phase prompts. */
export const HOLLOW_TITAN_DO_NOT_PROMPT_BULLETS: string[] = [
  "Do NOT use CC0 L01–L04 white cylinders as primary trunk when LPMagicalForest prefabs are available.",
  "Do NOT attach world scatter spawns to Hollow Titan landmark floors — landmark catalog only.",
  "Do NOT place Hollow Titan on cave mouths, labyrinth benches, or trail corridor tiles.",
  "Do NOT rebuild FullWorld surface to fix a single meat-phase failure — re-run that phase only.",
  "Do NOT edit C# before opening concept images + HollowTitanResearchExecutionBrief.json.",
  "Do NOT block entrance arch with dead branch scatter — mask along EntranceForward.",
  "Do NOT fabricate hollow_titan research in chat — sync ResearchCache via npm script only.",
];
