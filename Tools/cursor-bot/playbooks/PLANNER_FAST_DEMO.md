# PLANNER_FAST_DEMO

1. Read `CaveBuildActiveSessionConfig.json` — tile count, flags are scope truth.
2. When `agentInvokes: false`: skip terrain meat tsx, crater repair, blocking helper exports.
3. When `use3DCaveSystem: false`: skip cave queue layout gates; surface-only demo path.
4. Bind step budget from planner (`CaveBuildPlannedStepBudget.ConfigureForRequest`), not FullWorld 289.

Tag: `// [bot:rung:planner_fast_demo:DATE]`
