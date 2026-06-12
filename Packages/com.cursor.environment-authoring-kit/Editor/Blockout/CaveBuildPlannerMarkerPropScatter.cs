#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Plan-locked vegetation at marker + concept positions on top of terrain.
    /// Reduces random scatter overlap on planner play disk.
    /// </summary>
    public static class CaveBuildPlannerMarkerPropScatter
    {
        public const float MarkerExclusionRadiusM = 3.5f;
        static bool _placedThisBuild;

        public static void ResetBuildSession() => _placedThisBuild = false;

        public static bool UsesPlannerMarkerScatter(WorldGenerationRequest request) =>
            CaveBuildSessionConfig.HasFinalizedActive &&
            (request == null || CaveBuildSessionConfig.IsSessionRequest(request)) &&
            CaveBuildSessionConfig.ShouldScatterBiomeProps();

        public static bool TryPlaceLockedVegetation(
            SceneGroundInfo ground,
            Transform surfaceRoot,
            WorldGenerationRequest request,
            out string message)
        {
            message = null;
            if (_placedThisBuild)
            {
                message = "Marker vegetation already placed this build.";
                return true;
            }

            if (!UsesPlannerMarkerScatter(request) || ground?.Terrain == null || surfaceRoot == null)
            {
                message = "Marker scatter skipped — not a planner props session.";
                return false;
            }

            if (!CaveBuildPlannerLayoutBridge.TryLoad(out var brief, out message))
                return false;

            var plan = brief.layoutPlan;
            var specs = CaveBuildPlannerLayoutBridge.ResolveSpecs(plan);
            var main = ground.Terrain;
            var hubY = SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main).y;
            var tileSize = main.terrainData.size;
            var catalog = BiomePropCatalog.Load(request);
            var veg = SurfaceIntelligentPropPlacer.LoadVegetationCatalog();
            var vegRoot = surfaceRoot.Find(SurfaceIntelligentPropPlacer.VegetationLayerName);
            if (vegRoot == null)
            {
                var go = new GameObject(SurfaceIntelligentPropPlacer.VegetationLayerName);
                CaveEditorUndo.RegisterCreated(go, "Vegetation root");
                go.transform.SetParent(surfaceRoot, false);
                vegRoot = go.transform;
            }
            var seed = request?.Seed ?? 0;
            var placed = 0;

            if (plan?.markers != null)
            {
                foreach (var m in plan.markers)
                {
                    if (m == null || !ShouldPlaceVegetationAtMarker(m))
                        continue;

                    if (!TryResolveMarkerWorld(main, ground, m, specs, hubY, tileSize, out var world))
                        continue;

                    var category = CategoryForMarker(m);
                    if (!TryPlaceAtWorld(vegRoot, catalog, veg, world, category, m, seed + (m.label?.GetHashCode() ?? 0)))
                        continue;
                    placed++;
                }
            }

            placed += PlaceConceptPropSamples(main, ground, vegRoot, catalog, veg, request, specs, hubY, seed);

            var pruned = PruneRandomNearMarkers(surfaceRoot, main, ground, plan, specs, hubY, tileSize);
            _placedThisBuild = true;
            message =
                $"Marker-locked vegetation — {placed} placed, {pruned} random prop(s) pruned near plan markers " +
                $"(concept+marker authority over generic scatter).";
            CaveBuildEditorLog.LogSurface("[PlannerProps] " + message, forceUnityConsole: true);
            return placed > 0;
        }

        public static bool IsNearPlanMarker(Vector3 world, CaveBuildPlannerLayoutBridge.LayoutPlan plan)
        {
            if (plan?.markers == null)
                return false;

            foreach (var m in plan.markers)
            {
                if (m == null)
                    continue;
                if (string.Equals(m.kind, "spawn", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (m.row >= 0 && m.row <= 2 && m.col >= 0 && m.col <= 2)
                {
                    var off = CaveBuildPlannerLayoutBridge.PlayRowColToOffset(m.row, m.col);
                    if (off.x is >= -1 and <= 1 && off.y is >= -1 and <= 1)
                        return true;
                }
            }

            return false;
        }

        static bool ShouldPlaceVegetationAtMarker(CaveBuildPlannerLayoutBridge.Marker m)
        {
            if (CaveBuildPlannerLayoutBridge.IsHubDressingMarker(m) ||
                CaveBuildPlannerLayoutBridge.IsScatterHubMarker(m))
                return true;

            var label = m.label ?? string.Empty;
            return label.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   label.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   label.IndexOf("rock", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   label.IndexOf("hub-prop", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static SurfacePropCategory CategoryForMarker(CaveBuildPlannerLayoutBridge.Marker m)
        {
            var label = m.label ?? string.Empty;
            if (label.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0)
                return SurfacePropCategory.Trees;
            if (label.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0)
                return SurfacePropCategory.Grass;
            if (label.IndexOf("rock", StringComparison.OrdinalIgnoreCase) >= 0)
                return SurfacePropCategory.Rocks;
            return SurfacePropCategory.Bushes;
        }

        static bool TryResolveMarkerWorld(
            Terrain main,
            SceneGroundInfo ground,
            CaveBuildPlannerLayoutBridge.Marker m,
            CaveBuildPlannerLayoutBridge.TechnicalSpecs specs,
            float hubY,
            Vector3 tileSize,
            out Vector3 world)
        {
            if (string.Equals(m.zone, "island", StringComparison.OrdinalIgnoreCase))
                return CaveBuildPlannerLayoutAuthor.TryResolveIslandMarkerWorld(main, m, specs, hubY, tileSize, out world);
            return CaveBuildPlannerLayoutAuthor.TryResolvePlayMarkerWorld(main, ground, m, specs, hubY, tileSize, out world);
        }

        static int PlaceConceptPropSamples(
            Terrain main,
            SceneGroundInfo ground,
            Transform vegRoot,
            BiomePropCatalog catalog,
            SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog veg,
            WorldGenerationRequest request,
            CaveBuildPlannerLayoutBridge.TechnicalSpecs specs,
            float hubY,
            int seed)
        {
            if (!CaveBuildPlannerConceptGuide.IsActive)
                return 0;

            var hub = SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main);
            var tileSize = main.terrainData.size;
            var span = tileSize.x * 2.6f;
            var placed = 0;

            for (var iz = 0; iz < 7; iz++)
            {
                for (var ix = 0; ix < 7; ix++)
                {
                    var wx = hub.x + (ix / 6f - 0.5f) * span * 2f;
                    var wz = hub.z + (iz / 6f - 0.5f) * span * 2f;
                    var world = new Vector3(wx, hubY + specs.platformHeightM, wz);

                    if (!CaveBuildPlannerConceptGuide.TrySampleExpectedZone(
                            world, main, ground, out var zone, out var conf) ||
                        conf < 0.8f ||
                        zone != CaveBuildPlannerConceptGuide.PlannerZone.Prop)
                        continue;

                    if (TryPlaceAtWorld(vegRoot, catalog, veg, world, SurfacePropCategory.Bushes, null, seed + ix * 13 + iz))
                        placed++;
                }
            }

            return placed;
        }

        static bool TryPlaceAtWorld(
            Transform vegRoot,
            BiomePropCatalog catalog,
            SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog veg,
            Vector3 world,
            SurfacePropCategory category,
            CaveBuildPlannerLayoutBridge.Marker marker,
            int seed)
        {
            var rng = new System.Random(seed);
            GameObject prefab = null;
            if (marker != null && CaveBuildPlannerApprovedAssets.EnsureLoaded())
                prefab = CaveBuildPlannerApprovedAssets.ResolvePrefabForMarker(marker.kind, marker.label);
            if (prefab == null && CaveBuildPlannerApprovedAssets.EnsureLoaded())
                prefab = CaveBuildPlannerApprovedAssets.ResolvePropPrefab(category);

            if (prefab == null)
            {
                var pool = catalog?.PoolForBiome(WorldSurfaceBiomeId.PlayKarst, category);
                if (pool == null || pool.Count == 0)
                    pool = SurfaceIntelligentPropPlacer.PoolForCategory(veg, category);
                if (pool == null || pool.Count == 0)
                    return false;

                prefab = pool[rng.Next(pool.Count)];
            }
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
                return false;

            CaveEditorUndo.RegisterCreated(instance, "Planner marker prop");
            instance.transform.SetParent(vegRoot, false);
            instance.transform.position = world;
            instance.transform.rotation = Quaternion.Euler(0f, rng.Next(0, 360), 0f);
            return true;
        }

        static int PruneRandomNearMarkers(
            Transform surfaceRoot,
            Terrain main,
            SceneGroundInfo ground,
            CaveBuildPlannerLayoutBridge.LayoutPlan plan,
            CaveBuildPlannerLayoutBridge.TechnicalSpecs specs,
            float hubY,
            Vector3 tileSize)
        {
            if (plan?.markers == null)
                return 0;

            var markerWorlds = new System.Collections.Generic.List<Vector3>();
            foreach (var m in plan.markers)
            {
                if (m == null)
                    continue;
                if (TryResolveMarkerWorld(main, ground, m, specs, hubY, tileSize, out var w))
                    markerWorlds.Add(w);
            }

            var veg = surfaceRoot.Find(SurfaceIntelligentPropPlacer.VegetationLayerName);
            if (veg == null)
                return 0;

            var removed = 0;
            for (var i = veg.childCount - 1; i >= 0; i--)
            {
                var child = veg.GetChild(i);
                if (child == null)
                    continue;

                var p = child.position;
                foreach (var mw in markerWorlds)
                {
                    if (Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(mw.x, 0f, mw.z)) > MarkerExclusionRadiusM)
                        continue;
                    CaveEditorUndo.DestroyImmediate(child.gameObject);
                    removed++;
                    break;
                }
            }

            return removed;
        }
    }
}
#endif
