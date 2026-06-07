# Assets/EnvironmentKit/Presets

Unity **ScriptableObject** assets used by the Environment Authoring Kit for **atmosphere, terrain scatter, cave materials, and XR budgets**.

These are **not** the Hub **FullWorld generation presets (1–20)**. For build flavor (labyrinth, mouths, cave maze, water, fog), use:

**[FULLWORLD_GENERATION_PRESETS.md](../../../Packages/com.cursor.environment-authoring-kit/docs/FULLWORLD_GENERATION_PRESETS.md)**

---

## What lives here

| Asset pattern | Role |
|---------------|------|
| `ForestTerrain`, `DesertTerrain`, `SnowTerrain`, … | Terrain layer / height scatter profiles per biome |
| `ForestScatter`, `CityScatter`, … | Prop density and categories for surface placement |
| `ForestOvercast`, `SnowDay`, `DungeonDim`, … | Time-of-day + weather atmosphere presets |
| `CaveLavaEmissive_URP`, `CaveUndergroundWater_URP`, … | Cave rendering materials |
| `VitureXRPro` | XR optimization profile applied during full builds |
| `BiomeCatalog` | Biome id lookup for generation requests |

Hub preset **06 — Alpine snow peaks** sets `Biome = Snow`, which pulls **`SnowTerrain`** / **`SnowScatter`** / **`SnowDay`**-style settings through the normal generation path — it does not edit these assets automatically.

---

## Best practice

1. **Duplicate** an existing preset asset before large edits (Unity: right-click → Duplicate).
2. Keep **URP** material references compatible with your project’s render pipeline.
3. After changing terrain/scatter presets, run **Build Surface World Only** or a full rebuild to see prop and height changes.
4. Tune **VitureXRPro** only when targeting glasses builds — values affect LOD and collider budgets globally during **Build Complete Cave**.

---

## Related

| Doc | Content |
|-----|---------|
| [FULLWORLD_GENERATION_PRESETS.md](../../../Packages/com.cursor.environment-authoring-kit/docs/FULLWORLD_GENERATION_PRESETS.md) | Hub dropdown presets 1–20 |
| [Package README](../../../Packages/com.cursor.environment-authoring-kit/README.md) | Menus and pipeline |
| [Recipes README](../Recipes/README.md) | JSON build recipes |
