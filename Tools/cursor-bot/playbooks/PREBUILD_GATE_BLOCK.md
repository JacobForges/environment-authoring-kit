# PREBUILD_GATE_BLOCK

1. Hub shows pre-build gate blocked — fix compile_gate first if CS errors present.
2. When planner has `preBuildReloop: false`, do not force FullWorld reloop; fix layout audit.
3. Wait for compile wait / diagnostics export to finish before starting paced build.
4. One rung fix only — no Full AAA Rebuild unless human explicitly asked.

Tag: `// [bot:rung:prebuild_gate:DATE]`
