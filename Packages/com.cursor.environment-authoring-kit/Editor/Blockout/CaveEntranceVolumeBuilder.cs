#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Professional cave mouth: terrain bowl carve + single route mesh ramp + rock portal (no stacked onion slabs).
    /// </summary>
    public static class CaveEntranceVolumeBuilder
    {
        public const string DescentMeshName = "EntranceDescentMesh";
        public const string PortalRootName = "EntranceRockPortal";

        const int CrossSegments = 4;

        /// <summary>Remove stacked horizontal slabs near the walk-in mouth before rebuilding descent.</summary>
        public static int StripEntranceOnionSlabs(Transform caveRoot, SceneGroundInfo ground)
        {
            if (caveRoot == null)
                return 0;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
                return 0;

            var mouthWorld = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot, ground);
            var removed = 0;

            foreach (var t in geometry.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == geometry)
                    continue;

                var n = t.name;
                if (n.StartsWith("Entrance_Shaft_") ||
                    n.StartsWith("PathCeiling_") ||
                    (n.StartsWith("WalkFloor_Descent_") && t.parent != null && t.parent.name == CaveSurfaceEntranceBuilder.DescentWalkName))
                {
                    if (Vector3.Distance(t.position, mouthWorld) < 18f)
                    {
                        CaveEditorUndo.DestroyImmediate(t.gameObject);
                        removed++;
                    }
                }
            }

            removed += DestroyStackedHorizontalBands(geometry, mouthWorld, 14f);
            CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
            return removed;
        }

        /// <summary>
        /// Ensures the player can physically pass through the mouth into the underground route by removing
        /// blocking colliders in a short corridor ahead of the mouth. This does not change the route floor.
        /// </summary>
        public static int ClearMouthPortalBlockingColliders(
            Transform caveRoot,
            SceneGroundInfo ground,
            CaveMazeLayout layout,
            float corridorLengthMeters = 10f,
            float corridorRadiusMeters = 3.2f)
        {
            if (caveRoot == null || layout?.SolutionPath == null || layout.SolutionPath.Count == 0)
                return 0;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
                return 0;

            var mouthWorld = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot, ground);
            if (mouthWorld.sqrMagnitude < 0.01f)
                return 0;

            // Corridor direction: mouth → first route floor cell.
            var start = layout.SolutionPath[0];
            var routeFloorLocal = layout.GetFloorSurfaceLocal(start.x, start.y);
            var routeFloorWorld = caveRoot.TransformPoint(routeFloorLocal);
            var dir = routeFloorWorld - mouthWorld;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
                dir = caveRoot.forward;
            dir.Normalize();

            var from = mouthWorld + dir * 0.6f + Vector3.up * 0.35f;
            var to = from + dir * Mathf.Max(2f, corridorLengthMeters);
            var radius = Mathf.Clamp(corridorRadiusMeters, 1.8f, 6f);

            var removed = 0;
            foreach (var col in geometry.GetComponentsInChildren<Collider>(true))
            {
                if (col == null || col.isTrigger)
                    continue;

                var go = col.gameObject;
                var n = go.name;

                // Preserve walk surfaces / intentional entrance pieces.
                if (n == CaveEnclosureShellBuilder.FloorRootName ||
                    n == CaveEnclosureShellBuilder.CeilingRootName ||
                    n == DescentMeshName ||
                    n == CaveSurfaceEntranceBuilder.MouthPadName ||
                    n.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix))
                    continue;
                if (go.GetComponent<CaveWalkableMarker>() != null)
                    continue;

                // Keep far geometry and most of the cave; only clear colliders intersecting the mouth corridor.
                var p = col.bounds.center;
                var dist = DistancePointToSegmentXZ(p, from, to);
                if (dist > radius)
                    continue;

                CaveEditorUndo.DestroyImmediate(col);
                removed++;
            }

            if (removed > 0)
                Debug.Log($"[CaveBuild] Mouth portal clearance: removed {removed} blocking collider(s) in corridor.");
            return removed;
        }

        static float DistancePointToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            var ap = new Vector2(p.x - a.x, p.z - a.z);
            var ab = new Vector2(b.x - a.x, b.z - a.z);
            var abLen2 = ab.sqrMagnitude;
            if (abLen2 < 0.0001f)
                return ap.magnitude;
            var t = Mathf.Clamp01(Vector2.Dot(ap, ab) / abLen2);
            var closest = new Vector2(a.x, a.z) + ab * t;
            return Vector2.Distance(new Vector2(p.x, p.z), closest);
        }

        static int DestroyStackedHorizontalBands(Transform geometry, Vector3 mouthWorld, float radius)
        {
            var candidates = new List<Renderer>();
            foreach (var r in geometry.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled)
                    continue;
                if (Vector3.Distance(r.bounds.center, mouthWorld) > radius)
                    continue;
                var ext = r.bounds.extents;
                if (ext.y < 1.2f && ext.x > 1.5f && ext.z > 1.5f)
                    candidates.Add(r);
            }

            if (candidates.Count < 3)
                return 0;

            var removed = 0;
            foreach (var r in candidates)
            {
                if (r == null)
                    continue;
                var go = r.gameObject;
                if (go.name.Contains("RouteTerrain") || go.name.Contains("MouthPad"))
                    continue;
                CaveEditorUndo.DestroyImmediate(go);
                removed++;
            }

            return removed;
        }

        /// <summary>Single mesh walk ramp from surface mouth into route start.</summary>
        public static int BuildProfessionalDescent(
            Transform walkInRoot,
            CaveMazeLayout layout,
            Material floorMat,
            Material rockMat,
            SceneGroundInfo ground,
            LavaTubePrefabCatalog catalog,
            System.Random rng)
        {
            if (walkInRoot == null || layout?.SolutionPath == null || layout.SolutionPath.Count == 0)
                return 0;

            var existing = walkInRoot.Find(DescentMeshName);
            if (existing != null)
                CaveEditorUndo.DestroyImmediate(existing.gameObject);

            var geometry = walkInRoot.parent;
            var cavesRoot = geometry != null ? geometry.parent : null;
            var start = layout.SolutionPath[0];
            var routeFloorLocal = layout.GetFloorSurfaceLocal(start.x, start.y);
            var routeFloorWorld = geometry != null
                ? geometry.TransformPoint(routeFloorLocal)
                : routeFloorLocal;

            Vector3 topWorld;
            Vector3 bottomWorld;
            if (ground != null && ground.HasAnchor && cavesRoot != null)
            {
                var mouthWorld = CaveGroundPlacementUtility.GetEntranceMouthWorld(cavesRoot, ground);
                var surfaceY = CaveGroundPlacementUtility.SampleWalkableSurfaceWorldY(ground, mouthWorld);
                topWorld = new Vector3(mouthWorld.x, surfaceY + 0.18f, mouthWorld.z);
                bottomWorld = routeFloorWorld;
                var minDrop = Mathf.Max(5f, CaveGeometryPaths.UndergroundDepthMeters * 0.55f);
                if (topWorld.y - bottomWorld.y < minDrop)
                {
                    bottomWorld = new Vector3(
                        routeFloorWorld.x,
                        topWorld.y - minDrop,
                        routeFloorWorld.z);
                }
            }
            else
            {
                topWorld = routeFloorWorld + Vector3.up * Mathf.Max(3f, CaveGeometryPaths.UndergroundDepthMeters * 0.35f);
                bottomWorld = routeFloorWorld + Vector3.up * 0.15f;
            }

            floorMat = ProjectCaveMaterialResolver.EnsureUsable(
                floorMat, ProjectCaveMaterialResolver.MaterialRole.Floor, catalog);
            rockMat = ProjectCaveMaterialResolver.EnsureUsable(
                rockMat, ProjectCaveMaterialResolver.MaterialRole.Rock, catalog);
            floorMat ??= CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();

            const int sampleCount = 11;
            var worldPoints = new List<Vector3>(sampleCount);
            for (var i = 0; i < sampleCount; i++)
            {
                var t = i / (float)(sampleCount - 1);
                var p = Vector3.Lerp(topWorld, bottomWorld, t);
                if (ground != null && ground.HasAnchor && t < 0.42f)
                {
                    var lipY = CaveGroundPlacementUtility.SampleWalkableSurfaceWorldY(ground, p);
                    var blend = 1f - (t / 0.42f);
                    p.y = Mathf.Lerp(p.y, lipY + 0.14f, blend);
                }

                worldPoints.Add(p);
            }

            var points = new List<Vector3>(worldPoints.Count);
            foreach (var wp in worldPoints)
                points.Add(walkInRoot.InverseTransformPoint(wp));

            var placed = TryBuildDescentFromFloorModules(walkInRoot, points, layout, catalog, rng);
            var halfWidth = layout.PlatformSpan * 0.58f;
            var mesh = BuildWalkStripMesh(points, halfWidth, layout.CorridorHeight * 0.08f);
            if (mesh == null || floorMat == null)
                return placed;

            var go = new GameObject(DescentMeshName);
            CaveEditorUndo.RegisterCreated(go, "Entrance descent mesh");
            go.transform.SetParent(walkInRoot, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = floorMat;
            if (placed >= 3)
                mr.enabled = false;

            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;

            if (go.GetComponent<CaveWalkableMarker>() == null)
                go.AddComponent<CaveWalkableMarker>();

            placed += 1;
            placed += BuildRockPortal(walkInRoot, points[0], points[points.Count - 1], layout, rockMat, catalog, rng);
            RepairWalkInMaterials(walkInRoot);
            return placed;
        }

        static int TryBuildDescentFromFloorModules(
            Transform walkInRoot,
            List<Vector3> localPoints,
            CaveMazeLayout layout,
            LavaTubePrefabCatalog catalog,
            System.Random rng)
        {
            if (walkInRoot == null || catalog == null || catalog.Floors.Count == 0 || localPoints == null || localPoints.Count < 2)
                return 0;

            var modulesRoot = walkInRoot.Find("DescentFloorModules");
            if (modulesRoot != null)
                CaveEditorUndo.DestroyImmediate(modulesRoot.gameObject);

            var root = new GameObject("DescentFloorModules");
            CaveEditorUndo.RegisterCreated(root, "Descent floor modules");
            root.transform.SetParent(walkInRoot, false);

            var prefab = catalog.Pick(catalog.Floors, rng);
            if (prefab == null)
                return 0;

            var placed = 0;
            var step = Mathf.Max(layout.CellSize * 0.85f, 2.8f);
            for (var i = 0; i < localPoints.Count - 1; i++)
            {
                var a = localPoints[i];
                var b = localPoints[i + 1];
                var seg = b - a;
                var len = seg.magnitude;
                if (len < 0.05f)
                    continue;

                var forward = seg / len;
                var right = Vector3.Cross(Vector3.up, forward).normalized;
                var rot = Quaternion.LookRotation(forward, Vector3.up);
                var tiles = Mathf.Max(1, Mathf.CeilToInt(len / step));
                for (var t = 0; t < tiles; t++)
                {
                    var along = Mathf.Min(len, (t + 0.5f) * step);
                    var pos = a + forward * along;
                    var scale = Vector3.one * Mathf.Clamp(layout.PlatformSpan / 4.5f, 0.75f, 1.35f);
                    if (CavePrefabScatter.PlaceModule(
                            root.transform, prefab, pos, rot, scale, "EntranceDescent", false))
                        placed++;
                }

                if (catalog.Walls.Count > 0 && i % 2 == 0)
                {
                    var wallPrefab = catalog.Pick(catalog.Walls, rng);
                    var wallOffset = right * (layout.PlatformSpan * 0.55f);
                    CavePrefabScatter.PlaceModule(
                        root.transform,
                        wallPrefab,
                        a + forward * (len * 0.5f) + wallOffset,
                        rot,
                        Vector3.one * 0.9f,
                        "EntrancePortalSide",
                        false);
                }
            }

            return placed;
        }

        static void RepairWalkInMaterials(Transform walkInRoot)
        {
            if (walkInRoot == null)
                return;

            foreach (var renderer in walkInRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;
                CaveSceneMaterialRepair.ApplyModuleMaterials(renderer.gameObject, renderer.transform.lossyScale);
            }
        }

        public static void CarveTerrainBowlAtMouth(Transform caveRoot, SceneGroundInfo ground, float radiusMeters = 9f)
        {
            if (caveRoot == null || ground?.Terrain == null)
                return;

            var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot, ground);
            CaveTerrainCarveUtility.CarveEntranceDepression(ground.Terrain, mouth, radiusMeters, depthMeters: 2.8f);
        }

        static int BuildRockPortal(
            Transform parent,
            Vector3 localTop,
            Vector3 localBottom,
            CaveMazeLayout layout,
            Material rockMat,
            LavaTubePrefabCatalog catalog,
            System.Random rng)
        {
            var portal = parent.Find(PortalRootName);
            if (portal != null)
                CaveEditorUndo.DestroyImmediate(portal.gameObject);

            var root = new GameObject(PortalRootName);
            CaveEditorUndo.RegisterCreated(root, "Entrance portal");
            root.transform.SetParent(parent, false);

            var mid = (localTop + localBottom) * 0.5f;
            var forward = localBottom - localTop;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var placed = 0;

            if (catalog != null && catalog.Walls.Count > 0)
            {
                placed += PlacePortalModule(root.transform, catalog, rng, mid + right * (layout.PlatformSpan * 0.62f), forward, rockMat);
                placed += PlacePortalModule(root.transform, catalog, rng, mid - right * (layout.PlatformSpan * 0.62f), -forward, rockMat);
                var lintel = mid + Vector3.up * (layout.CorridorHeight * 0.55f);
                placed += PlacePortalModule(root.transform, catalog, rng, lintel, forward, rockMat, scaleMul: 0.85f);
            }
            else if (rockMat != null)
            {
                placed += PlacePrimitiveFin(root.transform, mid + right * 2f, rockMat);
                placed += PlacePrimitiveFin(root.transform, mid - right * 2f, rockMat);
            }

            return placed;
        }

        static int PlacePortalModule(
            Transform parent,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            Vector3 localPos,
            Vector3 lookForward,
            Material fallbackMat,
            float scaleMul = 1f)
        {
            var prefab = catalog.Pick(catalog.Walls, rng);
            if (prefab == null)
                return 0;

            var rot = Quaternion.LookRotation(lookForward, Vector3.up);
            if (CavePrefabScatter.PlaceModule(parent, prefab, localPos, rot, Vector3.one * scaleMul, "EntrancePortal", false))
                return 1;

            return 0;
        }

        static int PlacePrimitiveFin(Transform parent, Vector3 localPos, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(go, "Portal rock");
            go.name = "EntrancePortal_Rock";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(1.2f, 2.4f, 0.9f);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (col != null)
                CaveEditorUndo.DestroyImmediate(col);
            return 1;
        }

        static Mesh BuildWalkStripMesh(List<Vector3> path, float halfWidth, float thickness)
        {
            if (path == null || path.Count < 2)
                return null;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (var s = 0; s < path.Count; s++)
            {
                var p = path[s];
                var forward = s < path.Count - 1 ? path[s + 1] - p : p - path[s - 1];
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.01f)
                    forward = Vector3.forward;
                forward.Normalize();
                var right = Vector3.Cross(Vector3.up, forward).normalized;

                for (var c = 0; c <= CrossSegments; c++)
                {
                    var t = c / (float)CrossSegments;
                    var lateral = Mathf.Lerp(-halfWidth, halfWidth, t);
                    verts.Add(p + right * lateral + Vector3.up * thickness);
                    uvs.Add(new Vector2(t, s / (float)(path.Count - 1)));
                }
            }

            var row = CrossSegments + 1;
            for (var s = 0; s < path.Count - 1; s++)
            {
                for (var c = 0; c < CrossSegments; c++)
                {
                    var i0 = s * row + c;
                    var i1 = i0 + 1;
                    var i2 = i0 + row;
                    var i3 = i2 + 1;
                    tris.Add(i0);
                    tris.Add(i2);
                    tris.Add(i1);
                    tris.Add(i1);
                    tris.Add(i2);
                    tris.Add(i3);
                }
            }

            var mesh = new Mesh { name = "EntranceDescentStrip" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
