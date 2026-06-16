# Pre-ONNX Q&A Ladder — terrain
## scope: Build scope & goals
- Keywords: `terrain_scope, props_scatter, npc_roles, combat_loop, puzzle_interactables, units_m`
- Rubric: Include concrete goals + playable demo intent; mention mountains/water/labyrinth.
- Example GOOD:
  - Playable world gate. Ship a fast playable slice: mountains + water edge + labyrinth traversal. Goal: entry-to-goal loop with NPC social beats and a clear challenge tier (tier 1).
  - Playable world gate. Ship a fast playable slice: mountains + water edge + labyrinth traversal. Goal: entry-to-goal loop with NPC social beats and a clear challenge tier (tier 2).
- Example BAD:
  - Make it good. scope variant 0.
  - Make it good. scope variant 1.

## props: Props & scatter
- Keywords: `terrain_scope, props_scatter, npc_roles, combat_loop, puzzle_interactables, units_m`
- Rubric: Mention prop scatter style + distribution principle (not sparse/only center).
- Example GOOD:
  - Prop scatter is corridor-biased: trail corridor higher density, horizon soft backdrop, no uniform random. Use tile-based scatter with exclusion for spawn/wizard pad. Variant 0.
  - Prop scatter is corridor-biased: trail corridor higher density, horizon soft backdrop, no uniform random. Use tile-based scatter with exclusion for spawn/wizard pad. Variant 1.
- Example BAD:
  - Make it good. props variant 0.
  - Make it good. props variant 1.

## npcs: NPCs & characters
- Keywords: `terrain_scope, props_scatter, npc_roles, combat_loop, puzzle_interactables, units_m`
- Rubric: Name at least 1 NPC and what they do (brief role + placement at high level).
- Example GOOD:
  - Include 2 NPCs: quest-giver and ambient flavor. Quest-giver stands near the central 3x3 play disk; ambient points players toward labyrinth route. Keep dialog short and non-blocking (variant 0).
  - Include 2 NPCs: quest-giver and ambient flavor. Quest-giver stands near the central 3x3 play disk; ambient points players toward labyrinth route. Keep dialog short and non-blocking (variant 1).
- Example BAD:
  - Make it good. npcs variant 0.
  - Make it good. npcs variant 1.

## enemies: Enemies & combat
- Keywords: `terrain_scope, props_scatter, npc_roles, combat_loop, puzzle_interactables, units_m`
- Rubric: Name enemy type(s) + combat loop intent + approximate challenge tier.
- Example GOOD:
  - Enemies + combat included. Use one wave type around labyrinth turns with readable telegraph. Difficulty tier 1 with respawn tuned for demo pacing.
  - Enemies + combat included. Use one wave type around labyrinth turns with readable telegraph. Difficulty tier 2 with respawn tuned for demo pacing.
- Example BAD:
  - Make it good. enemies variant 0.
  - Make it good. enemies variant 1.

## puzzles: Puzzles / interactables
- Keywords: `terrain_scope, props_scatter, npc_roles, combat_loop, puzzle_interactables, units_m`
- Rubric: Name 1–2 interactables or traversal blockers and how player uses them.
- Example GOOD:
  - Puzzles/interactables: one lever/terminal that opens a short traversal gate; one pickup that teaches combat tutorial. Must be reachable within demo loop (variant 0).
  - Puzzles/interactables: one lever/terminal that opens a short traversal gate; one pickup that teaches combat tutorial. Must be reachable within demo loop (variant 1).
- Example BAD:
  - Make it good. puzzles variant 0.
  - Make it good. puzzles variant 1.

## terrain: Mountains / water / labyrinth
- Keywords: `terrain_scope, props_scatter, npc_roles, combat_loop, puzzle_interactables, units_m`
- Rubric: Describe terrain plan elements (mountain/water/labyrinth) with clear scope.
- Example GOOD:
  - Terrain plan: mountain silhouettes with water edge and a labyrinth route integrated into tile grid. Ensure slopes are walkable and avoid cliff-ring artifacts (variant 0).
  - Terrain plan: mountain silhouettes with water edge and a labyrinth route integrated into tile grid. Ensure slopes are walkable and avoid cliff-ring artifacts (variant 1).
- Example BAD:
  - Make it good. terrain variant 0.
  - Make it good. terrain variant 1.

## speed: Speed vs quality tier
- Keywords: `terrain_scope, props_scatter, npc_roles, combat_loop, puzzle_interactables, units_m`
- Rubric: State speed vs quality trade-off explicitly (fast tier vs high quality tier).
- Example GOOD:
  - Speed vs quality: medium tier. Prefer 1-2 passes for acceptable readability; skip expensive high-detail dressing outside critical path. Variant 0.
  - Speed vs quality: medium tier. Prefer 1-2 passes for acceptable readability; skip expensive high-detail dressing outside critical path. Variant 1.
