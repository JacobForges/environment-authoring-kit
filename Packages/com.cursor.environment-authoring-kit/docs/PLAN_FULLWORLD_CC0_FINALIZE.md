# Plan — FullWorld CC0 finalize (grid 70% → cave continuation)

**Goal:** CC0 import after extended grid weld completes **without editor beachball** on 16GB MacBook Air; Hub sub-action stays live.

**Research:** [RESEARCH_FULLWORLD_CC0_FINALIZE.md](RESEARCH_FULLWORLD_CC0_FINALIZE.md) (`fullworld_cc0_finalize`, 20 URLs)

---

## Pipeline position

```
Extended flat grid + terraform (~289 tiles)  ~70% grid
  → grid weld / snap
  → Cc0ContentImportPipeline.QueueEnsureAll
  → continue surface / cave queue
```

---

## Root causes (2026-06-07 audit)

| # | Symptom | Cause | Fix |
|---|---------|-------|-----|
| 1 | Freeze at `CC0 save assets` | `AssetDatabase.SaveAssets()` all dirty assets | Scoped `SaveAssetIfDirty` on SO only |
| 2 | Freeze at `save assets 4/4` | MemoryGuard full unload every 6 queue steps during low pressure | Tiered release — GC only below 72% pressure |
| 3 | RAM spike 21→25% | Monolithic finalize + all enemy Resources | Paced slices; titan-only Resources at weld |
| 4 | `Full AAA — Full AAA — …` | Duplicate prefix in Hub | Strip operation prefix from queue detail |

---

## Queue chain (target)

| Step | Sub-action | Weight |
|------|------------|--------|
| CC0 prep | fix import settings | Light |
| CC0 characters | humanoid prefabs | Light |
| CC0 items | 1 mesh / step | Micro |
| Item registry | `item-prefab-registry.asset` dirty | Light |
| Catalog | 12 manifest entries / step | Light |
| Catalog commit | reload editor catalog | Light |
| Titan Resources | 1 slot or loot / step, StartAssetEditing | Light |
| Save registry | `SaveAssetIfDirty` (small) | Light |
| Finalize | in-memory catalog commit + registry save | Heavy + delayCall |
| ~~Save catalog~~ | **Deferred** to grid pipeline finish | — |

**Removed:** mid-grid `SaveCatalogAsset` on Resources path (root cause of beachball at ~70%).

---

## Verification checklist

- [ ] Fresh FullWorld build after recompile (do not resume frozen session)
- [ ] Sub-action passes `save registry` → `save catalog` → `CC0 import complete`
- [ ] No `[MemoryGuard] Working-set release` with full unload during save catalog (unless pressure ≥72%)
- [ ] Step counter continues past ~1,677 toward cave phases
- [ ] Record video + note success in portfolio checklist

---

## Optional next

- Pace `FixAllItemModelImportSettings` if prep still spikes
- `SaveAssetIfDirty` per catalog slice if 81+ entries grow
- Export timing JSON per sub-step for demo portfolio metadata
