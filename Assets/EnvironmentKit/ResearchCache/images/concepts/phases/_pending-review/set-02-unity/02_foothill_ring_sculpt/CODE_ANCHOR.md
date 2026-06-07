# Code anchor — 02_foothill_ring_sculpt

Foothill ring sculpt — exclude south annex tiles from generic denoise.

Source: `/Users/jacob/Hub/Packages/com.cursor.environment-authoring-kit/Editor/Blockout/SurfaceMountainSouthAnnex.cs`

```csharp
public static Terrain[] CollectFoothillTilesOutsideSouthAnnex(Terrain mainTerrain)
{
    // Skip SouthFoothillAnnexOffsets (-1,-2), (0,-2), (1,-2) — labyrinth benches come later.
}
```
