# Natural Underground Cave (VITURE XR PRO)

Playable **natural lava-tube / cavern** under your scene ground plane.  
**No buildings, doors, windows, or town geometry** — only geology, organic props, water, and one surface entrance.

## Asset sources

| Source | Role |
|--------|------|
| `Assets/BillemotdonggulLavaTubePack/Prefabs/` | Primary tunnels: floors, walls, ceilings, rockfalls, cupolas |
| `Assets/BillemotdonggulLavaTubePack/Material/` | URP materials (auto-fixed on build) |
| **Any folder under `Assets/`** | Extra natural props (mushrooms, crystals, rocks, vines) via automatic scan |
| `Assets/IgniteCoders/Simple Water Shader/` | Underground pool (`CaveUndergroundWater_URP.mat`) — animated URP water |
| `Assets/SUIMONO - WATER SYSTEM 2/PREFABS/fx_object.prefab` | Optional waterfall particles only (scripts stripped) |
| **Excluded** | SUIMONO surface/module scripts, UI, buildings, generic Unity water |

Each placed piece gets a **`CavePrefabSource`** component with the full `asset_reference: Assets/...` path.

## One-click build (15 stages)

1. Open a scene with a **Ground**-tagged walkable surface (e.g. `Grid` in MainScene).
2. **Window → Environment Kit → Build Complete Cave Level (Active Scene)**  
   Builds a **continuous organic spline mesh tube** (one enclosed surface — no box rooms or layered floors), descending only below Ground, with **Ignite** underground water.
3. Save scene, then **Window → Rendering → Lighting → Generate Lighting** for baked GI.
4. **Window → Environment Kit → Setup MainScene Cave Portal** (if needed)

### Pipeline stages

| # | Stage |
|---|--------|
| 1 | Validate catalog & ground |
| 2 | Random layout seed |
| 3 | Natural entrance + organic mesh tube + descending water branch |
| 4 | Organic mesh enclosure check (skips legacy ring QA) |
| 5 | Natural props along path |
| 6 | Occlusion shell |
| 7 | Cave rock materials + Ignite underground water |
| 8 | Cave lighting |
| 9 | FX |
| 10 | Colliders + LOD (XR) |
| 11 | NavMesh (floors) |
| 12 | Spawn + portal + warp |
| 13 | Playability (spawn pad, cleanup) |
| 14 | Enclosure validation |
| 15 | Final report |

## Scene hierarchy

```
Grid (Ground)
└── LavaTubeCaveSystem
    ├── Entrance              tags: CaveEntrance — natural mouth + spawn
    ├── SplineMesh
    │   └── MainCaveTube      single MeshCollider + URP rock material
    ├── Water
    │   ├── UndergroundRiver_Pool   tag: CaveWater — Ignite shader quad
    │   ├── HiddenWaterfall_Fx      particle FX (no SUIMONO scripts)
    │   └── WaterBranchTube         descending branch mesh
    ├── Details               mushrooms / crystals / minable rocks
    ├── OcclusionShell        rock caps (blocks sky leaks)
    ├── CaveAtmosphereZone    dark camera background underground
    └── Lighting              fill + chamber probes
```

## Portal & play

| Control | Action |
|---------|--------|
| **F** | Surface portal → underground (`CaveEntrance_SpawnPoint` via `PortalFive` / `MainScene_CavePortal`) |
| **E** | Attack / mine (with pickaxe) |
| **Shift+R** | Reset to **surface** spawn (`PlayerSpawnPoint` / `PlayerSpawn` tag — not the cave) |

After teleport: short **warp fade** (not a flying cinematic camera). Underground camera uses solid dark background (surface sky unchanged).

## Tags for scripting

| Tag | Use |
|-----|-----|
| `CaveEntrance` | Entrance marker + spawn point |
| `CaveWater` | Underground pool / river basin |
| `HiddenWaterfall` | Secret waterfall chamber |
| `Minable` | Pickaxe-destructible rocks (`MinableRock`) |
| `CavePortal` | Surface portal trigger |

## Minable / pickaxe

1. Add **PickaxeTool** to your pickaxe prefab.
2. Targets: **MinableRock** + tag **Minable** (rockfalls placed in entrance, tunnels, chambers).
3. Mining removes the mesh and fires `onMined` (optional VFX).

## XR budgets (VITURE)

Profile: `Assets/EnvironmentKit/Presets/VitureXRPro.asset`

- **&lt; 50k triangles** per mesh chunk (LOD on meshes &gt; 8k tris)
- **&lt; 100 draw calls** target (reported after build)
- **NavMesh**: floors only, agent height **2 m**, radius **0.35 m**
- **2 m** `_NavClearance` trigger per tunnel module

## Material fixes

If pink/magenta rocks appear:

- **Window → Environment Kit → Fix Lava Tube Materials (URP)**
- **Window → Environment Kit → Fix Cave Pink Materials (Active Scene)**

## Batch (CI)

```bash
Unity -batchmode -projectPath /path/to/Hub \
  -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.GenerateLavaTubeCave -quit
```
