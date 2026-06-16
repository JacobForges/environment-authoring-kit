/**
 * Cursor web research for AI Build Planner — uses Agent.create + web search tools.
 * Usage: npx tsx planner-research.ts <request.json>
 */
import "./planner-load-dotenv.ts";
import { readFileSync } from "node:fs";
import { Agent } from "@cursor/sdk";

type ResearchRequest = {
  hubRoot: string;
  tabId?: string;
  researchFocus?: string;
  brief?: {
    title?: string;
    summary?: string;
    userGoals?: string[];
    researchQueries?: string[];
  };
  sessionConfig?: Record<string, unknown>;
  queries?: string[];
};

type ResearchItem = {
  id: string;
  title: string;
  url: string;
  category: string;
  summary: string;
  sourceType: string;
  topics: string;
  year: number;
  approved: boolean;
};

const TAB_AGENT_INTRO: Record<string, string> = {
  terrain:
    "Environment Kit **Terrain** research agent — procedural world scope, tile grids, Florida karst surface, playable demo footprint.",
  "surface-content":
    "Environment Kit **Surface content** research agent — MainScene NPCs, hybrid Talk+Shop, quest gating, plaza placement.",
  caves:
    "Environment Kit **Caves** research agent — lava tube routes, dungeon room graphs, underground navmesh.",
  mazes:
    "Environment Kit **Mazes** research agent — terrain labyrinth topology, grid navigation, landmark cells.",
  "interior-content":
    "Environment Kit **Interior content** research agent — dungeon population, patrol routes, quest items inside caves.",
  atmosphere:
    "Environment Kit **Atmosphere** research agent — lighting, water, fog, VFX, cutscene trigger volumes.",
  music:
    "Music Director research agent — pop/trap production, bedroom vocal chain, game OST instrumental prompts, streaming mix.",
};

const TAB_CATEGORIES: Record<string, string[]> = {
  terrain: ["fullworld_generation_style", "terrain_scope", "navmesh", "playable_demo"],
  "surface-content": ["npc_dialog", "quest_design", "content_placement", "shop_ux"],
  caves: ["cave_structure", "dungeon_graph", "navmesh", "entrance_design"],
  mazes: ["maze_topology", "landmark_nav", "terrain_blend"],
  "interior-content": ["dungeon_population", "loot_tables", "patrol_routes"],
  atmosphere: ["lighting", "water_vfx", "cutscene_triggers", "time_of_day"],
  music: ["pop_trap_production", "vocal_chain", "instrumental_ai", "streaming_mix"],
};

const TAB_PREFERRED_SOURCES: Record<string, string> = {
  terrain: "Unity docs, GDC talks, procedural generation blogs, level design case studies",
  "surface-content": "Unity docs, game UX articles, quest design GDC, indie postmortems",
  caves: "procedural generation papers, Unity cave tutorials, dungeon design GDC",
  mazes: "maze generation algorithms, level design blogs, open world navigation",
  "interior-content": "dungeon design, loot economy, Unity placement patterns",
  atmosphere: "Unity lighting docs, VFX artist blogs, cinematic trigger design",
  music: "music production blogs, mixing/mastering guides, trap/pop tutorials, game audio GDC",
};

const reqPath = process.argv[2];
if (!reqPath) {
  console.log(JSON.stringify({ error: "usage: planner-research.ts <request.json>" }));
  process.exit(1);
}

const req = JSON.parse(readFileSync(reqPath, "utf8")) as ResearchRequest;
const apiKey = process.env.CURSOR_API_KEY?.trim();
if (!apiKey) {
  console.log(JSON.stringify({ error: "CURSOR_API_KEY missing in cave-grader/.env" }));
  process.exit(1);
}

const hubRoot = req.hubRoot?.trim() || process.env.HUB_ROOT?.trim() || process.cwd();
const modelId = process.env.CAVE_CURSOR_MODEL?.trim() || "composer-2.5";
const tabId = (req.tabId || "terrain").trim();
const brief = req.brief ?? {};
const queries = (req.queries?.length ? req.queries : brief.researchQueries) ?? [
  "procedural terrain game design playable demo 2025 2026",
  "unity open world tile grid best practices",
];

