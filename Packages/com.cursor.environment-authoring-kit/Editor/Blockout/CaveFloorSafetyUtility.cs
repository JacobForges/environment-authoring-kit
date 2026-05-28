using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Prevents falling through to the outer shell floor at the bottom of the maze box.</summary>
    public static class CaveFloorSafetyUtility
    {
        public static int Apply(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var fixedCount = 0;
            fixedCount += EnsureRouteTerrainPlayCollider(caveRoot);
            fixedCount += StripOuterShellFloorColliders(caveRoot);
            fixedCount += MarkWalkableFloors(caveRoot);
            fixedCount += CaveColliderUtility.EnsureMazeVolumeColliders(caveRoot);
            fixedCount += ThickenWalkColliders(caveRoot);
            if (!UsesRouteTerrainFloor(caveRoot))
                fixedCount += EnsureVisibleWalkways(caveRoot);
            else
                CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);

            return fixedCount;
        }

        /// <summary>MeshCollider + walkable marker on RouteTerrainFloor / LayoutWalkFloor (prevents fall-through after perf passes).</summary>
        public static int EnsureRouteTerrainPlayCollider(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
                return 0;

            var fixedCount = 0;
            fixedCount += EnsureWalkSurfaceCollider(geometry.Find(CaveEnclosureShellBuilder.FloorRootName));
            fixedCount += EnsureWalkSurfaceCollider(geometry.Find(CaveLayoutPrototypeGenerator.FlatFloorRootName));
            return fixedCount;
        }

        static int EnsureWalkSurfaceCollider(Transform surface)
        {
            if (surface == null)
                return 0;

            var mf = surface.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
                return 0;

            var mc = surface.GetComponent<MeshCollider>();
            if (mc == null)
                mc = surface.gameObject.AddComponent<MeshCollider>();

            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
            mc.enabled = true;

            if (surface.GetComponent<CaveWalkableMarker>() == null)
                surface.gameObject.AddComponent<CaveWalkableMarker>();

            return 1;
        }

        public static bool UsesRouteTerrainFloor(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
                return false;

            var floor = geometry.Find(CaveEnclosureShellBuilder.FloorRootName);
            return floor != null && floor.GetComponentInChildren<MeshRenderer>() != null;
        }

        public static int EnsureVisibleWalkways(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            if (UsesRouteTerrainFloor(caveRoot))
            {
                CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
                return 0;
            }

            var mat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();
            var fixedCount = 0;

            var walkRoot = caveRoot.Find("Walkways");
            if (walkRoot != null)
            {
                foreach (Transform child in walkRoot)
                {
                    if (!child.name.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix))
                        continue;

                    if (EnsureWalkFloorRenderer(child.gameObject, mat))
                        fixedCount++;
                }
            }

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            // PathPlatforms stay hidden when RouteTerrainFloor is active (see UsesRouteTerrainFloor).

            var features = caveRoot.Find(CaveAdventureFeaturesBuilder.RootName);
            if (features != null)
            {
                foreach (Transform child in features)
                {
                    if (!child.name.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix))
                        continue;

                    if (EnsureWalkFloorRenderer(child.gameObject, mat))
                        fixedCount++;
                }
            }

            return fixedCount;
        }

        public static bool EnsureWalkFloorRenderer(GameObject go, Material mat = null)
        {
            if (go == null)
                return false;

            mat ??= CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();

            var mf = go.GetComponent<MeshFilter>();
            if (mf == null)
            {
                mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            }

            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null)
                mr = go.AddComponent<MeshRenderer>();

            mr.enabled = true;
            if (mat != null)
                mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;

            if (go.GetComponent<CaveWalkableMarker>() == null)
                go.AddComponent<CaveWalkableMarker>();

            return true;
        }

        static int StripOuterShellFloorColliders(Transform caveRoot)
        {
            Transform shell = null;
            var maze = caveRoot.Find($"SplineMesh/{CaveMazeVolumeBuilder.MazeVolumeRootName}");
            if (maze != null)
                shell = maze.Find(CaveMazeVolumeBuilder.OuterShellRootName);

            if (shell == null)
            {
                var adventure = caveRoot.Find(
                    $"{CaveAdventureCaveGenerator.GeometryRootName}/{CaveAdventureShellBuilder.ShellRootName}");
                shell = adventure != null ? adventure.Find(CaveMazeVolumeBuilder.OuterShellRootName) : null;
            }

            if (shell == null)
                return 0;

            var removed = 0;
            foreach (var col in shell.GetComponentsInChildren<Collider>(true))
            {
                if (col == null)
                    continue;

                var n = col.gameObject.name;
                if (n == "Outer_Floor" || n == "Outer_Ceiling")
                {
                    CaveEditorUndo.DestroyImmediate(col);
                    removed++;
                }
            }

            return removed;
        }

        static int MarkWalkableFloors(Transform caveRoot)
        {
            var marked = 0;
            var walkRoot = caveRoot.Find("Walkways");
            if (walkRoot != null)
            {
                foreach (var col in walkRoot.GetComponentsInChildren<Collider>(true))
                {
                    if (col == null || col.isTrigger)
                        continue;
                    if (col.GetComponent<CaveWalkableMarker>() == null)
                    {
                        col.gameObject.AddComponent<CaveWalkableMarker>();
                        marked++;
                    }
                }
            }

            var maze = caveRoot.Find($"SplineMesh/{CaveMazeVolumeBuilder.MazeVolumeRootName}");
            if (maze != null)
            {
                foreach (var col in maze.GetComponentsInChildren<Collider>(true))
                {
                    if (col == null || col.isTrigger)
                        continue;
                    var n = col.gameObject.name;
                    if (!n.Contains("Floor") && !n.StartsWith("Ledge_"))
                        continue;
                    if (col.GetComponent<CaveWalkableMarker>() == null)
                    {
                        col.gameObject.AddComponent<CaveWalkableMarker>();
                        marked++;
                    }
                }
            }

            var spawnPad = caveRoot.Find("Entrance/CaveEntrance_SpawnPoint/SpawnGroundPad");
            if (spawnPad != null && spawnPad.GetComponent<CaveWalkableMarker>() == null)
            {
                spawnPad.gameObject.AddComponent<CaveWalkableMarker>();
                marked++;
            }

            var adventureShell = caveRoot.Find(
                $"{CaveAdventureCaveGenerator.GeometryRootName}/{CaveAdventureShellBuilder.ShellRootName}");
            if (adventureShell != null)
            {
                foreach (var col in adventureShell.GetComponentsInChildren<Collider>(true))
                {
                    if (col == null || col.isTrigger)
                        continue;

                    var n = col.gameObject.name;
                    if (!n.Contains("Floor") && !n.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix) &&
                        !n.Contains("Entrance_Floor"))
                        continue;

                    if (col.GetComponent<CaveWalkableMarker>() == null)
                    {
                        col.gameObject.AddComponent<CaveWalkableMarker>();
                        marked++;
                    }
                }
            }

            return marked;
        }

        static int ThickenWalkColliders(Transform caveRoot)
        {
            var walkRoot = caveRoot.Find("Walkways");
            if (walkRoot == null)
                return 0;

            var count = 0;
            foreach (var col in walkRoot.GetComponentsInChildren<BoxCollider>(true))
            {
                if (col == null)
                    continue;

                var t = col.transform;
                if (t.localScale.y < 0.35f)
                {
                    t.localScale = new Vector3(t.localScale.x, 0.42f, t.localScale.z);
                    count++;
                }

                col.size = Vector3.one;
                col.center = Vector3.zero;
                col.isTrigger = false;
            }

            return count;
        }
    }
}
