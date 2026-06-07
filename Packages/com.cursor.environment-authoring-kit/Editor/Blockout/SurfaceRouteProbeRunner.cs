#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using UnityEngine;
using UnityEngine.AI;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Editor bot: walks surface trails/roads to cave mouth before underground route probe.
    /// </summary>
    public static class SurfaceRouteProbeRunner
    {
        public const string ReportPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildSurfaceRouteProbe.json";

        public sealed class SurfaceRouteReport
        {
            public bool Passed;
            public int WaypointCount;
            public int SurfaceStepsWalked;
            public bool ReachedCaveMouth;
            public readonly List<string> Issues = new();
        }

        public static SurfaceRouteReport Run(Transform caveRoot = null, bool lightweightBuildProbe = false)
        {
            var report = new SurfaceRouteReport();
            var ground = SceneGroundResolver.Resolve();
            if (ground?.Terrain != null && !lightweightBuildProbe)
                SurfaceTerrainPlayRegion.FlushAllSurfaceTerrains(ground.Terrain);

            var waypoints = CollectSurfaceWaypoints(ground, caveRoot, out var mouth);
            if (!lightweightBuildProbe)
                TryBakeSurfaceNav(ground, caveRoot, waypoints);
            report.WaypointCount = waypoints.Count;

            if (waypoints.Count < 2)
            {
                report.Issues.Add("[terrain_integration] Surface route has fewer than 2 waypoints — build surface world first.");
                report.Passed = false;
                return report;
            }

            var eyeHeight = 1.1f;
            var maxStepsPerFrame = lightweightBuildProbe ? 16 : 48;
            var maxSteps = Mathf.Min(waypoints.Count, maxStepsPerFrame);
            if (waypoints.Count > maxStepsPerFrame)
            {
                report.Issues.Add(
                    $"[terrain_integration] Route probe sampled {maxSteps}/{waypoints.Count} waypoints (editor pacing cap).");
            }

            for (var i = 0; i < maxSteps; i++)
            {
                var wpRaw = waypoints[i];
                var wp = GetSurfaceProbePoint(ground, wpRaw, eyeHeight);
                report.SurfaceStepsWalked++;

                if (!RaycastWalkFloor(ground, wpRaw, wp, out var hit))
                {
                    report.Issues.Add(
                        $"[terrain_integration] No walkable ground at surface waypoint {i} ({wpRaw.x:F1},{wpRaw.z:F1}).");
                    continue;
                }

                if (CountInvisibleNear(hit.point, 1.2f) > 0)
                {
                    report.Issues.Add(
                        $"[geometry_integrity] Invisible blocker near surface waypoint {i}.");
                }

                if (i > 0)
                {
                    var prevRaw = waypoints[i - 1];
                    var prev = GetSurfaceProbePoint(ground, prevRaw, eyeHeight);
                    if (!HasClearanceSegment(prev, wp, 1.4f))
                    {
                        report.Issues.Add(
                            $"[terrain_integration] Blocked segment between surface waypoint {i - 1} and {i}.");
                    }

                    ProbeNavSegment(
                        GetNavProbePoint(ground, prevRaw, eyeHeight),
                        GetNavProbePoint(ground, wpRaw, eyeHeight),
                        i,
                        report.Issues);
                }
            }

            if (mouth.HasValue)
            {
                var mouthEye = GetSurfaceProbePoint(ground, mouth.Value, eyeHeight);
                report.ReachedCaveMouth = RaycastWalkFloor(ground, mouth.Value, mouthEye, out _);
                if (!report.ReachedCaveMouth)
                {
                    report.Issues.Add("[ground_placement] Cave mouth waypoint not on walkable terrain.");
                }
            }
            else
            {
                report.Issues.Add("[ground_placement] Could not resolve cave mouth world position.");
            }

            if (!lightweightBuildProbe)
            {
                var surface = SurfacePlaytestValidator.Run(caveRoot);
                foreach (var line in surface.Issues)
                    report.Issues.Add(line);
            }

            report.Passed = lightweightBuildProbe
                ? report.Issues.Count == 0
                : report.Issues.Count == 0 && report.ReachedCaveMouth;
            return report;
        }

        public static void Export(SurfaceRouteReport report)
        {
            if (report == null)
                return;

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"passed\": {(report.Passed ? "true" : "false")},");
            sb.AppendLine($"  \"waypointCount\": {report.WaypointCount},");
            sb.AppendLine($"  \"surfaceStepsWalked\": {report.SurfaceStepsWalked},");
            sb.AppendLine($"  \"reachedCaveMouth\": {(report.ReachedCaveMouth ? "true" : "false")},");
            sb.AppendLine("  \"probeOrder\": \"surface_trails_roads_then_cave_mouth\",");
            sb.AppendLine("  \"issues\": [");
            for (var i = 0; i < report.Issues.Count; i++)
            {
                var comma = i < report.Issues.Count - 1 ? "," : "";
                sb.AppendLine($"    {JsonQuote(report.Issues[i])}{comma}");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
        }

        static void TryBakeSurfaceNav(SceneGroundInfo ground, Transform caveRoot, List<Vector3> waypoints)
        {
            if (ground?.Terrain == null)
                return;

            var sample = ground.HasAnchor
                ? ground.Anchor.position
                : new Vector3(ground.Bounds.center.x, ground.SurfaceY, ground.Bounds.center.z);
            var tileCount = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(ground.Terrain).Count;
            var sampleRadius = tileCount > 1 ? 12f : 6f;
            if (NavMesh.SamplePosition(sample + Vector3.up * 1.1f, out _, sampleRadius, NavMesh.AllAreas) &&
                NavMeshCoversTrailWaypoints(waypoints) &&
                TrailNavSegmentsComplete(waypoints))
                return;

            var env = Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            var surface = env != null ? env.transform.Find(SurfaceWorldPaths.RootName) : null;
            if (env != null)
                SurfaceNavMeshBaker.BakePhase(env.transform, ground.Terrain, surface, waypoints, out _);
        }

        static bool TrailNavSegmentsComplete(List<Vector3> waypoints)
        {
            if (waypoints == null || waypoints.Count < 2)
                return true;

            const float eye = 1.1f;
            const float sampleRadius = 8f;
            var check = Mathf.Min(waypoints.Count, 8);
            for (var i = 1; i < check; i++)
            {
                var from = waypoints[i - 1] + Vector3.up * eye;
                var to = waypoints[i] + Vector3.up * eye;
                if (!NavMesh.SamplePosition(from, out var startHit, sampleRadius, NavMesh.AllAreas) ||
                    !NavMesh.SamplePosition(to, out var endHit, sampleRadius, NavMesh.AllAreas))
                    return false;

                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete)
                    return false;
            }

            return true;
        }

        static bool NavMeshCoversTrailWaypoints(List<Vector3> waypoints)
        {
            if (waypoints == null || waypoints.Count == 0)
                return true;

            const float eye = 1.1f;
            const float sampleRadius = 4f;
            var check = Mathf.Min(waypoints.Count, 6);
            for (var i = 0; i < check; i++)
            {
                if (!NavMesh.SamplePosition(waypoints[i] + Vector3.up * eye, out _, sampleRadius, NavMesh.AllAreas))
                    return false;
            }

            return true;
        }

        static bool TrySampleNavPoint(Vector3 point, float primaryRadius, float fallbackRadius, out NavMeshHit hit)
        {
            if (NavMesh.SamplePosition(point, out hit, primaryRadius, NavMesh.AllAreas))
                return true;
            return NavMesh.SamplePosition(point, out hit, fallbackRadius, NavMesh.AllAreas);
        }

        static void ProbeNavSegment(Vector3 from, Vector3 to, int index, List<string> issues)
        {
            var primaryRadius = index >= 8 ? 10f : 4f;
            const float fallbackRadius = 8f;
            if (!TrySampleNavPoint(from, primaryRadius, fallbackRadius, out var startHit))
            {
                issues.Add($"[terrain_integration] NavMesh missing at surface waypoint {index - 1}.");
                return;
            }

            if (!TrySampleNavPoint(to, primaryRadius, fallbackRadius, out var endHit))
            {
                issues.Add($"[terrain_integration] NavMesh missing at surface waypoint {index}.");
                return;
            }

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete)
            {
                issues.Add(
                    $"[terrain_integration] Human tester cannot walk NavMesh {index - 1}→{index} ({path.status}) — smooth terrain or fix trail.");
            }
        }

        /// <summary>Trail/road/mouth waypoints using an explicit surface root (trail repair + probe).</summary>
        public static List<Vector3> CollectSurfaceWaypointsForRepair(
            SceneGroundInfo ground,
            Transform caveRoot,
            Transform surfaceRoot)
        {
            var list = new List<Vector3>();
            if (surfaceRoot == null)
                return list;

            AppendTrailWaypoints(surfaceRoot.Find(SurfaceWorldPaths.TrailsName), list);
            AppendTrailWaypoints(surfaceRoot.Find(SurfaceWorldPaths.RoadsName), list);

            var openings = surfaceRoot.Find(SurfaceWorldPaths.CaveOpeningsName);
            if (openings != null && openings.childCount > 0)
                list.Add(openings.GetChild(0).position);

            if (caveRoot != null)
            {
                var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot);
                if (mouth.sqrMagnitude > 0.01f)
                {
                    // Walk probes stay on the surface lip — interior mouth markers sit inside entrance colliders.
                    var approach = ProjectMouthToSurfaceApproach(ground, mouth);
                    if (list.Count > 0)
                    {
                        var last = list[list.Count - 1];
                        var xzGap = Vector2.Distance(
                            new Vector2(last.x, last.z),
                            new Vector2(approach.x, approach.z));
                        if (xzGap > 2.5f)
                            list.Add(approach);
                    }
                    else
                    {
                        list.Add(approach);
                    }
                }
            }

            if (list.Count == 0 && ground != null && ground.HasAnchor)
                list.Add(ground.Bounds.center);

            return list;
        }

        /// <summary>True when a collider overlaps the surface walk height band near a probe waypoint.</summary>
        public static bool IntersectsSurfaceWalkBand(Collider col, Vector3 walkPoint, float surfaceY)
        {
            if (col == null)
                return false;

            const float bandAbove = 4f;
            const float bandBelow = 6f;
            var minY = surfaceY - bandBelow;
            var maxY = surfaceY + bandAbove;
            if (col.bounds.max.y < minY || col.bounds.min.y > maxY)
                return false;

            var closest = col.bounds.ClosestPoint(walkPoint);
            var xzDist = Vector2.Distance(
                new Vector2(closest.x, closest.z),
                new Vector2(walkPoint.x, walkPoint.z));
            var horizontal = Mathf.Max(5.5f, col.bounds.extents.x, col.bounds.extents.z);
            return xzDist <= horizontal;
        }

        static List<Vector3> CollectSurfaceWaypoints(
            SceneGroundInfo ground,
            Transform caveRoot,
            out Vector3? mouthWorld)
        {
            mouthWorld = null;
            var env = Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            var surface = env != null ? env.transform.Find(SurfaceWorldPaths.RootName) : null;
            if (surface == null)
                return new List<Vector3>();

            var list = CollectSurfaceWaypointsForRepair(ground, caveRoot, surface);
            if (caveRoot != null)
            {
                var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot);
                if (mouth.sqrMagnitude > 0.01f)
                    mouthWorld = mouth;
            }

            return list;
        }

        static void AppendTrailWaypoints(Transform root, List<Vector3> list)
        {
            if (root == null)
                return;

            foreach (Transform trail in root)
            {
                if (trail == null)
                    continue;
                foreach (Transform child in trail)
                {
                    if (child != null && child.name.StartsWith("Waypoint_"))
                        list.Add(child.position);
                }
            }
        }

        const float ProbeRayDownMeters = 64f;

        static bool RaycastWalkFloor(
            SceneGroundInfo ground,
            Vector3 waypointXZ,
            Vector3 from,
            out RaycastHit hit)
        {
            var hits = Physics.RaycastAll(from, Vector3.down, ProbeRayDownMeters, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var candidate in hits)
            {
                if (candidate.collider == null || candidate.collider.isTrigger)
                    continue;
                if (IsInvisibleSolidBlocker(candidate.collider))
                    continue;

                var terrain = candidate.collider.GetComponent<TerrainCollider>();
                if (terrain != null)
                {
                    hit = candidate;
                    return true;
                }

                if (candidate.collider.GetComponentInParent<EnvironmentAuthoringKit.EnvironmentRoot>() != null)
                {
                    hit = candidate;
                    return true;
                }
            }

            if (TrySampleTerrainWalkHit(ground, waypointXZ, from, out hit))
                return true;

            hit = default;
            return false;
        }

        /// <summary>Editor fallback when terrain heightmaps changed but Physics queries miss the tile collider.</summary>
        static bool TrySampleTerrainWalkHit(
            SceneGroundInfo ground,
            Vector3 waypointXZ,
            Vector3 from,
            out RaycastHit hit)
        {
            hit = default;
            if (ground?.Terrain == null)
                return false;

            if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(ground.Terrain, waypointXZ.x, waypointXZ.z, out var tile))
                return false;

            var collider = tile.GetComponent<TerrainCollider>();
            if (collider == null)
                return false;

            var terrainY = tile.SampleHeight(waypointXZ) + tile.transform.position.y;
            if (from.y < terrainY - 0.05f)
                return false;

            return collider.Raycast(new Ray(from, Vector3.down), out hit, ProbeRayDownMeters);
        }

        static bool IsWalkSurfaceCollider(Collider col) =>
            col != null && (col is TerrainCollider || col.GetComponent<Terrain>() != null);

        static bool IsInvisibleSolidBlocker(Collider col) =>
            col != null && !col.isTrigger && !IsWalkSurfaceCollider(col) &&
            !CaveRendererVisibility.HasVisibleRenderer(col, true);

        static bool HasClearanceSegment(Vector3 from, Vector3 to, float radius)
        {
            var dir = to - from;
            var dist = dir.magnitude;
            if (dist < 0.01f)
                return true;
            if (!Physics.SphereCast(
                    from, radius * 0.35f, dir.normalized, out var hit, dist, ~0, QueryTriggerInteraction.Ignore))
                return true;

            if (IsWalkSurfaceCollider(hit.collider))
                return true;
            if (IsInvisibleSolidBlocker(hit.collider))
                return false;
            return false;
        }

        static int CountInvisibleNear(Vector3 worldPoint, float radius)
        {
            var count = 0;
            foreach (var col in Physics.OverlapSphere(worldPoint, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (col == null || col.isTrigger || IsWalkSurfaceCollider(col))
                    continue;
                if (IsInvisibleSolidBlocker(col))
                    count++;
            }

            return count;
        }

        static Vector3 GetSurfaceProbePoint(SceneGroundInfo ground, Vector3 waypoint, float eyeHeight)
        {
            // Waypoints are authored transforms and may carry stale anchor Y on multi-tile builds.
            // Sample the tile under XZ so carved bowls below SceneGroundInfo.SurfaceY still raycast hit.
            if (ground == null)
                return waypoint + Vector3.up * eyeHeight;

            var baseY = ResolveProbeBaseY(ground, waypoint);
            return new Vector3(waypoint.x, baseY + eyeHeight + 6f, waypoint.z);
        }

        /// <summary>Eye-height sample for NavMesh — must match <see cref="NavMeshCoversTrailWaypoints"/> (not raycast origin +6m).</summary>
        static Vector3 GetNavProbePoint(SceneGroundInfo ground, Vector3 waypoint, float eyeHeight)
        {
            if (ground == null)
                return waypoint + Vector3.up * eyeHeight;

            var baseY = ResolveProbeBaseY(ground, waypoint);
            return new Vector3(waypoint.x, baseY + eyeHeight, waypoint.z);
        }

        static float ResolveProbeBaseY(SceneGroundInfo ground, Vector3 waypoint)
        {
            if (ground.Terrain != null &&
                SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(ground.Terrain, waypoint.x, waypoint.z, out var tile))
            {
                var terrainY = tile.SampleHeight(waypoint) + tile.transform.position.y;
                return Mathf.Max(terrainY, waypoint.y);
            }

            var sampled = CaveGroundPlacementUtility.SampleSurfaceWorldY(ground, waypoint);
            return Mathf.Max(sampled, waypoint.y, ground.SurfaceY);
        }

        /// <summary>Surface trail terminus at mouth XZ — avoids routing probes through entrance solid geometry.</summary>
        public static Vector3 ProjectMouthToSurfaceApproach(SceneGroundInfo ground, Vector3 mouthWorld)
        {
            if (ground?.Terrain == null)
                return mouthWorld;

            var surfaceY = CaveGroundPlacementUtility.SampleSurfaceWorldY(ground, mouthWorld);
            if (float.IsNaN(surfaceY))
                surfaceY = ground.SurfaceY;

            return new Vector3(mouthWorld.x, surfaceY + 0.14f, mouthWorld.z);
        }

        static string JsonQuote(string s) =>
            "\"" + (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
#endif
