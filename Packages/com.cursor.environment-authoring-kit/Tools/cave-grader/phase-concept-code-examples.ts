/**
 * Short C# anchors per concept category — included beside phase concept images in prompts.
 * Source: Environment Kit Editor/Blockout (trimmed for agents; open full files when editing).
 */
export type PhaseConceptCodeExample = {
  /** Hub-relative primary type (for navigation) */
  sourceRel: string;
  /** One-line what this code enforces */
  caption: string;
  /** Trimmed excerpt — must stay under ~25 lines */
  snippet: string;
};

export const PHASE_CONCEPT_CODE_BY_FOLDER: Record<string, PhaseConceptCodeExample> = {
  "00_fullworld_overview": {
    sourceRel:
      "Packages/com.cursor.environment-authoring-kit/Editor/Generation/WorldGenerationRequest.cs",
    caption: "220 m tiles, 3×3 play disk, FullWorld outer ring flags.",
    snippet: `public float SurfaceExtentMeters = 220f;
public bool ForceNineTileSquareGrid = true;
public bool UseOuterRingMountains = true;
public bool SurfaceIncludeMountains = true;
public bool SurfaceIncludeMountainLabyrinth = true;`,
  },
  "01_play_disk": {
    sourceRel:
      "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/SurfaceTerrainTileExpansion.cs",
    caption: "Nine gameplay tiles — center + ring offsets; horizontal play region.",
    snippet: `public static readonly Vector2Int[] NineTileRingOffsets = { /* 8 neighbors + center */ };
public static bool UsesFixedNineTileSquare(WorldGenerationRequest request, bool fullWorld) =>
    request.ForceNineTileSquareGrid && fullWorld;
// Play disk = 3×3 at grid origin — NOT the south 3×2 annex.`,
  },
  "02_foothill_ring_sculpt": {
    sourceRel:
      "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/SurfaceMountainSouthAnnex.cs",
    caption: "Foothill ring sculpt — exclude south annex tiles from generic denoise.",
    snippet: `public static Terrain[] CollectFoothillTilesOutsideSouthAnnex(Terrain mainTerrain)
{
    // Skip SouthFoothillAnnexOffsets (-1,-2), (0,-2), (1,-2) — labyrinth benches come later.
}`,
  },
  "03_south_foothill_labyrinth_row": {
    sourceRel:
      "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/SurfaceMountainSouthAnnex.cs",
    caption: "South foothill row (z=-2) — labyrinth stage on rolling hills, not flat slabs.",
    snippet: `public const int SouthColumns = 3;
public const int SouthRows = 2;
public static readonly Vector2Int[] SouthFoothillAnnexOffsets = {
    new(-1, -2), new(0, -2), new(1, -2),
};
// Wide bench walkways carved HERE — behind peaks, toward play disk north.`,
  },
  "04_peak_ring": {
    sourceRel:
      "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/SurfaceMountainSouthAnnex.cs",
    caption: "South peak row (z=-3) behind foothill labyrinth row.",
    snippet: `public static readonly Vector2Int[] SouthPeakAnnexOffsets = {
    new(-1, -3), new(0, -3), new(1, -3),
};
public static Terrain[] CollectSouthPeakAnnexTiles(Terrain mainTerrain) =>
    CollectTilesAtOffsets(mainTerrain, SouthPeakAnnexOffsets, MountainPeakTileNamePrefix);`,
  },
  "05_wilderness_mountains": {
    sourceRel:
      "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/SurfaceOuterRingMountainsAuthor.cs",
    caption: "9×9 wilderness grid — stitched 220 m neighbors, perimeter cliffs only.",
    snippet: `// Outer ring: many Terrain children under mountain wilderness root.
// Each tile = SurfaceExtentMeters (220) — stitch heightmap edges before sculpt passes.`,
  },
  "06_mountain_labyrinth_carve": {
    sourceRel:
      "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/SurfaceMountainLabyrinthAuthor.cs",
    caption: "ONE spine batch on south 3×2 annex — wide benches, no per-edge queue.",
    snippet: `public const float CorridorHalfWidthMeters = 12.5f;
// QueueApply → BuildCarvePolylines (3–5 spines), south annex only:
// CollectSouthAnnexTiles → 6 terrains (foothill row + peak row).
// AfterLabyrinthCarve → QueueRestoreNonAnnexFoothillRollingHills (13-tile ring).`,
  },
  "07_mountain_trails": {
    sourceRel:
      "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/SurfaceTrailCaveMouthConnector.cs",
    caption: "Trails after labyrinth — connect play disk → annex entrance → wilderness mouths.",
    snippet: `// mountain_trails rung: perimeter bench trails on foothill/peak ring.
// NavMesh must follow carved benches — max grade readable in third-person.`,
  },
  "08_wilderness_cave_mouth": {
    sourceRel:
      "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/SurfaceMountainWildernessCaveMouthAuthor.cs",
    caption: "Three south peak mouths — shallow bore, sectors 201–203, not play-disk pit.",
    snippet: `public const float WildernessMouthRadiusMeters = 14f;
public const float WildernessMouthDepthMeters = 3.1f;
public const int SectorMainMountain = 201;   // center peak — main connector
public const int SectorTrapMountain = 202;   // west peak
public const int SectorLabyrinthMountain = 203; // east peak`,
  },
  "09_cave_primary_mouth": {
    sourceRel:
      "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/CaveSurfaceEntranceBuilder.cs",
    caption: "Play-disk primary mouth — surface walk-in snapped to underground route.",
    snippet: `SurfaceTrailCaveMouthConnector.ConnectPrimaryTrailToCaveMouth(
    surfaceRoot, cavesRoot, ground);
// Mouth seal / underground only in cave phases — do not re-sculpt FullWorld terrain.`,
  },
  "10_cave_underground": {
    sourceRel:
      "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/CaveEnclosureShellBuilder.cs",
    caption: "Buried tube + RouteTerrain floor/ceiling — no onion stacked slabs.",
    snippet: `public const string FloorRootName = "RouteTerrainFloor";
public const string CeilingRootName = "RouteTerrainCeiling";
// Visible walk surface = RouteTerrainFloor; hide duplicate PathPlatforms slabs.`,
  },
};

export function formatConceptCodeBlock(folder: string, hubRoot: string): string {
  const ex = PHASE_CONCEPT_CODE_BY_FOLDER[folder];
  if (!ex) return "";
  const hub = hubRoot.replace(/\/$/, "");
  const lines = [
    "**Kit code anchor:**",
    `- ${ex.caption}`,
    `- Open: \`${hub}/${ex.sourceRel}\``,
    "",
    "```csharp",
    ex.snippet.trimEnd(),
    "```",
    "",
  ];
  return lines.join("\n");
}
