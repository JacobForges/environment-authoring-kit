# Environment Kit — build recipes

JSON recipes under this folder configure **scope**, **gates**, **meat loop**, and **research** behavior for editor builds.

**Public GitHub:** these files **are** committed. Generated output under `../Generated/` is **not**.

| Recipe | Use |
|--------|-----|
| `aaa-full-cave-production.json` | **Default for Build Complete Cave** — FullWorld terrain-first + **120-step** queued cave pipeline, gates, meat loop toward Ship tier |
| `showcase-florida-karst-xr.json` | Showcase / reel profile (same ladder family; tune for your demo scene locally) |
| `surface-only-iteration.json` | Surface / terrain iteration without full cave geo |

Recipe selection is applied by `CaveBuildAaaProductionBootstrap` and Hub build actions — see [package docs](../../../Packages/com.cursor.environment-authoring-kit/docs/PRODUCT_BOUNDARY.md).

**Requires locally:** a Unity scene with **Ground** + **`PortalFive`**, your licensed prefabs, and optional `ResearchCache/` after `npm run sync-research-pull`.
