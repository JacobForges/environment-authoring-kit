# Code anchor — 04_peak_ring

South peak row (z=-3) behind foothill labyrinth row.

Source: `/Users/jacob/Hub/Packages/com.cursor.environment-authoring-kit/Editor/Blockout/SurfaceMountainSouthAnnex.cs`

```csharp
public static readonly Vector2Int[] SouthPeakAnnexOffsets = {
    new(-1, -3), new(0, -3), new(1, -3),
};
public static Terrain[] CollectSouthPeakAnnexTiles(Terrain mainTerrain) =>
    CollectTilesAtOffsets(mainTerrain, SouthPeakAnnexOffsets, MountainPeakTileNamePrefix);
```
