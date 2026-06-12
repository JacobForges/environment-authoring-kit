# PLANNER_FAST_DEMO

1. Read `CaveBuildActiveSessionConfig.json` — tile count and flags are scope truth (not FullWorld preset dropdown).
2. Read `CaveBuildPlannerBrief.json` — `layoutPlan.markers`, `technicalSpecs`, `trails` drive Unity authors (non-AI path OK).
3. When `agentInvokes: false`: skip terrain meat tsx, crater repair, blocking helper exports.
4. When `use3DCaveSystem: false`: skip cave queue; surface + planner layout only.
5. When `floatingTiles: true` + `tileCount <= 81`: 13-tile cardinal island demo path.
6. Concept PNG optional — without it, marker plan sculpts terrain (`CaveBuildPlannerConceptGuide` inactive).
7. Bind step budget from planner (`CaveBuildPlannedStepBudget.ConfigureForRequest`), not default 289-tile FullWorld.
8. Full doc: [docs/PLANNER_SESSION.md](../../docs/PLANNER_SESSION.md).

Tag: `// [bot:rung:planner_fast_demo:DATE]`
