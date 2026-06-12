#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Places hub props, switches, NPCs, enemies, and island dressing from planner markers.</summary>
    public static class CaveBuildPlannerContentAuthor
    {
        static bool _appliedThisBuild;

        public static void ResetBuildSession() => _appliedThisBuild = false;

        public static bool TryApply(
            SceneGroundInfo ground,
            Transform surfaceRoot,
            WorldGenerationRequest request,
            out string message)
        {
            message = null;
            if (_appliedThisBuild)
            {
                message = "Planner content already applied this build.";
                return true;
            }

            if (ground?.Terrain == null || surfaceRoot == null)
            {
                message = "No terrain/surface root.";
                return false;
            }

            if (!CaveBuildPlannerLayoutBridge.TryLoad(out var brief, out message))
                return false;

            var plan = brief.layoutPlan;
            var specs = CaveBuildPlannerLayoutBridge.ResolveSpecs(plan);
            var main = ground.Terrain;
            var hubY = SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main).y;
            var tileSize = main.terrainData.size;

            var layoutRoot = surfaceRoot.Find(CaveBuildPlannerLayoutAuthor.LayoutRootName);
            if (layoutRoot == null)
            {
                var go = new GameObject(CaveBuildPlannerLayoutAuthor.LayoutRootName);
                CaveEditorUndo.RegisterCreated(go, "Planner layout root");
                go.transform.SetParent(surfaceRoot, false);
                layoutRoot = go.transform;
            }

            var catalog = BiomePropCatalog.Load(request);
            var veg = SurfaceIntelligentPropPlacer.LoadVegetationCatalog();
            var hubRoot = GetOrCreateChild(layoutRoot, "HubDressing");
            var switchRoot = GetOrCreateChild(layoutRoot, "HubSwitches");
            var islandRoot = GetOrCreateChild(layoutRoot, "IslandDressing");
            var npcRoot = GetOrCreateChild(layoutRoot, "PlannerNpcs");
            var enemyRoot = GetOrCreateChild(layoutRoot, "PlannerEnemies");

            ClearChildren(hubRoot);
            ClearChildren(switchRoot);
            ClearChildren(islandRoot);
            ClearChildren(npcRoot);
            ClearChildren(enemyRoot);

            var placed = 0;
            var switches = 0;
            var npcs = 0;
            var enemies = 0;

            if (plan?.markers != null)
            {
                foreach (var m in plan.markers)
                {
                    if (m == null)
                        continue;

                    var kind = m.kind ?? string.Empty;
                    if (string.Equals(kind, "spawn", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (CaveBuildPlannerLayoutBridge.IsHubDressingMarker(m))
                    {
                        if (!CaveBuildPlannerLayoutAuthor.TryResolvePlayMarkerWorld(
                                main, ground, m, specs, hubY, tileSize, out var world))
                            continue;
                        placed += PlaceCatalogProp(hubRoot, m.label, world, catalog, veg, request?.Seed ?? 0);
                        continue;
                    }

                    if (CaveBuildPlannerLayoutBridge.IsSwitchMarker(m))
                    {
                        if (!CaveBuildPlannerLayoutAuthor.TryResolvePlayMarkerWorld(
                                main, ground, m, specs, hubY, tileSize, out var world))
                            continue;
                        switches += PlaceSwitch(switchRoot, m.label, world);
                        continue;
                    }

                    if (CaveBuildPlannerLayoutBridge.IsIslandJunctionMarker(m))
                    {
                        if (!CaveBuildPlannerLayoutAuthor.TryResolveIslandMarkerWorld(
                                main, m, specs, hubY, tileSize, out var world))
                            continue;
                        placed += PlaceCatalogProp(islandRoot, m.label, world, catalog, veg, request?.Seed ?? 0);
                        continue;
                    }

                    if (string.Equals(kind, "npc", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!TryResolveAnyMarkerWorld(main, ground, m, specs, hubY, tileSize, out var world))
                            continue;
                        npcs += PlaceNpcMarker(npcRoot, m, world);
                        continue;
                    }

                    if (string.Equals(kind, "enemy", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!TryResolveAnyMarkerWorld(main, ground, m, specs, hubY, tileSize, out var world))
                            continue;
                        enemies += PlaceEnemySpawner(enemyRoot, m, world, request?.Seed ?? 0);
                    }
                }
            }

            _appliedThisBuild = true;
            message =
                $"Content — {placed} prop(s), {switches} switch(es), {npcs} NPC(s), {enemies} enemy spawner(s) " +
                $"from {plan.markers?.Length ?? 0} markers.";
            Debug.Log("[CaveBuild] Planner content applied — " + message);
            return true;
        }

        static bool TryResolveAnyMarkerWorld(
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

        static int PlaceCatalogProp(
            Transform parent,
            string label,
            Vector3 world,
            BiomePropCatalog catalog,
            SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog veg,
            int seed)
        {
            var prefab = PickPropPrefab(catalog, veg, seed + (label?.GetHashCode() ?? 0));
            if (prefab == null)
                return PlaceFallbackProp(parent, label, world);

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
                return 0;

            CaveEditorUndo.RegisterCreated(instance, "Planner prop");
            instance.name = SanitizeName(label ?? "hub_prop");
            instance.transform.SetParent(parent, false);
            instance.transform.position = world;
            instance.transform.rotation = Quaternion.Euler(0f, (seed % 360) * 0.7f, 0f);
            return 1;
        }

        static GameObject PickPropPrefab(
            BiomePropCatalog catalog,
            SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog veg,
            int seed)
        {
            if (CaveBuildPlannerApprovedAssets.EnsureLoaded())
            {
                var approved = CaveBuildPlannerApprovedAssets.ResolvePropPrefab(SurfacePropCategory.Bushes);
                if (approved != null)
                    return approved;
            }

            var rng = new System.Random(seed);
            if (catalog != null && catalog.HasAny)
            {
                foreach (var category in new[]
                         {
                             SurfacePropCategory.Rocks,
                             SurfacePropCategory.Bushes,
                             SurfacePropCategory.GroundCover,
                         })
                {
                    var pool = catalog.PoolForBiome(WorldSurfaceBiomeId.PlayKarst, category);
                    if (pool != null && pool.Count > 0)
                        return pool[rng.Next(pool.Count)];
                }
            }

            foreach (var category in new[]
                     {
                         SurfacePropCategory.Rocks,
                         SurfacePropCategory.Bushes,
                         SurfacePropCategory.Trees,
                     })
            {
                var pool = SurfaceIntelligentPropPlacer.PoolForCategory(veg, category);
                if (pool != null && pool.Count > 0)
                    return pool[rng.Next(pool.Count)];
            }

            return null;
        }

        static int PlaceFallbackProp(Transform parent, string label, Vector3 world)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = SanitizeName(label ?? "hub_prop");
            CaveEditorUndo.RegisterCreated(go, "Planner fallback prop");
            go.transform.SetParent(parent, false);
            go.transform.position = world + Vector3.up * 0.45f;
            go.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            return 1;
        }

        static int PlaceSwitch(Transform parent, string label, Vector3 world)
        {
            var go = new GameObject(SanitizeName(label ?? "switch"));
            CaveEditorUndo.RegisterCreated(go, "Planner switch");
            go.transform.SetParent(parent, false);
            go.transform.position = world + Vector3.up * 0.5f;

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(1.2f, 1.4f, 1.2f);
            col.center = Vector3.up * 0.7f;

            go.AddComponent<PlannerSwitchGate>();
            return 1;
        }

        static int PlaceNpcMarker(Transform parent, CaveBuildPlannerLayoutBridge.Marker marker, Vector3 world)
        {
            var label = marker?.label;
            var prefab = CaveBuildPlannerApprovedAssets.ResolveForLayoutMarker(marker)
                         ?? CaveBuildPlannerApprovedAssets.ResolvePrefab("npc", label)
                         ?? CaveBuildPlannerApprovedAssets.ResolvePrefab("npc");
            if (prefab != null)
            {
                var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance != null)
                {
                    CaveEditorUndo.RegisterCreated(instance, "Planner NPC");
                    instance.name = SanitizeName(label ?? "npc");
                    instance.transform.SetParent(parent, false);
                    instance.transform.position = world;
                    return 1;
                }
            }

            var go = new GameObject(SanitizeName(label ?? "npc"));
            CaveEditorUndo.RegisterCreated(go, "Planner NPC");
            go.transform.SetParent(parent, false);
            go.transform.position = world;

            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "EditorMarker";
            capsule.transform.SetParent(go.transform, false);
            capsule.transform.localPosition = Vector3.up * 1f;
            capsule.transform.localScale = new Vector3(0.5f, 1f, 0.5f);
            return 1;
        }

        static int PlaceEnemySpawner(
            Transform parent,
            CaveBuildPlannerLayoutBridge.Marker marker,
            Vector3 world,
            int seed)
        {
            var label = marker?.label;
            var go = new GameObject(SanitizeName(label ?? "enemy"));
            CaveEditorUndo.RegisterCreated(go, "Planner enemy");
            go.transform.SetParent(parent, false);
            go.transform.position = world;

            var prefab = CaveBuildPlannerApprovedAssets.ResolveForLayoutMarker(marker)
                         ?? CaveBuildPlannerApprovedAssets.ResolvePrefab("enemy", label)
                         ?? CaveCombatSetupUtility.EnsureEnemyPrefab();
            var spawner = go.AddComponent<CaveMobSpawner>();
            spawner.enemyPrefab = prefab;
            spawner.mobAggression = CaveMobAggression.Aggressive;
            spawner.spawnCount = 1;
            spawner.radius = 6f;
            spawner.spawnSeed = seed + label.GetHashCode();
            spawner.spawnOnStart = true;
            return 1;
        }

        static Transform GetOrCreateChild(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null)
                return t;
            var go = new GameObject(name);
            CaveEditorUndo.RegisterCreated(go, name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static void ClearChildren(Transform root)
        {
            if (root == null)
                return;
            for (var i = root.childCount - 1; i >= 0; i--)
                CaveEditorUndo.DestroyImmediate(root.GetChild(i).gameObject);
        }

        static string SanitizeName(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return "planner_node";
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (var c in raw)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.ToString();
        }
    }
}
#endif
