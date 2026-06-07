#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Anchors underground route geometry and entrance markers to surface cave-opening markers
    /// so the walk-in descent starts at the carved mouth and the rest of the cave stays buried.
    /// </summary>
    public static class CaveRouteMouthSnapUtility
    {
        public const float MinHorizontalSnapMeters = 0.35f;
        public const float MaxGeometryHorizontalSnapMeters = 48f;

        /// <summary>Primary play-disk mouth first, then sector, then nearest portal, then any marker.</summary>
        public static SurfaceCaveOpeningMarker ResolveActiveOpeningMarker(int preferredSector = -1)
        {
            var markers = SurfaceWorldGenerator.FindCaveOpenings();
            if (markers == null || markers.Count == 0)
                return null;

            if (preferredSector >= 0)
            {
                foreach (var m in markers)
                {
                    if (m != null && m.sectorIndex == preferredSector)
                        return m;
                }
            }

            foreach (var m in markers)
            {
                if (m != null && m.isPrimaryEntrance)
                    return m;
            }

            var portal = CaveBuildPortalSettings.PortalForBuild;
            if (portal != null)
            {
                SurfaceCaveOpeningMarker nearest = null;
                var bestDist = float.MaxValue;
                foreach (var marker in markers)
                {
                    if (marker == null)
                        continue;
                    var dist = (marker.transform.position - portal.transform.position).sqrMagnitude;
                    if (dist >= bestDist)
                        continue;
                    bestDist = dist;
                    nearest = marker;
                }

                if (nearest != null)
                    return nearest;
            }

            return markers[0];
        }

        /// <summary>
        /// After root align: nudge entrance shaft so walk-in marker XZ matches the surface opening lip.
        /// </summary>
        public static bool TrySyncEntranceToSurfaceOpening(
            Transform cavesRoot,
            SceneGroundInfo ground,
            SurfaceCaveOpeningMarker opening = null)
        {
            if (cavesRoot == null || ground == null || !ground.HasAnchor)
                return false;

            opening ??= ResolveActiveOpeningMarker(-1);
            if (opening == null)
                return false;

            var entrance = cavesRoot.Find("Entrance");
            if (entrance == null)
                return false;

            SplineCaveSpawnAligner.RestoreEntranceMarkerAtShaftMouth(entrance);

            var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(cavesRoot, ground);
            var target = opening.transform.position;
            target.y = CaveGroundPlacementUtility.SampleWalkableSurfaceWorldY(ground, target);

            var err = target - mouth;
            err.y = 0f;
            if (err.sqrMagnitude < MinHorizontalSnapMeters * MinHorizontalSnapMeters)
                return false;

            CaveEditorUndo.RecordObject(entrance, "Snap entrance to surface opening");
            var localErr = cavesRoot.InverseTransformVector(err);
            localErr.y = 0f;
            entrance.localPosition += localErr;

            SplineCaveSpawnAligner.RestoreEntranceMarkerAtShaftMouth(entrance);
            CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(cavesRoot, ground, out _);
            Debug.Log(
                $"[CaveBuild] Entrance marker synced to surface opening '{opening.name}' (Δ{err.magnitude:F1}m horizontal).");
            return true;
        }

        /// <summary>
        /// Shifts <see cref="CaveGeometryPaths.GeometryRoot"/> in XZ so maze route start sits under the walk-in mouth.
        /// </summary>
        public static bool TrySnapGeometryRouteStartToMouth(
            Transform cavesRoot,
            Transform geometry,
            CaveMazeLayout layout,
            SceneGroundInfo ground)
        {
            if (cavesRoot == null || geometry == null || layout?.SolutionPath == null || layout.SolutionPath.Count == 0)
                return false;

            var mouth = ResolveMouthAnchorWorld(cavesRoot, ground);
            if (mouth.sqrMagnitude < 0.01f)
                return false;

            var start = layout.SolutionPath[0];
            var routeLocal = layout.GetFloorSurfaceLocal(start.x, start.y);
            var routeWorld = geometry.TransformPoint(routeLocal);

            var err = mouth - routeWorld;
            err.y = 0f;
            if (err.sqrMagnitude < MinHorizontalSnapMeters * MinHorizontalSnapMeters)
                return false;

            if (err.magnitude > MaxGeometryHorizontalSnapMeters)
            {
                Debug.LogWarning(
                    $"[CaveBuild] Route start mouth snap skipped — horizontal Δ{err.magnitude:F1}m exceeds {MaxGeometryHorizontalSnapMeters:F0}m.");
                return false;
            }

            CaveEditorUndo.RecordObject(geometry, "Snap route start to cave mouth");
            var localDelta = geometry.InverseTransformVector(err);
            localDelta.y = 0f;
            geometry.localPosition += localDelta;

            Debug.Log(
                $"[CaveBuild] Route start snapped under mouth (Δ{err.magnitude:F1}m on {geometry.name}).");
            return true;
        }

        /// <summary>Align root, sync entrance, snap route — call before surface walk-in / descent rebuild.</summary>
        public static bool ApplyMouthAnchoredPlacement(
            Transform cavesRoot,
            Transform geometry,
            CaveMazeLayout layout,
            SceneGroundInfo ground,
            int preferredSector = -1)
        {
            if (cavesRoot == null || ground == null || !ground.HasAnchor)
                return false;

            var changed = SurfaceCaveOpeningAligner.TryAlignCaveRootToOpening(cavesRoot, ground, preferredSector);
            var opening = ResolveActiveOpeningMarker(preferredSector);
            if (TrySyncEntranceToSurfaceOpening(cavesRoot, ground, opening))
                changed = true;
            if (TrySnapGeometryRouteStartToMouth(cavesRoot, geometry, layout, ground))
                changed = true;

            return changed;
        }

        /// <summary>Bury ceiling under heightmap and strip above-ground roof meshes (mouth exempt).</summary>
        public static bool FinalizeUndergroundEnvelope(Transform cavesRoot, SceneGroundInfo ground)
        {
            if (cavesRoot == null || ground == null || !ground.HasAnchor)
                return false;

            var changed = CaveGroundPlacementUtility.EnsureFullyBuriedUnderSurface(cavesRoot, ground, out _);
            SurfaceCaveRoofAuditor.AuditAndStrip(cavesRoot, ground, out _);
            return changed;
        }

        static Vector3 ResolveMouthAnchorWorld(Transform cavesRoot, SceneGroundInfo ground)
        {
            var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(cavesRoot, ground);
            if (mouth.sqrMagnitude > 0.01f)
                return mouth;

            var opening = ResolveActiveOpeningMarker(-1);
            if (opening == null)
                return Vector3.zero;

            var pos = opening.transform.position;
            if (ground != null && ground.HasAnchor)
                pos.y = CaveGroundPlacementUtility.SampleWalkableSurfaceWorldY(ground, pos);
            return pos;
        }
    }
}
#endif
