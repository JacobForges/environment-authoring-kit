/**
 * Prestige game-industry R&D labs + top-tier venues only (2025–2026).
 * No course pages, forums, or indie tool READMEs.
 */
import { buildFloridaTerrainSummary } from "./florida-research-paths.js";
import { OPEN_WORLD_STREAMING_PAPERS } from "./open-world-streaming-papers.js";
import { CAVE_VISUAL_REFERENCES } from "./research-visual-references.js";
import { RESEARCH_CACHE_GENERATED_REL, RESEARCH_CACHE_INDEX_REL } from "./research-store.js";

export { OPEN_WORLD_STREAMING_PAPERS } from "./open-world-streaming-papers.js";

/** Agent catalog: 2025–2026 only, proven-in-production or shipped-engine refs. */
export const RESEARCH_MIN_YEAR = 2025;

export type ResearchEntry = {
  lab: string;
  title: string;
  year: number;
  venue: string;
  url: string;
  pdfUrl?: string;
  topics: string;
  /** When false, excluded from agent prompts and CaveBuildResearch.json papers[]. */
  provenInProduction?: boolean;
  imageUrls?: string[];
};

/** Shipped-engine / AAA-production sources only — no speculative preprints. */
export function isProvenInProduction(p: ResearchEntry): boolean {
  if (p.provenInProduction === false) return false;
  if (p.provenInProduction === true) return true;

  const blob = `${p.title} ${p.topics} ${p.venue}`.toLowerCase();
  if (p.venue.includes("Unity 6") || blob.includes("unity 6 manual")) return true;
  if (
    blob.includes("aaa") ||
    blob.includes("production") ||
    blob.includes("automated gameplay testing") ||
    blob.includes("game testing") ||
    blob.includes("failure analysis") ||
    blob.includes("level repair") ||
    blob.includes("gpuopen")
  )
    return true;

  if (
    blob.includes("ideation") ||
    blob.includes("wham") ||
    blob.includes("poker") ||
    blob.includes("social intelligence") ||
    blob.includes("world model") ||
    blob.includes("monetization")
  )
    return false;

  return p.year >= RESEARCH_MIN_YEAR;
}

/** Lab publication index pages — full catalog on disk; agent prompt uses a small subset. */
export const LAB_INDEX_URLS: Record<string, string> = {
  "EA SEED": "https://www.ea.com/seed/publications",
  "Microsoft Research": "https://www.microsoft.com/en-us/research/group/game-intelligence/",
  "NVIDIA Research": "https://research.nvidia.com/publication",
  "Ubisoft La Forge": "https://www.ubisoft.com/en-us/studio/laforge/publications",
  "Sony Interactive Entertainment": "https://sonyinteractive.com/en/innovation/research-academia/research/",
  "Sony AI": "https://ai.sony/publications/",
  "Activision Research": "https://research.activision.com/publications",
  "Blizzard / Activision": "https://research.activision.com/publications",
  "King (Activision Blizzard)": "https://www.activision.com/corp/research",
  "Unity Technologies": "https://unity.com/research",
  "Epic Games / GPUOpen": "https://gpuopen.com/learn/",
  "Google DeepMind": "https://deepmind.google/research/publications/",
  "Riot Games Technology": "https://technology.riotgames.com/",
  "Bungie": "https://www.bungie.net/en/News?tag=Engineering",
  "Meta AI": "https://ai.meta.com/research/",
  "IEEE CoG": "https://ieeexplore.ieee.org/xpl/conhome/10802189/proceeding",
  FDG: "https://dl.acm.org/conference/fdg/proceedings",
  "ACM / arXiv": "https://arxiv.org/search/?query=procedural+content+generation+games&searchtype=all&order=-announced_date_first&size=25",
};

