# Pre-build rung: research_manifest

**Goal:** `CaveBuildResearch.json` exists on disk (full prestige catalog).

## Fix

- [ ] Run pre-build export in Unity (blocked build writes it) or `npx tsx export-research-catalog.ts`.
- [ ] Ensure `CaveBuildResearchExporter` seed path is valid.
- [ ] Do not skip research/plan phases in workflow — manifest supports them.
