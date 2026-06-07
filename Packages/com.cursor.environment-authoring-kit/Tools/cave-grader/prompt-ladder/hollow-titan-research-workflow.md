# Hollow Titan research workflow (agents)

## Order of operations

1. Open **`Assets/EnvironmentKit/Generated/HollowTitanResearchExecutionBrief.json`** — mandatory read order + cache entry ids.
2. Open **`Assets/EnvironmentKit/Generated/HollowTitanResearchExecutionBrief.md`** — human-readable brief for active phase.
3. Open **master concept** — `ResearchCache/images/hollow-titan-concepts/master/concept.png`.
4. Open **active phase concept** — `ResearchCache/images/hollow-titan-concepts/{NN}/concept.png`.
5. Open **`Assets/EnvironmentKit/ResearchCache/categories/hollow_titan/index.json`** — dedicated landmark research.
6. Open **`Assets/EnvironmentKit/ResearchCache/categories/hollow_titan_do_not/index.json`** — anti-patterns.
7. Read **`ResearchCache/entries/{id}/content.md`** — at least 2 hollow_titan entries + 1 hollow_titan_do_not entry.
8. Open **`HollowTitanActivePhasePrompt.md`** + **`HollowTitanResearchActionPlan.json`** for the active meat phase.
9. Web search **only** on cache miss (max queries in action plan).

## Local files (no API)

- **`ResearchCache/entries/{id}/content.md`** — serialized summaries per source.
- **`ResearchCache/categories/hollow_titan/index.json`** — 25+ landmark entries.
- **`ResearchCache/categories/hollow_titan_do_not/index.json`** — DO NOT guardrails.
- **`docs/RESEARCH_HOLLOW_TITAN.md`** — landmark rules + phase table.

## Phase-specific priorities

| Phase | Read first |
|-------|------------|
| 0–1 | Site pick + SampleHeight grounding entries |
| 2, 6, 7 | LPMagicalForest + Substance bark + SideFX scatter |
| 3–5 | Interior void, floors, spiral stairs |
| 8–11 | Landmark spawn manifest — never world scatter |
| 9 | URP fog + light probes + NVIDIA RTX GI mood refs |

Always include `hollow_titan` + `hollow_titan_do_not` regardless of phase.

## Generated artifacts (per meat phase)

| File | Purpose |
|------|---------|
| `HollowTitanPhaseResearchGate.json` | Gate pass/fail + concept paths |
| `HollowTitanResearchAgentPrompt.md` | Consolidated research + cache block |
| `HollowTitanActivePhasePrompt.md` | Single active task |
| `HollowTitanResearchActionPlan.json` | URLs + do-not + plan steps |
| `HollowTitanPhasePromptManifest.json` | All 12 phase prompts |
| `HollowTitanLandmarkSpawnManifest.json` | Spawn QA (phases 8–11) |

## Maintainer refresh

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
HUB_ROOT=/path/to/Hub npm run sync-hollow-titan-research
```

This runs: research cache sync → hollow titan brief export → all `generate-hollow-titan-*` scripts.

Unity Hub: **Export Hollow Titan research prompts** or **Sync Hollow Titan research** (Environment Kit Hub).

## Preventing mistakes

| Symptom | Likely cause | Fix |
|--------|----------------|-----|
| White cylinder trunk | CC0 L01 used as primary | Verify LPMagicalForest in BiomePropCatalog; read hollow_titan_do_not |
| Enemies across whole map | World scatter table mixed in | Use landmark catalog only; phase 8 do-not rules |
| Floating trunk | Skipped SampleHeight re-snap | Re-run phase 1 after terrain sculpt |
| No research in prompt | Cache not synced | `npm run sync-hollow-titan-research` |
| Agent invents URLs | Skipped execution brief | Open brief JSON first; cite disk entry ids |

## Cross-refs

- FullWorld guardrails: `fullworld_do_not` (surface landmark required, not in cave)
- Surface props: `surface_props` for LP prefab scatter patterns
- Pipeline pacing: `pipeline_editor_responsiveness` — meat phases are paced in Unity