- Example BAD:
  - Make it good. speed variant 0.
  - Make it good. speed variant 1.

## tech_jump: Learn: jump gaps & platform spacing (meters)
- Keywords: `terrain_scope, props_scatter, npc_roles, combat_loop, puzzle_interactables, units_m`
- Rubric: Include jump gap spacing in meters; mention platform spacing or jump rules.
- Example GOOD:
  - Jump gaps: target 2.0m–3.0m gap spacing, platform spacing 1.5m–2.5m so jump arcs are consistent at demo movement speed (variant 0).
  - Jump gaps: target 2.0m–3.0m gap spacing, platform spacing 1.5m–2.5m so jump arcs are consistent at demo movement speed (variant 1).
- Example BAD:
  - Make it good. tech_jump variant 0.
  - Make it good. tech_jump variant 1.

## tech_seams: Learn: tile height offsets & seams
- Keywords: `terrain_scope, props_scatter, npc_roles, combat_loop, puzzle_interactables, units_m`
- Rubric: Include tile height offsets + seam handling strategy.
- Example GOOD:
  - Seams: set tile height offsets so neighboring tiles share a consistent seam band width; eliminate visible step discontinuities using tile-height blending (variant 0).
  - Seams: set tile height offsets so neighboring tiles share a consistent seam band width; eliminate visible step discontinuities using tile-height blending (variant 1).
- Example BAD:
  - Make it good. tech_seams variant 0.
  - Make it good. tech_seams variant 1.

## tech_navmesh: Learn: walkable vs jump-only routes
- Keywords: `terrain_scope, props_scatter, npc_roles, combat_loop, puzzle_interactables, units_m`
- Rubric: Include navmesh intent (walkable vs jump-only routes).
- Example GOOD:
  - Navmesh: ensure walkable routes on navmesh for main path; mark only a few jump-only shortcuts and keep them optional. (variant 0).
  - Navmesh: ensure walkable routes on navmesh for main path; mark only a few jump-only shortcuts and keep them optional. (variant 1).
- Example BAD:
  - Make it good. tech_navmesh variant 0.
  - Make it good. tech_navmesh variant 1.

## tech_spawn: Learn: spawn point & fall respawn
- Keywords: `terrain_scope, props_scatter, npc_roles, combat_loop, puzzle_interactables, units_m`
- Rubric: Include spawn point + fall respawn / reset behavior.
- Example GOOD:
  - Spawn: place spawn at play disk center; respawn on fall uses a lower kill volume radius and a fall-respawn point just outside hazard so demo reattempts are fast (variant 0).
  - Spawn: place spawn at play disk center; respawn on fall uses a lower kill volume radius and a fall-respawn point just outside hazard so demo reattempts are fast (variant 1).
- Example BAD:
  - Make it good. tech_spawn variant 0.
  - Make it good. tech_spawn variant 1.

## tech_seed: Learn: fixed vs random seed
- Keywords: `terrain_scope, props_scatter, npc_roles, combat_loop, puzzle_interactables, units_m`
- Rubric: State fixed vs random seed and why.
- Example GOOD:
  - Seed: fixed seed for reproducibility during authoring, then switch to random only if quality passes are locked (variant 0).
  - Seed: fixed seed for reproducibility during authoring, then switch to random only if quality passes are locked (variant 1).
- Example BAD:
  - Make it good. tech_seed variant 0.
  - Make it good. tech_seed variant 1.

## tech_collision: Learn: triggers, kill volumes, platform colliders
- Keywords: `terrain_scope, props_scatter, npc_roles, combat_loop, puzzle_interactables, units_m`
- Rubric: Include triggers/kill volumes/platform colliders at least by type.
- Example GOOD:
  - Collision: define trigger volumes for tutorial prompt, kill volumes for jump-fail, and platform colliders for bridge edges; ensure no invisible blockers on main route (variant 0).
  - Collision: define trigger volumes for tutorial prompt, kill volumes for jump-fail, and platform colliders for bridge edges; ensure no invisible blockers on main route (variant 1).
- Example BAD:
  - Make it good. tech_collision variant 0.
  - Make it good. tech_collision variant 1.

## tech_perf: Learn: demo perf / prop budget
- Keywords: `terrain_scope, props_scatter, npc_roles, combat_loop, puzzle_interactables, units_m`
- Rubric: Include prop budget or perf target (FPS/triangle-ish) and how you reduce load.
- Example GOOD:
  - Perf budget: keep prop budget under a strict cap; reduce spawn density of high-cost props off-path and preserve FPS. Target demo perf with conservative collision complexity (variant 0).
  - Perf budget: keep prop budget under a strict cap; reduce spawn density of high-cost props off-path and preserve FPS. Target demo perf with conservative collision complexity (variant 1).
- Example BAD:
  - Make it good. tech_perf variant 0.
  - Make it good. tech_perf variant 1.

