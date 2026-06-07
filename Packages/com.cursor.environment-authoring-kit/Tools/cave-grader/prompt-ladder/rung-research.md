# Ladder rung: research (mandatory — first after cave generation)

**Do not edit kit C# in this pass.** Web research + written plan only.

## Checklist

- [ ] **Use all on-disk research first:** `ResearchCache/index.json`, `entries/*/content.md`, `images/*/ref-*.png`, `images/fl-*-hillshade/`.
- [ ] Read `CaveBuildResearchCache.json` → `floridaTerrain` paths (mandatory pull already ran in Unity).
- [ ] Read `CaveBuildResearch.json` (papers + Florida aquifer refs).
- [ ] HTTP-fetch papers **only** when missing from `ResearchCache/entries/`.
- [ ] Read `CaveBuildQualityReport.json` + failing stages.
- [ ] Read `CaveBuildAgentMemory.json` — avoid listed fingerprints.
- [ ] Write a **numbered plan table** (minimum 6 rows). **Each row must cite a disk path** (`ResearchCache/entries/.../content.md` or hillshade PNG):

| Step | JSON metric / stage | Research source (cache path + lab/year) | Kit file(s) | Expected grade change |
|------|---------------------|----------------------------------------|-------------|------------------------|

- [ ] Plan must drive **execution** — later rungs implement these rows using the same cache files (not generic advice).

- [ ] End with: "Next Unity phase: **compile_gate** — fix all CS errors before scene fixes."

Output the plan in your assistant response. Unity will run **compile_gate** automatically after you finish.

**Last line (required):** `[CaveCursor:phase-complete] workflow=pre_build rung=research reason=done`
