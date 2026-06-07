#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// Terrain v2 closure — props on every surface tile (81-grid), selective cliff accents,
    /// research-cache markers, plus intelligent rock pass when available.
    /// </summary>
    public static class WorldTerrainV2ClosureAuthor
    {
        public const string RootName = "WorldTerrainV2Closure";
        public const string ResearchCacheRootName = "WorldResearchCacheProps";

        static readonly string[] CachePropIds = { "L09", "M01", "M02", "M03", "G-T01", "PL01" };
        static readonly string[] CliffPropIds = { "L09", "M02", "M03" };

        public static void Apply(Terrain mainTerrain, WorldGenerationRequest request)
        {
            if (mainTerrain == null || request == null)
                return;
            if (request.ContentTier == WorldBuildContentTier.Light)
                return;

            var tiles = CollectAllSurfaceTiles(mainTerrain);
            if (tiles.Count == 0)
            {
                Debug.LogWarning("[TerrainV2] No surface tiles found for closure pass.");
                return;
            }

            ClearPrevious();
            var root = new GameObject(RootName);
            var cacheRoot = new GameObject(ResearchCacheRootName);
            cacheRoot.transform.SetParent(root.transform, false);

            var rng = new System.Random(request.Seed + 0x543231); // "TERR"
            var propsPlaced = 0;
            var cliffsPlaced = 0;
            var cacheMarkers = 0;

            foreach (var tile in tiles)
            {
                if (tile?.terrainData == null)
                    continue;

                if (!SurfaceTerrainTileExpansion.TryParseTileOffset(tile.name, out var off))
                    off = Vector2Int.zero;

                var biome = SurfaceWorldBiomeAuthor.ResolveBiomeForTileOffset(off, off.y <= -1);
                var propsPerTile = request.ContentTier == WorldBuildContentTier.Aaa ? 4 : 2;

                for (var i = 0; i < propsPerTile; i++)
                {
                    if (!WorldLootSpawnDirector.TrySamplePointPublic(tile, rng, out var world))
                        continue;

                    var id = PickPropId(rng, biome, steep: false);
                    if (PlaceProp(root.transform, id, world, rng, 0.35f, 0.55f))
                        propsPlaced++;
                }

                for (var c = 0; c < 2; c++)
                {
                    if (!TrySampleSteepPoint(tile, rng, out var cliffWorld))
                        continue;

                    var cliffId = CliffPropIds[rng.Next(CliffPropIds.Length)];
                    if (PlaceProp(root.transform, cliffId, cliffWorld, rng, 0.8f, 1.4f))
                        cliffsPlaced++;
                }

                if (WorldLootSpawnDirector.TrySamplePointPublic(tile, rng, out var cachePos))
                {
                    var cacheId = CachePropIds[rng.Next(CachePropIds.Length)];
                    if (PlaceProp(cacheRoot.transform, cacheId, cachePos, rng, 0.5f, 0.9f))
                    {
                        cacheMarkers++;
                        var markerGo = cacheRoot.transform.GetChild(cacheRoot.transform.childCount - 1);
                        if (markerGo != null)
                            markerGo.name = $"ResearchCache_{off.x}_{off.y}_{cacheId}";
                    }
                }
            }

            TryIntelligentRockPass(mainTerrain, request, root.transform);
            QueueCliffHeightPass(mainTerrain);

            Debug.Log(
                $"[TerrainV2] Closure on {tiles.Count} tile(s) — props {propsPlaced}, cliff props {cliffsPlaced}, " +
                $"research-cache {cacheMarkers}.",
                root);
        }

        static List<Terrain> CollectAllSurfaceTiles(Terrain mainTerrain)
        {
            var set = new HashSet<Terrain>();
            foreach (var t in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain))
            {
                if (t != null)
                    set.Add(t);
            }

            foreach (var t in SurfaceTerrainTileExpansion.CollectGameplayTiles(mainTerrain))
            {
                if (t != null)
                    set.Add(t);
            }

            foreach (var t in SurfaceTerrainTileExpansion.CollectMountainWildernessTiles(mainTerrain))
            {
                if (t != null)
                    set.Add(t);
            }

            if (mainTerrain != null)
                set.Add(mainTerrain);

            return new List<Terrain>(set);
        }

        static void ClearPrevious()
        {
            var a = GameObject.Find(RootName);
            if (a != null)
                Object.DestroyImmediate(a);
            var b = GameObject.Find(ResearchCacheRootName);
            if (b != null)
                Object.DestroyImmediate(b);
            var legacy = GameObject.Find(WorldTilePropScatter.RootName);
            if (legacy != null)
                Object.DestroyImmediate(legacy);
        }

        static string PickPropId(System.Random rng, WorldSurfaceBiomeId biome, bool steep)
        {
            if (steep)
                return CliffPropIds[rng.Next(CliffPropIds.Length)];

            return biome switch
            {
                WorldSurfaceBiomeId.FoothillGreen => rng.NextDouble() < 0.5 ? "PL02" : "M01",
                WorldSurfaceBiomeId.PeakStone => rng.NextDouble() < 0.5 ? "L09" : "M02",
                WorldSurfaceBiomeId.HorizonMist => rng.NextDouble() < 0.5 ? "M03" : "PL03",
                WorldSurfaceBiomeId.AnnexLabyrinth => rng.NextDouble() < 0.5 ? "L07" : "K02",
                _ => rng.NextDouble() < 0.5 ? "PL01" : "M01",
            };
        }

        static bool PlaceProp(
            Transform parent,
            string id,
            Vector3 world,
            System.Random rng,
            float minScale,
            float maxScale)
        {
            var prefab = Cc0ContentImportUtility.LoadItemPrefab(id);
            GameObject go;
            if (prefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.transform.position = world + Vector3.up * 0.12f;
                var s = minScale + (float)rng.NextDouble() * (maxScale - minScale);
                go.transform.localScale = Vector3.one * s;
                go.transform.rotation = Quaternion.Euler(0f, rng.Next(0, 360), 0f);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.transform.SetParent(parent, false);
                go.transform.position = world + Vector3.up * 0.25f;
                go.transform.localScale = new Vector3(0.5f, 0.7f, 0.5f);
            }

            go.name = $"Prop_{id}_{parent.childCount:D3}";
            return true;
        }

        static bool TrySampleSteepPoint(Terrain tile, System.Random rng, out Vector3 world)
        {
            world = default;
            var td = tile.terrainData;
            if (td == null)
                return false;

            for (var i = 0; i < 12; i++)
            {
                var lx = (float)rng.NextDouble() * td.size.x;
                var lz = (float)rng.NextDouble() * td.size.z;
                var steep = td.GetSteepness(lx / td.size.x, lz / td.size.z);
                if (steep < 28f)
                    continue;

                var h = td.GetInterpolatedHeight(lx / td.size.x, lz / td.size.z);
                world = tile.transform.position + new Vector3(lx, h, lz);
                return true;
            }

            return false;
        }

        static void TryIntelligentRockPass(Terrain mainTerrain, WorldGenerationRequest request, Transform parent)
        {
            var surfaceRoot = mainTerrain.transform.root;
            var center = SurfaceTerrainPlayRegion.TerrainTileCenter(mainTerrain);
            var extent = SurfaceTerrainPlayRegion.ResolveRequestExtentMeters(request);

            if (!SurfaceIntelligentPropPlacer.TryPlaceCategoryLadderPass(
                    surfaceRoot,
                    mainTerrain,
                    center,
                    extent,
                    request.Seed + 771,
                    SurfacePropCategory.Rocks,
                    out var msg))
            {
                Debug.Log($"[TerrainV2] Intelligent rock pass skipped: {msg}");
                return;
            }

            Debug.Log($"[TerrainV2] Intelligent rock pass: {msg}", parent);
        }

        static void QueueCliffHeightPass(Terrain mainTerrain)
        {
            SurfaceMountainTerrainPhases.QueueCliffAccent(
                mainTerrain,
                () => Debug.Log("[TerrainV2] Heightmap cliff accent pass complete."));
        }
    }
}
#endif
