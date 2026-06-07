#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Indexes every surface terrain tile (grid offset, ring, merge target) for stitch/seed and AI/tooling.
    /// Writes <see cref="ManifestRel"/> after spawn and after nine-tile lock.
    /// </summary>
    public static class SurfaceTerrainGridRegistry
    {
        public const string ManifestRel = CaveBuildAgentContextExporter.Folder + "/SurfaceTerrainGridManifest.json";

        [Serializable]
        public class Manifest
        {
            public string generatedUtc;
            public string scene;
            public bool playDiskLocked;
            public int playNeighborCountExpected = 8;
            public int playNeighborCountFound;
            public int outerRingTileCount;
            public float maxPositionMismatchMeters;
            public string blockoutGridPath;
            public string blockoutRoomsPath;
            public float blockoutCenterX;
            public float blockoutCenterY;
            public float blockoutCenterZ;
            public int blockoutFootprintCount;
            public string groundAnchorName;
            public string groundAnchorSource;
            public float groundAnchorX;
            public float groundAnchorY;
            public float groundAnchorZ;
            public float groundSurfaceY;
            public float flatNormalizedHeight;
            public TileEntry[] tiles = Array.Empty<TileEntry>();
        }

        [Serializable]
        public class TileEntry
        {
            public string terrainObjectName;
            public string tileId;
            public int gridX;
            public int gridZ;
            public string ring;
            public string mergeTargetTileId;
            public int chebyshevFromPlay;
            public int heightmapResolution;
            public float originX;
            public float originY;
            public float originZ;
            public float sizeX;
            public float sizeY;
            public float sizeZ;
            public bool positionMatchesGrid;
            public float positionMismatchMeters;
            public string neighborLeftId;
            public string neighborRightId;
            public string neighborTopId;
            public string neighborBottomId;
        }

        public static void ResetSession() => SurfaceFloridaDemBuildState.ResetForBuildSession();

        public static void ApplyIndex(Terrain terrain, string ringId, Vector2Int gridOffset, string mergeTargetTileId)
        {
            if (terrain == null)
                return;

            var index = terrain.GetComponent<SurfaceTerrainGridIndex>();
            if (index == null)
                index = terrain.gameObject.AddComponent<SurfaceTerrainGridIndex>();

            index.Apply(gridOffset.x, gridOffset.y, ringId, mergeTargetTileId);
        }

        public static Manifest ReindexFromMain(Terrain mainTerrain, bool playDiskLocked)
        {
            var manifest = BuildManifest(mainTerrain, playDiskLocked);
            WriteManifest(manifest);
            return manifest;
        }

        public static bool ValidatePlayDiskComplete(Terrain mainTerrain, out string message)
        {
            message = string.Empty;
            if (mainTerrain == null)
            {
                message = "Play disk invalid — no main terrain.";
                return false;
            }

            var missing = new List<string>();
            foreach (var off in SurfaceTerrainTileExpansion.NineTileRingOffsets)
            {
                if (SurfaceTerrainTileExpansion.TryFindTileAtOffset(
                        SurfaceTerrainTileExpansion.FindTilesRootPublic(mainTerrain),
                        off,
                        out var tile) &&
                    tile != null)
                    continue;

                missing.Add($"({off.x},{off.y})");
            }

            if (missing.Count > 0)
            {
                message =
                    $"Play disk incomplete — missing neighbor slot(s): {string.Join(", ", missing)}. " +
                    "Fix names SurfaceTerrainTile_x_y and positions before outer ring.";
                return false;
            }

            var gameplay = SurfaceTerrainTileExpansion.CollectValidatedGameplayTiles(mainTerrain, out var badNames);
            if (badNames.Count > 0)
            {
                message =
                    $"Play disk has {badNames.Count} misnamed tile(s) (need SurfaceTerrainTile_x_y): " +
                    string.Join(", ", badNames);
                return false;
            }

            if (gameplay.Length < SurfaceTerrainTileExpansion.NineTileRingOffsets.Length)
            {
                message =
                    $"Play disk has {gameplay.Length}/8 parsed neighbors — cannot seed or stitch outer ring.";
                return false;
            }

            return true;
        }

        public static Manifest BuildManifest(Terrain mainTerrain, bool playDiskLocked)
        {
            var manifest = new Manifest
            {
                generatedUtc = DateTime.UtcNow.ToString("o"),
                scene = SceneManager.GetActiveScene().name,
                playDiskLocked = playDiskLocked,
            };

            if (mainTerrain == null)
                return manifest;

            var ground = SceneGroundResolver.ResolveForFullWorld(mainTerrain.transform);
            if (ground != null && ground.HasAnchor)
            {
                manifest.groundAnchorName = ground.Anchor.name;
                manifest.groundAnchorSource = ground.Anchor.CompareTag("Ground")
                    ? "tag:Ground"
                    : string.Equals(ground.Anchor.name, SceneGroundResolver.PreferredGroundObjectName, StringComparison.OrdinalIgnoreCase)
                        ? $"named:{SceneGroundResolver.PreferredGroundObjectName}"
                        : "resolved";
                manifest.groundAnchorX = ground.Anchor.position.x;
                manifest.groundAnchorY = ground.Anchor.position.y;
                manifest.groundAnchorZ = ground.Anchor.position.z;
                manifest.groundSurfaceY = ground.SurfaceY;
            }
            else
            {
                manifest.groundAnchorSource = "fallback";
            }

            manifest.flatNormalizedHeight = SurfaceFullWorldDirectionalBuild.FlatNormalizedHeight;

            if (SurfaceTerrainBlockoutLayoutAuthor.TryFindBlockoutGrid(out var grid, out var rooms))
            {
                manifest.blockoutGridPath = GetHierarchyPath(grid);
                manifest.blockoutRoomsPath = rooms != null ? GetHierarchyPath(rooms) : string.Empty;
                if (SurfaceTerrainBlockoutLayoutAuthor.ComputeBlockoutBounds(grid, out var blockout))
                {
                    manifest.blockoutCenterX = blockout.center.x;
                    manifest.blockoutCenterY = blockout.center.y;
                    manifest.blockoutCenterZ = blockout.center.z;
                }

                manifest.blockoutFootprintCount = rooms != null
                    ? rooms.GetComponentsInChildren<Renderer>(true).Length
                    : grid.GetComponentsInChildren<Renderer>(true).Length;
            }

            var entries = new List<TileEntry>();
            AddMain(entries, mainTerrain);

            foreach (var tile in SurfaceTerrainTileExpansion.CollectValidatedGameplayTiles(mainTerrain, out _))
            {
                if (tile == null || !SurfaceTerrainTileExpansion.TryParseTileOffset(tile.name, out var off))
                    continue;
                var mergeId = SurfaceTerrainGridIndex.BuildTileId(SurfaceTerrainGridIndex.PlayMainRing, 0, 0);
                if (off != Vector2Int.zero &&
                    SurfaceTerrainTileExpansion.TryFindTileAtOffset(
                        SurfaceTerrainTileExpansion.FindTilesRootPublic(mainTerrain),
                        StepTowardOrigin(off),
                        out var inner) &&
                    inner != null &&
                    TryResolveTileId(inner, out var innerId))
                    mergeId = innerId;

                ApplyIndex(tile, SurfaceTerrainGridIndex.PlayNeighborRing, off, mergeId);
                entries.Add(BuildEntry(tile, off, SurfaceTerrainGridIndex.PlayNeighborRing, mergeId, mainTerrain));
            }

            manifest.playNeighborCountFound = entries.Count - 1;

            foreach (var tile in SurfaceTerrainTileExpansion.CollectMountainFoothillTiles(mainTerrain))
                AddOuter(entries, tile, SurfaceTerrainGridIndex.FoothillRing, mainTerrain);

            foreach (var tile in SurfaceTerrainTileExpansion.CollectMountainPeakTiles(mainTerrain))
                AddOuter(entries, tile, SurfaceTerrainGridIndex.PeakRing, mainTerrain);

            manifest.tiles = entries.ToArray();
            manifest.outerRingTileCount = manifest.tiles.Length - 1 - manifest.playNeighborCountFound;
            manifest.maxPositionMismatchMeters = 0f;
            foreach (var e in manifest.tiles)
                manifest.maxPositionMismatchMeters = Mathf.Max(manifest.maxPositionMismatchMeters, e.positionMismatchMeters);

            return manifest;
        }

        static void AddMain(List<TileEntry> entries, Terrain mainTerrain)
        {
            ApplyIndex(
                mainTerrain,
                SurfaceTerrainGridIndex.PlayMainRing,
                Vector2Int.zero,
                string.Empty);
            entries.Add(BuildEntry(
                mainTerrain,
                Vector2Int.zero,
                SurfaceTerrainGridIndex.PlayMainRing,
                string.Empty,
                mainTerrain));
        }

        static void AddOuter(List<TileEntry> entries, Terrain tile, string ringId, Terrain mainTerrain)
        {
            if (tile == null || !SurfaceTerrainTileExpansion.TryParseOuterRingTileOffset(tile.name, out var off))
                return;

            var mergeOff = StepTowardOrigin(off);
            var mergeId = SurfaceTerrainGridIndex.BuildTileId(SurfaceTerrainGridIndex.PlayMainRing, 0, 0);
            if (SurfaceTerrainTileExpansion.TryResolveTerrainAtGridOffset(
                    mainTerrain,
                    SurfaceTerrainTileExpansion.FindTilesRootPublic(mainTerrain),
                    SurfaceTerrainTileExpansion.FindMountainWildernessRootPublic(mainTerrain),
                    mergeOff,
                    out var mergeTerrain) &&
                mergeTerrain != null &&
                TryResolveTileId(mergeTerrain, out var resolved))
                mergeId = resolved;

            ApplyIndex(tile, ringId, off, mergeId);
            entries.Add(BuildEntry(tile, off, ringId, mergeId, mainTerrain));
        }

        static TileEntry BuildEntry(
            Terrain tile,
            Vector2Int off,
            string ringId,
            string mergeTargetTileId,
            Terrain mainTerrain)
        {
            var data = tile.terrainData;
            var pos = tile.transform.position;
            var size = data != null ? data.size : Vector3.one * 512f;
            var expected = ExpectedOrigin(mainTerrain, off);
            var mismatch = new Vector2(pos.x - expected.x, pos.z - expected.z).magnitude;

            var entry = new TileEntry
            {
                terrainObjectName = tile.name,
                tileId = SurfaceTerrainGridIndex.BuildTileId(
                    ringId == SurfaceTerrainGridIndex.PlayMainRing
                        ? SurfaceTerrainGridIndex.PlayMainRing
                        : ringId,
                    off.x,
                    off.y),
                gridX = off.x,
                gridZ = off.y,
                ring = ringId,
                mergeTargetTileId = mergeTargetTileId,
                chebyshevFromPlay = Mathf.Max(Mathf.Abs(off.x), Mathf.Abs(off.y)),
                heightmapResolution = data != null ? data.heightmapResolution : 0,
                originX = pos.x,
                originY = pos.y,
                originZ = pos.z,
                sizeX = size.x,
                sizeY = size.y,
                sizeZ = size.z,
                positionMatchesGrid = mismatch < 1.5f,
                positionMismatchMeters = mismatch,
            };

            TryNeighborId(mainTerrain, off + Vector2Int.left, out entry.neighborLeftId);
            TryNeighborId(mainTerrain, off + Vector2Int.right, out entry.neighborRightId);
            TryNeighborId(mainTerrain, off + Vector2Int.up, out entry.neighborTopId);
            TryNeighborId(mainTerrain, off + Vector2Int.down, out entry.neighborBottomId);
            return entry;
        }

        public static Vector3 ExpectedOrigin(Terrain mainTerrain, Vector2Int off)
        {
            if (mainTerrain?.terrainData == null)
                return Vector3.zero;

            var tileSize = mainTerrain.terrainData.size;
            var ground = SceneGroundResolver.ResolveForFullWorld(mainTerrain.transform);
            var sharedY = SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, mainTerrain).y;
            var anchor = SurfaceTerrainTileExpansion.FindFullWorldGridAnchorPublic(mainTerrain);
            if (anchor != null)
            {
                var expected = anchor.TransformPoint(
                    new Vector3(off.x * tileSize.x, 0f, off.y * tileSize.z));
                expected.y = sharedY;
                return expected;
            }

            var mainOrigin = mainTerrain.transform.position;
            return new Vector3(
                mainOrigin.x + off.x * tileSize.x,
                sharedY,
                mainOrigin.z + off.y * tileSize.z);
        }

        static void TryNeighborId(Terrain mainTerrain, Vector2Int off, out string id)
        {
            id = string.Empty;
            if (off == Vector2Int.zero)
            {
                TryResolveTileId(mainTerrain, out id);
                return;
            }

            if (SurfaceTerrainTileExpansion.TryResolveTerrainAtGridOffset(
                    mainTerrain,
                    SurfaceTerrainTileExpansion.FindTilesRootPublic(mainTerrain),
                    SurfaceTerrainTileExpansion.FindMountainWildernessRootPublic(mainTerrain),
                    off,
                    out var terrain) &&
                terrain != null)
                TryResolveTileId(terrain, out id);
        }

        static bool TryResolveTileId(Terrain terrain, out string id)
        {
            id = string.Empty;
            if (terrain == null)
                return false;

            var index = terrain.GetComponent<SurfaceTerrainGridIndex>();
            if (index != null && !string.IsNullOrEmpty(index.tileId))
            {
                id = index.tileId;
                return true;
            }

            if (terrain.name == SurfaceTerrainTileExpansion.MainTerrainName)
            {
                id = SurfaceTerrainGridIndex.BuildTileId(SurfaceTerrainGridIndex.PlayMainRing, 0, 0);
                return true;
            }

            if (SurfaceTerrainTileExpansion.TryParseTileOffset(terrain.name, out var playOff))
            {
                id = SurfaceTerrainGridIndex.BuildTileId(SurfaceTerrainGridIndex.PlayNeighborRing, playOff.x, playOff.y);
                return true;
            }

            if (SurfaceTerrainTileExpansion.TryParseOuterRingTileOffset(terrain.name, out var outerOff))
            {
                var ring = terrain.name.StartsWith(SurfaceTerrainTileExpansion.MountainPeakTileNamePrefix, StringComparison.Ordinal)
                    ? SurfaceTerrainGridIndex.PeakRing
                    : SurfaceTerrainGridIndex.FoothillRing;
                id = SurfaceTerrainGridIndex.BuildTileId(ring, outerOff.x, outerOff.y);
                return true;
            }

            return false;
        }

        public static Vector2Int StepTowardOrigin(Vector2Int off) =>
            new(
                off.x == 0 ? 0 : off.x > 0 ? off.x - 1 : off.x + 1,
                off.y == 0 ? 0 : off.y > 0 ? off.y - 1 : off.y + 1);

        public static void WriteManifest(Manifest manifest)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ManifestRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"generatedUtc\": \"{Escape(manifest.generatedUtc)}\",");
            sb.AppendLine($"  \"scene\": \"{Escape(manifest.scene)}\",");
            sb.AppendLine($"  \"playDiskLocked\": {(manifest.playDiskLocked ? "true" : "false")},");
            sb.AppendLine($"  \"playNeighborCountExpected\": {manifest.playNeighborCountExpected},");
            sb.AppendLine($"  \"playNeighborCountFound\": {manifest.playNeighborCountFound},");
            sb.AppendLine($"  \"outerRingTileCount\": {manifest.outerRingTileCount},");
            sb.AppendLine($"  \"maxPositionMismatchMeters\": {manifest.maxPositionMismatchMeters:F4},");
            sb.AppendLine($"  \"blockoutGridPath\": \"{Escape(manifest.blockoutGridPath)}\",");
            sb.AppendLine($"  \"blockoutRoomsPath\": \"{Escape(manifest.blockoutRoomsPath)}\",");
            sb.AppendLine($"  \"blockoutCenterX\": {manifest.blockoutCenterX:F3},");
            sb.AppendLine($"  \"blockoutCenterY\": {manifest.blockoutCenterY:F3},");
            sb.AppendLine($"  \"blockoutCenterZ\": {manifest.blockoutCenterZ:F3},");
            sb.AppendLine($"  \"blockoutFootprintCount\": {manifest.blockoutFootprintCount},");
            sb.AppendLine($"  \"groundAnchorName\": \"{Escape(manifest.groundAnchorName)}\",");
            sb.AppendLine($"  \"groundAnchorSource\": \"{Escape(manifest.groundAnchorSource)}\",");
            sb.AppendLine($"  \"groundAnchorX\": {manifest.groundAnchorX:F3},");
            sb.AppendLine($"  \"groundAnchorY\": {manifest.groundAnchorY:F3},");
            sb.AppendLine($"  \"groundAnchorZ\": {manifest.groundAnchorZ:F3},");
            sb.AppendLine($"  \"groundSurfaceY\": {manifest.groundSurfaceY:F3},");
            sb.AppendLine($"  \"flatNormalizedHeight\": {manifest.flatNormalizedHeight:F4},");
            sb.AppendLine("  \"tiles\": [");
            for (var i = 0; i < manifest.tiles.Length; i++)
            {
                var t = manifest.tiles[i];
                var comma = i < manifest.tiles.Length - 1 ? "," : "";
                sb.AppendLine("    {");
                sb.AppendLine($"      \"terrainObjectName\": \"{Escape(t.terrainObjectName)}\",");
                sb.AppendLine($"      \"tileId\": \"{Escape(t.tileId)}\",");
                sb.AppendLine($"      \"gridX\": {t.gridX},");
                sb.AppendLine($"      \"gridZ\": {t.gridZ},");
                sb.AppendLine($"      \"ring\": \"{Escape(t.ring)}\",");
                sb.AppendLine($"      \"mergeTargetTileId\": \"{Escape(t.mergeTargetTileId)}\",");
                sb.AppendLine($"      \"chebyshevFromPlay\": {t.chebyshevFromPlay},");
                sb.AppendLine($"      \"heightmapResolution\": {t.heightmapResolution},");
                sb.AppendLine($"      \"originX\": {t.originX:F3},");
                sb.AppendLine($"      \"originY\": {t.originY:F3},");
                sb.AppendLine($"      \"originZ\": {t.originZ:F3},");
                sb.AppendLine($"      \"sizeX\": {t.sizeX:F3},");
                sb.AppendLine($"      \"sizeY\": {t.sizeY:F3},");
                sb.AppendLine($"      \"sizeZ\": {t.sizeZ:F3},");
                sb.AppendLine($"      \"positionMatchesGrid\": {(t.positionMatchesGrid ? "true" : "false")},");
                sb.AppendLine($"      \"positionMismatchMeters\": {t.positionMismatchMeters:F4},");
                sb.AppendLine($"      \"neighborLeftId\": \"{Escape(t.neighborLeftId)}\",");
                sb.AppendLine($"      \"neighborRightId\": \"{Escape(t.neighborRightId)}\",");
                sb.AppendLine($"      \"neighborTopId\": \"{Escape(t.neighborTopId)}\",");
                sb.AppendLine($"      \"neighborBottomId\": \"{Escape(t.neighborBottomId)}\"");
                sb.AppendLine($"    }}{comma}");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
            CaveBuildEditorLog.LogSurface(
                $"[Surface] Terrain grid manifest — {manifest.tiles.Length} tile(s), play neighbors {manifest.playNeighborCountFound}/8, " +
                $"locked={manifest.playDiskLocked} → {ManifestRel}",
                forceUnityConsole: false);
        }

        static string GetHierarchyPath(Transform t)
        {
            if (t == null)
                return string.Empty;

            var path = t.name;
            var p = t.parent;
            while (p != null)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }

            return path;
        }

        static string Escape(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
#endif
