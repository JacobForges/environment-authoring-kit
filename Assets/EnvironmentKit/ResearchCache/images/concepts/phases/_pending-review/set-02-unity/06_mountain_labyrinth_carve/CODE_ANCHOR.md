# Code anchor — 06_mountain_labyrinth_carve

ONE spine batch on south 3×2 annex — wide benches, no per-edge queue.

Source: `/Users/jacob/Hub/Packages/com.cursor.environment-authoring-kit/Editor/Blockout/SurfaceMountainLabyrinthAuthor.cs`

```csharp
public const float CorridorHalfWidthMeters = 12.5f;
// QueueApply → BuildCarvePolylines (3–5 spines), south annex only:
// CollectSouthAnnexTiles → 6 terrains (foothill row + peak row).
// AfterLabyrinthCarve → QueueRestoreNonAnnexFoothillRollingHills (13-tile ring).
```
