# Code anchor — 01_play_disk

Nine gameplay tiles — center + ring offsets; horizontal play region.

Source: `/Users/jacob/Hub/Packages/com.cursor.environment-authoring-kit/Editor/Blockout/SurfaceTerrainTileExpansion.cs`

```csharp
public static readonly Vector2Int[] NineTileRingOffsets = { /* 8 neighbors + center */ };
public static bool UsesFixedNineTileSquare(WorldGenerationRequest request, bool fullWorld) =>
    request.ForceNineTileSquareGrid && fullWorld;
// Play disk = 3×3 at grid origin — NOT the south 3×2 annex.
```