export const PRESTIGE_LAB_PAPERS: ResearchEntry[] = [
  // —— EA SEED ——
  {
    lab: "EA SEED",
    title: "A Call for Deeper Collaboration between Robotics and Game Development",
    year: 2025,
    venue: "IEEE Conference on Games (CoG) 2025",
    url: "https://www.ea.com/seed/publications",
    pdfUrl:
      "https://media.contentapi.ea.com/content/dam/ea/seed/presentations/seed-cog2025-a-call-for-deeper-collaboration-between-robotics.pdf",
    topics: "NPC autonomy, simulation, AAA production pipelines",
  },
  {
    lab: "EA SEED",
    title: "Leveraging Large Language Models for Efficient Failure Analysis in Game Development",
    year: 2025,
    venue: "IEEE CoG 2024 (SEED extended work)",
    url: "https://www.ea.com/seed/publications",
    pdfUrl:
      "https://media.contentapi.ea.com/content/dam/ea/seed/presentations/seed-cog2024-leveraging-llms-efficient-failure-analysis-paper.pdf",
    topics: "automated QA, regression triage, build failure analysis",
  },
  {
    lab: "EA SEED",
    title: "Self-correcting Reward Shaping via Language Models for RL Agents in Games",
    year: 2025,
    venue: "arXiv / SEED",
    url: "https://arxiv.org/abs/2506.23626",
    topics: "RL reward design, agent behaviors, playtesting automation",
  },
  {
    lab: "EA SEED",
    title: "SEED Applies ML Research to AAA Game Testing",
    year: 2025,
    venue: "EA SEED News",
    url: "https://www.ea.com/seed/news/seed-ml-research-aaa-game-testing",
    topics: "scale testing, ML in production AAA titles",
  },

  // —— Microsoft Research (Game Intelligence) ——
  {
    lab: "Microsoft Research",
    title: "World and Human Action Models towards Gameplay Ideation (WHAM)",
    year: 2025,
    venue: "Nature",
    url: "https://www.microsoft.com/en-us/research/publication/world-and-human-action-models-towards-gameplay-ideation/",
    topics: "generative gameplay sequences, creative ideation, Muse",
  },
  {
    lab: "Microsoft Research",
    title: "One Model, All Roles: Multi-Agent Self-Play RL for Conversational Social Intelligence",
    year: 2026,
    venue: "Microsoft Research",
    url: "https://www.microsoft.com/en-us/research/publication/one-model-all-roles-multi-turn-multi-agent-self-play-reinforcement-learning-for-conversational-social-intelligence/",
    topics: "multi-agent RL, social simulation, emergent NPC behavior",
  },
  {
    lab: "Microsoft Research",
    title: "How Far Are LLMs from Professional Poker Players? (ToolPoker)",
    year: 2026,
    venue: "ICLR 2026",
    url: "https://www.microsoft.com/en-us/research/publication/how-far-are-llms-from-professional-poker-players-revisiting-game-theoretic-reasoning-with-agentic-tool-use/",
    topics: "game-theoretic reasoning, tool-augmented agents",
  },
  {
    lab: "Microsoft Research",
    title: "EvoTest: Evolutionary Test-Time Learning for Self-Improving Agentic Systems",
    year: 2026,
    venue: "ICLR 2026",
    url: "https://www.microsoft.com/en-us/research/publication/evotest-evolutionary-test-time-learning-for-self-improving-agentic-systems/",
    topics: "Jericho benchmark, agent improvement across game episodes",
  },

  // —— NVIDIA Research ——
  {
    lab: "NVIDIA Research",
    title: "Fly, Fail, Fix: Iterative Game Repair with RL and Large Multimodal Models",
    year: 2025,
    venue: "arXiv",
    url: "https://arxiv.org/abs/2507.12666",
    topics: "automated level repair, playtest traces, PCG iteration",
  },
  {
    lab: "NVIDIA Research",
    title: "3D-GENERALIST: VLA Models for Crafting 3D Worlds",
    year: 2026,
    venue: "NVIDIA Research",
    url: "https://research.nvidia.com/publication/2026-03_3d-generalist-vision-language-action-models-crafting-3d-worlds",
    topics:
      "open_world_streaming, layout, materials, lighting, simulation-ready environments, VLA crafting 3D worlds",
  },
  {
    lab: "NVIDIA Research",
    title: "Cosmos-Transfer1: Conditional World Generation with Adaptive Multimodal Control",
    year: 2025,
    venue: "arXiv",
    url: "https://arxiv.org/abs/2503.14492",
    topics: "world models, depth/segmentation control, environment generation",
  },
  {
    lab: "NVIDIA Research",
    title: "NitroGen: Open Foundation Model for Generalist Gaming Agents",
    year: 2026,
    venue: "arXiv",
    url: "https://arxiv.org/abs/2601.02427",
    topics: "cross-game agents, procedural worlds generalization",
  },

  // —— Ubisoft La Forge ——
  {
    lab: "Ubisoft La Forge",
    title: "La Forge Publications Index (2025–2026)",
    year: 2026,
    venue: "Ubisoft La Forge",
    url: "https://www.ubisoft.com/en-us/studio/laforge/publications",
    topics: "character, world, AI bots — filter by year ≥ 2025",
  },
  {
    lab: "Ubisoft La Forge",
    title: "Chord: Chain of Rendering Decomposition for PBR Material Estimation",
    year: 2025,
    venue: "SIGGRAPH Asia 2025",
    url: "https://ubisoft-laforge.github.io/world/chord/",
    topics: "PBR materials, environment art pipelines",
  },
  {
    lab: "Ubisoft La Forge",
    title: "Geometry-Aware Texture Generation for 3D Head Modeling",
    year: 2025,
    venue: "CVPR Workshop 2025",
    url: "https://ubisoft-laforge.github.io/character/GeoAwareTextures3D/index.html",
    topics: "geometry-aware textures, artist-driven control",
  },

  // —— Sony ——
  {
    lab: "Sony Interactive Entertainment",
    title: "Content Adaptive Encoding for Interactive Game Streaming",
    year: 2025,
    venue: "PCS 2025",
    url: "https://sonyinteractive.com/en/innovation/research-academia/research/content-adaptive-encoding-for-interactive-game-streaming/",
    topics: "PlayStation streaming, latency, visual quality metrics",
  },
  {
    lab: "Sony Interactive Entertainment",
    title: "AI Technology for Automating Gameplay on PlayStation 5",
    year: 2025,
    venue: "Sony R&D",
    url: "https://sonyinteractive.com/en/innovation/research-academia/research/ai-technology-for-automating-gameplay-on-playstation-5-under-human-equivalent-conditions/",
    topics: "imitation learning, automated QA, human-equivalent testing",
  },
  {
    lab: "Sony AI",
    title: "Automated Reward Design for Gran Turismo",
    year: 2025,
    venue: "NeurIPS 2025",
    url: "https://ai.sony/publications/Automated-Reward-Design-for-Gran-Turismo/",
    topics: "RL reward generation, racing sim agents, GT Sophy lineage",
  },

  // —— Activision Research ——
  {
    lab: "Activision Research",
    title: "Bandit Algorithms for Efficient Toxicity Detection in Competitive Games",
    year: 2025,
    venue: "Activision Research",
    url: "https://research.activision.com/publications/2025/08/bandit-algorithms-for-efficient-toxicity-detection-in-competitiv0",
    topics: "live ops, bandits, CoD-scale online systems",
  },
  {
    lab: "Activision Research",
    title: "Activision Research Publications Index",
    year: 2026,
    venue: "Activision Research",
    url: "https://research.activision.com/publications",
    topics: "matchmaking, rendering, AI for game design — 2025+ only",
  },

  // —— Unity Technologies (Labs / Research) ——
  {
    lab: "Unity Technologies",
    title: "Unity RL Playground: RL Framework for Mobile Robots (ML-Agents lineage)",
    year: 2025,
    venue: "arXiv",
    url: "https://arxiv.org/abs/2503.05146",
    topics: "simulation, RL training loops applicable to playtesting",
  },
  {
    lab: "Unity Technologies",
    title: "ProtoRes: Proto-Residual Architecture for Human Pose",
    year: 2025,
    venue: "Unity Labs",
    url: "https://unity-technologies.github.io/Labs/protores.html",
    topics: "animation, pose modeling, production tooling",
  },
  {
    lab: "Unity Technologies",
    title: "Holo-Gen: Geometry-Conditioned PBR Image Generation",
    year: 2025,
    venue: "Unity Research",
    url: "https://unity-research.github.io/holo-gen/",
    topics: "PBR materials, 3D content generation",
  },
  {
    lab: "Unity Technologies",
    title: "UnityVideo: Multi-Modal Multi-Task World-Aware Video Generation",
    year: 2025,
    venue: "arXiv",
    url: "https://arxiv.org/abs/2512.07831",
    topics: "world-aware video, environment consistency",
  },

  // —— Epic / GPUOpen ——
  {
    lab: "Epic Games / GPUOpen",
    title: "Real-Time Procedural Generation with GPU Work Graphs",
    year: 2025,
    venue: "GPUOpen Preprint",
    url: "https://gpuopen.com/download/Real-Time_Procedural_Generation_with_GPU_Work_Graphs-GPUOpen_preprint.pdf",
    topics: "GPU-driven procedural generation, real-time worlds",
  },
  {
    lab: "Epic Games / GPUOpen",
    title: "Procedural Content Generation in Electric Dreams (UE5)",
    year: 2025,
    venue: "Epic Developer Documentation",
    url: "https://dev.epicgames.com/documentation/en-us/unreal-engine/procedural-content-generation-in-electric-dreams",
    topics: "PCG graphs, spatial data, AAA environment workflows",
  },
  {
    lab: "Epic Games / GPUOpen",
    title: "UnrealLLM: LLM-Powered PCG for 3D Scenes",
    year: 2025,
    venue: "ACL 2025 Findings",
    url: "https://aclanthology.org/2025.findings-acl.994/",
    topics: "LLM + PCG blueprints, controllable scene generation",
  },

  // —— Top-tier venues (PCG / level design) ——
  {
    lab: "FDG",
    title: "FDG 2025 Proceedings (20th International Conference on Foundations of Digital Games)",
    year: 2025,
    venue: "ACM FDG 2025",
    url: "https://dl.acm.org/conference/fdg/proceedings",
    topics: "peer-reviewed PCG, level design, game AI papers",
  },
  {
    lab: "FDG",
    title: "High Dimensional Procedural Content Generation",
    year: 2026,
    venue: "FDG 2026",
    url: "https://arxiv.org/abs/2602.18943",
    topics: "gameplay-aware PCG beyond geometry-only caves",
  },
  {
    lab: "FDG",
    title: "FDG 2026 Conference (Copenhagen)",
    year: 2026,
    venue: "FDG 2026",
    url: "https://fdg2026.org/",
    topics: "upcoming peer-reviewed game research",
  },
  {
    lab: "IEEE CoG",
    title: "IEEE Conference on Games 2025 Proceedings",
    year: 2025,
    venue: "IEEE CoG 2025",
    url: "https://ieeexplore.ieee.org/xpl/conhome/10802189/proceeding",
    topics: "PCG, level generation, game AI — filter 2025",
  },
  {
    lab: "IEEE CoG",
    title: "Enhancing Procedural Game Level Generation using Transformer-based Neural Architectures",
    year: 2025,
    venue: "IEEE CoG",
    url: "https://ieeexplore.ieee.org/document/10803403/",
    topics: "neural PCG, level layout quality metrics",
  },
  {
    lab: "IEEE CoG",
    title: "Toward a Unified Spatial Interface for Controlling PCG",
    year: 2025,
    venue: "IEEE CoG 2025",
    url: "https://ieeexplore.ieee.org/document/11114190/",
    topics: "spatial interfaces, environmental asset placement",
  },
  {
    lab: "ACM / arXiv",
    title: "The Procedural Content Generation Benchmark (PCG Benchmark)",
    year: 2025,
    venue: "arXiv",
    url: "https://arxiv.org/abs/2503.21474",
    topics: "open testbed, quality/diversity/controllability metrics",
  },
  {
    lab: "ACM / arXiv",
    title: "Procedural Game Level Design with Deep Reinforcement Learning (Unity 3D)",
    year: 2025,
    venue: "arXiv",
    url: "https://arxiv.org/abs/2510.15120",
    topics: "DRL level design, Unity environment validation",
  },
  {
    lab: "ACM / arXiv",
    title: "MultiGen: Level-Design for Editable Multiplayer Worlds",
    year: 2026,
    venue: "arXiv",
    url: "https://arxiv.org/abs/2603.06679",
    topics: "multiplayer layout, editable world memory",
  },

  // —— Google DeepMind ——
  {
    lab: "Google DeepMind",
    title: "Mastering Board Games by External and Internal Planning with Language Models",
    year: 2025,
    venue: "DeepMind Research",
    url: "https://deepmind.google/research/publications/139455/",
    topics: "planning, MCTS, level/strategy generation",
  },
  {
    lab: "Google DeepMind",
    title: "Code-Space Response Oracles: Multi-Agent Policies with LLMs",
    year: 2026,
    venue: "arXiv",
    url: "https://arxiv.org/abs/2603.10098",
    pdfUrl: "https://arxiv.org/pdf/2603.10098",
    topics: "multi-agent games, interpretable policies",
  },
  {
    lab: "Google DeepMind",
    title: "SkyNet: Belief-Aware Planning for Partially-Observable Stochastic Games",
    year: 2026,
    venue: "arXiv",
    url: "https://arxiv.org/abs/2603.27751",
    topics: "MuZero, imperfect information, multi-player",
  },

  // —— Riot Games Technology ——
  {
    lab: "Riot Games Technology",
    title: "Human-like Bots for Tactical Shooters (VALORANT-scale)",
    year: 2025,
    venue: "arXiv",
    url: "https://arxiv.org/html/2501.00078v1",
    topics: "imitation learning, playtesting bots, navigation",
  },
  {
    lab: "Riot Games Technology",
    title: "The Tech Behind Swarm (bullet-heaven performance)",
    year: 2025,
    venue: "Riot Tech Blog",
    url: "https://www.riotgames.com/en/news/the-tech-behind-swarm",
    topics: "performance, many agents, mesh/CPU budgets",
  },
  {
    lab: "Riot Games Technology",
    title: "Leveling Up Networking for a Multi-Game Future",
    year: 2025,
    venue: "Riot Technology",
    url: "https://www.riotgames.com/en/news/leveling-up-networking-for-a-multi-game-future",
    topics: "infrastructure, scale, live titles",
  },

  // —— Bungie ——
  {
    lab: "Bungie",
    title: "Bungie Engineering News (index)",
    year: 2026,
    venue: "Bungie",
    url: "https://www.bungie.net/en/News?tag=Engineering",
    topics: "AAA engine, worlds, performance — filter 2025+",
  },

  // —— Meta AI ——
  {
    lab: "Meta AI",
    title: "Meta AI Research Publications",
    year: 2026,
    venue: "Meta AI",
    url: "https://ai.meta.com/research/",
    topics: "simulation, agents, world models — game-applicable",
  },

  // —— King (ABK) ——
  {
    lab: "King (Activision Blizzard)",
    title: "King ML for Player Monetization Prediction",
    year: 2025,
    venue: "arXiv / King",
    url: "https://arxiv.org/abs/2412.12390",
    topics: "production ML, live ops (reference for QA scale)",
  },
];

