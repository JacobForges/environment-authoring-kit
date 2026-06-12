#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// 3D mesh landscape on top of terrain tiles — maze walls, hop pads, bridge ramps, island rims, concept sculpt clusters.
    /// Terrain heightmaps + LiDAR sculpt are unchanged; this is an additive layer.
    /// </summary>
    public static class CaveBuildPlannerMeshLandscapeAuthor
    {
        public const string RootName = "PlannerMeshLandscape";

        static bool _appliedThisBuild;
        static Material _wallMat;
        static Material _walkMat;
        static Material _rimMat;
        static LavaTubePrefabCatalog _moduleCatalog;
        static System.Random _rng;

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
                message = "Mesh landscape already applied this build.";
                return true;
            }

            if (ground?.Terrain == null || surfaceRoot == null)
            {
                message = "No terrain/surface root.";
                return false;
            }

            if (!CaveBuildSessionConfig.ShouldImport3DObjects())
            {
                message = "import3DObjects off — mesh landscape skipped.";
                return false;
            }

            if (!CaveBuildPlannerLayoutBridge.TryLoad(out var brief, out message))
                return false;

            var plan = brief.layoutPlan;
            var specs = CaveBuildPlannerLayoutBridge.ResolveSpecs(plan);
            var main = ground.Terrain;
            var hubY = SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main).y;
            var tileSize = main.terrainData.size;
            var topo = CaveBuildPlannerMazeTopology.Resolve(plan);

            EnsureMaterials();
            _moduleCatalog = LavaTubePrefabCatalog.Load();
            _rng = new System.Random(request?.Seed ?? 0);

            var root = surfaceRoot.Find(RootName);
            if (root == null)
            {
                var go = new GameObject(RootName);
                CaveEditorUndo.RegisterCreated(go, "Planner mesh landscape");
                go.transform.SetParent(surfaceRoot, false);
                root = go.transform;
            }

            ClearChildren(root);
            var wallsRoot = GetChild(root, "MazeWalls");
            var hopsRoot = GetChild(root, "HopPads");
            var bridgesRoot = GetChild(root, "BridgeRamps");
            var rimsRoot = GetChild(root, "IslandRims");
            var sculptRoot = GetChild(root, "ConceptSculptClusters");

            var walls = PlaceMazeWallMeshes(wallsRoot, main, ground, plan, specs, hubY, tileSize, topo);
            var hops = PlaceHopPadMeshes(hopsRoot, main, ground, plan, specs, hubY, tileSize);
            var bridges = PlaceBridgeRampMeshes(bridgesRoot, main, ground, plan, specs, hubY, tileSize);
            var rims = PlaceIslandRimMeshes(rimsRoot, main, ground, plan, specs, hubY, tileSize);
            var sculpt = PlaceConceptSculptClusters(sculptRoot, main, ground, request, specs, hubY);

            _appliedThisBuild = true;
            var kitNote = _moduleCatalog != null && _moduleCatalog.IsValid
                ? "kit prefabs"
                : "blockout cubes (import modular cave pack for kit meshes)";
            message =
                $"Mesh landscape on terrain ({kitNote}) — {walls} wall(s), {hops} hop pad(s), {bridges} bridge segment(s), " +
                $"{rims} island rim(s), {sculpt} sculpt cluster(s) (maze: {topo.Source}).";
            CaveBuildEditorLog.LogSurface("[PlannerMesh] " + message, forceUnityConsole: true);
            return true;
        }

        static int PlaceMazeWallMeshes(
            Transform parent,
            Terrain main,
            SceneGroundInfo ground,
            CaveBuildPlannerLayoutBridge.LayoutPlan plan,
            CaveBuildPlannerLayoutBridge.TechnicalSpecs specs,
            float hubY,
            Vector3 tileSize,
            CaveBuildPlannerMazeTopology.Topology topo)
        {
            if (plan?.playDisk == null || !plan.playDisk.labyrinth)
                return 0;

            const float wallH = 2.1f;
            const float wallT = 1.25f;
            var count = 0;

            for (var row = 0; row < 3; row++)
            {
                for (var col = 0; col < 3; col++)
                {
                    var off = CaveBuildPlannerLayoutBridge.PlayRowColToOffset(row, col);
                    var y = hubY + (CaveBuildPlannerLayoutBridge.IsCornerCell(row, col)
                        ? specs.plateauHeightM
                        : specs.platformHeightM);
                    var center = TileCenter(main, off, y);

                    if (row > 0 && CaveBuildPlannerMazeTopology.HasHorizontalWall(topo, row - 1, col))
                    {
                        count += PlaceWallBox(parent, center + new Vector3(0f, wallH * 0.5f, -tileSize.z * 0.48f),
                            new Vector3(tileSize.x * 0.92f, wallH, wallT), $"MazeWall_H_{row}_{col}");
                    }

                    if (row < 2 && CaveBuildPlannerMazeTopology.HasHorizontalWall(topo, row, col))
                    {
                        count += PlaceWallBox(parent, center + new Vector3(0f, wallH * 0.5f, tileSize.z * 0.48f),
                            new Vector3(tileSize.x * 0.92f, wallH, wallT), $"MazeWall_H_{row + 1}_{col}");
                    }

                    if (col > 0 && CaveBuildPlannerMazeTopology.HasVerticalWall(topo, row, col - 1))
                    {
                        count += PlaceWallBox(parent, center + new Vector3(-tileSize.x * 0.48f, wallH * 0.5f, 0f),
                            new Vector3(wallT, wallH, tileSize.z * 0.92f), $"MazeWall_V_{row}_{col}");
                    }

                    if (col < 2 && CaveBuildPlannerMazeTopology.HasVerticalWall(topo, row, col))
                    {
                        count += PlaceWallBox(parent, center + new Vector3(tileSize.x * 0.48f, wallH * 0.5f, 0f),
                            new Vector3(wallT, wallH, tileSize.z * 0.92f), $"MazeWall_V_{row}_{col + 1}");
                    }
                }
            }

            return count;
        }

        static int PlaceHopPadMeshes(
            Transform parent,
            Terrain main,
            SceneGroundInfo ground,
            CaveBuildPlannerLayoutBridge.LayoutPlan plan,
            CaveBuildPlannerLayoutBridge.TechnicalSpecs specs,
            float hubY,
            Vector3 tileSize)
        {
            if (plan?.markers == null)
                return 0;

            var count = 0;
            foreach (var m in plan.markers)
            {
                if (m == null || !CaveBuildPlannerLayoutBridge.IsPlatformMarker(m))
                    continue;
                if (!CaveBuildPlannerLayoutAuthor.TryResolvePlayMarkerWorld(
                        main, ground, m, specs, hubY, tileSize, out var world))
                    continue;

                var isBridge = (m.label ?? string.Empty).IndexOf("bridge", StringComparison.OrdinalIgnoreCase) >= 0;
                var size = isBridge
                    ? new Vector3(3.6f, 0.45f, 2.4f)
                    : new Vector3(2.3f, 0.35f, 2.3f);
                var name = isBridge ? $"WalkBridgePad_{m.label}" : $"WalkHopPad_{m.label}";
                count += PlaceWalkBox(parent, world + Vector3.up * (size.y * 0.5f), size, name);
            }

            return count;
        }

        static int PlaceBridgeRampMeshes(
            Transform parent,
            Terrain main,
            SceneGroundInfo ground,
            CaveBuildPlannerLayoutBridge.LayoutPlan plan,
            CaveBuildPlannerLayoutBridge.TechnicalSpecs specs,
            float hubY,
            Vector3 tileSize)
        {
            if (plan?.trails == null || plan.trails.Length == 0)
                return 0;

            var count = 0;
            foreach (var tr in plan.trails)
            {
                if (tr == null || string.IsNullOrEmpty(tr.to) || !tr.to.StartsWith("island-", StringComparison.OrdinalIgnoreCase))
                    continue;

                var leg = tr.to.Substring(tr.to.Length - 1).ToUpperInvariant();
                var play = TileCenter(main, Vector2Int.zero, hubY + specs.platformHeightM);
                var islandOff = CaveBuildPlannerLayoutBridge.CardinalDirToOffset(leg);
                var island = TileCenter(main, islandOff, hubY + specs.plateauHeightM);

                const int steps = 10;
                for (var i = 0; i <= steps; i++)
                {
                    var t = i / (float)steps;
                    var pos = Vector3.Lerp(play, island, t);
                    pos.y = Mathf.Lerp(play.y, island.y, t) + 0.15f;
                    var fwd = (island - play).normalized;
                    var rot = fwd.sqrMagnitude > 0.01f ? Quaternion.LookRotation(fwd, Vector3.up) : Quaternion.identity;
                    count += PlaceWalkBox(parent, pos, new Vector3(3.2f, 0.32f, 2.8f),
                        $"WalkBridgeRamp_{tr.label ?? leg}_{i:D2}", rot);
                }
            }

            return count;
        }

        static int PlaceIslandRimMeshes(
            Transform parent,
            Terrain main,
            SceneGroundInfo ground,
            CaveBuildPlannerLayoutBridge.LayoutPlan plan,
            CaveBuildPlannerLayoutBridge.TechnicalSpecs specs,
            float hubY,
            Vector3 tileSize)
        {
            if (plan?.islands == null || plan.islands.Length == 0)
                return 0;

            var count = 0;
            foreach (var island in plan.islands)
            {
                if (island == null || string.IsNullOrEmpty(island.dir))
                    continue;

                var off = CaveBuildPlannerLayoutBridge.CardinalDirToOffset(island.dir);
                if (off == Vector2Int.zero)
                    continue;

                var y = hubY + specs.plateauHeightM;
                var center = TileCenter(main, off, y);
                var rimH = 1.1f;
                var rimT = 0.85f;
                var half = tileSize * 0.42f;

                count += PlaceWallBox(parent, center + new Vector3(0f, rimH * 0.5f, half.z),
                    new Vector3(tileSize.x * 0.88f, rimH, rimT), $"IslandRim_{island.dir}_N", _rimMat);
                count += PlaceWallBox(parent, center + new Vector3(0f, rimH * 0.5f, -half.z),
                    new Vector3(tileSize.x * 0.88f, rimH, rimT), $"IslandRim_{island.dir}_S", _rimMat);
                count += PlaceWallBox(parent, center + new Vector3(half.x, rimH * 0.5f, 0f),
                    new Vector3(rimT, rimH, tileSize.z * 0.88f), $"IslandRim_{island.dir}_E", _rimMat);
                count += PlaceWallBox(parent, center + new Vector3(-half.x, rimH * 0.5f, 0f),
                    new Vector3(rimT, rimH, tileSize.z * 0.88f), $"IslandRim_{island.dir}_W", _rimMat);
            }

            return count;
        }

        static int PlaceConceptSculptClusters(
            Transform parent,
            Terrain main,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            CaveBuildPlannerLayoutBridge.TechnicalSpecs specs,
            float hubY)
        {
            if (!CaveBuildPlannerConceptGuide.IsActive)
                return 0;

            var catalog = BiomePropCatalog.Load(request);
            var veg = SurfaceIntelligentPropPlacer.LoadVegetationCatalog();
            var hub = SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main);
            var tileSize = main.terrainData.size;
            var span = tileSize.x * 3f;
            var count = 0;
            var seed = request?.Seed ?? 0;

            for (var iz = 0; iz < 9; iz++)
            {
                for (var ix = 0; ix < 9; ix++)
                {
                    var wx = hub.x + (ix / 8f - 0.5f) * span * 2f;
                    var wz = hub.z + (iz / 8f - 0.5f) * span * 2f;
                    var world = new Vector3(wx, hubY + specs.platformHeightM, wz);

                    if (!CaveBuildPlannerConceptGuide.TrySampleExpectedZone(
                            world, main, ground, out var zone, out var conf) ||
                        conf < 0.72f ||
                        zone != CaveBuildPlannerConceptGuide.PlannerZone.Prop)
                        continue;

                    if (!TrySampleTerrainY(world, main, out var y))
                        continue;

                    world.y = y + 0.2f;
                    if (!TryInstantiateSculptPrefab(parent, catalog, veg, world, seed + ix * 17 + iz, out var go))
                        continue;

                    go.name = $"ConceptSculpt_{ix}_{iz}";
                    count++;
                }
            }

            return count;
        }

        static bool TryInstantiateSculptPrefab(
            Transform parent,
            BiomePropCatalog catalog,
            SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog veg,
            Vector3 world,
            int seed,
            out GameObject instance)
        {
            instance = null;
            var rng = new System.Random(seed);
            GameObject prefab = null;

            var rocks = catalog?.PoolForBiome(WorldSurfaceBiomeId.PlayKarst, SurfacePropCategory.Rocks);
            if (rocks != null && rocks.Count > 0)
                prefab = rocks[rng.Next(rocks.Count)];
            else
            {
                var pool = SurfaceIntelligentPropPlacer.PoolForCategory(veg, SurfacePropCategory.Rocks);
                if (pool != null && pool.Count > 0)
                    prefab = pool[rng.Next(pool.Count)];
            }

            if (prefab == null)
                return false;

            instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
                return false;

            CaveEditorUndo.RegisterCreated(instance, "Concept sculpt");
            instance.transform.SetParent(parent, false);
            instance.transform.position = world;
            instance.transform.rotation = Quaternion.Euler(0f, rng.Next(0, 360), 0f);
            instance.transform.localScale *= 0.85f + (float)rng.NextDouble() * 0.3f;
            return true;
        }

        static int PlaceWallBox(Transform parent, Vector3 pos, Vector3 size, string name, Material mat = null)
        {
            if (TryPlaceKitWall(parent, pos, size, name, mat))
                return 1;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            CaveEditorUndo.RegisterCreated(go, "Planner mesh wall");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            var rend = go.GetComponent<Renderer>();
            if (rend != null)
                rend.sharedMaterial = mat ?? _wallMat;
            EnsureBoxCollider(go, size);
            return 1;
        }

        static int PlaceWalkBox(Transform parent, Vector3 pos, Vector3 size, string name, Quaternion? rot = null)
        {
            if (TryPlaceKitWalk(parent, pos, size, name, rot))
                return 1;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            CaveEditorUndo.RegisterCreated(go, "Planner walk mesh");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            if (rot.HasValue)
                go.transform.rotation = rot.Value;
            var rend = go.GetComponent<Renderer>();
            if (rend != null)
                rend.sharedMaterial = _walkMat;
            EnsureBoxCollider(go, size);
            return 1;
        }

        static bool TryPlaceKitWall(Transform parent, Vector3 pos, Vector3 size, string name, Material mat)
        {
            var prefab = PickModule(_moduleCatalog?.Walls, name);
            if (prefab == null)
                prefab = PickModule(_moduleCatalog?.Rockfalls, name);
            if (prefab == null)
                return false;

            return PlaceScaledKitModule(parent, prefab, pos, size, name, mat ?? _wallMat, walkSurface: false);
        }

        static bool TryPlaceKitWalk(Transform parent, Vector3 pos, Vector3 size, string name, Quaternion? rot)
        {
            var prefab = PickModule(_moduleCatalog?.Floors, name);
            if (prefab == null)
                prefab = PickModule(_moduleCatalog?.Rockfalls, name);
            if (prefab == null)
                return false;

            return PlaceScaledKitModule(parent, prefab, pos, size, name, _walkMat, walkSurface: true, rot);
        }

        static GameObject PickModule(System.Collections.Generic.List<GameObject> pool, string seedKey)
        {
            if (pool == null || pool.Count == 0)
                return null;
            var idx = Mathf.Abs(seedKey.GetHashCode() + (_rng?.Next() ?? 0)) % pool.Count;
            return pool[idx];
        }

        static bool PlaceScaledKitModule(
            Transform parent,
            GameObject prefab,
            Vector3 pos,
            Vector3 targetSize,
            string name,
            Material fallbackMat,
            bool walkSurface,
            Quaternion? rot = null)
        {
            if (prefab == null)
                return false;

            var instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
                return false;

            CaveEditorUndo.RegisterCreated(instance, walkSurface ? "Planner walk kit" : "Planner wall kit");
            instance.name = name;
            instance.transform.position = pos;
            if (rot.HasValue)
                instance.transform.rotation = rot.Value;

            if (!TryGetLocalBounds(instance, out var localBounds))
            {
                CaveEditorUndo.DestroyImmediate(instance);
                return false;
            }

            var fit = new Vector3(
                targetSize.x / Mathf.Max(localBounds.size.x, 0.05f),
                targetSize.y / Mathf.Max(localBounds.size.y, 0.05f),
                targetSize.z / Mathf.Max(localBounds.size.z, 0.05f));
            instance.transform.localScale = fit;

            foreach (var col in instance.GetComponentsInChildren<Collider>(true))
            {
                if (col != null)
                    CaveEditorUndo.DestroyImmediate(col);
            }

            CaveSceneMaterialRepair.ApplyModuleMaterials(instance, fit);
            EnsureBoxCollider(instance, targetSize);

            if (fallbackMat != null)
            {
                foreach (var rend in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (rend != null && rend.sharedMaterial == null)
                        rend.sharedMaterial = fallbackMat;
                }
            }

            if (walkSurface && instance.GetComponent<CaveWalkableMarker>() == null)
                instance.AddComponent<CaveWalkableMarker>();

            return true;
        }

        static bool TryGetLocalBounds(GameObject root, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.one);
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return false;

            var first = true;
            foreach (var rend in renderers)
            {
                if (rend == null)
                    continue;
                if (first)
                {
                    bounds = rend.bounds;
                    first = false;
                }
                else
                    bounds.Encapsulate(rend.bounds);
            }

            if (first)
                return false;

            var localCenter = root.transform.InverseTransformPoint(bounds.center);
            var ext = bounds.extents;
            var sx = Mathf.Max(Mathf.Abs(root.transform.lossyScale.x), 0.001f);
            var sy = Mathf.Max(Mathf.Abs(root.transform.lossyScale.y), 0.001f);
            var sz = Mathf.Max(Mathf.Abs(root.transform.lossyScale.z), 0.001f);
            bounds = new Bounds(localCenter, new Vector3(ext.x * 2f / sx, ext.y * 2f / sy, ext.z * 2f / sz));
            return bounds.size.sqrMagnitude > 0.001f;
        }

        static void EnsureBoxCollider(GameObject go, Vector3 targetSize)
        {
            var box = go.GetComponent<BoxCollider>();
            if (box == null)
                box = go.AddComponent<BoxCollider>();

            if (TryGetLocalBounds(go, out var localBounds))
            {
                box.size = localBounds.size;
                box.center = localBounds.center;
            }
            else
            {
                box.size = targetSize;
                box.center = Vector3.zero;
            }

            box.isTrigger = false;
        }

        static Vector3 TileCenter(Terrain main, Vector2Int off, float y)
        {
            var tileSize = main.terrainData.size;
            var hub = main.transform.position + tileSize * 0.5f;
            return new Vector3(hub.x + off.x * tileSize.x, y, hub.z + off.y * tileSize.z);
        }

        static bool TrySampleTerrainY(Vector3 world, Terrain main, out float y)
        {
            y = world.y;
            foreach (var t in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(main))
            {
                if (t?.terrainData == null)
                    continue;
                var pos = t.transform.position;
                var size = t.terrainData.size;
                if (world.x < pos.x || world.z < pos.z || world.x > pos.x + size.x || world.z > pos.z + size.z)
                    continue;
                y = t.SampleHeight(world) + pos.y;
                return true;
            }

            return false;
        }

        static void EnsureMaterials()
        {
            _wallMat ??= CreateSolidMaterial(new Color(0.32f, 0.38f, 0.34f));
            _walkMat ??= CreateSolidMaterial(new Color(0.55f, 0.52f, 0.48f));
            _rimMat ??= CreateSolidMaterial(new Color(0.42f, 0.48f, 0.55f));
        }

        static Material CreateSolidMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return new Material(shader) { color = color };
        }

        static Transform GetChild(Transform parent, string name)
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
            for (var i = root.childCount - 1; i >= 0; i--)
                CaveEditorUndo.DestroyImmediate(root.GetChild(i).gameObject);
        }
    }
}
#endif
