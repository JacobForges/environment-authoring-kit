#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Places surface trees/bushes/flowers from project prefabs using trail sectors + terrain snap (not random cave scatter).
    /// Writes per-pass placement plan JSON for Cursor agent review.
    /// </summary>
    public static class SurfaceIntelligentPropPlacer
    {
        public const string VegetationLayerName = SurfaceWorldPaths.VegetationName;
        public const string PlacementPlanRel =
            "Assets/EnvironmentKit/Generated/SurfacePropPlacementPlan.json";

        public const string PlacementPlanByCategoryRel =
            "Assets/EnvironmentKit/Generated/SurfacePropPlacementPlan_{0}.json";

        static readonly string[] TreeKeywords = { "tree", "pine", "oak", "palm", "cedar", "birch" };
        static readonly string[] BushKeywords = { "bush", "shrub", "hedge", "fern" };
        static readonly string[] GrassKeywords = { "grass", "meadow", "lawn", "clover", "reed", "hay" };
        static readonly string[] FlowerKeywords = { "flower", "bloom", "rose", "lily", "plant", "moss" };
        static readonly string[] RockKeywords =
            { "rock", "boulder", "stone", "rubble", "debris", "cliff", "granite", "pebble" };
        static readonly string[] ExcludeKeywords =
        {
            "house", "building", "wall", "door", "window", "fence", "lamp", "street", "sign", "crate_ui",
            "portal", "teleport", "warp",
        };

        public enum VegetationPass
        {
            Mixed = 0,
            TreesFocus = 1,
            Understory = 2,
        }

        /// <summary>Import all vegetation prefabs once per build (avoids 4× blocking import during prop pass).</summary>
        public static void ImportCatalogPrefabsOnce(SurfaceVegetationCatalog catalog)
        {
            if (catalog == null || !catalog.HasAny)
                return;

            var all = new List<GameObject>();
            catalog.AppendAllTo(all);
            var ready = 0;
            foreach (var prefab in all)
            {
                if (prefab == null)
                    continue;
                var path = AssetDatabase.GetAssetPath(prefab);
                if (!string.IsNullOrEmpty(path) && AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                    ready++;
            }

            CaveBuildEditorLog.LogSurface(
                $"[Surface] Vegetation catalog {ready}/{all.Count} prefabs ready — skipped bulk ForceSynchronousImport (avoids LPMagicalForest freeze).",
                forceUnityConsole: true);
        }

        public const int DefaultPropsPerEditorChunk = 48;

        /// <summary>Paced surface prop pass — survives across editor queue ticks.</summary>
        public sealed class CategoryPlacementSession
        {
            public SurfacePropCategory Category;
            public List<PlacementSlot> OrderedSlots = new();
            public int Cursor;
            public int Placed;
            public int MaxCount;
            public int TileCount;
            public HashSet<int> UsedSlotIndices = new();
            public Dictionary<string, int> PerTileCounts = new(StringComparer.Ordinal);
            public List<PlacementPlanEntry> PlanEntries = new();
            public List<GameObject> Pool = new();
            public VegetationPass VegPass;
            public bool Finalized;

            public bool IsComplete => Finalized || Placed >= MaxCount || Cursor >= OrderedSlots.Count;
        }

        /// <summary>Terrain ladder: place one prop category at a time from scanned project prefabs.</summary>
        public static bool TryPlaceCategoryLadderPass(
            Transform surfaceRoot,
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            SurfacePropCategory category,
            out string message) =>
            TryPlaceCategoryLadderPass(
                surfaceRoot, terrain, groundCenter, extentMeters, seed, category, null, out message);

        public static bool TryPlaceCategoryLadderPass(
            Transform surfaceRoot,
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            SurfacePropCategory category,
            SurfaceVegetationCatalog catalog,
            out string message) =>
            TryPlaceCategoryLadderPass(
                surfaceRoot, terrain, groundCenter, extentMeters, seed, category, catalog, null, out message);

        public static bool TryPlaceCategoryLadderPass(
            Transform surfaceRoot,
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            SurfacePropCategory category,
            SurfaceVegetationCatalog catalog,
            CategoryPlacementSession session,
            out string message)
        {
            message = string.Empty;
            if (surfaceRoot == null || terrain == null)
            {
                message = "No surface root or terrain.";
                return false;
            }

            if (!TryBeginCategoryPlacementSession(
                    surfaceRoot,
                    terrain,
                    groundCenter,
                    extentMeters,
                    seed,
                    category,
                    catalog,
                    session,
                    out session,
                    out var vegRoot,
                    out _))
            {
                message = session == null ? "Could not start prop placement session." : "No placement slots.";
                return false;
            }

            while (!session.IsComplete)
            {
                if (!TryPlaceCategoryLadderPassChunk(
                        terrain,
                        vegRoot,
                        seed,
                        category,
                        session,
                        int.MaxValue,
                        out _))
                    break;
            }

            return TryFinalizeCategoryPlacementSession(
                terrain,
                vegRoot,
                category,
                session,
                out message);
        }

        public static bool TryBeginCategoryPlacementSession(
            Transform surfaceRoot,
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            SurfacePropCategory category,
            SurfaceVegetationCatalog catalog,
            CategoryPlacementSession session,
            out CategoryPlacementSession activeSession,
            out Transform vegRoot,
            out string message)
        {
            message = string.Empty;
            activeSession = session;
            vegRoot = null;

            if (surfaceRoot == null || terrain == null)
            {
                message = "No surface root or terrain.";
                return false;
            }

            catalog ??= LoadVegetationCatalog();
            var pool = PoolForCategory(catalog, category);
            if (pool.Count == 0)
            {
                pool = category == SurfacePropCategory.Rocks
                    ? catalog.Rocks
                    : catalog.Mixed;
                if (pool.Count == 0)
                {
                    message = $"No prefabs for category {category}.";
                    return false;
                }
            }

            vegRoot = surfaceRoot.Find(VegetationLayerName);
            if (vegRoot == null)
            {
                var go = new GameObject(VegetationLayerName);
                CaveEditorUndo.RegisterCreated(go, "Surface vegetation root");
                go.transform.SetParent(surfaceRoot, false);
                vegRoot = go.transform;
            }

            var lockFile = SurfaceTerrainPropPlacementRegion.ActiveLock;
            var tileCount = lockFile?.terrainTileCount ??
                              SurfaceTerrainPlayRegion.CollectSurfaceTerrains(terrain).Count;
            var unifiedExtent = lockFile != null
                ? lockFile.extentMeters
                : SurfaceTerrainPlayRegion.ResolveUnifiedSurfaceExtent(terrain, groundCenter, extentMeters);
            var playCenter = lockFile != null
                ? new Vector3(lockFile.playCenterX, lockFile.playCenterY, lockFile.playCenterZ)
                : groundCenter;

            var needsRebuild = activeSession == null ||
                               activeSession.Finalized ||
                               activeSession.Category != category ||
                               activeSession.OrderedSlots == null ||
                               activeSession.OrderedSlots.Count == 0;
            if (needsRebuild)
            {
                activeSession = new CategoryPlacementSession
                {
                    Category = category,
                    TileCount = tileCount,
                    MaxCount = SurfaceTerrainPropPlacementRegion.TargetCountForCategory(category, tileCount),
                    VegPass = category switch
                    {
                        SurfacePropCategory.Trees => VegetationPass.TreesFocus,
                        SurfacePropCategory.Bushes => VegetationPass.Understory,
                        _ => VegetationPass.Mixed,
                    },
                    Pool = pool,
                    PerTileCounts = BuildPerTilePlacementCounts(lockFile, terrain),
                };

                var trails = surfaceRoot.Find(SurfaceWorldPaths.TrailsName);
                var slots = new List<PlacementSlot>();
                if (category == SurfacePropCategory.Rocks)
                {
                    SurfaceTerrainPropPlacementRegion.CollectPostCaveRockSlots(
                        terrain, playCenter, unifiedExtent, seed, slots);
                }
                else
                {
                    SurfaceTerrainPropPlacementRegion.CollectPlacementSlotsForCategory(
                        terrain, trails, playCenter, unifiedExtent, seed, category, slots);
                }

                if (slots.Count == 0)
                {
                    message = "No placement slots on locked surface terrains.";
                    return false;
                }

                OrderSlotsForFullAreaSpread(slots, terrain, seed + (int)category * 509);
                activeSession.OrderedSlots = slots;
                activeSession.Cursor = 0;
                activeSession.Placed = 0;
                activeSession.UsedSlotIndices.Clear();
                activeSession.PlanEntries.Clear();
                activeSession.Finalized = false;
            }

            activeSession.Pool = pool;
            return true;
        }

        public static bool TryPlaceCategoryLadderPassChunk(
            Terrain terrain,
            Transform vegRoot,
            int seed,
            SurfacePropCategory category,
            CategoryPlacementSession session,
            int maxPlacementsThisChunk,
            out int placedThisChunk)
        {
            placedThisChunk = 0;
            if (terrain == null || vegRoot == null || session == null || session.Finalized)
                return false;

            var pool = session.Pool;
            if (pool == null || pool.Count == 0)
                return false;

            var slots = session.OrderedSlots;
            var rng = new System.Random(seed + (int)category * 509 + session.Placed);
            var minPerTile = SurfaceTerrainPropPlacementRegion.MinPlacementsPerTile(category);
            var budget = maxPlacementsThisChunk < 1 ? int.MaxValue : maxPlacementsThisChunk;

            while (placedThisChunk < budget && session.Placed < session.MaxCount && session.Cursor < slots.Count)
            {
                if (!AllTilesMeetMinimum(session.PerTileCounts, minPerTile))
                {
                    if (TryPlaceNextMinimumTileSlot(
                            terrain, vegRoot, slots, pool, rng, category, session, minPerTile))
                    {
                        placedThisChunk++;
                        continue;
                    }
                }

                var slotIndex = session.Cursor++;
                var slot = slots[slotIndex];
                if (session.UsedSlotIndices.Contains(slotIndex))
                    continue;
                if (!SlotMatchesCategory(slot.Category, category))
                    continue;
                if (!TryPlaceSlot(
                        terrain,
                        vegRoot,
                        slot,
                        pool,
                        rng,
                        category,
                        session.PlanEntries,
                        out _,
                        out var resolvedTerrain))
                    continue;

                session.UsedSlotIndices.Add(slotIndex);
                if (!string.IsNullOrEmpty(resolvedTerrain) &&
                    session.PerTileCounts.ContainsKey(resolvedTerrain))
                    session.PerTileCounts[resolvedTerrain] = session.PerTileCounts[resolvedTerrain] + 1;

                session.Placed++;
                placedThisChunk++;

                if (session.Placed % 6 == 0)
                    CaveBuildLiveSceneFlushUtility.FlushWorldView(terrain);
            }

            if (session.Placed < session.MaxCount && session.Cursor >= slots.Count)
            {
                session.Placed += EnforcePerTileMinimum(
                    terrain,
                    vegRoot,
                    slots,
                    pool,
                    rng,
                    category,
                    minPerTile,
                    session.PerTileCounts,
                    session.UsedSlotIndices,
                    session.PlanEntries,
                    session.Placed,
                    session.MaxCount);
            }

            return placedThisChunk > 0 || !session.IsComplete;
        }

        static bool TryPlaceNextMinimumTileSlot(
            Terrain terrain,
            Transform vegRoot,
            List<PlacementSlot> slots,
            List<GameObject> pool,
            System.Random rng,
            SurfacePropCategory category,
            CategoryPlacementSession session,
            int minPerTile)
        {
            foreach (var terrainName in session.PerTileCounts.Keys)
            {
                if (session.PerTileCounts[terrainName] >= minPerTile)
                    continue;

                for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
                {
                    if (session.UsedSlotIndices.Contains(slotIndex))
                        continue;

                    var slot = slots[slotIndex];
                    if (!string.Equals(slot.TerrainName, terrainName, StringComparison.Ordinal))
                        continue;
                    if (!SlotMatchesCategory(slot.Category, category))
                        continue;
                    if (!TryPlaceSlot(
                            terrain,
                            vegRoot,
                            slot,
                            pool,
                            rng,
                            category,
                            session.PlanEntries,
                            out _,
                            out var resolvedTerrain))
                        continue;

                    session.UsedSlotIndices.Add(slotIndex);
                    var key = string.IsNullOrEmpty(resolvedTerrain) ? terrainName : resolvedTerrain;
                    if (session.PerTileCounts.ContainsKey(key))
                        session.PerTileCounts[key] = session.PerTileCounts[key] + 1;
                    session.Placed++;
                    return true;
                }
            }

            return false;
        }

        public static bool TryFinalizeCategoryPlacementSession(
            Terrain terrain,
            Transform vegRoot,
            SurfacePropCategory category,
            CategoryPlacementSession session,
            out string message)
        {
            message = string.Empty;
            if (session == null || session.Finalized)
                return false;

            var pool = session.Pool;
            if (session.Placed == 0 &&
                category is SurfacePropCategory.Trees or SurfacePropCategory.Grass or SurfacePropCategory.Bushes
                    or SurfacePropCategory.GroundCover)
            {
                TryPlaceFromExistingPlacementPlan(
                    terrain,
                    vegRoot,
                    category,
                    pool,
                    session.MaxCount,
                    session.PlanEntries,
                    out var fallbackPlaced);
                session.Placed = fallbackPlaced;
            }

            WritePlacementPlanForCategory(
                category,
                session.VegPass,
                session.PlanEntries,
                session.Placed,
                session.TileCount,
                session.MaxCount);
            if (vegRoot != null)
                EditorUtility.SetDirty(vegRoot.gameObject);

            session.Finalized = true;
            var minPerTile = SurfaceTerrainPropPlacementRegion.MinPlacementsPerTile(category);
            var tilesAtMin = 0;
            foreach (var count in session.PerTileCounts.Values)
            {
                if (count >= minPerTile)
                    tilesAtMin++;
            }

            message =
                $"Placed {session.Placed}/{session.MaxCount} {category} on {session.TileCount} terrain(s) " +
                $"({tilesAtMin}/{session.PerTileCounts.Count} tiles at ≥{minPerTile} each).";
            CaveBuildLiveSceneFlushUtility.FlushWorldView(terrain);
            return session.Placed > 0;
        }

        /// <summary>Surface rocks/boulders after cave geometry — uses post-cave grid only.</summary>
        public static bool TryPlacePostCaveSurfaceRocks(
            Transform surfaceRoot,
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            out string message)
        {
            if (CaveRouteProbeRunner.FindCaveRoot() == null)
            {
                message = "Post-cave rocks skipped — no cave in scene yet.";
                return false;
            }

            return TryPlaceCategoryLadderPass(
                surfaceRoot,
                terrain,
                groundCenter,
                extentMeters,
                seed + 90210,
                SurfacePropCategory.Rocks,
                out message);
        }

        /// <summary>Top-up pass after primary categories — fills toward 92% of contract using score-sorted slots.</summary>
        public static bool TryPolishCategoryDensityPass(
            Transform surfaceRoot,
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            SurfacePropCategory category,
            SurfaceVegetationCatalog catalog,
            out string message)
        {
            message = string.Empty;
            if (surfaceRoot == null || terrain == null)
                return false;

            catalog ??= LoadVegetationCatalog();
            var pool = PoolForCategory(catalog, category);
            if (pool.Count == 0)
            {
                message = $"Polish skip — no prefabs for {category}.";
                return false;
            }

            var vegRoot = surfaceRoot.Find(VegetationLayerName);
            if (vegRoot == null)
                return false;

            var lockFile = SurfaceTerrainPropPlacementRegion.ActiveLock;
            var tileCount = lockFile?.terrainTileCount ??
                              SurfaceTerrainPlayRegion.CollectSurfaceTerrains(terrain).Count;
            var maxCount = SurfaceTerrainPropPlacementRegion.TargetCountForCategory(category, tileCount);
            var target = Mathf.CeilToInt(maxCount * 0.98f);
            var existing = CountPlacedForCategory(vegRoot, category);
            if (existing >= target)
            {
                message = $"Polish {category}: already at {existing}/{target}.";
                return false;
            }

            var playCenter = lockFile != null
                ? new Vector3(lockFile.playCenterX, lockFile.playCenterY, lockFile.playCenterZ)
                : groundCenter;
            var unifiedExtent = lockFile != null
                ? lockFile.extentMeters
                : SurfaceTerrainPlayRegion.ResolveUnifiedSurfaceExtent(terrain, groundCenter, extentMeters);
            var trails = surfaceRoot.Find(SurfaceWorldPaths.TrailsName);
            var slots = new List<PlacementSlot>();
            SurfaceTerrainPropPlacementRegion.CollectPlacementSlotsForCategory(
                terrain, trails, playCenter, unifiedExtent, seed + 9001, category, slots);
            DedupeSlotsPreferGrid(slots, SurfaceTerrainPropPlacementRegion.PerTileGridSpacing(category) * 0.24f);
            OrderSlotsForFullAreaSpread(slots, terrain, seed + 8803);

            var rng = new System.Random(seed + (int)category * 1201);
            var planEntries = new List<PlacementPlanEntry>();
            var placed = 0;
            var tagPrefix = $"Surface_{category}_";

            foreach (var slot in slots)
            {
                if (existing + placed >= target)
                    break;
                if (!SlotMatchesCategory(slot.Category, category))
                    continue;
                if (!TryPlaceSlot(terrain, vegRoot, slot, pool, rng, category, planEntries, out _, out _))
                    continue;
                placed++;
            }

            if (placed > 0)
            {
                WritePlacementPlanForCategory(
                    category,
                    VegetationPass.Mixed,
                    planEntries,
                    existing + placed,
                    tileCount,
                    maxCount);
                EditorUtility.SetDirty(vegRoot.gameObject);
            }

            message = placed > 0
                ? $"Polish {category}: +{placed} (now {existing + placed}/{maxCount})."
                : $"Polish {category}: no additional slots ({existing}/{target}).";
            return placed > 0;
        }

        static int CountPlacedForCategory(Transform vegRoot, SurfacePropCategory category)
        {
            if (vegRoot == null)
                return 0;

            var prefix = $"Surface_{category}_";
            var count = 0;
            foreach (Transform child in vegRoot)
            {
                if (child != null && child.name.StartsWith(prefix, StringComparison.Ordinal))
                    count++;
            }

            return count;
        }

        public static void SortSlotsByPlacementScore(
            List<PlacementSlot> slots,
            Terrain terrain,
            Transform trailsRoot,
            Vector3 playCenter,
            SurfacePropCategory category,
            int seed)
        {
            if (slots == null || slots.Count < 2)
                return;

            var trailPoints = CollectTrailSamplePoints(trailsRoot);
            slots.Sort((a, b) =>
            {
                var scoreA = ScorePlacementSlot(a, terrain, trailPoints, playCenter, category, seed);
                var scoreB = ScorePlacementSlot(b, terrain, trailPoints, playCenter, category, seed);
                return scoreB.CompareTo(scoreA);
            });
        }

        static List<Vector3> CollectTrailSamplePoints(Transform trailsRoot)
        {
            var points = new List<Vector3>();
            if (trailsRoot == null)
                return points;

            foreach (Transform trail in trailsRoot)
            {
                if (trail == null)
                    continue;
                for (var i = 0; i < trail.childCount; i++)
                {
                    var c = trail.GetChild(i);
                    if (c != null && c.name.StartsWith("Waypoint", StringComparison.Ordinal))
                        points.Add(c.position);
                }
            }

            return points;
        }

        static int ScorePlacementSlot(
            PlacementSlot slot,
            Terrain terrain,
            List<Vector3> trailPoints,
            Vector3 playCenter,
            SurfacePropCategory category,
            int seed)
        {
            var score = slot.SectorIndex % 97;
            var pos = slot.Position;

            if (trailPoints != null && trailPoints.Count > 0)
            {
                var minTrail = float.MaxValue;
                foreach (var p in trailPoints)
                {
                    var dx = pos.x - p.x;
                    var dz = pos.z - p.z;
                    minTrail = Mathf.Min(minTrail, dx * dx + dz * dz);
                }

                // Favor a band near trails, not points hugging the spline itself.
                if (minTrail < 36f)
                    score -= 24;
                else if (minTrail < 196f)
                    score += 28;
                else if (minTrail < 1225f)
                    score += 12;
            }

            if (terrain != null &&
                SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(terrain, pos.x, pos.z, out var tile) &&
                tile?.terrainData != null)
            {
                var local = pos - tile.transform.position;
                var size = tile.terrainData.size;
                var nx = Mathf.Clamp01(local.x / size.x);
                var nz = Mathf.Clamp01(local.z / size.z);
                var steep = tile.terrainData.GetSteepness(nx, nz);
                if (steep < 18f)
                    score += 12;
                else if (steep > 28f)
                    score -= 18;
            }

            if (category == SurfacePropCategory.Trees && !IsNearCaveEntrance(pos, 36f))
                score += 16;

            if (category is SurfacePropCategory.Grass or SurfacePropCategory.GroundCover &&
                !IsNearCaveEntrance(pos, 14f))
                score += 8;

            var distCenter = Vector2.Distance(
                new Vector2(pos.x, pos.z),
                new Vector2(playCenter.x, playCenter.z));
            score += Mathf.RoundToInt(Mathf.Clamp(distCenter * 0.04f, 0f, 22f));

            score += (seed + slot.SectorIndex * 13) % 7;
            return score;
        }

        static Dictionary<string, int> BuildPerTilePlacementCounts(
            SurfaceTerrainPropPlacementRegion.TerrainLockFile lockFile,
            Terrain mainTerrain)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (lockFile?.terrains != null)
            {
                foreach (var entry in lockFile.terrains)
                {
                    if (!string.IsNullOrEmpty(entry.terrainName))
                        counts[entry.terrainName] = 0;
                }
            }

            if (counts.Count == 0 && mainTerrain != null)
            {
                foreach (var t in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain))
                {
                    if (t != null)
                        counts[t.name] = 0;
                }
            }

            return counts;
        }

        static bool AllTilesMeetMinimum(Dictionary<string, int> perTileCounts, int minPerTile)
        {
            if (perTileCounts == null || perTileCounts.Count == 0)
                return false;

            foreach (var count in perTileCounts.Values)
            {
                if (count < minPerTile)
                    return false;
            }

            return true;
        }

        static bool TryPlaceSlot(
            Terrain terrain,
            Transform vegRoot,
            PlacementSlot slot,
            List<GameObject> pool,
            System.Random rng,
            SurfacePropCategory category,
            List<PlacementPlanEntry> planEntries,
            out Vector3 world,
            out string terrainName)
        {
            world = Vector3.zero;
            terrainName = slot.TerrainName;
            var prefab = pool[rng.Next(pool.Count)];
            if (!TryPlacementOnSurfaceTerrain(terrain, slot.Position, out world, out var onTerrain))
                return false;

            terrainName = onTerrain != null ? onTerrain.name : slot.TerrainName;
            var local = vegRoot.InverseTransformPoint(world);
            var rot = Quaternion.Euler(0f, slot.YawDeg, 0f);
            var scale = Vector3.one * slot.Scale;
            var tag = $"Surface_{category}_{prefab.name}";
            if (!CavePrefabScatter.PlaceModule(vegRoot, prefab, local, rot, scale, tag, false))
                return false;

            planEntries.Add(new PlacementPlanEntry
            {
                prefabName = prefab.name,
                category = category.ToString(),
                worldX = world.x,
                worldY = world.y,
                worldZ = world.z,
                terrainName = terrainName,
                rationale =
                    $"Locked terrain grid/trail {category}: {terrainName}, sector {slot.SectorIndex}, spacing {slot.SpacingM:F0}m.",
                agentPrompt =
                    CaveBuildPhasePromptBridge.PhasePromptsIndexPath +
                    $" — place {prefab.name} as {category} on {terrainName} at ({world.x:F1},{world.z:F1}).",
            });
            return true;
        }

        static int EnforcePerTileMinimum(
            Terrain terrain,
            Transform vegRoot,
            List<PlacementSlot> slots,
            List<GameObject> pool,
            System.Random rng,
            SurfacePropCategory category,
            int minPerTile,
            Dictionary<string, int> perTileCounts,
            HashSet<int> usedSlotIndices,
            List<PlacementPlanEntry> planEntries,
            int placedSoFar,
            int maxCount)
        {
            if (terrain == null || vegRoot == null || slots == null || perTileCounts == null)
                return 0;

            var added = 0;
            foreach (var terrainName in new List<string>(perTileCounts.Keys))
            {
                while (perTileCounts[terrainName] < minPerTile && placedSoFar + added < maxCount)
                {
                    var placedOne = false;
                    for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
                    {
                        if (placedSoFar + added >= maxCount)
                            break;

                        var slot = slots[slotIndex];
                        if (!string.Equals(slot.TerrainName, terrainName, StringComparison.Ordinal))
                            continue;

                        if (!TryPlaceSlot(
                                terrain,
                                vegRoot,
                                slot,
                                pool,
                                rng,
                                category,
                                planEntries,
                                out _,
                                out var resolvedTerrain))
                            continue;

                        usedSlotIndices?.Add(slotIndex);
                        var key = string.IsNullOrEmpty(resolvedTerrain) ? terrainName : resolvedTerrain;
                        if (perTileCounts.ContainsKey(key))
                            perTileCounts[key] = perTileCounts[key] + 1;

                        added++;
                        placedOne = true;
                        break;
                    }

                    if (!placedOne)
                        break;
                }
            }

            return added;
        }

        static bool TryPlaceFromExistingPlacementPlan(
            Terrain terrain,
            Transform vegRoot,
            SurfacePropCategory category,
            List<GameObject> pool,
            int maxCount,
            List<PlacementPlanEntry> planEntries,
            out int placed)
        {
            placed = 0;
            if (terrain == null || vegRoot == null || pool == null || pool.Count == 0)
                return false;

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var rel = string.Format(PlacementPlanByCategoryRel, category.ToString().ToLowerInvariant());
            var path = Path.Combine(hub, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                return false;

            PlacementPlanFile wrapper;
            try
            {
                var json = File.ReadAllText(path);
                wrapper = JsonUtility.FromJson<PlacementPlanFile>(json);
            }
            catch
            {
                wrapper = null;
            }

            if (wrapper?.placements == null || wrapper.placements.Length == 0)
                return false;

            if (PlacementPlanLooksDegenerate(wrapper.placements))
                return false;

            var prefabByName = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in pool)
            {
                if (p == null)
                    continue;
                prefabByName[p.name] = p;
            }

            foreach (var entry in wrapper.placements)
            {
                if (placed >= maxCount)
                    break;
                if (entry == null || string.IsNullOrEmpty(entry.prefabName))
                    continue;

                if (!prefabByName.TryGetValue(entry.prefabName, out var prefab) || prefab == null)
                    continue;

                var candidate = new Vector3(entry.worldX, entry.worldY, entry.worldZ);
                if (!TryPlacementOnSurfaceTerrain(terrain, candidate, out var world, out var onTerrain))
                    continue;

                var local = vegRoot.InverseTransformPoint(world);
                var tag = $"Surface_{category}_{prefab.name}";
                if (!CavePrefabScatter.PlaceModule(vegRoot, prefab, local, Quaternion.identity, Vector3.one, tag, false))
                    continue;

                placed++;
                planEntries.Add(new PlacementPlanEntry
                {
                    prefabName = prefab.name,
                    category = category.ToString(),
                    worldX = world.x,
                    worldY = world.y,
                    worldZ = world.z,
                    terrainName = onTerrain != null ? onTerrain.name : entry.terrainName,
                    rationale = entry.rationale,
                    agentPrompt = entry.agentPrompt
                });
            }

            return placed > 0;
        }

        public static bool WritePlacementPlanBeforeExecute(
            Terrain mainTerrain,
            Transform surfaceRoot,
            Vector3 playCenter,
            float extentMeters,
            int seed,
            SurfaceVegetationCatalog catalog,
            out string message)
        {
            message = string.Empty;
            if (mainTerrain == null || surfaceRoot == null)
            {
                message = "Cannot plan props — missing terrain or surface root.";
                return false;
            }

            catalog ??= LoadVegetationCatalog();
            var grade = SurfaceTerrainPropPlacementRegion.GradePlacementPlan(
                mainTerrain,
                surfaceRoot,
                catalog,
                playCenter,
                extentMeters,
                seed);

            if (!SurfaceTerrainAiPhases.IsPipelineActive &&
                !CaveBuildPhasePromptBridge.RequiresNonBlockingTsx)
            {
                CaveBuildUnifiedPromptBridge.RefreshForPhase(
                    "surface_vegetation_intelligent",
                    "terrain_integration",
                    4,
                    52,
                    seed,
                    out _);
            }

            message =
                $"Prop plan graded {grade.overallScore}/100 across {grade.terrainTileCount} terrain(s) — lock: {SurfaceTerrainPropPlacementRegion.TerrainLockRel}.";
            return grade.buildAcceptable;
        }

        public static int PoolSizeForCategory(SurfaceVegetationCatalog catalog, SurfacePropCategory category) =>
            PoolForCategory(catalog, category).Count;

        static bool SlotMatchesCategory(string slotCategory, SurfacePropCategory category) =>
            category switch
            {
                SurfacePropCategory.Trees => slotCategory == "tree",
                SurfacePropCategory.Grass => slotCategory is "flower" or "ground_cover",
                SurfacePropCategory.Bushes => slotCategory == "bush",
                SurfacePropCategory.GroundCover => slotCategory is "flower" or "ground_cover",
                SurfacePropCategory.Rocks => slotCategory == "rock",
                _ => true,
            };

        /// <summary>
        /// Trail slot categories are lowercase strings; ladder grading counts <c>[Surface_{enum}_</c> in instance names.
        /// </summary>
        internal static SurfacePropCategory LadderCategoryForSlot(string slotCategory) =>
            slotCategory switch
            {
                "tree" => SurfacePropCategory.Trees,
                "bush" => SurfacePropCategory.Bushes,
                "ground_cover" => SurfacePropCategory.GroundCover,
                "flower" => SurfacePropCategory.Grass,
                _ => SurfacePropCategory.Grass,
            };

        internal static System.Collections.Generic.List<GameObject> PoolForCategory(
            SurfaceVegetationCatalog catalog,
            SurfacePropCategory category) =>
            category switch
            {
                SurfacePropCategory.Trees => catalog.Trees,
                SurfacePropCategory.Grass => catalog.Grass.Count > 0 ? catalog.Grass : catalog.Flowers,
                SurfacePropCategory.Bushes => catalog.Bushes,
                SurfacePropCategory.GroundCover => catalog.Flowers.Count > 0 ? catalog.Flowers : catalog.Bushes,
                SurfacePropCategory.Rocks => catalog.Rocks,
                _ => catalog.Mixed,
            };

        public static SurfaceVegetationCatalog LoadVegetationCatalog() => SurfaceVegetationCatalog.Load();

        public static bool TryPlaceVegetationPass(
            Transform surfaceRoot,
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            int pass,
            int seed,
            VegetationPass vegPass,
            out string message)
        {
            message = string.Empty;
            if (!CaveBuildWorkflowCoordinator.TryConsumeMeatSurfaceVegetationPass())
            {
                message = "Surface vegetation cap reached for this build.";
                return false;
            }

            if (surfaceRoot == null || terrain == null)
            {
                message = "No surface root or terrain.";
                return false;
            }

            var catalog = SurfaceVegetationCatalog.Load();
            if (!catalog.HasAny)
            {
                message = "No usable vegetation prefabs in project assets.";
                return false;
            }

            var vegRoot = surfaceRoot.Find(VegetationLayerName);
            if (vegRoot == null)
            {
                var go = new GameObject(VegetationLayerName);
                CaveEditorUndo.RegisterCreated(go, "Surface vegetation root");
                go.transform.SetParent(surfaceRoot, false);
                vegRoot = go.transform;
            }

            var trails = surfaceRoot.Find(SurfaceWorldPaths.TrailsName);
            var slots = new List<PlacementSlot>();
            SurfaceTerrainPropPlacementRegion.CollectPlacementSlotsForCategory(
                terrain,
                trails,
                groundCenter,
                extentMeters,
                seed,
                SurfacePropCategory.Mixed,
                slots);
            if (slots.Count == 0)
            {
                message = "No trail slots on terrain for vegetation.";
                return false;
            }

            var rng = new System.Random(seed + pass * 4177 + (int)vegPass * 97);
            var planEntries = new List<PlacementPlanEntry>();
            var placed = 0;
            var tileCount = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(terrain).Count;
            var maxThisPass = (vegPass == VegetationPass.TreesFocus ? 14 : vegPass == VegetationPass.Understory ? 18 : 12) *
                              Mathf.Max(1, tileCount);

            foreach (var slot in slots)
            {
                if (placed >= maxThisPass)
                    break;

                var prefab = PickPrefab(catalog, slot, vegPass, rng);
                if (prefab == null)
                    continue;

                if (!TryPlacementOnSurfaceTerrain(terrain, slot.Position, out var world, out _))
                    continue;

                var local = vegRoot.InverseTransformPoint(world);
                var rot = Quaternion.Euler(0f, slot.YawDeg, 0f);
                var scale = Vector3.one * slot.Scale;
                var ladderCat = LadderCategoryForSlot(slot.Category);
                var tag = $"Surface_{ladderCat}_{prefab.name}";
                if (!CavePrefabScatter.PlaceModule(vegRoot, prefab, local, rot, scale, tag, false))
                    continue;

                placed++;
                planEntries.Add(new PlacementPlanEntry
                {
                    prefabName = prefab.name,
                    category = slot.Category,
                    worldX = world.x,
                    worldY = world.y,
                    worldZ = world.z,
                    rationale =
                        $"Sector {slot.SectorIndex}: {slot.Category} along trail; spacing {slot.SpacingM:F0}m; " +
                        "align with satellite/hillshade green belt (research plan).",
                    agentPrompt =
                        $"Place {prefab.name} as {slot.Category} at ({world.x:F1},{world.z:F1}). " +
                        "Use ResearchCache hillshade + reference URLs; avoid buildings and water mesh.",
                });
            }

            WritePlacementPlan(pass, vegPass, planEntries, placed);
            EditorUtility.SetDirty(vegRoot.gameObject);
            message = $"Intelligent surface vegetation: {placed} props ({vegPass}, plan written).";
            CaveBuildPipelineLog.Info(message, "Surface-Meat");
            return placed > 0;
        }

        static bool TryPlacementOnSurfaceTerrain(
            Terrain mainTerrain,
            Vector3 candidate,
            out Vector3 world,
            out Terrain onTerrain)
        {
            world = candidate;
            onTerrain = null;
            if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(
                    mainTerrain,
                    candidate.x,
                    candidate.z,
                    out onTerrain))
                return false;

            world = SnapToTerrain(onTerrain, candidate);
            return true;
        }

        public static void AppendTrailSlots(
            List<PlacementSlot> slots,
            Transform trailsRoot,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            VegetationPass vegPass,
            Terrain mainTerrain)
        {
            if (slots == null || trailsRoot == null)
                return;

            CollectPlacementSlots(
                slots,
                trailsRoot,
                groundCenter,
                extentMeters,
                seed,
                vegPass,
                mainTerrain,
                includeRadialFallback: false);
        }

        internal static bool IsNearCaveEntrance(Vector3 world, float minDistanceMeters = 24f)
        {
            var caveRoot = CaveGeometryPaths.FindCaveSystemRoot();
            if (caveRoot != null)
            {
                var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot);
                if (mouth.sqrMagnitude > 0.01f)
                {
                    var dx = world.x - mouth.x;
                    var dz = world.z - mouth.z;
                    if (dx * dx + dz * dz < minDistanceMeters * minDistanceMeters)
                        return true;
                }
            }

            var envRoot = UnityEngine.Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            if (envRoot == null)
                return false;

            var surfaceRoot = envRoot.transform.Find(SurfaceWorldPaths.RootName);
            if (surfaceRoot == null)
                return false;

            var openings = surfaceRoot.Find(SurfaceWorldPaths.CaveOpeningsName);
            if (openings == null)
                return false;

            var minSq = minDistanceMeters * minDistanceMeters;
            foreach (Transform child in openings)
            {
                if (child == null)
                    continue;
                var p = child.position;
                var dx = world.x - p.x;
                var dz = world.z - p.z;
                if (dx * dx + dz * dz < minSq)
                    return true;
            }

            return false;
        }

        static void ShufflePlacementSlots(List<PlacementSlot> slots, int seed)
        {
            if (slots == null || slots.Count < 2)
                return;

            var rng = new System.Random(seed);
            for (var i = slots.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (slots[i], slots[j]) = (slots[j], slots[i]);
            }
        }

        static bool PlacementPlanLooksDegenerate(PlacementPlanEntry[] placements)
        {
            if (placements == null || placements.Length < 3)
                return false;

            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minZ = float.MaxValue;
            var maxZ = float.MinValue;
            var count = 0;
            foreach (var e in placements)
            {
                if (e == null)
                    continue;
                count++;
                minX = Mathf.Min(minX, e.worldX);
                maxX = Mathf.Max(maxX, e.worldX);
                minZ = Mathf.Min(minZ, e.worldZ);
                maxZ = Mathf.Max(maxZ, e.worldZ);
            }

            if (count < 3)
                return false;

            var spanX = maxX - minX;
            var spanZ = maxZ - minZ;
            return spanX < 3f || spanZ < 3f;
        }

        public static void DedupeSlots(List<PlacementSlot> slots, float minSeparationMeters)
        {
            if (slots == null || slots.Count < 2 || minSeparationMeters <= 0.1f)
                return;

            var kept = new List<PlacementSlot>(slots.Count);
            var minSq = minSeparationMeters * minSeparationMeters;
            foreach (var slot in slots)
            {
                var duplicate = false;
                for (var i = 0; i < kept.Count; i++)
                {
                    var dx = slot.Position.x - kept[i].Position.x;
                    var dz = slot.Position.z - kept[i].Position.z;
                    if (dx * dx + dz * dz < minSq)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                    kept.Add(slot);
            }

            slots.Clear();
            slots.AddRange(kept);
        }

        /// <summary>When two slots overlap, keep the grid slot (has TerrainName) over trail spline samples.</summary>
        public static void DedupeSlotsPreferGrid(List<PlacementSlot> slots, float minSeparationMeters)
        {
            if (slots == null || slots.Count < 2 || minSeparationMeters <= 0.1f)
                return;

            var kept = new List<PlacementSlot>(slots.Count);
            var minSq = minSeparationMeters * minSeparationMeters;
            foreach (var slot in slots)
            {
                var duplicateIndex = -1;
                for (var i = 0; i < kept.Count; i++)
                {
                    var dx = slot.Position.x - kept[i].Position.x;
                    var dz = slot.Position.z - kept[i].Position.z;
                    if (dx * dx + dz * dz >= minSq)
                        continue;

                    duplicateIndex = i;
                    break;
                }

                if (duplicateIndex < 0)
                {
                    kept.Add(slot);
                    continue;
                }

                var existing = kept[duplicateIndex];
                var slotIsGrid = !string.IsNullOrEmpty(slot.TerrainName);
                var existingIsGrid = !string.IsNullOrEmpty(existing.TerrainName);
                if (slotIsGrid && !existingIsGrid)
                    kept[duplicateIndex] = slot;
            }

            slots.Clear();
            slots.AddRange(kept);
        }

        public static void CapTrailOnlySlots(List<PlacementSlot> slots, int trailStartIndex, int maxTrailOnlySlots)
        {
            if (slots == null || maxTrailOnlySlots < 1 || trailStartIndex >= slots.Count)
                return;

            var trailOnly = new List<PlacementSlot>();
            for (var i = trailStartIndex; i < slots.Count; i++)
            {
                if (string.IsNullOrEmpty(slots[i].TerrainName))
                    trailOnly.Add(slots[i]);
            }

            if (trailOnly.Count <= maxTrailOnlySlots)
                return;

            var rng = new System.Random(trailStartIndex * 31 + slots.Count);
            for (var i = trailOnly.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (trailOnly[i], trailOnly[j]) = (trailOnly[j], trailOnly[i]);
            }

            var keep = new HashSet<Vector3>();
            for (var i = 0; i < maxTrailOnlySlots; i++)
                keep.Add(trailOnly[i].Position);

            for (var i = slots.Count - 1; i >= trailStartIndex; i--)
            {
                if (!string.IsNullOrEmpty(slots[i].TerrainName))
                    continue;
                if (!keep.Contains(slots[i].Position))
                    slots.RemoveAt(i);
            }
        }

        /// <summary>Round-robin across terrain tiles and XZ grid cells so props fill the whole tile, not one line.</summary>
        public static void OrderSlotsForFullAreaSpread(List<PlacementSlot> slots, Terrain mainTerrain, int seed)
        {
            if (slots == null || slots.Count < 2 || mainTerrain == null)
                return;

            const int cellsPerAxis = 5;
            var terrains = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            var buckets = new Dictionary<string, List<PlacementSlot>>(StringComparer.Ordinal);

            foreach (var slot in slots)
            {
                var terrainName = slot.TerrainName;
                Terrain tile = null;
                if (!string.IsNullOrEmpty(terrainName))
                {
                    foreach (var t in terrains)
                    {
                        if (t != null && t.name == terrainName)
                        {
                            tile = t;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(terrainName))
                {
                    if (SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(
                            mainTerrain,
                            slot.Position.x,
                            slot.Position.z,
                            out tile) &&
                        tile != null)
                        terrainName = tile.name;
                    else
                        terrainName = "_trail";
                }

                var cellX = 0;
                var cellZ = 0;
                if (tile?.terrainData != null)
                {
                    var local = slot.Position - tile.transform.position;
                    var size = tile.terrainData.size;
                    var nx = Mathf.Clamp01(local.x / size.x);
                    var nz = Mathf.Clamp01(local.z / size.z);
                    cellX = Mathf.Clamp(Mathf.FloorToInt(nx * cellsPerAxis), 0, cellsPerAxis - 1);
                    cellZ = Mathf.Clamp(Mathf.FloorToInt(nz * cellsPerAxis), 0, cellsPerAxis - 1);
                }
                else
                {
                    cellX = Mathf.Abs(slot.SectorIndex) % cellsPerAxis;
                    cellZ = (Mathf.Abs(slot.SectorIndex) / cellsPerAxis) % cellsPerAxis;
                }

                var key = $"{terrainName}:{cellX}:{cellZ}";
                if (!buckets.TryGetValue(key, out var list))
                {
                    list = new List<PlacementSlot>();
                    buckets[key] = list;
                }

                list.Add(slot);
            }

            var keys = new List<string>(buckets.Keys);
            keys.Sort(StringComparer.Ordinal);
            var rng = new System.Random(seed + 90210);
            for (var i = keys.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (keys[i], keys[j]) = (keys[j], keys[i]);
            }

            foreach (var list in buckets.Values)
            {
                for (var i = list.Count - 1; i > 0; i--)
                {
                    var j = rng.Next(i + 1);
                    (list[i], list[j]) = (list[j], list[i]);
                }
            }

            var ordered = new List<PlacementSlot>(slots.Count);
            var any = true;
            while (any)
            {
                any = false;
                foreach (var key in keys)
                {
                    var list = buckets[key];
                    if (list.Count == 0)
                        continue;

                    ordered.Add(list[list.Count - 1]);
                    list.RemoveAt(list.Count - 1);
                    any = true;
                }
            }

            slots.Clear();
            slots.AddRange(ordered);
        }

        public static string CategoryForSector(int sector, VegetationPass vegPass)
        {
            if (vegPass == VegetationPass.TreesFocus)
                return "tree";
            if (vegPass == VegetationPass.Understory)
                return (sector % 3) == 0 ? "flower" : "bush";
            var bucket = sector % 4;
            return bucket switch
            {
                0 => "tree",
                1 => "bush",
                2 => "flower",
                _ => "ground_cover",
            };
        }

        static void CollectPlacementSlots(
            List<PlacementSlot> slots,
            Transform trailsRoot,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            VegetationPass vegPass,
            Terrain mainTerrain,
            bool includeRadialFallback)
        {
            if (slots == null || trailsRoot == null)
                return;

            var spacing = vegPass == VegetationPass.TreesFocus ? 22f : vegPass == VegetationPass.Understory ? 14f : 18f;
            var sector = 0;
            foreach (Transform trail in trailsRoot)
            {
                var points = new List<Vector3>();
                for (var i = 0; i < trail.childCount; i++)
                {
                    var c = trail.GetChild(i);
                    if (c.name.StartsWith("Waypoint", StringComparison.Ordinal))
                        points.Add(c.position);
                }

                if (points.Count < 2)
                {
                    sector++;
                    continue;
                }

                var accum = 0f;
                var slotRng = new System.Random(seed + sector * 7919 + 3);
                for (var i = 1; i < points.Count; i++)
                {
                    var segLen = Vector3.Distance(points[i - 1], points[i]);
                    var segDelta = points[i] - points[i - 1];
                    var segDir = segDelta.normalized;
                    var segXZ = new Vector2(segDelta.x, segDelta.z);
                    if (segXZ.sqrMagnitude < 0.25f)
                    {
                        accum = 0f;
                        continue;
                    }

                    while (accum + spacing <= segLen)
                    {
                        accum += spacing;
                        var t = accum / segLen;
                        var p = Vector3.Lerp(points[i - 1], points[i], t);
                        var lateralMag = 12f + (float)slotRng.NextDouble() * 26f;
                        var lateralSign = ((sector + i) & 1) == 0 ? -1f : 1f;
                        var lateral = lateralSign * lateralMag *
                                      Vector3.Cross(Vector3.up, segDir).normalized;
                        p += lateral;
                        var dist = Vector2.Distance(
                            new Vector2(p.x, p.z),
                            new Vector2(groundCenter.x, groundCenter.z));
                        if (dist < extentMeters * 0.05f || dist > extentMeters * 1.02f)
                            continue;

                        if (IsNearCaveEntrance(p, minDistanceMeters: vegPass == VegetationPass.TreesFocus ? 32f : 18f))
                            continue;

                        if (mainTerrain != null &&
                            !SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(mainTerrain, p.x, p.z, out _))
                            continue;

                        slots.Add(new PlacementSlot
                        {
                            Position = p,
                            SectorIndex = sector,
                            SpacingM = spacing,
                            YawDeg = (seed + sector * 40) % 360,
                            Scale = vegPass == VegetationPass.TreesFocus ? 0.9f + (sector % 3) * 0.08f : 0.65f + (sector % 4) * 0.05f,
                            Category = CategoryForSector(sector, vegPass),
                        });
                    }

                    accum = 0f;
                }

                sector++;
            }

            if (includeRadialFallback && mainTerrain != null)
                AppendRadialFallbackSlots(slots, mainTerrain, groundCenter, extentMeters, seed, vegPass);
        }

        static void AppendRadialFallbackSlots(
            List<PlacementSlot> slots,
            Terrain mainTerrain,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            VegetationPass vegPass)
        {
            if (slots.Count >= 12 || mainTerrain == null)
                return;

            var rng = new System.Random(seed + 44017);
            var count = vegPass == VegetationPass.TreesFocus ? 14 : 22;
            for (var i = 0; i < count; i++)
            {
                var angle = (i / (float)count) * Mathf.PI * 2f + (seed % 360) * Mathf.Deg2Rad;
                var radius = extentMeters * (0.28f + 0.55f * (float)rng.NextDouble());
                var p = new Vector3(
                    groundCenter.x + Mathf.Cos(angle) * radius,
                    groundCenter.y,
                    groundCenter.z + Mathf.Sin(angle) * radius);

                if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(mainTerrain, p.x, p.z, out _))
                    continue;

                slots.Add(new PlacementSlot
                {
                    Position = p,
                    SectorIndex = i,
                    SpacingM = 16f,
                    YawDeg = (seed + i * 37) % 360,
                    Scale = vegPass == VegetationPass.TreesFocus ? 0.92f : 0.7f,
                    Category = CategoryForSector(i, vegPass),
                });
            }
        }

        static GameObject PickPrefab(
            SurfaceVegetationCatalog catalog,
            PlacementSlot slot,
            VegetationPass vegPass,
            System.Random rng)
        {
            List<GameObject> pool;
            switch (slot.Category)
            {
                case "tree":
                    pool = catalog.Trees;
                    break;
                case "bush":
                    pool = catalog.Bushes;
                    break;
                case "flower":
                case "ground_cover":
                    pool = catalog.Flowers.Count > 0 ? catalog.Flowers : catalog.Bushes;
                    break;
                default:
                    pool = catalog.Mixed;
                    break;
            }

            if (pool.Count == 0)
                pool = catalog.Mixed;
            return pool.Count == 0 ? null : pool[rng.Next(pool.Count)];
        }

        static Vector3 SnapToTerrain(Terrain terrain, Vector3 world)
        {
            if (terrain == null)
                return world;
            var h = terrain.SampleHeight(world) + terrain.transform.position.y;
            return new Vector3(world.x, h, world.z);
        }

        static void WritePlacementPlan(int pass, VegetationPass vegPass, List<PlacementPlanEntry> entries, int placed)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, PlacementPlanRel);
            var mission = CaveBuildMeatLoopPassPlan.GetMission(pass);
            var wrapper = new PlacementPlanFile
            {
                meatPass = pass,
                missionTitle = mission.Title,
                vegetationPass = vegPass.ToString(),
                placedCount = placed,
                generatedUtc = DateTime.UtcNow.ToString("o"),
                placements = entries.ToArray(),
            };
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonUtility.ToJson(wrapper, true));
        }

        static void WritePlacementPlanForCategory(
            SurfacePropCategory category,
            VegetationPass vegPass,
            List<PlacementPlanEntry> entries,
            int placed,
            int terrainTileCount,
            int targetCount)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var rel = string.Format(PlacementPlanByCategoryRel, category.ToString().ToLowerInvariant());
            var path = Path.Combine(hub, rel.Replace('/', Path.DirectorySeparatorChar));
            var wrapper = new PlacementPlanFile
            {
                meatPass = (int)category,
                missionTitle = $"Surface props — {category}",
                vegetationPass = vegPass.ToString(),
                placedCount = placed,
                targetCount = targetCount,
                terrainTileCount = terrainTileCount,
                generatedUtc = DateTime.UtcNow.ToString("o"),
                placements = entries.ToArray(),
            };
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonUtility.ToJson(wrapper, true));
            File.WriteAllText(
                Path.Combine(hub, PlacementPlanRel.Replace('/', Path.DirectorySeparatorChar)),
                JsonUtility.ToJson(wrapper, true));
        }

        public struct PlacementSlot
        {
            public Vector3 Position;
            public int SectorIndex;
            public float SpacingM;
            public float YawDeg;
            public float Scale;
            public string Category;
            public string TerrainName;
        }

        [Serializable]
        class PlacementPlanFile
        {
            public int meatPass;
            public string missionTitle;
            public string vegetationPass;
            public int placedCount;
            public int targetCount;
            public int terrainTileCount;
            public string generatedUtc;
            public PlacementPlanEntry[] placements;
        }

        [Serializable]
        public class PlacementPlanEntry
        {
            public string prefabName;
            public string category;
            public string terrainName;
            public float worldX;
            public float worldY;
            public float worldZ;
            public string rationale;
            public string agentPrompt;
        }

        public sealed class SurfaceVegetationCatalog
        {
            public readonly List<GameObject> Trees = new();
            public readonly List<GameObject> Bushes = new();
            public readonly List<GameObject> Flowers = new();
            public readonly List<GameObject> Grass = new();
            public readonly List<GameObject> Rocks = new();
            public readonly List<GameObject> Mixed = new();

            public bool HasAny =>
                Trees.Count + Bushes.Count + Flowers.Count + Grass.Count + Rocks.Count + Mixed.Count > 0;

            public bool HasCategory(SurfacePropCategory category) =>
                PoolForCategory(this, category).Count > 0;

            public void AppendAllTo(List<GameObject> dest)
            {
                if (dest == null)
                    return;

                foreach (var p in Trees)
                    AddUnique(dest, p);
                foreach (var p in Bushes)
                    AddUnique(dest, p);
                foreach (var p in Flowers)
                    AddUnique(dest, p);
                foreach (var p in Grass)
                    AddUnique(dest, p);
                foreach (var p in Rocks)
                    AddUnique(dest, p);
                foreach (var p in Mixed)
                    AddUnique(dest, p);
            }

            static void AddUnique(List<GameObject> list, GameObject prefab)
            {
                if (prefab != null && !list.Contains(prefab))
                    list.Add(prefab);
            }

            public static SurfaceVegetationCatalog Load()
            {
                var cat = new SurfaceVegetationCatalog();
                var lava = LavaTubePrefabCatalog.Load();
                foreach (var p in lava.MossProps)
                    Classify(cat, p);
                foreach (var p in lava.Mushrooms)
                    Classify(cat, p);
                foreach (var p in lava.Rockfalls)
                    Classify(cat, p);

                var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in EnvironmentKitSettings.CavePropPrefabFolders.Split(';'))
                {
                    var t = (f ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(t))
                        folders.Add(t);
                }

                folders.Add(LavaTubePrefabCatalog.DefaultPropRoot);
                foreach (var folder in folders)
                {
                    if (!AssetDatabase.IsValidFolder(folder.TrimEnd('/')))
                        continue;
                    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (prefab != null)
                            Classify(cat, prefab, path);
                    }
                }

                if (EnvironmentKitSettings.CaveScanAllAssets)
                    ScanAllNatural(cat);

                return cat;
            }

            static void Classify(SurfaceVegetationCatalog cat, GameObject prefab, string path = null)
            {
                var name = prefab.name.ToLowerInvariant();
                var full = (path ?? prefab.name).ToLowerInvariant();
                if (ExcludeKeywords.Any(k => name.Contains(k) || full.Contains(k)))
                    return;

                if (RockKeywords.Any(k => name.Contains(k) || full.Contains(k)))
                    AddUniqueToPool(cat.Rocks, prefab);
                else if (TreeKeywords.Any(k => name.Contains(k) || full.Contains(k)))
                    AddUniqueToPool(cat.Trees, prefab);
                else if (BushKeywords.Any(k => name.Contains(k) || full.Contains(k)))
                    AddUniqueToPool(cat.Bushes, prefab);
                else if (GrassKeywords.Any(k => name.Contains(k) || full.Contains(k)))
                    AddUniqueToPool(cat.Grass, prefab);
                else if (FlowerKeywords.Any(k => name.Contains(k) || full.Contains(k)))
                    AddUniqueToPool(cat.Flowers, prefab);
                else if (full.Contains("magicalforest") || full.Contains("plant") || full.Contains("environmental"))
                    AddUniqueToPool(cat.Mixed, prefab);
            }

            static void ScanAllNatural(SurfaceVegetationCatalog cat)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.Contains("/Editor/", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null)
                        Classify(cat, prefab, path);
                }
            }

            static void AddUniqueToPool(List<GameObject> list, GameObject prefab)
            {
                if (!list.Contains(prefab))
                    list.Add(prefab);
            }
        }
    }
}
#endif
