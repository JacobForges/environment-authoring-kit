# World recipes (data-driven, not code)

Versioned JSON recipes describe a full **Florida karst surface + lava-tube cave** build. Far Cry–style: change the recipe, not C#, to iterate biomes and scopes.

## Files

| Recipe | Purpose |
|--------|---------|
| `aaa-full-cave-production.json` | **Default for Build Complete Cave** — FullWorld terrain-first + 63-step cave queue, gates, meat loop until ship |
| `showcase-florida-karst-xr.json` | Demo reel alias — same pipeline; pinned seed **7721001** |
| `surface-only-iteration.json` | **Faster** — terrain/trails/NavMesh only; no cave, no pre-build gate, no autonomous polish |

**Local generation time:** recipes are passive JSON. Nothing runs until you use a menu item or CI schedule. Incremental ladder stays on for re-runs.

## Apply in editor

- **Window → Environment Kit → Run Showcase Build (Florida Karst XR)** — full pipeline
- **Window → Environment Kit → Cave Build → Run Surface Iteration Recipe (fast)** — surface only
- **Window → Environment Kit → Cave Build → Diagnostics → Apply Showcase Recipe Settings Only**

## Headless / farm

```bash
cd /path/to/your/Unity/project
Unity -batchmode -projectPath . \
  -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunShowcaseHeadless \
  -quit -logFile Logs/showcase-headless.log
```

Surface-only headless (optional; not in nightly CI):

```bash
Unity -batchmode -projectPath . \
  -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunSurfaceIterationHeadless \
  -quit
```

## Nightly CI (GitHub Actions)

Workflow: `.github/workflows/environment-kit-showcase-nightly.yml`

- Runs on **schedule + manual dispatch only** — not on push or pull_request.
- Set repo variable `UNITY_PATH` to your Unity binary on a self-hosted runner, or the job no-ops (exit 0).
- Does not affect editor build time on your machine.

## Schema (`schemaVersion: 1`)

See `CaveBuildRecipeDefinition` in the package. Key fields: `seed`, `surfaceScope`, `surfaceIncludeTrails`, `useIncrementalLadder`, `allowedResearchEntryIds`.

## Product boundary

`Packages/com.cursor.environment-authoring-kit/docs/PRODUCT_BOUNDARY.md`
