# COMPILE_GATE

1. Read `CaveBuildCompileDiagnostics.json` — fix only `verifiedOnDisk: true` CS errors.
2. Run **Environment Kit → Export Compile Diagnostics For Agent** (or batchmode compile gate).
3. Do not touch terrain/cave logic until compile is clean.
4. Session harness exit **4** means compile still failing — retry after Unity refresh.

Tag: `// [bot:rung:compile_gate:DATE]`
