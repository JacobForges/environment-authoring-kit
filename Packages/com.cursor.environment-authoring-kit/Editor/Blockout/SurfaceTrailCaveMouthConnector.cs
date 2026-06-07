#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Extends the primary surface trail from its last waypoint to the cave opening lip at ground level
    /// so the walk route meets the surface mouth before descending underground.
    /// </summary>
    public static class SurfaceTrailCaveMouthConnector
    {
        const float SegmentSpacingMeters = 5f;
        const float MaxTrailExtensionMeters = 140f;

        public static int ConnectPrimaryTrailToCaveMouth(
            Transform surfaceRoot,
            Transform caveRoot,
            SceneGroundInfo ground)
        {
            if (surfaceRoot == null || caveRoot == null || ground?.Terrain == null)
                return 0;

            var trailsRoot = surfaceRoot.Find(SurfaceWorldPaths.TrailsName);
            if (trailsRoot == null || trailsRoot.childCount == 0)
                return 0;

            var mouthWorld = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot, ground);
            if (mouthWorld.sqrMagnitude < 0.01f)
                return 0;

            var lip = SurfaceRouteProbeRunner.ProjectMouthToSurfaceApproach(ground, mouthWorld);
            if (lip.sqrMagnitude < 0.01f)
                lip = mouthWorld;

            lip.y = CaveGroundPlacementUtility.SampleWalkableSurfaceWorldY(ground, lip) + 0.12f;

            var trail = PickTrailTowardMouth(trailsRoot, lip);
            if (trail == null)
                return 0;

            var waypoints = CollectWaypoints(trail);
            if (waypoints.Count == 0)
                return 0;

            var last = waypoints[waypoints.Count - 1].position;
            var dist = Vector3.Distance(
                new Vector3(last.x, 0f, last.z),
                new Vector3(lip.x, 0f, lip.z));
            if (dist < 2.5f)
            {
                SnapWaypoint(waypoints[waypoints.Count - 1], lip);
                return 0;
            }

            if (dist > MaxTrailExtensionMeters)
                return 0;

            var segment = DensifySegment(last, lip, SegmentSpacingMeters);
            var added = 0;
            for (var i = 1; i < segment.Length; i++)
            {
                var p = segment[i];
                if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(ground.Terrain, p.x, p.z, out _))
                    break;

                p.y = CaveGroundPlacementUtility.SampleWalkableSurfaceWorldY(ground, p) + 0.1f;
                var wp = new GameObject($"Waypoint_{waypoints.Count + added}");
                CaveEditorUndo.RegisterCreated(wp, "Trail mouth extension");
                wp.transform.SetParent(trail, false);
                wp.transform.position = p;
                waypoints.Add(wp.transform);
                added++;
            }

            if (added > 0)
            {
                SurfaceTerrainRadialAuthor.FlattenTrailBench(ground.Terrain, segment, 3.2f, 0.55f);
                Debug.Log(
                    $"[CaveBuild] Extended surface trail '{trail.name}' with {added} waypoint(s) to cave mouth @ {lip}.");
            }

            return added;
        }

        static Transform PickTrailTowardMouth(Transform trailsRoot, Vector3 lip)
        {
            Transform best = null;
            var bestScore = float.MaxValue;
            foreach (Transform child in trailsRoot)
            {
                if (child == null || child.childCount < 1)
                    continue;

                var wps = CollectWaypoints(child);
                if (wps.Count == 0)
                    continue;

                var end = wps[wps.Count - 1].position;
                var score = Vector3.Distance(end, lip);
                if (score >= bestScore)
                    continue;
                bestScore = score;
                best = child;
            }

            return best ?? (trailsRoot.childCount > 0 ? trailsRoot.GetChild(0) : null);
        }

        static List<Transform> CollectWaypoints(Transform trail)
        {
            var list = new List<Transform>();
            for (var i = 0; i < trail.childCount; i++)
            {
                var c = trail.GetChild(i);
                if (c != null && c.name.StartsWith("Waypoint_"))
                    list.Add(c);
            }

            list.Sort((a, b) =>
            {
                var ai = ParseWaypointIndex(a.name);
                var bi = ParseWaypointIndex(b.name);
                return ai.CompareTo(bi);
            });
            return list;
        }

        static int ParseWaypointIndex(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith("Waypoint_"))
                return 0;
            return int.TryParse(name.Substring("Waypoint_".Length), out var idx) ? idx : 0;
        }

        static void SnapWaypoint(Transform waypoint, Vector3 lip)
        {
            if (waypoint == null)
                return;
            CaveEditorUndo.RecordObject(waypoint, "Snap trail to mouth");
            waypoint.position = lip;
        }

        static Vector3[] DensifySegment(Vector3 a, Vector3 b, float spacing)
        {
            var dist = Vector3.Distance(a, b);
            var steps = Mathf.Max(1, Mathf.CeilToInt(dist / Mathf.Max(1f, spacing)));
            var pts = new Vector3[steps + 1];
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                pts[i] = Vector3.Lerp(a, b, t);
            }

            return pts;
        }
    }
}
#endif
