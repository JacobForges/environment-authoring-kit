#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Lightweight 3D mesh props along tile seam weld points — rocks, berms, cliff skirts that hide
    /// visible height gaps without full re-sculpt. Runs after peak↔foothill seam welds.
    /// </summary>
    public static class SurfaceSeamDressingPropAuthor
    {
        public const string SeamDressingRootName = "SurfaceSeamDressing";

        const float EdgeInsetMeters = 4f;
        const float SeamSampleSpacingMeters = 18f;
        const int PropsPerEdgeMax = 6;
        const float MinSlopeForSkirt = 0.12f;

        static readonly Vector2Int[] EdgeDeltas =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        };

        public static void QueueDressSeamEdges(
            Terrain mainTerrain,
            Transform surfaceRoot,
            int seed,
            Action onComplete,
            bool floatingIslandEdges = false)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            var tiles = CollectDressableTiles(mainTerrain, floatingIslandEdges);
            if (tiles.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var prefabs = LoadDressingPrefabs();
            if (prefabs.Count == 0)
            {
                CaveBuildEditorLog.LogSurfaceWarning(
                    "[Surface] Seam dressing skipped — no rock prefabs found (rock01–05 / BackRock).",
                    forceUnityConsole: true);
                onComplete?.Invoke();
                return;
            }

            var slots = BuildSeamSlots(mainTerrain, tiles, seed, floatingIslandEdges);
            if (slots.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var modeLabel = floatingIslandEdges ? "3D cover (floating islands)" : "peak/foothill";
            CaveBuildEditorLog.LogSurface(
                $"[Surface] Seam dressing — {modeLabel}: {slots.Count} prop slot(s) on {tiles.Count} tile edge(s), " +
                $"{prefabs.Count} prefab(s).",
                forceUnityConsole: true);

            var root = EnsureDressingRoot(surfaceRoot);
            var rng = new System.Random(seed + 44017);
            var index = 0;

            void PlaceNext()
            {
                if (index >= slots.Count)
                {
                    onComplete?.Invoke();
                    return;
                }

                var slot = slots[index++];
                CaveBuildActionPacing.ScheduleLight(
                    () =>
                    {
                        var prefab = prefabs[rng.Next(prefabs.Count)];
                        if (prefab != null)
                            SpawnDressingProp(root, prefab, slot, rng);
                        PlaceNext();
                    },
                    CaveBuildPipelineDomains.QueueLabel(
                        floatingIslandEdges
                            ? $"3D seam cover {index}/{slots.Count}"
                            : $"seam dress {index}/{slots.Count}"));
            }

            PlaceNext();
        }

        /// <summary>Lightweight builds: rock skirts along all shared tile edges (no heightmap seam blend).</summary>
        public static void QueueDressFloatingIslandSeamCover(
            Terrain mainTerrain,
            Transform surfaceRoot,
            int seed,
            Action onComplete) =>
            QueueDressSeamEdges(mainTerrain, surfaceRoot, seed, onComplete, floatingIslandEdges: true);

        static List<Terrain> CollectDressableTiles(Terrain mainTerrain, bool floatingIslandEdges)
        {
            var list = new List<Terrain>();
            if (floatingIslandEdges)
            {
                if (mainTerrain?.terrainData != null)
                    list.Add(mainTerrain);

                foreach (var t in SurfaceTerrainTileExpansion.CollectGameplayTiles(mainTerrain))
                {
                    if (t?.terrainData != null && !list.Contains(t))
                        list.Add(t);
                }

                foreach (var t in SurfaceTerrainTileExpansion.CollectMountainWildernessTiles(mainTerrain))
                {
                    if (t?.terrainData != null && !list.Contains(t))
                        list.Add(t);
                }

                return list;
            }

            foreach (var t in SurfaceTerrainTileExpansion.CollectMountainFoothillTiles(mainTerrain))
            {
                if (t?.terrainData != null)
                    list.Add(t);
            }

            foreach (var t in SurfaceTerrainTileExpansion.CollectMountainPeakTiles(mainTerrain))
            {
                if (t?.terrainData != null && !list.Contains(t))
                    list.Add(t);
            }

            if (CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid)
            {
                foreach (var t in SurfaceTerrainTileExpansion.CollectOpenWorldTiles(mainTerrain))
                {
                    if (t?.terrainData != null && !list.Contains(t))
                        list.Add(t);
                }
            }

            return list;
        }

        static List<SeamDressSlot> BuildSeamSlots(
            Terrain mainTerrain,
            List<Terrain> tiles,
            int seed,
            bool floatingIslandEdges)
        {
            var slots = new List<SeamDressSlot>();
            var rng = new System.Random(seed + 991);
            var seen = new HashSet<long>();
            var spacing = floatingIslandEdges ? 22f : SeamSampleSpacingMeters;
            var propsMax = floatingIslandEdges ? 4 : PropsPerEdgeMax;
            var minSlope = floatingIslandEdges ? 0.02f : MinSlopeForSkirt;

            for (var i = 0; i < tiles.Count; i++)
            {
                var tile = tiles[i];
                if (!SurfaceTerrainTileExpansion.TryResolveSurfaceTileGridOffset(mainTerrain, tile, out var off))
                    continue;

                var origin = tile.transform.position;
                var size = tile.terrainData.size;

                for (var e = 0; e < EdgeDeltas.Length; e++)
                {
                    var delta = EdgeDeltas[e];
                    var neighborOff = off + delta;
                    if (!TryFindTileAtOffset(mainTerrain, neighborOff, out var neighbor) || neighbor == null)
                        continue;

                    var isPeakFoothill = IsPeakFoothillPair(tile, neighbor);
                    if (!floatingIslandEdges &&
                        !isPeakFoothill &&
                        !IsOpenWorldEdge(tile, neighbor))
                        continue;

                    var edgeLen = delta.x != 0 ? size.z : size.x;
                    var count = Mathf.Min(propsMax, Mathf.Max(2, Mathf.RoundToInt(edgeLen / spacing)));
                    var alongStart = EdgeInsetMeters;
                    var alongEnd = (delta.x != 0 ? size.z : size.x) - EdgeInsetMeters;
                    var span = alongEnd - alongStart;

                    for (var s = 0; s < count; s++)
                    {
                        var t = (s + 0.5f) / count;
                        var along = alongStart + span * t + (float)(rng.NextDouble() - 0.5) * 6f;
                        var wx = origin.x + (delta.x != 0 ? (delta.x > 0 ? size.x : 0f) : along);
                        var wz = origin.z + (delta.y != 0 ? (delta.y > 0 ? size.z : 0f) : along);
                        var wy = tile.SampleHeight(new Vector3(wx, 0f, wz)) + origin.y;

                        var key = QuantizeKey(wx, wz);
                        if (!seen.Add(key))
                            continue;

                        var slope = SampleSlope(tile, wx, wz);
                        var heightGap = Mathf.Abs(
                            tile.SampleHeight(new Vector3(wx, 0f, wz)) + origin.y -
                            (neighbor.SampleHeight(new Vector3(wx, 0f, wz)) + neighbor.transform.position.y));
                        if (slope < minSlope && !isPeakFoothill && !(floatingIslandEdges && heightGap > 0.35f))
                            continue;

                        slots.Add(new SeamDressSlot
                        {
                            Position = new Vector3(wx, wy, wz),
                            Normal = EstimateEdgeNormal(tile, neighbor, delta),
                            Slope = slope,
                            PeakFoothill = isPeakFoothill || (floatingIslandEdges && heightGap > 1.2f),
                        });
                    }
                }
            }

            return slots;
        }

        static bool IsPeakFoothillPair(Terrain a, Terrain b)
        {
            var aPeak = a.name.StartsWith(SurfaceTerrainTileExpansion.MountainPeakTileNamePrefix, StringComparison.Ordinal);
            var bPeak = b.name.StartsWith(SurfaceTerrainTileExpansion.MountainPeakTileNamePrefix, StringComparison.Ordinal);
            var aFh = a.name.StartsWith(SurfaceTerrainTileExpansion.MountainFoothillTileNamePrefix, StringComparison.Ordinal);
            var bFh = b.name.StartsWith(SurfaceTerrainTileExpansion.MountainFoothillTileNamePrefix, StringComparison.Ordinal);
            return (aPeak && bFh) || (aFh && bPeak);
        }

        static bool IsOpenWorldEdge(Terrain a, Terrain b) =>
            a.name.StartsWith(SurfaceTerrainTileExpansion.OpenWorldTileNamePrefix, StringComparison.Ordinal) ||
            b.name.StartsWith(SurfaceTerrainTileExpansion.OpenWorldTileNamePrefix, StringComparison.Ordinal);

        static bool TryFindTileAtOffset(Terrain mainTerrain, Vector2Int off, out Terrain tile)
        {
            tile = null;
            if (off == Vector2Int.zero)
            {
                tile = mainTerrain;
                return tile != null;
            }

            if (SurfaceTerrainTileExpansion.TryFindTileAtOffset(
                    SurfaceTerrainTileExpansion.FindTilesRootPublic(mainTerrain),
                    off,
                    out tile) &&
                tile != null)
                return true;

            var wildernessRoot = SurfaceTerrainTileExpansion.FindMountainWildernessRootPublic(mainTerrain);
            if (wildernessRoot == null)
                return false;

            foreach (var prefix in new[]
                     {
                         SurfaceTerrainTileExpansion.MountainFoothillTileNamePrefix,
                         SurfaceTerrainTileExpansion.MountainPeakTileNamePrefix,
                         SurfaceTerrainTileExpansion.MountainHorizonTileNamePrefix,
                         SurfaceTerrainTileExpansion.OpenWorldTileNamePrefix,
                     })
            {
                var expected = $"{prefix}{off.x}_{off.y}";
                for (var c = 0; c < wildernessRoot.childCount; c++)
                {
                    var child = wildernessRoot.GetChild(c);
                    if (child.name != expected)
                        continue;

                    tile = child.GetComponent<Terrain>();
                    return tile?.terrainData != null;
                }
            }

            return false;
        }

        static long QuantizeKey(float x, float z) =>
            ((long)Mathf.Round(x * 0.5f) << 32) | (uint)Mathf.Round(z * 0.5f);

        static float SampleSlope(Terrain terrain, float wx, float wz)
        {
            const float sample = 2.5f;
            var h = terrain.SampleHeight(new Vector3(wx, 0f, wz));
            var hx = terrain.SampleHeight(new Vector3(wx + sample, 0f, wz));
            var hz = terrain.SampleHeight(new Vector3(wx, 0f, wz + sample));
            var rise = Mathf.Max(Mathf.Abs(hx - h), Mathf.Abs(hz - h));
            return Mathf.Clamp01(rise / (sample * 0.55f));
        }

        static Vector3 EstimateEdgeNormal(Terrain tile, Terrain neighbor, Vector2Int delta)
        {
            var lateral = new Vector3(delta.x, 0f, delta.y);
            if (lateral.sqrMagnitude < 0.01f)
                return Vector3.up;

            var edge = tile.transform.position;
            var size = tile.terrainData.size;
            var mid = edge + new Vector3(
                delta.x != 0 ? (delta.x > 0 ? size.x : 0f) : size.x * 0.5f,
                0f,
                delta.y != 0 ? (delta.y > 0 ? size.z : 0f) : size.z * 0.5f);
            var h0 = tile.SampleHeight(mid) + edge.y;
            var h1 = neighbor.SampleHeight(mid) + neighbor.transform.position.y;
            var rise = new Vector3(0f, h1 - h0, 0f);
            return Vector3.Normalize(Vector3.up * 0.65f + lateral.normalized * 0.25f + rise.normalized * 0.1f);
        }

        static void SpawnDressingProp(Transform root, GameObject prefab, SeamDressSlot slot, System.Random rng)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            if (instance == null)
                return;

            CaveEditorUndo.RegisterCreated(instance, "Seam dressing prop");
            var scale = slot.PeakFoothill
                ? 0.85f + (float)rng.NextDouble() * 0.55f
                : 0.65f + (float)rng.NextDouble() * 0.45f;
            instance.transform.position = slot.Position;
            instance.transform.rotation = Quaternion.FromToRotation(Vector3.up, slot.Normal) *
                                          Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            instance.transform.localScale = Vector3.one * scale;
            instance.name = $"SeamDress_{prefab.name}";
        }

        static Transform EnsureDressingRoot(Transform surfaceRoot)
        {
            if (surfaceRoot == null)
                return null;

            var existing = surfaceRoot.Find(SeamDressingRootName);
            if (existing != null)
                return existing;

            var go = new GameObject(SeamDressingRootName);
            go.transform.SetParent(surfaceRoot, false);
            CaveEditorUndo.RegisterCreated(go, SeamDressingRootName);
            return go.transform;
        }

        static List<GameObject> LoadDressingPrefabs()
        {
            var list = new List<GameObject>();
            var tokens = new[]
            {
                "rock01.prefab", "rock02.prefab", "rock03.prefab",
                "rock04.prefab", "rock05.prefab", "BackRock.prefab",
            };

            foreach (var token in tokens)
            {
                foreach (var guid in AssetDatabase.FindAssets($"{Path.GetFileNameWithoutExtension(token)} t:Prefab"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path) || !path.EndsWith(token, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && !list.Contains(prefab))
                        list.Add(prefab);
                }
            }

            return list;
        }

        struct SeamDressSlot
        {
            public Vector3 Position;
            public Vector3 Normal;
            public float Slope;
            public bool PeakFoothill;
        }
    }
}
#endif