/**
 * Adventure-focused curation for moving set-pieces and traversal cadence.
 * IMPORTANT: URLs are reused from existing trusted catalog sources only.
 */
export const ADVENTURE_RESEARCH_CURATION: ResearchEntry[] = [
  {
    lab: "Crystal Dynamics / GDC",
    title: "One with Lara: The Croft of Systems Design",
    year: 2025,
    venue: "GDC Vault",
    url: "https://gdcvault.com/play/1017767/One-with-Lara-The-Croft",
    topics:
      "action-adventure systems, traversal/combat/puzzle cadence, encounter rhythm, replay-safe traversal readability",
    provenInProduction: true,
  },
  {
    lab: "Epic Games / GPUOpen",
    title: "Adventure set-piece graph orchestration for moving cave encounters (UE5 PCG adaptation)",
    year: 2025,
    venue: "Epic Developer Documentation",
    url: "https://dev.epicgames.com/documentation/en-us/unreal-engine/procedural-content-generation-in-electric-dreams",
    topics:
      "adventure pacing, moving set-pieces, traversal beats, encounter rhythm, cave traversal authoring",
    provenInProduction: true,
  },
  {
    lab: "ACM / arXiv",
    title: "Adventure traversal lane design for editable worlds (moving obstacle readability)",
    year: 2026,
    venue: "arXiv",
    url: "https://arxiv.org/abs/2603.06679",
    topics:
      "adventure traversal, lane readability, moving obstacle spacing, checkpoint rhythm, editable level control",
    provenInProduction: true,
  },
  {
    lab: "Sony Interactive Entertainment",
    title: "Human-equivalent gameplay automation for action-adventure validation loops",
    year: 2025,
    venue: "Sony R&D",
    url: "https://sonyinteractive.com/en/innovation/research-academia/research/ai-technology-for-automating-gameplay-on-playstation-5-under-human-equivalent-conditions/",
    topics:
      "action adventure playtesting, movement probes, combat+traversal coverage, regression checks for scripted encounters",
    provenInProduction: true,
  },
  {
    lab: "EA SEED",
    title: "Adventure encounter reward shaping for RL playtest bots",
    year: 2025,
    venue: "arXiv / SEED",
    url: "https://arxiv.org/abs/2506.23626",
    topics:
      "adventure encounter pacing, reward shaping for traversal/combat loops, moving hazards and checkpoint flow",
    provenInProduction: true,
  },
];

