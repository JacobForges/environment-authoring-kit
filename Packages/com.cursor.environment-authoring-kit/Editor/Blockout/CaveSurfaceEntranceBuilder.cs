using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Ground-level walk-in mouth and descent walkway into the maze route (no invisible blocking shells).
    /// </summary>
    public static class CaveSurfaceEntranceBuilder
    {
        public const string RootName = "SurfaceWalkIn";
        public const string MouthPadName = "SurfaceMouthPad";
        public const string DescentWalkName = "DescentWalk";

        const float MouthSurfaceToleranceMeters = 0.45f;

        /// <summary>Build or refresh surface mouth + ramp into route start. Idempotent.</summary>
        public static int Build(
            Transform cavesRoot,
            Transform geometry,
            CaveMazeLayout layout,
            Material floorMat,
            Material rockMat,
            SceneGroundInfo ground,
            LavaTubePrefabCatalog catalog = null,
            int seed = 0)
        {
            if (cavesRoot == null || geometry == null || layout?.SolutionPath == null || layout.SolutionPath.Count == 0)
                return 0;

            CaveEntranceVolumeBuilder.StripEntranceOnionSlabs(cavesRoot, ground);
            CaveEntranceVolumeBuilder.CarveTerrainBowlAtMouth(cavesRoot, ground);

            var existing = geometry.Find(RootName);
            if (existing != null)
                CaveEditorUndo.DestroyImmediate(existing.gameObject);

            var root = new GameObject(RootName);
            CaveEditorUndo.RegisterCreated(root, "Surface Walk-In");
            root.transform.SetParent(geometry, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            var entrance = cavesRoot.Find("Entrance");
            if (entrance != null)
                SplineCaveSpawnAligner.RestoreEntranceMarkerAtShaftMouth(entrance);

            catalog ??= LavaTubePrefabCatalog.Load();
            var rng = new System.Random(seed + 8803);

            var placed = 0;
            placed += BuildMouthPad(cavesRoot, root.transform, entrance, floorMat, ground);
            placed += CaveEntranceVolumeBuilder.BuildProfessionalDescent(
                root.transform, layout, floorMat, rockMat, ground, catalog, rng);

            // Finally: ensure the mouth corridor is actually passable (remove blocking colliders near entrance).
            placed += CaveEntranceVolumeBuilder.ClearMouthPortalBlockingColliders(
                cavesRoot, ground, layout, corridorLengthMeters: 11f, corridorRadiusMeters: 3.4f);
            return placed;
        }

        public static bool ValidateMouthOnGround(Transform caveRoot, SceneGroundInfo ground, out string issue)
        {
            issue = null;
            if (caveRoot == null || ground == null || !ground.HasAnchor)
                return true;

            var err = CaveGroundPlacementUtility.MeasureEntranceMouthSurfaceError(caveRoot, ground);
            if (Mathf.Abs(err) <= MouthSurfaceToleranceMeters)
                return true;

            issue = $"Entrance mouth {err:F2}m from ground surface (max {MouthSurfaceToleranceMeters:F1}m).";
            return false;
        }

        public static bool HasDescentWalk(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
                return false;

            var walkIn = geometry.Find($"{RootName}/{CaveEntranceVolumeBuilder.DescentMeshName}");
            if (walkIn != null)
                return true;
            walkIn = geometry.Find($"{RootName}/{DescentWalkName}");
            if (walkIn != null)
                return true;

            foreach (var t in geometry.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == CaveEntranceVolumeBuilder.DescentMeshName)
                    return true;
            }

            return false;
        }

        static int BuildMouthPad(
            Transform cavesRoot,
            Transform parent,
            Transform entrance,
            Material floorMat,
            SceneGroundInfo ground)
        {
            var markerLocal = new Vector3(1f, CaveGroundPlacementUtility.DefaultMarkerLiftAboveShaftMeters, 0f);
            if (entrance != null)
            {
                var marker = entrance.Find(CaveEntranceTeleport.EntranceMarkerObjectName);
                if (marker != null)
                    markerLocal = marker.localPosition;
            }

            var mouthLocal = new Vector3(markerLocal.x, CaveGeometryPaths.UndergroundDepthMeters + 0.05f, markerLocal.z + 1.2f);
            if (ground != null && ground.HasAnchor)
            {
                var worldMouth = cavesRoot.TransformPoint(mouthLocal);
                var targetY = CaveGroundPlacementUtility.SampleWalkableSurfaceWorldY(ground, worldMouth);
                mouthLocal.y = cavesRoot.InverseTransformPoint(new Vector3(worldMouth.x, targetY + 0.08f, worldMouth.z)).y;
            }

            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(pad, "Surface Mouth Pad");
            pad.name = MouthPadName;
            pad.transform.SetParent(parent, false);
            pad.transform.localPosition = mouthLocal;
            pad.transform.localRotation = Quaternion.identity;
            pad.transform.localScale = new Vector3(5.5f, 0.35f, 4.5f);

            var mr = pad.GetComponent<MeshRenderer>();
            if (mr != null && floorMat != null)
                mr.sharedMaterial = floorMat;

            if (pad.GetComponent<CaveWalkableMarker>() == null)
                pad.AddComponent<CaveWalkableMarker>();

            var col = pad.GetComponent<BoxCollider>();
            if (col != null)
                col.isTrigger = false;

            pad.isStatic = true;
            return 1;
        }

        static int BuildDescentWalk(
            Transform parent,
            CaveMazeLayout layout,
            Material floorMat,
            Material rockMat)
        {
            var start = layout.SolutionPath[0];
            var routeFloor = layout.GetFloorSurfaceLocal(start.x, start.y);
            var mouthY = CaveGeometryPaths.UndergroundDepthMeters;
            var top = new Vector3(routeFloor.x, mouthY + 0.15f, routeFloor.z + layout.CellSize * 0.35f);
            var bottom = routeFloor + Vector3.up * 0.12f;
            var delta = bottom - top;
            var length = delta.magnitude;
            if (length < 3f)
                return 0;

            var walkRoot = new GameObject(DescentWalkName);
            CaveEditorUndo.RegisterCreated(walkRoot, "Descent Walk");
            walkRoot.transform.SetParent(parent, false);

            var forward = delta;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward).normalized;

            var steps = Mathf.Clamp(Mathf.CeilToInt(length / 2.8f), 2, 6);
            var placed = 0;
            for (var i = 0; i < steps; i++)
            {
                var t0 = i / (float)steps;
                var t1 = (i + 1) / (float)steps;
                var a = Vector3.Lerp(top, bottom, t0);
                var b = Vector3.Lerp(top, bottom, t1);
                var mid = (a + b) * 0.5f;
                var segLen = Vector3.Distance(a, b) + 0.4f;

                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                CaveEditorUndo.RegisterCreated(slab, "Descent Slab");
                slab.name = $"{CaveWalkwayBuilder.WalkFloorPrefix}Descent_{i:D2}";
                slab.transform.SetParent(walkRoot.transform, false);
                slab.transform.localPosition = mid;
                slab.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);
                slab.transform.localScale = new Vector3(layout.PlatformSpan * 1.05f, 0.42f, segLen);

                var mr = slab.GetComponent<MeshRenderer>();
                if (mr != null && floorMat != null)
                    mr.sharedMaterial = floorMat;

                if (slab.GetComponent<CaveWalkableMarker>() == null)
                    slab.AddComponent<CaveWalkableMarker>();

                placed++;

                if (rockMat != null && i % 2 == 0)
                    placed += PlaceRockFin(walkRoot.transform, mid + right * (layout.PlatformSpan * 0.55f), rockMat);
                if (rockMat != null && i % 2 == 1)
                    placed += PlaceRockFin(walkRoot.transform, mid - right * (layout.PlatformSpan * 0.55f), rockMat);
            }

            return placed;
        }

        static int PlaceRockFin(Transform parent, Vector3 localPos, Material rockMat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(go, "Entrance Rock");
            go.name = "CaveBlock_EntranceVisual";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos + Vector3.up * 0.9f;
            go.transform.localScale = Vector3.one * 0.85f;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.sharedMaterial = rockMat;

            var col = go.GetComponent<Collider>();
            if (col != null)
                CaveEditorUndo.DestroyImmediate(col);

            return 1;
        }
    }
}
