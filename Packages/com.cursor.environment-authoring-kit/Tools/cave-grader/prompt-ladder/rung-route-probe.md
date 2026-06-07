## Rung: route_probe (automated cave bot)

**Goal:** Fix only what the route probe reports — do not rebuild the whole cave unless `path` or `ground_placement` failed.

### Read first

- `Assets/EnvironmentKit/Generated/CaveBuildRouteProbe.json` — `issues[].code`, `suggestedStageId`, `pathIndex`
- `Assets/EnvironmentKit/Generated/CaveLiveFixRequest.json` (play-mode bot / live triggers)

### Code map (targeted fixes)

| Issue code | Fix in kit |
|------------|------------|
| `invisible_solid`, `invisible_near_route`, `pit_blocked` | `CaveInvisibleColliderUtility`, `CaveAdventureBlockBuilder.PlaceBlock`, playability step 10 |
| `floor_missing`, `pit_no_recovery` | `CaveFloorSafetyUtility`, `CaveEnclosureShellBuilder`, `CavePitFallRecovery` |
| `ceiling_open`, `ceiling_low` | `CaveMazeLayout.CeilingClearanceAboveFloor`, `CaveEnclosureShellBuilder` ceiling mesh |
| `pit_missing` | `CaveAdventureFeaturesBuilder.BuildJumpGaps` |
| `spawn_unreachable` | `CaveSpawnAlignmentUtility`, spawn pad |

### Menu (Unity)

- **Diagnostics → Run Cave Route Probe (Bot)** — editor physics walk, no Play Mode
- **Diagnostics → Run Probe + Request Cursor Fix** — writes JSON + invokes agent when API key set
- **Play Mode → Run Cave Playtest Bot** — walks player along knots + tests pits

### Do not

- Re-run full `Build Complete Cave` for a single `geometry_integrity` cell unless probe shows entire path broken.
- Re-add `CaveBlock_Shell` colliders or per-cell ceiling slabs.