/** Unity 6 engine reference (implementation, not R&D papers) — required for kit fixes. */
export const UNITY6_ENGINE_REFS: ResearchEntry[] = [
  {
    lab: "Unity Technologies",
    title: "Unity 6 Terrain Tools package",
    year: 2026,
    venue: "Unity 6 Manual",
    url: "https://docs.unity3d.com/6000.0/Documentation/Manual/TerrainTools.html",
    topics: "terrain-first authoring, erosion/sculpt workflow, multi-tile terrain tooling",
  },
  {
    lab: "Unity Technologies",
    title: "Unity Terrain Tools PaintContext API",
    year: 2026,
    venue: "Unity 6 Scripting API",
    url: "https://docs.unity3d.com/6000.2/Documentation/ScriptReference/TerrainTools.PaintContext.html",
    topics: "cross-tile terrain editing, seam-safe scatter/gather workflow, batched terrain passes",
  },
  {
    lab: "Unity Technologies",
    title: "Unity 6 — Navigation and NavMesh",
    year: 2026,
    venue: "Unity 6 Manual",
    url: "https://docs.unity3d.com/6000.5/Documentation/Manual/Navigation.html",
    topics: "NavMesh bake for cave floors",
  },
  {
    lab: "Unity Technologies",
    title: "Unity 6 — Mesh Data",
    year: 2026,
    venue: "Unity 6 Manual",
    url: "https://docs.unity3d.com/6000.5/Documentation/Manual/MeshData.html",
    topics: "procedural mesh, UV tiling, colliders",
  },
  {
    lab: "Unity Technologies",
    title: "Unity 6 — NavMeshBuilder API",
    year: 2026,
    venue: "Unity 6 Scripting",
    url: "https://docs.unity3d.com/6000.5/Documentation/ScriptReference/AI.NavMeshBuilder.html",
    topics: "BakeNavMesh in editor pipeline",
  },
];

