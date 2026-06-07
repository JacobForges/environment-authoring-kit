#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using EnvironmentAuthoringKit.Editor.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Peak tiles only — slope-painted rock terrain layers + scattered rock prefabs from project assets.
    /// Skips play disk, foothills, and shallow walkable bands.
    /// </summary>
    public static class SurfaceMountainPeakDressingAuthor
    {
        public const string PeakRocksRootName = "MountainPeakRocks";

        static readonly HashSet<int> PerTileDressedIds = new();

        const float MinScatterSlope = 0.18f;
        const float MaxScatterSlope = 0.94f;
        const float CliffBaseSlopeMin = 0.28f;
        const float TrailRockProximityMeters = 7f;
        const int RocksPerTileMax = 14;
        const int RocksPerSouthAnnexPeakMax = 22;
        const int RocksPerFoothillTileAaaMax = 24;
        const int RocksPerPeakTileAaaMax = 32;
        const float InnerSeamGrassPreserveMeters = 6f;

        static readonly string[] RockPrefabSearchTokens =
        {
            "rock01.prefab",
            "rock02.prefab",
            "rock03.prefab",
            "rock04.prefab",
            "rock05.prefab",
            "BackRock.prefab",
        };

        public static void QueueApply(
            Terrain mainTerrain,
            Transform surfaceRoot,
            int seed,
            Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            var peaks = SurfaceTerrainTileExpansion.CollectMountainPeakTiles(mainTerrain);
            var dressTiles = new List<Terrain>(peaks);
            if (CaveBuildAaaSessionPolicy.IsFullAaaRebuild)
            {
                foreach (var fh in SurfaceTerrainTileExpansion.CollectMountainFoothillTiles(mainTerrain))
                {
                    if (fh != null && !dressTiles.Contains(fh))
                        dressTiles.Add(fh);
                }
            }

            dressTiles.RemoveAll(t => t != null && PerTileDressedIds.Contains(UnityObjectCompat.ReferenceId(t)));

            if (dressTiles.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var rockLayers = ProjectTerrainLayerResolver.TryResolveMountainRockLayers();
            var rockPrefabs = LoadRockPrefabs();
            var tileArray = dressTiles.ToArray();
            CaveBuildEditorLog.LogSurface(
                $"[Surface] Peak rock dressing — {tileArray.Length} tile(s) " +
                $"(peaks{(CaveBuildAaaSessionPolicy.IsFullAaaRebuild ? " + foothills" : "")}), " +
                $"{rockLayers?.Length ?? 0} layer(s), {rockPrefabs.Count} prefab(s).",
                forceUnityConsole: true);

            QueueDressPeakAtIndex(mainTerrain, surfaceRoot, tileArray, rockLayers, rockPrefabs, seed, 0, onComplete);
        }

        /// <summary>Lightweight alphamap + rock scatter for one peak tile right after terraform.</summary>
        public static void QueueDressSinglePeakTile(
            Terrain peakTile,
            Transform surfaceRoot,
            int seed,
            Action onComplete)
        {
            if (peakTile?.terrainData == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (PerTileDressedIds.Contains(UnityObjectCompat.ReferenceId(peakTile)))
            {
                onComplete?.Invoke();
                return;
            }

            var rockLayers = ProjectTerrainLayerResolver.TryResolveMountainRockLayers();
            var rockPrefabs = LoadRockPrefabs();
            CaveBuildEditorLog.LogSurface(
                $"[Surface] Peak rock dressing — immediate pass on {peakTile.name} " +
                $"(layers {rockLayers?.Length ?? 0}, prefabs {rockPrefabs.Count}).",
                forceUnityConsole: true);

            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    var southAnnex = SurfaceMountainSouthAnnex.IsSouthPeakAnnexTile(peakTile);
                    ApplyRockLayersSlopePaint(peakTile, rockLayers, southAnnex);
                    var rockCap = southAnnex ? RocksPerSouthAnnexPeakMax : RocksPerTileMax;
                    ScatterRockPrefabs(peakTile, surfaceRoot, rockPrefabs, seed + UnityObjectCompat.ReferenceId(peakTile), rockCap);
                    PerTileDressedIds.Add(UnityObjectCompat.ReferenceId(peakTile));
                    onComplete?.Invoke();
                },
                CaveBuildPipelineDomains.QueueLabel($"peak rock dress {peakTile.name}"));
        }

        static void QueueDressPeakAtIndex(
            Terrain mainTerrain,
            Transform surfaceRoot,
            Terrain[] peaks,
            TerrainLayer[] rockLayers,
            IReadOnlyList<GameObject> rockPrefabs,
            int seed,
            int index,
            Action onComplete)
        {
            if (index >= peaks.Length)
            {
                onComplete?.Invoke();
                return;
            }

            var tile = peaks[index];
            CaveBuildActionPacing.ScheduleHeavy(
                () =>
                {
                    if (tile?.terrainData != null)
                    {
                        var southAnnex = SurfaceMountainSouthAnnex.IsSouthPeakAnnexTile(tile);
                        var isFoothill = tile.name.StartsWith(
                            SurfaceTerrainTileExpansion.MountainFoothillTileNamePrefix,
                            StringComparison.Ordinal);
                        ApplyRockLayersSlopePaint(tile, rockLayers, southAnnex);
                        var rockCap = CaveBuildAaaSessionPolicy.IsFullAaaRebuild
                            ? isFoothill
                                ? RocksPerFoothillTileAaaMax
                                : southAnnex
                                    ? RocksPerSouthAnnexPeakMax + 8
                                    : RocksPerPeakTileAaaMax
                            : southAnnex
                                ? RocksPerSouthAnnexPeakMax
                                : RocksPerTileMax;
                        ScatterRockPrefabs(
                            tile,
                            surfaceRoot,
                            rockPrefabs,
                            seed + index * 313,
                            rockCap);
                        PerTileDressedIds.Add(UnityObjectCompat.ReferenceId(tile));
                    }

                    QueueDressPeakAtIndex(
                        mainTerrain,
                        surfaceRoot,
                        peaks,
                        rockLayers,
                        rockPrefabs,
                        seed,
                        index + 1,
                        onComplete);
                },
                CaveBuildPipelineDomains.QueueLabel($"peak rock dress {index + 1}/{peaks.Length}"));
        }

        static void ApplyRockLayersSlopePaint(Terrain terrain, TerrainLayer[] rockLayers, bool southAnnexPeak)
        {
            if (terrain?.terrainData == null || rockLayers == null || rockLayers.Length == 0)
                return;

            var data = terrain.terrainData;
            CaveEditorUndo.RecordObject(data, "Peak rock layers");

            var grassLayer = ProjectTerrainLayerResolver.TryResolveTerrainLayers(1);
            var layers = new List<TerrainLayer>(2 + rockLayers.Length);
            if (grassLayer != null && grassLayer.Length > 0 && grassLayer[0] != null)
                layers.Add(grassLayer[0]);
            layers.AddRange(rockLayers);

            var unique = new List<TerrainLayer>();
            foreach (var layer in layers)
            {
                if (layer == null)
                    continue;
                var dup = false;
                for (var i = 0; i < unique.Count; i++)
                {
                    if (unique[i] == layer)
                    {
                        dup = true;
                        break;
                    }
                }

                if (!dup)
                    unique.Add(layer);
            }

            if (unique.Count == 0)
                return;

            data.terrainLayers = unique.ToArray();
            var alphaW = data.alphamapWidth;
            var alphaH = data.alphamapHeight;
            var map = new float[alphaW, alphaH, unique.Count];
            var origin = terrain.transform.position;
            var size = data.size;

            for (var y = 0; y < alphaH; y++)
            {
                for (var x = 0; x < alphaW; x++)
                {
                    var wx = origin.x + x / (float)(alphaW - 1) * size.x;
                    var wz = origin.z + y / (float)(alphaH - 1) * size.z;
                    var slope = SampleSlope01(terrain, wx, wz);
                    var height01 = SampleHeight01(terrain, wx, wz);
                    var slopeLow = southAnnexPeak ? 0.04f : 0.06f;
                    var slopeHigh = southAnnexPeak ? 0.36f : 0.40f;
                    var rockWeight = Mathf.SmoothStep(slopeLow, slopeHigh, slope);
                    rockWeight = Mathf.Clamp01(rockWeight + Mathf.SmoothStep(0.32f, 0.82f, height01) * (southAnnexPeak ? 0.68f : 0.62f));
                    rockWeight *= PreserveGrassNearInnerSeam(terrain, wx, wz);

                    map[x, y, 0] = 1f - rockWeight;
                    if (unique.Count > 1)
                        map[x, y, 1] = rockWeight * 0.72f;
                    if (unique.Count > 2)
                        map[x, y, 2] = rockWeight * 0.28f;
                }
            }

            data.SetAlphamaps(0, 0, map);
            terrain.Flush();
        }

        static float SampleHeight01(Terrain terrain, float wx, float wz)
        {
            var originY = terrain.transform.position.y;
            var topY = originY + terrain.terrainData.size.y;
            var y = terrain.SampleHeight(new Vector3(wx, 0f, wz)) + originY;
            return Mathf.InverseLerp(originY, topY, y);
        }

        static float SampleSlope01(Terrain terrain, float wx, float wz)
        {
            const float sample = 2.5f;
            var h = terrain.SampleHeight(new Vector3(wx, 0f, wz));
            var hx = terrain.SampleHeight(new Vector3(wx + sample, 0f, wz));
            var hz = terrain.SampleHeight(new Vector3(wx, 0f, wz + sample));
            var rise = Mathf.Max(Mathf.Abs(hx - h), Mathf.Abs(hz - h));
            return Mathf.Clamp01(rise / (sample * 0.55f));
        }

        static float PreserveGrassNearInnerSeam(Terrain terrain, float wx, float wz)
        {
            if (!SurfaceMountainSouthAnnex.IsSouthPeakAnnexTile(terrain))
                return 1f;

            var origin = terrain.transform.position;
            var size = terrain.terrainData.size;
            var distToPlayFacingEdge = origin.z + size.z - wz;
            if (distToPlayFacingEdge > InnerSeamGrassPreserveMeters)
                return 1f;

            return Mathf.SmoothStep(0f, 1f, distToPlayFacingEdge / InnerSeamGrassPreserveMeters);
        }

        static void ScatterRockPrefabs(
            Terrain terrain,
            Transform surfaceRoot,
            IReadOnlyList<GameObject> prefabs,
            int seed,
            int maxRocks)
        {
            if (terrain?.terrainData == null || prefabs == null || prefabs.Count == 0)
                return;

            var root = EnsureRockRoot(surfaceRoot);
            var rng = new System.Random(seed + 8803);
            var origin = terrain.transform.position;
            var size = terrain.terrainData.size;
            var trailPoints = CollectNearbyTrailPoints(surfaceRoot, origin, size);
            var placed = 0;
            var isPeak = terrain.name.StartsWith(
                SurfaceTerrainTileExpansion.MountainPeakTileNamePrefix,
                StringComparison.Ordinal);

            for (var attempt = 0; attempt < maxRocks * 8 && placed < maxRocks; attempt++)
            {
                var wx = origin.x + (float)rng.NextDouble() * size.x;
                var wz = origin.z + (float)rng.NextDouble() * size.z;
                var slope = SampleSlope01(terrain, wx, wz);
                var nearTrail = IsNearTrailPoint(trailPoints, wx, wz, TrailRockProximityMeters);
                var minSlope = nearTrail ? MinScatterSlope * 0.85f : MinScatterSlope;
                if (slope < minSlope || slope > MaxScatterSlope)
                    continue;

                if (isPeak && slope < CliffBaseSlopeMin && !nearTrail && rng.NextDouble() > 0.22f)
                    continue;

                var wy = terrain.SampleHeight(new Vector3(wx, 0f, wz)) + origin.y;
                var prefab = prefabs[rng.Next(prefabs.Count)];
                if (prefab == null)
                    continue;

                var rot = Quaternion.Euler(
                    (float)(rng.NextDouble() * 18f - 9f),
                    (float)rng.NextDouble() * 360f,
                    (float)(rng.NextDouble() * 14f - 7f));
                var scale = nearTrail || slope >= CliffBaseSlopeMin
                    ? 0.75f + (float)rng.NextDouble() * 1.1f
                    : 0.85f + (float)rng.NextDouble() * 1.4f;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                if (instance == null)
                    continue;

                CaveEditorUndo.RegisterCreated(instance, "Peak rock scatter");
                instance.transform.position = new Vector3(wx, wy, wz);
                instance.transform.rotation = rot;
                instance.transform.localScale = Vector3.one * scale;
                placed++;
            }
        }

        static Transform EnsureRockRoot(Transform surfaceRoot)
        {
            if (surfaceRoot == null)
                return null;

            var existing = surfaceRoot.Find(PeakRocksRootName);
            if (existing != null)
                return existing;

            var go = new GameObject(PeakRocksRootName);
            CaveEditorUndo.RegisterCreated(go, "Peak rocks root");
            go.transform.SetParent(surfaceRoot, false);
            return go.transform;
        }

        static List<Vector3> CollectNearbyTrailPoints(Transform surfaceRoot, Vector3 tileOrigin, Vector3 tileSize)
        {
            var list = new List<Vector3>(64);
            if (surfaceRoot == null)
                return list;

            var trails = surfaceRoot.Find(SurfaceWorldPaths.TrailsName);
            if (trails == null)
                return list;

            var minX = tileOrigin.x;
            var maxX = tileOrigin.x + tileSize.x;
            var minZ = tileOrigin.z;
            var maxZ = tileOrigin.z + tileSize.z;
            foreach (Transform trail in trails)
            {
                if (trail == null)
                    continue;
                foreach (Transform child in trail)
                {
                    if (child == null || !child.name.StartsWith("Waypoint_", StringComparison.Ordinal))
                        continue;
                    var p = child.position;
                    if (p.x < minX - TrailRockProximityMeters || p.x > maxX + TrailRockProximityMeters ||
                        p.z < minZ - TrailRockProximityMeters || p.z > maxZ + TrailRockProximityMeters)
                        continue;
                    list.Add(p);
                }
            }

            return list;
        }

        static bool IsNearTrailPoint(IReadOnlyList<Vector3> trailPoints, float wx, float wz, float radiusMeters)
        {
            if (trailPoints == null || trailPoints.Count == 0)
                return false;

            var r2 = radiusMeters * radiusMeters;
            for (var i = 0; i < trailPoints.Count; i++)
            {
                var p = trailPoints[i];
                var dx = wx - p.x;
                var dz = wz - p.z;
                if (dx * dx + dz * dz <= r2)
                    return true;
            }

            return false;
        }

        static List<GameObject> LoadRockPrefabs()
        {
            var list = new List<GameObject>();
            for (var k = 1; k <= 8; k++)
            {
                var cc0 = Cc0ContentImportUtility.LoadItemPrefab($"K{k:D2}");
                if (cc0 != null && !list.Contains(cc0))
                    list.Add(cc0);
            }

            if (list.Count > 0)
                return list;

            foreach (var token in RockPrefabSearchTokens)
            {
                foreach (var guid in AssetDatabase.FindAssets($"{System.IO.Path.GetFileNameWithoutExtension(token)} t:Prefab"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path) || !path.EndsWith(token, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && !list.Contains(prefab))
                        list.Add(prefab);
                }
            }

            if (list.Count > 0)
                return list;

            foreach (var guid in AssetDatabase.FindAssets("rock t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) ||
                    path.IndexOf("rock", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && !list.Contains(prefab))
                    list.Add(prefab);
                if (list.Count >= 8)
                    break;
            }

            return list;
        }
    }
}
#endif