const goals = (brief.userGoals ?? []).map((g) => `- ${g}`).join("\n");
const queryBlock = queries.slice(0, 5).map((q, i) => `${i + 1}. ${q}`).join("\n");
const agentIntro = TAB_AGENT_INTRO[tabId] ?? TAB_AGENT_INTRO.terrain;
const categories = (TAB_CATEGORIES[tabId] ?? TAB_CATEGORIES.terrain).join(", ");
const preferredSources = TAB_PREFERRED_SOURCES[tabId] ?? TAB_PREFERRED_SOURCES.terrain;
const researchFocus =
  req.researchFocus?.trim() ||
  `2025–2026 best practices for ${tabId.replace(/-/g, " ")} in Unity game production`;

const prompt = `You are the ${agentIntro}

**Task:** Use your **web search** tool to research each query below. Find real, reputable sources published **2024–2026** when possible (docs, tutorials, GDC talks, engine guides, proven game-design / music-production articles). Do NOT invent URLs.

## Research focus
${researchFocus}

## Preferred source types
${preferredSources}

## Build context
Title: ${brief.title ?? "Build session"}
Summary: ${brief.summary ?? ""}
Tab: ${tabId}
${goals ? `Goals:\n${goals}` : ""}

## Research queries (run web search for each)
${queryBlock}

## Output
Return **valid JSON only** (no markdown fence) with 8–15 items total across all queries:
{
  "items": [
    {
      "title": "source title",
      "url": "https://real-url",
      "category": "${categories.split(", ")[0]}",
      "summary": "2-3 sentences: actionable insight for this ${tabId} wizard tab",
      "sourceType": "visual_ref",
      "topics": "comma-separated tags",
      "year": 2026
    }
  ]
}

Rules:
- Every item must come from an actual web search result you found this run.
- Use categories from: ${categories}
- Prefer sources from 2025–2026; include year field accurately.
- Skip paywalled or broken links.
- Stay strictly within **${tabId}** domain — do not mix terrain, content, caves, music, or video topics.`;

function parseJson(raw: string): { items: ResearchItem[] } {
  let text = raw.trim();
  if (text.startsWith("```")) {
    text = text.replace(/^```(?:json)?\s*/i, "").replace(/\s*```$/, "");
  }
  const match = text.match(/\{[\s\S]*\}/);
  if (!match) throw new Error("Research agent returned no JSON");
  const data = JSON.parse(match[0]) as { items?: ResearchItem[] };
  return { items: data.items ?? [] };
}

function stampItems(items: ResearchItem[]): ResearchItem[] {
  const ts = Math.floor(Date.now() / 1000);
  const defaultCategory = (TAB_CATEGORIES[tabId] ?? TAB_CATEGORIES.terrain)[0];
  return items.map((item, i) => ({
    id: `planner-cursor-${tabId}-${ts}-${i}`,
    title: String(item.title ?? "").slice(0, 200),
    url: String(item.url ?? "").slice(0, 500),
    category: item.category ?? defaultCategory,
    summary: String(item.summary ?? "").slice(0, 500),
    sourceType: item.sourceType ?? "visual_ref",
    topics: String(item.topics ?? `planner_session,${tabId},cursor_research`).slice(0, 200),
    year: Number(item.year) || new Date().getFullYear(),
    approved: false,
  }));
}

try {
  const agent = await Agent.create({
    apiKey,
    model: { id: modelId },
    local: {
      cwd: hubRoot,
      // Load Cursor user/project settings so web search + MCP tools match the IDE.
      settingSources: ["all"],
    },
  });

  try {
    const run = await agent.send(prompt);
    if (process.env.PLANNER_RESEARCH_STREAM === "1" && run.supports("stream")) {
      for await (const event of run.stream()) {
        if (event.type === "tool_call" && event.status === "completed") {
          process.stderr.write(`[research] tool ${event.name}\n`);
        }
      }
    }
    const result = await run.wait();

    if (result.status === "error" || result.status === "cancelled") {
      console.log(JSON.stringify({ error: result.error ?? `Cursor research ${result.status}` }));
      process.exit(1);
    }

    const text =
      typeof result.result === "string"
        ? result.result
        : result.messages?.map((m) => ("text" in m ? m.text : "")).join("\n").trim() ?? "";

    const parsed = parseJson(text);
    const items = stampItems(parsed.items).filter((i) => i.title && i.url.startsWith("http"));

    if (!items.length) {
      console.log(JSON.stringify({ error: "Cursor research returned no usable sources" }));
      process.exit(1);
    }

    console.log(
      JSON.stringify({ items, queries: queries.slice(0, 5), provider: "cursor", tabId, researchFocus })
    );
  } finally {
    await agent[Symbol.asyncDispose]();
  }
} catch (err) {
  console.log(JSON.stringify({ error: err instanceof Error ? err.message : String(err) }));
  process.exit(1);
}
