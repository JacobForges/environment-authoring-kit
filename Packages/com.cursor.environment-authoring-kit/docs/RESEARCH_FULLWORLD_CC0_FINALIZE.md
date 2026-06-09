# Research — FullWorld CC0 finalize (grid ~70% stall)

**Category:** `fullworld_cc0_finalize`  
**Catalog:** `Tools/cave-grader/fullworld-cc0-finalize-papers.ts` (20 entries)  
**Plan:** [PLAN_FULLWORLD_CC0_FINALIZE.md](PLAN_FULLWORLD_CC0_FINALIZE.md)  
**Parent:** [RESEARCH_PIPELINE_RESPONSIVENESS.md](RESEARCH_PIPELINE_RESPONSIVENESS.md)

**Last updated:** 2026-06-07

---

## Problem statement

FullWorld sequential build (**~289 tiles, 17×17 Chebyshev 8**) reaches **grid ~70%** (extended flat grid + terraform + weld), then runs **Full AAA — CC0 import** (catalog + Hollow Titan Resources). Observed failures:

| Symptom | Step | Likely cause (verified) |
|---------|------|-------------------------|
| RAM step 22% → 25% | CC0 finalize start | Catalog slices + titan prefab copies (paced — OK) |
| Hub stuck `CC0 save assets 4/4` | Last save micro-step | **Periodic MemoryGuard `UnloadUnusedAssets`** on same frame as save — not the 4 paths themselves |
| Duplicate label `Full AAA — Full AAA — …` | Hub sub-action | Queue label already contains `Full AAA —`; publisher prepends operation again |
| Earlier spike at `save assets` (monolithic) | Pre-fix | **`AssetDatabase.SaveAssets()`** flushed **all** dirty project assets (terrains + CC0) |

Research alone does not fix execution — see [RESEARCH_FULLWORLD_DO_NOT.md](RESEARCH_FULLWORLD_DO_NOT.md). C# must implement scoped persist + tiered memory release.

---

## Research database (curated URLs)

| ID | Source | URL | Kit action |
|----|--------|-----|------------|
| cc0-01 | Unity | [StartAssetEditing](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.StartAssetEditing.html) | Batch `CopyAsset` deps per titan queue step |
| cc0-02 | Unity | [SaveAssetIfDirty](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.SaveAssetIfDirty.html) | Registry + catalog only — **never** `SaveAssets()` |
| cc0-03 | Unity | [AssetDatabase](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.html) | Scoped import; status to `Library/` |
| cc0-04 | Unity | [SaveAsPrefabAsset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PrefabUtility.SaveAsPrefabAsset.html) | Resources prefabs already on disk — skip redundant save pass |
| cc0-05 | Unity | [UnloadUnusedAssets](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Resources.UnloadUnusedAssets.html) | **Only when queue idle or pressure ≥72%** |
| cc0-06 | Unity | [UnloadUnusedAssetsImmediate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorUtility.UnloadUnusedAssetsImmediate.html) | Memory pressure pause only |
| cc0-07 | Unity | [delayCall](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorApplication-delayCall.html) | One persist op per queue frame |
| cc0-08 | Unity | [EditorApplication.update](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorApplication-update.html) | Hub heartbeat during save chain |
| cc0-09 | Unity Discussions | [Terrain freeze thread](https://discussions.unity.com/t/editor-freezes-when-modifying-terrain/) | Split main-thread work (same rule for assets) |
| cc0-10 | Guerrilla | [Streaming the World](https://www.guerrilla-games.com/read/Streaming-the-World-of-Horizon-Zero-Dawn) | Incremental commit — catalog slices |
| cc0-21 | Environment Kit | LavaTube deferred flush pattern | `LavaTubeMaterialUpgrader.FlushDeferredAssetChanges` | Defer disk flush until safe queue boundary |
| cc0-22 | Environment Kit | Deferred catalog persist at grid finish | `WorldItemCatalogBuilder.QueuePacedSaveIfDirty` | Never SaveAssetIfDirty on Resources catalog at grid ~70% |

---

## Implemented safeguards (2026-06-07)

1. **Paced CC0 chain** — `Cc0ContentImportPipeline`: prep → characters → 1 item/step → registry → catalog slices (12) → titan Resources (1/step) → save registry → save catalog → deferred refresh.
2. **No project-wide SaveAssets** at finalize — `SaveAssetIfDirty` on registry + catalog ScriptableObjects only; prefabs use `SaveAsPrefabAsset` at copy time.
3. **Tiered MemoryGuard** — routine steps: terrain heightmap release + `GC.Collect(Optimized)`; full `UnloadUnusedAssets` only when pressure ≥72% **and** queue idle (or memory pause).
4. **StartAssetEditing** per titan Resources step — one import batch per queue tick.
5. **Hub sub-action** — strip duplicate `Full AAA —` from queue-derived detail.
6. **Deferred catalog persist** — in-memory catalog during grid; `QueuePacedSaveIfDirty` when `MarkFullWorldGridPipelineFinished` (AAA deferred flush).
7. **Cc0ContentImportAssetSession** — single `StartAssetEditing` nest during CC0 (mirrors `LavaTubeCaveBuilder.BeginBuildAssetEditing`).
8. **MemoryGuard off during CC0** — `SetCc0ImportPhaseActive(true)` suppresses periodic unload during import chain.

---

## Regenerate cache

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
npx tsx export-research-catalog.ts
npx tsx research-cache-sync.ts --no-fetch-images
```

Filter: `Assets/EnvironmentKit/ResearchCache/categories/fullworld_cc0_finalize/`

---

## Verification (FullWorld build mode)

1. Hub **Sequential FullWorld terrain (~289 tiles)** + **Build Complete Cave**.
2. At **grid ~70%**, sub-action walks: `CC0 import — items N/81` → `catalog` → `titan resources 1/14` → `save registry` → `save catalog` → `complete`.
3. **No** multi-minute silence on `save assets` (step removed — prefabs already persisted).
4. Hub elapsed + sub-action update within ~1s between micro-steps.
5. RAM graph: small steps; no vertical cliff from `UnloadUnusedAssets` during save chain.
