export const WIZARD_TABS = [
  { id: "terrain", label: "Terrain", hash: "terrain", css: "terrain" },
  { id: "surface-content", label: "Surface", hash: "surface-content", css: "surface" },
  { id: "caves", label: "Caves", hash: "caves", css: "caves" },
  { id: "mazes", label: "Mazes", hash: "mazes", css: "mazes" },
  { id: "interior-content", label: "Interior", hash: "interior-content", css: "interior" },
  { id: "atmosphere", label: "Atmosphere", hash: "atmosphere", css: "atmosphere" },
  { id: "video", label: "Video", hash: "video", css: "video" },
  { id: "music", label: "Music", hash: "music", css: "music" },
  { id: "onnx-brains", label: "ONNX Brains", hash: "onnx-brains", css: "onnx" },
];

export const TAB_CONFIGS = {
  "surface-content": {
    title: "Surface Content Planner",
    kicker: "Environment Kit · Surface content",
    phaseLabels: {
      idle: "Not started",
      qna: "Q&A",
      awaiting_approval: "Brief review",
      finalized: "Brief approved",
      cancelled: "Cancelled",
    },
    presets: [
      { id: "town_hub", label: "Town hub", hint: "Quest giver, ambient NPCs, trainer, gate blocker." },
      { id: "arena_trainers", label: "Arena trainers", hint: "Trainer NPC, shop at 70% training grade." },
      { id: "full_mainscene", label: "Full MainScene", hint: "Complete surface content pass." },
    ],
    checklistMeta: {
      scene_scope: { icon: "◎" },
      zones: { icon: "▦" },
      quest_npcs: { icon: "●" },
      trainer_npcs: { icon: "◆" },
      ambient_npcs: { icon: "○" },
      blocker_gates: { icon: "⛨" },
      enemy_patrols: { icon: "▲" },
      props_layout: { icon: "▪" },
      textures: { icon: "▤" },
      dialog_hybrid: { icon: "◈" },
      blockers_training: { icon: "↗" },
      blockers_quest: { icon: "★" },
      placement: { icon: "⌂" },
      playmode_smoke: { icon: "✓" },
    },
    scopeClass: "chat-scope-content",
  },
  caves: {
    title: "Caves & Dungeons Planner",
    kicker: "Environment Kit · Caves",
    phaseLabels: { idle: "Not started", qna: "Q&A", awaiting_approval: "Brief review", finalized: "Approved", cancelled: "Cancelled" },
    presets: [
      { id: "demo_cave", label: "Demo cave", hint: "Single lava-tube route for 9-tile demo." },
      { id: "dungeon_rooms", label: "Room dungeon", hint: "Multi-room dungeon graph." },
    ],
    checklistMeta: {
      cave_scope: { icon: "◎" },
      entrances: { icon: "⌂" },
      route_style: { icon: "〜" },
      room_graph: { icon: "▦" },
      depth_budget: { icon: "↓" },
      navmesh: { icon: "⌁" },
      atmosphere_hook: { icon: "◐" },
      generation_passes: { icon: "⚙" },
    },
    scopeClass: "chat-scope-caves",
  },
  mazes: {
    title: "Labyrinths & Mazes Planner",
    kicker: "Environment Kit · Mazes",
    phaseLabels: { idle: "Not started", qna: "Q&A", awaiting_approval: "Brief review", finalized: "Approved", cancelled: "Cancelled" },
    presets: [{ id: "surface_maze", label: "Surface maze", hint: "Terrain labyrinth with entrance and exit." }],
    checklistMeta: {
      maze_scope: { icon: "◎" },
      grid_topology: { icon: "▦" },
      entrances_exits: { icon: "⌂" },
      dead_ends: { icon: "⊗" },
      landmarks: { icon: "★" },
      surface_blend: { icon: "⛰" },
      nav_intent: { icon: "⌁" },
    },
    scopeClass: "chat-scope-mazes",
  },
  "interior-content": {
    title: "Interior Content Planner",
    kicker: "Environment Kit · Interior",
    phaseLabels: { idle: "Not started", qna: "Q&A", awaiting_approval: "Brief review", finalized: "Approved", cancelled: "Cancelled" },
    presets: [{ id: "cave_population", label: "Cave population", hint: "NPCs, enemies, quest items inside caves." }],
    checklistMeta: {
      interior_scope: { icon: "◎" },
      interior_npcs: { icon: "●" },
      interior_enemies: { icon: "▲" },
      interior_props: { icon: "▪" },
      quest_ties: { icon: "★" },
      quest_items: { icon: "◈" },
      player_gear: { icon: "⚔" },
      agent_gear: { icon: "◆" },
      shops_loot: { icon: "◎" },
      placement: { icon: "⌂" },
    },
    scopeClass: "chat-scope-interior",
  },
  atmosphere: {
    title: "Atmosphere & Cinematics Planner",
    kicker: "Environment Kit · Atmosphere",
    phaseLabels: { idle: "Not started", qna: "Q&A", awaiting_approval: "Brief review", finalized: "Approved", cancelled: "Cancelled" },
    presets: [{ id: "florida_dusk", label: "Florida dusk", hint: "Sun, fog, water, cutscene trigger at cave mouth." }],
    checklistMeta: {
      water: { icon: "≋" },
      sky_time: { icon: "◐" },
      lighting: { icon: "☀" },
      vfx: { icon: "✦" },
      audio_hooks: { icon: "♪" },
      cutscene_triggers: { icon: "▶" },
      quest_story_hooks: { icon: "★" },
    },
    scopeClass: "chat-scope-atmosphere",
  },
};

export function tabFromHash() {
  const h = (window.location.hash || "").replace("#", "").toLowerCase();
  const legacy = { world: "terrain", content: "surface-content" };
  const id = legacy[h] || h;
  return WIZARD_TABS.find((t) => t.id === id || t.hash === id)?.id || "terrain";
}
