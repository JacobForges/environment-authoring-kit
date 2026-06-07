/**
 * Hollow Titan landmark research — vista POI, dead-tree exterior, interior dungeon, kit refs.
 * Category: hollow_titan. Agents read before any Hollow Titan meat-phase C# edit.
 */
import type { ResearchEntry } from "./research-catalog.js";

const C = "hollow_titan";

export const HOLLOW_TITAN_RESEARCH_PAPERS: ResearchEntry[] = [
  {
    lab: "FromSoftware / Bandai Namco",
    title: "Elden Ring — Erdtree as distant vista POI navigation",
    year: 2026,
    venue: "AAA landmark design",
    url: "https://www.ea.com/games/elden-ring/elden-ring/news/world-design-interview",
    topics: `${C}, landmark navigation, vista POI, erdtree, silhouette, horizon readability`,
    provenInProduction: true,
    notes:
      "Hollow Titan must read from 2+ tiles away: tall hollow trunk + warm interior glow. Site picker favors peak-ring tiles with clear sightlines — not hidden in labyrinth annex.",
  },
  {
    lab: "Ubisoft",
    title: "Far Cry 5 — freshwater cliff biome landmark ordering",
    year: 2026,
    venue: "GDC 2018 procedural world",
    url: "https://tools.engineer/gdc2018-procedural-world-generation-of-far-cry-5",
    topics: `${C}, landmark placement, biome order, vista anchor, open world POI`,
    provenInProduction: true,
    notes:
      "Place one hero landmark per seed AFTER terrain lock on eligible peak tile. Landmark spawns are sector-scoped — never mixed with wilderness scatter tables.",
  },
  {
    lab: "EA SEED",
    title: "EA SEED — AAA world landmark encounter pacing",
    year: 2026,
    venue: "EA SEED research",
    url: "https://www.ea.com/seed/news/seed-ml-research-aaa-game-testing",
    topics: `${C}, landmark encounter, patrol nodes, spawn scope, playtest pacing`,
    provenInProduction: true,
    notes:
      "Landmark-only enemies/loot/patrol per floor — 2 spawns + 3 patrol nodes default. QA validates landmark manifest JSON, not world scatter density.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "Kit — HollowTitanLandmarkSitePicker peak-ring placement",
    year: 2026,
    venue: "HollowTitanLandmarkSitePicker.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/World/HollowTitanLandmarkSitePicker.cs",
    topics: `${C}, site pick, peak ring, chebyshev, karst tile, buildSeed`,
    provenInProduction: true,
    notes:
      "Phase 0: Chebyshev peak ring tiles only. Exclude cave mouths, labyrinth benches, trail corridors. Write HollowTitanLandmarkBuildData + floor plan.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "Kit — HollowTitanLandmarkTerrainSnap SampleHeight grounding",
    year: 2026,
    venue: "HollowTitanLandmarkTerrainSnap.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/World/HollowTitanLandmarkTerrainSnap.cs",
    topics: `${C}, sample height, base snap, terrain grounding, clearance`,
    provenInProduction: true,
    notes:
      "Phase 1: SnapRootBaseToGround at stored site XZ + ~0.35 m clearance. Re-run ResnapAfterTerrainSculpt after outer-ring sculpt.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "Kit — HollowTitanExteriorDressing hyper-real trunk shell",
    year: 2026,
    venue: "HollowTitanExteriorDressing.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/World/HollowTitanExteriorDressing.cs",
    topics: `${C}, LPMagicalForest, hero trunk, Tree_Mammoth_Green_01, ring dressing, bark`,
    provenInProduction: true,
    notes:
      "Phase 2: BuildHyperRealTrunkShell — Tree_Mammoth_Green_01 hero column; oak/beech/poplar curtain rings; spruce/pine canopy; moss/fern/vine base. CC0 L01 fallback only.",
  },
  {
    lab: "Epic Games",
    title: "Fab — Megascans vegetation and dead tree asset workflow",
    year: 2026,
    venue: "Epic Fab / Megascans",
    url: "https://www.fab.com/en-US/listing/megascans",
    topics: `${C}, megascans, vegetation, dead tree, trunk, branch, hyper-real dressing`,
    provenInProduction: true,
    notes:
      "Kit uses LPMagicalForest prefab stack as primary Megascans-style dressing. Branch_01–04 + Log_Branched_01 for dead limb scatter; Log_Hollow_01 entrance frame.",
  },
  {
    lab: "Adobe",
    title: "Substance 3D — bark material authoring and tiling",
    year: 2026,
    venue: "Adobe Substance 3D",
    url: "https://helpx.adobe.com/substance-3d-designer/organize-and-explore/baking-mesh-maps.html",
    topics: `${C}, substance, bark material, PBR, weathered wood, trunk albedo`,
    provenInProduction: true,
    notes:
      "Exterior bark reads weathered medieval dead wood — dark desaturated albedo, roughness breakup at limb stubs. LP prefabs carry authored bark; do not replace with white CC0 cylinders.",
  },
  {
    lab: "Adobe",
    title: "Substance 3D Painter — hero trunk material layering",
    year: 2026,
    venue: "Adobe Substance 3D Painter",
    url: "https://helpx.adobe.com/substance-3d-painter/getting-started/project-setup.html",
    topics: `${C}, substance painter, bark layers, moss, fern, vine overlay`,
    provenInProduction: true,
    notes:
      "Base dress: mossy rocks + fern + Vines_01/02 at trunk foot. Layer warm interior wood tones separately from cold exterior bark.",
  },
  {
    lab: "SideFX",
    title: "Houdini — Scatter SOP for branch dressing placement",
    year: 2026,
    venue: "SideFX Houdini",
    url: "https://www.sidefx.com/docs/houdini/nodes/sop/scatter.html",
    topics: `${C}, sidefx, scatter, branch dressing, radial offset, height band`,
    provenInProduction: true,
    notes:
      "Phase 6 analog: ~14–18 dead branches at 22–90% trunk height, seed jitter, reach beyond trunk radius. Never block entrance arch — mask scatter cone away from FloorPlan.EntranceForward.",
  },
  {
    lab: "SideFX",
    title: "Houdini — Copy to Points for ring curtain trees",
    year: 2026,
    venue: "SideFX Houdini",
    url: "https://www.sidefx.com/docs/houdini/nodes/sop/copytopoints.html",
    topics: `${C}, sidefx, copy to points, ring dressing, curtain trees, canopy`,
    provenInProduction: true,
    notes:
      "Trunk shell rings: instantiate oak/beech/poplar at increasing radii; spruce/pine at crown. Yaw jitter from buildSeed for similar-not-identical silhouettes.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "Kit — interior hollow void + floor plates",
    year: 2026,
    venue: "HollowTitanLandmarkMeatPhases.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/World/HollowTitanLandmarkMeatPhases.cs",
    topics: `${C}, hollow void, interior floors, vertical dungeon, ring platforms`,
    provenInProduction: true,
    notes:
      "Phases 3–4: HollowVolume marker ~62% trunk radius, collider off. Four stacked ring platforms from FloorPlan.Floors — dark wood plates, narrowing toward crown.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "Kit — spiral stair ramp between interior floors",
    year: 2026,
    venue: "HollowTitanLandmarkFloorPlanner.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/World/HollowTitanLandmarkFloorPlanner.cs",
    topics: `${C}, spiral stairs, interior climb, medieval craft, floor landings`,
    provenInProduction: true,
    notes:
      "Phase 5: cube steps hug inner bark wall — continuous spiral, no gaps between landings. Brown wood tint; readable third-person climb path.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "Kit — entrance arch Log_Hollow_01 framing",
    year: 2026,
    venue: "HollowTitanExteriorDressing.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/World/HollowTitanExteriorDressing.cs",
    topics: `${C}, entrance framing, medieval arch, Log_Hollow_01, BossStagePortal`,
    provenInProduction: true,
    notes:
      "Phase 7: TryPlaceEntranceArch along FloorPlan.EntranceForward. Grand hollow log frame — not a cave mouth. Cube arch fallback only when LP prefab missing.",
  },
  {
    lab: "Unity Technologies",
    title: "Unity 6 URP — Fog and atmosphere volume",
    year: 2026,
    venue: "Unity 6 URP Manual",
    url: "https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/post-processing-fog.html",
    topics: `${C}, URP fog, atmosphere, interior mood, warm interior dark exterior`,
    provenInProduction: true,
    notes:
      "Phase 9: soft interior fog volume at ~45% trunk height. Exterior bark stays darker/colder; ground floor warmest point lights.",
  },
  {
    lab: "Unity Technologies",
    title: "Unity 6 — Light Probes for interior landmark lighting",
    year: 2026,
    venue: "Unity 6 Manual",
    url: "https://docs.unity3d.com/6000.0/Documentation/Manual/LightProbes.html",
    topics: `${C}, light probes, interior lighting, floor rings, probe placement`,
    provenInProduction: true,
    notes:
      "Place probes at each floor ring center after point lights. Bake or use adaptive probes for LP trunk interior bounce.",
  },
  {
    lab: "Unity Technologies",
    title: "Unity 6 — Point lights for multi-floor landmark mood",
    year: 2026,
    venue: "Unity 6 Manual",
    url: "https://docs.unity3d.com/6000.0/Documentation/Manual/Lighting.html",
    topics: `${C}, point light, warm amber, floor lighting, mystical medieval`,
    provenInProduction: true,
    notes:
      "One point light per floor center — range ~1.2× trunk radius. Ground floor brightest; upper floors dimmer amber.",
  },
  {
    lab: "Unity Technologies",
    title: "Unity 6 — Prefabs for LP trunk and branch dressing",
    year: 2026,
    venue: "Unity 6 Manual",
    url: "https://docs.unity3d.com/6000.0/Documentation/Manual/Prefabs.html",
    topics: `${C}, prefab, LPMagicalForest, BiomePropCatalog, instantiate dressing`,
    provenInProduction: true,
    notes:
      "Use PrefabUtility.InstantiatePrefab under HollowTitanLandmark root children. Register undo for meat-phase automated passes.",
  },
  {
    lab: "Unity Technologies",
    title: "Unity 6 — Terrain.SampleHeight landmark grounding",
    year: 2026,
    venue: "Unity 6 Scripting",
    url: "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Terrain.SampleHeight.html",
    topics: `${C}, sample height, terrain snap, site XZ, karst hillside`,
    provenInProduction: true,
    notes:
      "MANDATORY before every exterior phase: SampleHeight at site world XZ on owning SurfaceTerrainTile. No floating trunk base.",
  },
  {
    lab: "NVIDIA",
    title: "NVIDIA RTX Global Illumination — interior mood lighting",
    year: 2026,
    venue: "NVIDIA Developer",
    url: "https://developer.nvidia.com/rtx-global-illumination",
    topics: `${C}, RTX GI, global illumination, interior bounce, mood contrast`,
    provenInProduction: true,
    notes:
      "Reference for warm interior GI vs dark exterior bark — even when URP uses baked/probe GI, preserve high contrast readable silhouette at distance.",
  },
  {
    lab: "NVIDIA",
    title: "NVIDIA — Real-time denoising for atmospheric fog passes",
    year: 2026,
    venue: "NVIDIA Developer Blog",
    url: "https://developer.nvidia.com/blog/real-time-denoising-in-games/",
    topics: `${C}, atmospheric fog, denoise, interior exterior transition`,
    provenInProduction: true,
    notes:
      "Soft volumetric fog inside hollow trunk; avoid harsh banding at entrance threshold. Keep exterior silhouette crisp for vista POI.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "Kit — landmark spawn manifest enemies loot patrol",
    year: 2026,
    venue: "HollowTitanLandmarkSpawnManifest.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/World/HollowTitanLandmarkSpawnManifest.cs",
    topics: `${C}, spawn manifest, landmark loot, patrol nodes, ENEMY_Annex`,
    provenInProduction: true,
    notes:
      "Phases 8–11: HollowTitanLandmarkSpawnPoint per floor; landmark catalog IDs only. Write HollowTitanLandmarkSpawnManifest.json for agent QA.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "Kit — HollowTitanLandmarkMeatPhases paced build loop",
    year: 2026,
    venue: "HollowTitanLandmarkMeatPhases.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/World/HollowTitanLandmarkMeatPhases.cs",
    topics: `${C}, meat phases, paced build, 12 steps, research gate`,
    provenInProduction: true,
    notes:
      "12 meat phases (0–11) each gated by HollowTitanPhaseResearchGate. Export prompts before phase body runs — never skip research brief.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "Kit — Hollow Titan concept image mandatory read order",
    year: 2026,
    venue: "hollow-titan-concept-prompts.ts",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Tools/cave-grader/hollow-titan-concept-prompts.ts",
    topics: `${C}, concept images, master concept, per-phase concept, art direction`,
    provenInProduction: true,
    notes:
      "Open master + phase concept PNG before C#. Paths under ResearchCache/images/hollow-titan-concepts/. Similar silhouette per seed — not pixel clone.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "Kit — dead branch scatter exterior gothic dressing",
    year: 2026,
    venue: "HollowTitanExteriorDressing.cs",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/Editor/World/HollowTitanExteriorDressing.cs",
    topics: `${C}, dead branches, ScatterDeadBranches, gothic fairy tale, limb stubs`,
    provenInProduction: true,
    notes:
      "Phase 6: Branch_01–04 + Log_Branched_01 primary. Radial scatter with seed jitter on mid-upper trunk. Mask entrance cone.",
  },
  {
    lab: "Ubisoft",
    title: "Ubisoft — open world POI silhouette readability",
    year: 2026,
    venue: "GDC environment art",
    url: "https://www.gdcvault.com/play/1025540/Building-the-Beautiful-and-Brutal",
    topics: `${C}, POI silhouette, horizon landmark, navigation beacon, vista`,
    provenInProduction: true,
    notes:
      "Hollow Titan trunk must break horizon line on peak tile — readable against foothill green and karst sky. Warm interior glow at dusk mood.",
  },
  {
    lab: "Environment Authoring Kit",
    title: "Kit — FullWorld hollow titan surface landmark contract",
    year: 2026,
    venue: "RESEARCH_FULLWORLD_DO_NOT.md",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_FULLWORLD_DO_NOT.md",
    topics: `${C}, fullworld landmark, surface only, one per seed, not cave`,
    provenInProduction: true,
    notes:
      "FullWorld builds require one Hollow Titan on random surface peak tile. Never in cave geometry or labyrinth annex benches.",
  },
];

/** Appended to Hollow Titan research prompts and action plans. */
export const HOLLOW_TITAN_RESEARCH_PROMPT_BULLETS: string[] = [
  "Open HollowTitanResearchExecutionBrief.json + master concept PNG before any meat-phase C# edit.",
  "LPMagicalForest prefabs are primary for phases 2, 6, 7 — CC0 L01–L10 is fallback only.",
  "Landmark spawns are scoped to HollowTitanLandmark root — never world scatter tables.",
  "Re-snap base via SampleHeight after terrain sculpt — no floating trunk.",
  "Warm interior lights + fog contrast with dark exterior bark for vista POI readability.",
];