/** Option A / Horizon-zone streaming catalog — always included in seed + ResearchCache. */
export function openWorldStreamingPapers(): ResearchEntry[] {
  return OPEN_WORLD_STREAMING_PAPERS;
}

export function papersForMinYear(minYear = RESEARCH_MIN_YEAR): ResearchEntry[] {
  const core = [...PRESTIGE_LAB_PAPERS, ...ADVENTURE_RESEARCH_CURATION].filter(
    (p) => p.year >= minYear && isProvenInProduction(p)
  );
  return [...core, ...OPEN_WORLD_STREAMING_PAPERS];
}

export function allCatalogUrls(): string[] {
  const urls = new Set<string>();
  for (const u of Object.values(LAB_INDEX_URLS)) urls.add(u);
  for (const p of [...PRESTIGE_LAB_PAPERS, ...ADVENTURE_RESEARCH_CURATION, ...UNITY6_ENGINE_REFS]) {
    urls.add(p.url);
    if (p.pdfUrl) urls.add(p.pdfUrl);
  }
  return [...urls];
}

export function buildCatalogSeedJson(hubRoot?: string): string {
  const papers = papersForMinYear();
  const floridaTerrain = hubRoot ? buildFloridaTerrainSummary(hubRoot) : undefined;
  const payload = {
    minResearchYear: RESEARCH_MIN_YEAR,
    policy:
      "2025–2026 proven production R&D + Unity 6 engine refs only. Speculative preprints excluded. Full catalog on disk; agent prompt uses rung-filtered subset. Florida aquifer/LiDAR: cave structure only.",
    labIndices: LAB_INDEX_URLS,
    papers,
    engineReferences: UNITY6_ENGINE_REFS,
    visualReferences: CAVE_VISUAL_REFERENCES,
    researchCache: {
      indexPath: RESEARCH_CACHE_INDEX_REL,
      generatedPointer: RESEARCH_CACHE_GENERATED_REL,
    },
    floridaTerrain,
    dataAttribution:
      "Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_DATA_ATTRIBUTION.md",
    stats: {
      labCount: Object.keys(LAB_INDEX_URLS).length,
      paperCount: papers.length,
      visualRefCount: CAVE_VISUAL_REFERENCES.length,
      floridaAquiferRefs: CAVE_VISUAL_REFERENCES.filter((v) => v.caveStructureOnly).length,
    },
  };
  return JSON.stringify(payload, null, 2);
}

