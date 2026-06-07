# Pre-build rung: scene_ground

**Goal:** Active scene has a valid **Ground** anchor before cave generation.

## Fix

- [ ] Tag walkable floor collider/mesh as `Ground`, or assign ground in Environment Kit settings.
- [ ] Verify `SceneGroundResolver` bounds are non-zero (not default empty bounds).
- [ ] Kit C# only if resolver logic is wrong — do not build cave geometry yet.

Read `CaveBuildPreBuildLadderReport.json` issues for this stage.
