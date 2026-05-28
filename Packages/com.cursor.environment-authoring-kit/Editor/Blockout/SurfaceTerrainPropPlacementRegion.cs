#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Locks SurfaceTerrainMain + neighbor tiles for vegetation scatter (one world center, grid + trails).
    /// </summary>
    static class SurfaceTerrainPropPlacementRegion
    {
        public const string TerrainLockRel =
            "Assets/EnvironmentKit/Generated/SurfacePropTerrainLock.json";

        public const string PlacementGradeRel =
            "Assets/EnvironmentKit/Generated/SurfacePropPlacementGrade.json";

        [Serializable]
        public class TerrainLockEntry
        {
            public string terrainName;
            public float originX;
            public float originZ;
            public float sizeX;
            public float sizeZ;
            public bool isMain;
        }

        [Serializable]
        public class TerrainLockFile
        {
            public string generatedUtc;
            public int seed;
            public int terrainTileCount;
            public float playCenterX;
            public float playCenterY;
            public float playCenterZ;
            public float extentMeters;
            public TerrainLockEntry[] terrains;
        }

        [Serializable]
        public class CategoryGradeEntry
        {
            public string category;
            public int targetCount;
            public int slotCount;
            public int catalogPoolSize;
            public int gradeScore;
            public bool passed;
            public string note;
        }

        [Serializable]
        public class PlacementGradeFile
        {
            public string generatedUtc;
            public int terrainTileCount;
            public bool catalogReady;
            public int overallScore;
            public bool buildAcceptable;
            public CategoryGradeEntry[] categories;
        }

        static TerrainLockFile _activeLock;

        public static TerrainLockFile ActiveLock => _activeLock;

        public static TerrainLockFile LockAndMarkSurfaceTerrains(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request)
        {
            _activeLock = null;
            if (mainTerrain == null || ground == null || !ground.HasAnchor)
                return null;

            var playCenter = SurfaceTerrainPlayRegion.ResolveUnifiedPlayCenter(ground, mainTerrain);
            var requestExtent = request?.SurfaceExtentMeters > 10f ? request.SurfaceExtentMeters : 220f;
            var extent = SurfaceTerrainPlayRegion.ResolveUnifiedSurfaceExtent(
                mainTerrain,
                playCenter,
                requestExtent);

            var entries = new List<TerrainLockEntry>();
            foreach (var terrain in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain))
            {
                if (terrain?.terrainData == null)
                    continue;

                var origin = terrain.transform.position;
                var size = terrain.terrainData.size;
                entries.Add(new TerrainLockEntry
                {
                    terrainName = terrain.name,
                    originX = origin.x,
                    originZ = origin.z,
                    sizeX = size.x,
                    sizeZ = size.z,
                    isMain = terrain == mainTerrain,
                });

                MarkTerrainForProps(terrain);
            }

            _activeLock = new TerrainLockFile
            {
                generatedUtc = DateTime.UtcNow.ToString("o"),
                seed = request?.Seed ?? 0,
                terrainTileCount = entries.Count,
                playCenterX = playCenter.x,
                playCenterY = playCenter.y,
                playCenterZ = playCenter.z,
                extentMeters = extent,
                terrains = entries.ToArray(),
            };

            WriteJson(TerrainLockRel, _activeLock);
            ClearStalePlacementPlanFiles();
            CaveBuildEditorLog.LogSurface(
                $"[Surface] Prop terrain lock — {entries.Count} terrain(s) marked for placement (extent {extent:F0}m).",
                forceUnityConsole: true);
            return _activeLock;
        }

        static void ClearStalePlacementPlanFiles()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var generated = Path.Combine(hub, "Assets/EnvironmentKit/Generated");
            if (!Directory.Exists(generated))
                return;

            foreach (var file in Directory.GetFiles(generated, "SurfacePropPlacementPlan*.json"))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Best-effort — stale plan must not force a vertical cluster.
                }
            }
        }

        /// <summary>Contract target per terrain tile (× tile count for world total).</summary>
        public static int TargetPerTile(SurfacePropCategory category) =>
            category switch
            {
                SurfacePropCategory.Trees => 58,
                SurfacePropCategory.Grass => 240,
                SurfacePropCategory.Bushes => 148,
                SurfacePropCategory.GroundCover => 172,
                _ => 28,
            };

        /// <summary>Hard minimum placements on each locked tile before the pass is acceptable.</summary>
        public static int MinPlacementsPerTile(SurfacePropCategory category) =>
            category switch
            {
                SurfacePropCategory.Trees => 42,
                SurfacePropCategory.Grass => 170,
                SurfacePropCategory.Bushes => 108,
                SurfacePropCategory.GroundCover => 122,
                _ => 22,
            };

        public static int TargetCountForCategory(SurfacePropCategory category, int terrainTileCount) =>
            TargetPerTile(category) * Mathf.Max(1, terrainTileCount);

        /// <summary>Grid spacing inside each terrain tile (not diluted by tile count).</summary>
        public static float PerTileGridSpacing(SurfacePropCategory category) =>
            category switch
            {
                SurfacePropCategory.Trees => 10f,
                SurfacePropCategory.Grass => 3.8f,
                SurfacePropCategory.Bushes => 5.2f,
                SurfacePropCategory.GroundCover => 4.6f,
                _ => 8f,
            };

        public static float SpacingForCategory(SurfacePropCategory category, int terrainTileCount)
        {
            var tiles = Mathf.Max(1, terrainTileCount);
            return PerTileGridSpacing(category) / Mathf.Sqrt(tiles);
        }

        /// <summary>Minimum total vegetation instances for a full 9-tile world (all categories combined).</summary>
        public static int MinimumSceneVegetationInstances(int terrainTileCount) =>
            Mathf.Max(1, terrainTileCount) * 55;

        /// <summary>Minimum instances on one terrain tile (any category) for spread audit.</summary>
        public static int MinimumInstancesPerTerrainTile(int terrainTileCount) =>
            terrainTileCount >= 9 ? 42 : 28;

        public static void CollectPlacementSlotsForCategory(
            Terrain mainTerrain,
            Transform trailsRoot,
            Vector3 playCenter,
            float extentMeters,
            int seed,
            SurfacePropCategory category,
            List<SurfaceIntelligentPropPlacer.PlacementSlot> slots)
        {
            slots ??= new List<SurfaceIntelligentPropPlacer.PlacementSlot>();
            slots.Clear();

            if (mainTerrain == null)
                return;

            var tileCount = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain).Count;
            var vegPass = category switch
            {
                SurfacePropCategory.Trees => SurfaceIntelligentPropPlacer.VegetationPass.TreesFocus,
                SurfacePropCategory.Bushes => SurfaceIntelligentPropPlacer.VegetationPass.Understory,
                _ => SurfaceIntelligentPropPlacer.VegetationPass.Mixed,
            };

            SurfaceIntelligentPropPlacer.AppendTrailSlots(
                slots,
                trailsRoot,
                playCenter,
                extentMeters,
                seed + (int)category * 131,
                vegPass,
                mainTerrain);

            AppendTerrainGridSlots(slots, mainTerrain, playCenter, extentMeters, seed, category, vegPass);

            var dedupeFactor = category is SurfacePropCategory.Grass or SurfacePropCategory.GroundCover
                ? 0.24f
                : 0.30f;
            SurfaceIntelligentPropPlacer.DedupeSlots(
                slots,
                minSeparationMeters: PerTileGridSpacing(category) * dedupeFactor);
        }

        static void AppendTerrainGridSlots(
            List<SurfaceIntelligentPropPlacer.PlacementSlot> slots,
            Terrain mainTerrain,
            Vector3 playCenter,
            float extentMeters,
            int seed,
            SurfacePropCategory category,
            SurfaceIntelligentPropPlacer.VegetationPass vegPass)
        {
            var terrains = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            var tileCount = terrains.Count;
            var spacing = PerTileGridSpacing(category);
            var rng = new System.Random(seed + (int)category * 7919 + 17);
            const int maxSlotsPerCategory = 8000;
            var perTileTarget = TargetPerTile(category);
            var maxSlotsPerTile = Mathf.Max(perTileTarget * 2, 180);

            foreach (var terrain in terrains)
            {
                if (slots.Count >= maxSlotsPerCategory)
                    break;

                if (terrain?.terrainData == null)
                    continue;

                var origin = terrain.transform.position;
                var size = terrain.terrainData.size;
                const float edgeInset = 0.04f;
                var minX = origin.x + size.x * edgeInset;
                var maxX = origin.x + size.x * (1f - edgeInset);
                var minZ = origin.z + size.z * edgeInset;
                var maxZ = origin.z + size.z * (1f - edgeInset);
                var cols = Mathf.Max(3, Mathf.FloorToInt(size.x / spacing));
                var rows = Mathf.Max(3, Mathf.FloorToInt(size.z / spacing));
                var stepX = (maxX - minX) / cols;
                var stepZ = (maxZ - minZ) / rows;
                var addedForTile = 0;

                for (var row = 0; row <= rows; row++)
                {
                    if (slots.Count >= maxSlotsPerCategory)
                        break;
                    if (addedForTile >= maxSlotsPerTile)
                        break;

                    for (var col = 0; col <= cols; col++)
                    {
                        if (slots.Count >= maxSlotsPerCategory)
                            break;
                        if (addedForTile >= maxSlotsPerTile)
                            break;

                        var jx = ((float)rng.NextDouble() - 0.5f) * spacing * 0.35f;
                        var jz = ((float)rng.NextDouble() - 0.5f) * spacing * 0.35f;
                        var wx = minX + col * stepX + jx;
                        var wz = minZ + row * stepZ + jz;

                        if (!SurfaceTerrainPlayRegion.ContainsWorldXZ(terrain, wx, wz))
                            continue;

                        if (!IsWalkableSlope(terrain, new Vector3(wx, playCenter.y, wz)))
                            continue;

                        var candidate = new Vector3(wx, playCenter.y, wz);
                        if (category == SurfacePropCategory.Trees &&
                            SurfaceIntelligentPropPlacer.IsNearCaveEntrance(candidate, minDistanceMeters: 32f))
                            continue;

                        var sector = (col + row * 3 + terrain.name.GetHashCode()) & 0x7fff;
                        slots.Add(new SurfaceIntelligentPropPlacer.PlacementSlot
                        {
                            Position = candidate,
                            SectorIndex = sector,
                            SpacingM = spacing,
                            YawDeg = (seed + sector * 29) % 360,
                            Scale = vegPass == SurfaceIntelligentPropPlacer.VegetationPass.TreesFocus
                                ? 0.88f + (sector % 5) * 0.04f
                                : 0.62f + (sector % 4) * 0.05f,
                            Category = SurfaceIntelligentPropPlacer.CategoryForSector(sector, vegPass),
                            TerrainName = terrain.name,
                        });
                        addedForTile++;
                    }
                }
            }
        }

        static bool IsWalkableSlope(Terrain terrain, Vector3 world)
        {
            if (terrain?.terrainData == null)
                return false;

            var local = world - terrain.transform.position;
            var size = terrain.terrainData.size;
            if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z)
                return false;

            var nx = local.x / size.x;
            var nz = local.z / size.z;
            return terrain.terrainData.GetSteepness(nx, nz) < 32f;
        }

        public static PlacementGradeFile GradePlacementPlan(
            Terrain mainTerrain,
            Transform surfaceRoot,
            SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog catalog,
            Vector3 playCenter,
            float extentMeters,
            int seed)
        {
            if (SurfaceTerrainAiPhases.IsPipelineActive ||
                CaveBuildPhasePromptBridge.RequiresNonBlockingTsx)
                return GradePlacementPlanFast(mainTerrain, surfaceRoot, catalog, playCenter, extentMeters, seed);

            var lockFile = _activeLock;
            var tileCount = lockFile?.terrainTileCount ??
                              SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain).Count;
            var trails = surfaceRoot != null ? surfaceRoot.Find(SurfaceWorldPaths.TrailsName) : null;
            catalog ??= SurfaceIntelligentPropPlacer.LoadVegetationCatalog();

            var categories = new List<CategoryGradeEntry>();
            var scores = new List<int>();
            var cats = new[]
            {
                SurfacePropCategory.Trees,
                SurfacePropCategory.Grass,
                SurfacePropCategory.Bushes,
                SurfacePropCategory.GroundCover,
            };

            var scratch = new List<SurfaceIntelligentPropPlacer.PlacementSlot>();
            foreach (var cat in cats)
            {
                CollectPlacementSlotsForCategory(
                    mainTerrain,
                    trails,
                    playCenter,
                    extentMeters,
                    seed,
                    cat,
                    scratch);

                var pool = SurfaceIntelligentPropPlacer.PoolSizeForCategory(catalog, cat);
                var target = TargetCountForCategory(cat, tileCount);
                var slotOk = scratch.Count >= Mathf.Max(8, Mathf.RoundToInt(target * 0.9f));
                var catalogOk = pool > 0;
                var grade = 58;
                if (!catalogOk)
                    grade = 52;
                else if (slotOk && scratch.Count >= target)
                    grade = 96;
                else if (slotOk)
                    grade = 84;
                else if (scratch.Count > 0)
                    grade = 72;

                categories.Add(new CategoryGradeEntry
                {
                    category = cat.ToString(),
                    targetCount = target,
                    slotCount = scratch.Count,
                    catalogPoolSize = pool,
                    gradeScore = grade,
                    passed = catalogOk && slotOk,
                    note = catalogOk
                        ? $"{scratch.Count} slots for {target} targets across {tileCount} terrain(s)."
                        : "No prefabs in catalog for category.",
                });
                scores.Add(grade);
            }

            var sum = 0;
            for (var i = 0; i < scores.Count; i++)
                sum += scores[i];
            var overall = scores.Count > 0 ? sum / scores.Count : 0;
            var gradeFile = new PlacementGradeFile
            {
                generatedUtc = DateTime.UtcNow.ToString("o"),
                terrainTileCount = tileCount,
                catalogReady = catalog != null && catalog.HasAny,
                overallScore = overall,
                buildAcceptable = overall >= 75 && catalog != null && catalog.HasAny,
                categories = categories.ToArray(),
            };

            WriteJson(PlacementGradeRel, gradeFile);
            return gradeFile;
        }

        /// <summary>Fast prop plan during active build — no full heightmap grid scan (prevents editor freeze).</summary>
        static PlacementGradeFile GradePlacementPlanFast(
            Terrain mainTerrain,
            Transform surfaceRoot,
            SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog catalog,
            Vector3 playCenter,
            float extentMeters,
            int seed)
        {
            var lockFile = _activeLock;
            var tileCount = lockFile?.terrainTileCount ??
                              SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain).Count;
            catalog ??= SurfaceIntelligentPropPlacer.LoadVegetationCatalog();

            var categories = new List<CategoryGradeEntry>();
            var scores = new List<int>();
            var cats = new[]
            {
                SurfacePropCategory.Trees,
                SurfacePropCategory.Grass,
                SurfacePropCategory.Bushes,
                SurfacePropCategory.GroundCover,
            };

            foreach (var cat in cats)
            {
                var pool = SurfaceIntelligentPropPlacer.PoolSizeForCategory(catalog, cat);
                var target = TargetCountForCategory(cat, tileCount);
                var estSlots = EstimateSlotCapacity(extentMeters, tileCount, cat);
                var catalogOk = pool > 0;
                var slotOk = estSlots >= Mathf.Max(8, target * 2 / 3);
                var grade = !catalogOk ? 52 : slotOk && estSlots >= target ? 92 : slotOk ? 82 : 70;

                categories.Add(new CategoryGradeEntry
                {
                    category = cat.ToString(),
                    targetCount = target,
                    slotCount = estSlots,
                    catalogPoolSize = pool,
                    gradeScore = grade,
                    passed = catalogOk && slotOk,
                    note =
                        $"Fast plan (no grid scan): ~{estSlots} est. slots for {target} targets on {tileCount} terrain(s).",
                });
                scores.Add(grade);
            }

            var sum = 0;
            for (var i = 0; i < scores.Count; i++)
                sum += scores[i];
            var overall = scores.Count > 0 ? sum / scores.Count : 0;
            var gradeFile = new PlacementGradeFile
            {
                generatedUtc = DateTime.UtcNow.ToString("o"),
                terrainTileCount = tileCount,
                catalogReady = catalog != null && catalog.HasAny,
                overallScore = overall,
                buildAcceptable = overall >= 70 && catalog != null && catalog.HasAny,
                categories = categories.ToArray(),
            };

            WriteJson(PlacementGradeRel, gradeFile);
            return gradeFile;
        }

        static int EstimateSlotCapacity(float extentMeters, int terrainTileCount, SurfacePropCategory category)
        {
            var tiles = Mathf.Max(1, terrainTileCount);
            var perTile = TargetPerTile(category) * 2;
            return perTile * tiles;
        }

        /// <summary>True when Vegetation exists on every locked tile at contract density.</summary>
        public static bool IsNineTileVegetationSufficient(Transform vegRoot, Terrain mainTerrain)
        {
            if (vegRoot == null || mainTerrain == null)
                return false;

            var terrains = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            var tileCount = terrains.Count;
            if (tileCount <= 0)
                return false;

            if (vegRoot.childCount < MinimumSceneVegetationInstances(tileCount))
                return false;

            var minOnTile = MinimumInstancesPerTerrainTile(tileCount);
            foreach (var terrain in terrains)
            {
                if (terrain == null)
                    continue;

                if (CountVegetationOnTerrain(vegRoot, terrain) < minOnTile)
                    return false;
            }

            return true;
        }

        public static int CountVegetationOnTerrain(Transform vegRoot, Terrain terrain)
        {
            if (vegRoot == null || terrain?.terrainData == null)
                return 0;

            var origin = terrain.transform.position;
            var size = terrain.terrainData.size;
            var maxX = origin.x + size.x;
            var maxZ = origin.z + size.z;
            var n = 0;
            foreach (Transform child in vegRoot)
            {
                if (child == null)
                    continue;

                var w = child.position;
                if (w.x >= origin.x && w.x <= maxX && w.z >= origin.z && w.z <= maxZ)
                    n++;
            }

            return n;
        }

        static void WriteJson(string rel, object data)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, rel.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonUtility.ToJson(data, true));
        }

        public static void ResetLock() => _activeLock = null;

        const string PropMarkChildName = "PropPlacementMarked";

        static void MarkTerrainForProps(Terrain terrain)
        {
            if (terrain == null)
                return;

            foreach (var tag in InternalEditorUtility.tags)
            {
                if (!string.Equals(tag, "Ground", StringComparison.Ordinal))
                    continue;

                terrain.gameObject.tag = "Ground";
                break;
            }

            var existing = terrain.transform.Find(PropMarkChildName);
            if (existing != null)
                return;

            var mark = new GameObject(PropMarkChildName);
            CaveEditorUndo.RegisterCreated(mark, "Mark terrain for prop placement");
            mark.transform.SetParent(terrain.transform, false);
            mark.transform.localPosition = Vector3.zero;
        }

        public static bool IsMarkedForProps(Terrain terrain) =>
            terrain != null && terrain.transform.Find(PropMarkChildName) != null;
    }
}
#endif