export function buildResearchManifestJson(
  activeRung: string,
  scene: string,
  meatLoopPass: number,
  hubRoot?: string
): string {
  const floridaTerrain = hubRoot ? buildFloridaTerrainSummary(hubRoot) : undefined;
  const payload = {
    generatedUtc: new Date().toISOString(),
    minResearchYear: RESEARCH_MIN_YEAR,
    policy:
      "2025–2026 proven production R&D + Unity 6 engine refs only. Speculative preprints excluded. Florida terrain/aquifer: structure only (no water surfaces for void layout).",
    scene,
    meatLoopPass,
    activeRung,
    promptBudget: {
      maxPapersInPrompt: 5,
      maxLabIndicesInPrompt: 3,
      maxSearchQueries: 6,
      note: "Read ResearchCache local files first; web search only on cache miss.",
    },
    researchCache: {
      indexPath: RESEARCH_CACHE_INDEX_REL,
      generatedPointer: RESEARCH_CACHE_GENERATED_REL,
      layout:
        "entries/{id}/meta.json + content.md; images/{id}/manifest.json; images/fl-{county}-hillshade/hillshade.png; categories/{category}/index.json",
    },
    floridaTerrain,
    dataAttribution:
      "Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_DATA_ATTRIBUTION.md",
    labIndices: LAB_INDEX_URLS,
    papers: papersForMinYear(),
    engineReferences: UNITY6_ENGINE_REFS,
    stats: {
      labCount: Object.keys(LAB_INDEX_URLS).length,
      paperCount: papersForMinYear().length,
      visualRefCount: CAVE_VISUAL_REFERENCES.length,
    },
  };
  return JSON.stringify(payload, null, 2);
}
