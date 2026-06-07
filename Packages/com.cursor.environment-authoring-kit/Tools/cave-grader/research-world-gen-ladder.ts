/**
 * World-generation pipeline ladder — AAA, engine, Houdini, and indie R&D.
 * Serialized to Assets/EnvironmentKit/ResearchCache via sync-research-cache.
 * Companion doc: Packages/com.cursor.environment-authoring-kit/docs/WORLD-GENERATION-PIPELINE-LADDER.md
 */
import type { VisualReference } from "./research-visual-references.js";

export const WORLD_GEN_LADDER_REFERENCES: VisualReference[] = [
  {
    id: "world-gen-pipeline-ladder-best-practices",
    title: "World generation pipeline ladder — ordered phases (synthesis)",
    year: 2026,
    category: "pcg_shell",
    provenInProduction: true,
    studio: "Environment Kit (curated from AAA + engine R&D)",
    docUrl: "https://tools.engineer/gdc2018-procedural-world-generation-of-far-cry-5",
    imageUrls: [],
    dataUrls: [
      "https://tools.engineer/gdc2018-procedural-world-generation-of-far-cry-5",
      "https://christianjmills.com/posts/procedural-tools-far-cry-5-notes/",
      "https://blog.playstation.com/2018/03/22/the-procedural-world-generation-of-far-cry-5/",
      "https://www.gdcvault.com/play/1024120/GPU-Based-Run-Time-Procedural",
      "https://www.guerrilla-games.com/read/gpu-based-procedural-placement-in-horizon-zero-dawn",
      "https://www.gdcvault.com/play/1024124/Creating-a-Tools-Pipeline-for",
      "https://www.strayspark.studio/blog/procedural-content-generation-pcg-framework-production-ue5-7",
      "https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/PCG/UPCGGraph",
      "https://www.sidefx.com/docs/houdini/heightfields/creation.html",
      "https://www.sidefx.com/docs/houdini/heightfields/scattersop.html",
      "https://www.polygon.com/2017/3/2/14790028/no-mans-sky-was-flat-procedural-world-generation-maths/",
      "https://en.wikipedia.org/wiki/No_Man%27s_Sky",
      "https://www.nvidia.com/en-us/on-demand/session/gtcspring23-s52180/",
      "https://research.nvidia.com/publication/2025-07_fly-fail-fix-iterative-game-repair-rl-and-large-multimodal-models",
    ],
    notes:
      "LADDER RULE: one global FIFO queue; each rung consumes immutable artifacts from upstream and writes new artifacts downstream. Never rerun a completed rung unless its inputs changed (seed, scope, or upstream mask). Invalidate downstream only. " +
      "ORDER (Environment Kit mapping): (0) research+seed lock; (1) macro terrain + Florida hillshade reference; (2) hydrology/karst masks (structure only, no water sim); (3) trails/splines + NavMesh walk band; (4) surface props scatter; (5) Cursor pre-build gate; (6) cave layout+spline; (7) route floor/ceiling mesh+NavMesh; (8) shell rings+materials; (9) gameplay props+mobs; (10) validation bots (read-only, no full playtest ladder); (11) polish/post only if grade fails. " +
      "AAA: Far Cry 5 — freshwater masks before cliffs before biomes; tools pass 2D masks between Houdini steps; nightly full regen only on build machines, not per artist click. " +
      "Guerrilla HZD — GPU procedural placement after terrain rules; artist graph defines order; runtime placement is downstream of terrain theme. " +
      "UE5 PCG — topological task order from graph edges; pin seeds while debugging; bake-time default, runtime only when streaming requires it. " +
      "SideFX — heightfield LOD upsample chain; scatter points separate from terrain mesh; masks drive both erosion and scatter density. " +
      "NMS/indie — single master seed + deterministic sub-seeds per cell; generate on demand from coordinates, do not store full world. " +
      "Bot: read this entry + Packages/com.cursor.environment-authoring-kit/docs/WORLD-GENERATION-PIPELINE-LADDER.md before changing queue order; ContinueCaveGeometryAfterPreBuild must skip rungs 0–5.",
  },
  {
    id: "ubisoft-farcry5-freshwater-cliff-biome-order",
    title: "Ubisoft — Far Cry 5 tool chain order (freshwater → cliffs → biomes)",
    year: 2018,
    category: "studio_environment",
    provenInProduction: true,
    studio: "Ubisoft",
    docUrl: "https://tools.engineer/gdc2018-procedural-world-generation-of-far-cry-5",
    imageUrls: [
      "https://cdn.akamai.steamstatic.com/steam/apps/552520/header.jpg",
    ],
    dataUrls: [
      "https://www.gamedeveloper.com/design/video-the-world-generation-tech-behind-i-far-cry-5-i-",
      "https://christianjmills.com/posts/procedural-tools-far-cry-5-notes/",
      "https://blog.playstation.com/2018/03/22/the-procedural-world-generation-of-far-cry-5/",
      "https://gdcvault.com/search?type=session&q=far+cry+5+procedural",
    ],
    notes:
      "Sequential Houdini Engine tools exchange terrain masks: freshwater splines produce water surface + waterside assets + water mask; cliff tool consumes slope masks; biome tool consumes freshwater/road/fence/cliff masks. Sectors 64×64m minimum bake unit. Nightly world regen on build farm — editor sessions bake incrementally per sector. Kit analog: surface hydrology/trails before CavePrefabScatter.",
  },
  {
    id: "guerrilla-horizon-gpu-procedural-placement",
    title: "Guerrilla — Horizon GPU runtime procedural placement (GDC 2017)",
    year: 2017,
    category: "pcg_shell",
    provenInProduction: true,
    studio: "Guerrilla Games",
    docUrl:
      "https://www.guerrilla-games.com/read/gpu-based-procedural-placement-in-horizon-zero-dawn",
    imageUrls: [
      "https://cdn.akamai.steamstatic.com/steam/apps/1151640/header.jpg",
    ],
    dataUrls: [
      "https://www.gdcvault.com/play/1024120/GPU-Based-Run-Time-Procedural",
      "https://www.gdcvault.com/play/1025066/Between-Tech-and-Art-The",
      "https://www.gdcvault.com/play/1024124/Creating-a-Tools-Pipeline-for",
    ],
    notes:
      "Artist rule graph → GPU placement of full environment slices (audio, VFX, wildlife, gameplay) around player. Terrain theme and roads are inputs to placement rules, not regenerated by scatter. Kit analog: prop/scatter rungs after terrain+trail masks exist; do not rebake terrain when placing props.",
  },
  {
    id: "epic-ue5-pcg-topological-execution",
    title: "Epic — UE5 PCG graph execution order and bake vs runtime",
    year: 2026,
    category: "pcg_shell",
    provenInProduction: true,
    studio: "Epic Games",
    docUrl:
      "https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/PCG/UPCGGraph",
    imageUrls: [],
    dataUrls: [
      "https://www.strayspark.studio/blog/procedural-content-generation-pcg-framework-production-ue5-7",
      "https://dev.epicgames.com/documentation/en-us/unreal-engine/procedural-content-generation-overview",
      "https://github.com/PCGEx/PCGExtendedToolkit/tree/5.7",
    ],
    notes:
      "Compiler weak-orders tasks: successors after predecessors; PreExecute/PostExecute bracket graph. Pin node seeds while debugging; world-position seeds in shipping. Prefer bake-time PCG; runtime PCG only for streaming cells. bIgnoreLandscapeTracking prevents accidental full regen on unrelated landscape edits — kit analog: scoped AssetDatabase refresh + phase completion flags.",
  },
  {
    id: "sidefx-houdini-heightfield-ladder",
    title: "SideFX — Heightfield creation → mask → scatter ladder",
    year: 2026,
    category: "terrain",
    provenInProduction: true,
    studio: "SideFX",
    docUrl: "https://www.sidefx.com/docs/houdini/heightfields/creation.html",
    imageUrls: [],
    dataUrls: [
      "https://www.sidefx.com/docs/houdini/heightfields/scattersop.html",
      "https://www.sidefx.com/docs/houdini/heightfields/scattersolaris.html",
      "https://www.sidefx.com/docs/houdini/heightfields/scatterattribs.html",
      "https://www.sidefx.com/community-main-menu/complete-a-z-terrain-handbook/",
    ],
    notes:
      "Low-res heightfield → detail passes → upsample → re-apply erosion/distortion per LOD step. Keep scatter points separate from terrain geometry (Keep Incoming Terrain off on scatter). Masks from noise/erosion drive COPs texturing and scatter density. Kit analog: SurfaceTerrainAiPhases ladder + separate prop scatter after terrain grade passes.",
  },
  {
    id: "hello-games-nms-deterministic-seed-pipeline",
    title: "Hello Games — No Man's Sky deterministic coordinate generation",
    year: 2017,
    category: "studio_environment",
    provenInProduction: true,
    studio: "Hello Games",
    docUrl:
      "https://www.polygon.com/2017/3/2/14790028/no-mans-sky-was-flat-procedural-world-generation-maths/",
    imageUrls: [
      "https://cdn.akamai.steamstatic.com/steam/apps/275850/header.jpg",
    ],
    dataUrls: [
      "https://en.wikipedia.org/wiki/No_Man%27s_Sky",
      "https://dev.to/dubeykartikay/how-no-mans-sky-creates-18-quintillion-planets-with-just-math-3fgf",
    ],
    notes:
      "64-bit master seed cascades to sub-seeds; planet content from coordinates + algorithms (Perlin, domain warp, biome rules) without storing worlds on servers. Positive/negative space passes for cliffs vs caves. Kit analog: CaveLayoutRoll + per-phase seed; session gate caches research/compile per seed — do not re-export 123 prompts on continue-after-pre-build.",
  },
  {
    id: "nvidia-fly-fail-fix-iterative-pcg-repair",
    title: "NVIDIA Research — Fly, Fail, Fix (iterative level repair)",
    year: 2025,
    category: "pcg_shell",
    provenInProduction: true,
    studio: "NVIDIA Research",
    docUrl: "https://arxiv.org/abs/2507.12666",
    imageUrls: [],
    dataUrls: [
      "https://research.nvidia.com/publication/2025-07_fly-fail-fix-iterative-game-repair-rl-and-large-multimodal-models",
    ],
    notes:
      "Validation/repair loops run after generation, adjusting only failed subsystems from playtest traces — not full regen. Kit analog: validation substeps + SurfaceTrailWalkabilityRepair capped passes; never invoke full CavePlaytestPreBuildPipeline inside surface route probe.",
  },
];
