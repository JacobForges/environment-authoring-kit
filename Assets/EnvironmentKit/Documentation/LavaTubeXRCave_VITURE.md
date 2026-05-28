# Natural underground cave (XR-oriented)

Playable **natural lava-tube / cavern** workflow under your scene ground plane.  
**No buildings, doors, windows, or town geometry** in the kit’s intent — geology, organic props, water, and one surface entrance.

> **Public repo note:** This documentation describes how the kit is *designed* to work in a full Unity project. **Asset Store meshes, water packs, and scenes are not in git.** You supply licensed prefabs under your local `Assets/` folders.

## Asset sources (your project)

| Source | Role |
|--------|------|
| **Your cave mesh pack** (e.g. lava-tube floor/wall/ceiling prefabs) | Primary tunnel modules — assign paths in kit catalog / scatter settings |
| **Your URP materials** | Rock, mist, emissive lava — or use `Assets/EnvironmentKit/Presets/*.mat` as templates |
| **Optional prop scan** | Any folder under `Assets/` for natural props (rocks, crystals, vegetation) when configured |
| **Optional water shader** | Underground pool material (`CaveUndergroundWater_URP.mat` preset included) |

Each placed piece can get a **`CavePrefabSource`** component with the full `asset_reference: Assets/...` path when using catalog-driven scatter.

## XR profile

Full builds can apply **`VitureXRPro`** (`Assets/EnvironmentKit/Presets/VitureXRPro.asset`) — LOD, colliders, and URP hints for stereoscopic / mobile XR budgets.

- **VITURE SDK:** not shipped in this repository. If you install it separately, `VitureIntegration` detects VITURE-named assemblies.
- **Without VITURE SDK:** use Unity **OpenXR** / **Android XR** packages (already in `Packages/manifest.json`) and configure XR Plug-in Management for your device.

## One-click build (queued pipeline)

1. Open a scene with a **Ground**-tagged walkable surface.
2. Place **`PortalFive`** for the cave entrance (not the shop portal).
3. **Window → Environment Kit → Hub** → **Build Complete Cave Level (Active Scene)**  
   Builds surface (when FullWorld) then the **120-step** queued cave pipeline (block tunnel / shell / spline per configuration).
4. Optional: **Generate Lighting** for baked GI.
5. Optional: portal setup menu if your scene uses the kit portal helpers.

See [package pipeline docs](../../../Packages/com.cursor.environment-authoring-kit/docs/WORLD-GENERATION-PIPELINE-LADDER.md) for current step contracts.

## Portal & play (when your game implements them)

| Control | Typical action |
|---------|----------------|
| **F** | Surface portal → underground spawn |
| **E** | Interact / mine (game-specific) |
| **Shift+R** | Reset to surface spawn |

## Tags for scripting

| Tag | Use |
|-----|-----|
| `CaveEntrance` | Entrance marker + spawn point |
| `CaveWater` | Underground pool / river basin |

## Quality

Use **Cave Build Grader** and `Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json` (local, gitignored) for letter grades and ship/beta thresholds.
