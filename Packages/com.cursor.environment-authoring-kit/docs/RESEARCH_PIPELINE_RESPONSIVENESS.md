# Research — pipeline editor responsiveness

**Category:** `pipeline_editor_responsiveness`  
**Catalog:** `Tools/cave-grader/pipeline-responsiveness-papers.ts` (25 entries)  
**Plan:** [PLAN_PIPELINE_RESPONSIVENESS.md](PLAN_PIPELINE_RESPONSIVENESS.md)

**Last updated:** 2026-05-30

---

## Why this batch exists

FullWorld builds were freezing at **neighbor seam stitch 8/8** while the **Cave Pipeline** window showed **stale elapsed time** (file not updated during main-thread stalls). This research batch supports pacing terrain I/O, `SetNeighbors`, and live status publishing.

---

## Key takeaways

| Topic | Practice for Environment Kit |
|-------|------------------------------|
| Terrain seams | `GetHeights`/`SetHeights` on edge bands only; pace across tiles |
| Connectivity | `Terrain.SetNeighbors` per tile, one frame each; then `SetConnectivityDirty` |
| Flush | Batch `Terrain.Flush` after connectivity, not every tile during stitch |
| Editor UI | Hub reads in-memory status + activity feed; must not rely on file-only during freezes |
| Status file | Write to `Library/EnvironmentKit/` every pulse; Assets mirror every 8s |
| Long builds | `CaveBuildActionPacing.ScheduleHeavyChain` + `TouchQueueActivity` |
| CC0 finalize (~70% grid) | See [RESEARCH_FULLWORLD_CC0_FINALIZE.md](RESEARCH_FULLWORLD_CC0_FINALIZE.md) — scoped `SaveAssetIfDirty`, tiered MemoryGuard, no `SaveAssets()` |

---

## Regenerate cache

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
npx tsx export-research-catalog.ts
npx tsx research-cache-sync.ts --no-fetch-images
```
