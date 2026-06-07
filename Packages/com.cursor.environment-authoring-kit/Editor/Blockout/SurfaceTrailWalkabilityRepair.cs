#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Trail rung fixes: snap waypoints to terrain, bench corridors, strip invisible blockers, rebake surface NavMesh.
    /// EvoTest-style route repair — primary trail to cave mouth only (see SurfaceRouteProbeRunner).
    /// </summary>
    public static class SurfaceTrailWalkabilityRepair
    {
        const float TrailHalfWidthMeters = 5.5f;
        const float TrailFlattenStrength = 0.28f;
        const float WaypointGroundOffsetMeters = 0.14f;
        const float CorridorBlockerRadiusMeters = 4.5f;
        const float WaypointBlockerRadiusMeters = 4f;
        const float NavFailHalfWidthMeters = 10f;
        const float NavFailFlattenStrength = 0.58f;
        const int MaxRepairPasses = 3;
        const int MaxQueuedRepairPasses = 3;

        sealed class QueuedRepairSession
        {
            public SceneGroundInfo Ground;
            public WorldGenerationRequest Request;
            public Transform SurfaceRoot;
            public Transform CaveRoot;
            public Terrain Terrain;
            public Vector3 Center;
            public float Extent;
            public int Pass;
            public int TotalStripped;
            public int TrailPolylines;
            public SurfaceRouteProbeRunner.SurfaceRouteReport Probe;
            public System.Action<bool, string> OnComplete;
            public bool Prepared;
        }

        static List<Terrain> CollectTerrains(Terrain mainTerrain)
        {
            var list = new List<Terrain>();
            if (mainTerrain == null)
                return list;

            foreach (var t in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain))
            {
                if (t != null && t.terrainData != null)
                    list.Add(t);
            }

            return list;
        }

        static bool TrySampleSurfaceY(List<Terrain> terrains, Vector3 world, out float y)
        {
            y = world.y;
            if (terrains == null || terrains.Count == 0)
                return false;

            for (var i = 0; i < terrains.Count; i++)
            {
                var t = terrains[i];
                if (t == null || t.terrainData == null)
                    continue;
                var tp = t.transform.position;
                var size = t.terrainData.size;
                if (world.x < tp.x || world.z < tp.z || world.x > tp.x + size.x || world.z > tp.z + size.z)
                    continue;

                y = t.SampleHeight(world) + tp.y;
                return true;
            }

            return false;
        }

        static void FlattenTrailBenchMulti(Terrain mainTerrain, Vector3[] pts, float halfWidthMeters, float strength)
        {
            if (mainTerrain == null || pts == null || pts.Length == 0)
                return;

            foreach (var t in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain))
            {
                if (t == null || t.terrainData == null)
                    continue;
                SurfaceTerrainRadialAuthor.FlattenTrailBench(t, pts, halfWidthMeters, strength);
            }
        }

        /// <summary>Snap primary trail, bench corridor, strip route blockers before route probe / grading.</summary>
        public static void PreparePrimaryRouteForProbe(
            SceneGroundInfo ground,
            Transform surfaceRoot,
            Transform caveRoot)
        {
            if (ground?.Terrain == null || surfaceRoot == null)
                return;

            var terrain = ground.Terrain;
            var trailsRoot = surfaceRoot.Find(SurfaceWorldPaths.TrailsName);
            if (trailsRoot == null)
                return;

            var target = ResolveRouteTarget(surfaceRoot, caveRoot, ground);
            var primary = SelectPrimaryTrail(trailsRoot, target);
            if (primary != null)
            {
                SnapTrailWaypointsToTerrain(CollectTerrains(terrain), primary);
                var pts = CollectWaypointPolyline(primary);
                if (pts != null && pts.Length >= 1)
                {
                    FlattenTrailBenchMulti(terrain, pts, TrailHalfWidthMeters, TrailFlattenStrength);
                    if (target.sqrMagnitude > 0.01f && pts.Length >= 1)
                    {
                        var mouthCorridor = DensifySegment(pts[pts.Length - 1], target, 6f);
                        FlattenTrailBenchMulti(
                            terrain, mouthCorridor, TrailHalfWidthMeters + 1.5f, TrailFlattenStrength + 0.1f);
                    }
                }
            }

            FlattenTerrainUnderProbeWaypoints(terrain, surfaceRoot, caveRoot, ground);
            FlattenProbeRouteTailSegments(terrain, surfaceRoot, caveRoot, ground);
            SnapSurfaceWaterToTerrain(surfaceRoot, ground);
            StripPrimaryRouteBlockers(surfaceRoot, caveRoot, ground);
            SurfaceTerrainPlayRegion.FlushAllSurfaceTerrains(terrain);

            // Intentionally skip NavMesh bake here; trail fixes now defer bake to the final step
            // to avoid repeated heavy rebakes during each grading/fix iteration.
        }

        public static bool TryRepair(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            Transform caveRoot,
            out string message)
        {
            message = string.Empty;
            if (ground?.Terrain == null || surfaceRoot == null || request == null)
            {
                message = "Missing terrain, surface root, or request.";
                return false;
            }

            var trailsRoot = surfaceRoot.Find(SurfaceWorldPaths.TrailsName);
            if (trailsRoot == null || trailsRoot.childCount < 1)
            {
                request.SurfaceIncludeTrails = true;
                var ensured = SurfaceWorldGenerator.EnsureWalkTrails(ground, request, surfaceRoot);
                if (ensured < 1)
                {
                    message = "No trail splines — regeneration failed.";
                    return false;
                }
            }

            var terrain = ground.Terrain;
            var extent = request.SurfaceExtentMeters > 10f ? request.SurfaceExtentMeters : 220f;
            var center = ground.HasAnchor
                ? ground.Anchor.position
                : new Vector3(ground.Bounds.center.x, ground.SurfaceY, ground.Bounds.center.z);

            if (caveRoot != null)
            {
                CaveGroundPlacementUtility.TryRepairLockedGroundPlacement(caveRoot, ground, out _);
            }

            var totalStripped = 0;
            var trailPolylines = 0;
            SurfaceRouteProbeRunner.SurfaceRouteReport probe = null;

            PreparePrimaryRouteForProbe(ground, surfaceRoot, caveRoot);

            for (var pass = 0; pass < MaxRepairPasses; pass++)
            {
                totalStripped += RunRepairPass(
                    ground, request, surfaceRoot, caveRoot, terrain, center, extent, ref trailPolylines);

                totalStripped += StripPrimaryRouteBlockers(surfaceRoot, caveRoot, ground);

                probe = SurfaceRouteProbeRunner.Run(caveRoot);
                if (probe.Passed)
                    break;

                FlattenNavFailedSegments(terrain, surfaceRoot, caveRoot, ground);
                FlattenTerrainUnderProbeWaypoints(terrain, surfaceRoot, caveRoot, ground);
                FlattenProbeRouteTailSegments(terrain, surfaceRoot, caveRoot, ground);
                SnapSurfaceWaterToTerrain(surfaceRoot, ground);
                SurfaceTerrainPlayRegion.FlushAllSurfaceTerrains(terrain);
                // Defer NavMesh rebake until final step for this repair attempt.
            }

            var navMsg = string.Empty;
            var envRoot = Object.FindAnyObjectByType<EnvironmentRoot>();
            if (envRoot != null)
            {
                var finalWaypoints =
                    SurfaceRouteProbeRunner.CollectSurfaceWaypointsForRepair(ground, caveRoot, surfaceRoot);
                SurfaceNavMeshBaker.BakePhase(envRoot.transform, terrain, surfaceRoot, finalWaypoints, out navMsg);
            }

            // Re-probe after the final bake since intermediate probes may have used stale nav data.
            probe = SurfaceRouteProbeRunner.Run(caveRoot);

            message =
                $"Trail repair ({MaxRepairPasses} pass max) — stripped {totalStripped} blocker(s), " +
                $"benched {trailPolylines} trail polyline(s). {navMsg}";
            if (probe != null)
                message += probe.Passed ? " Route probe PASS." : $" Route probe: {probe.Issues.Count} issue(s).";

            return probe != null && probe.Passed;
        }

        /// <summary>
        /// Non-blocking variant used by terrain meat loop: spreads repair across multiple editor frames.
        /// This avoids freezing Unity when NavMesh + heightmap updates are heavy on multi-tile worlds.
        /// </summary>
        public static void QueueTryRepair(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            Transform caveRoot,
            System.Action<bool, string> onComplete)
        {
            if (onComplete == null)
                return;

            if (ground?.Terrain == null || surfaceRoot == null || request == null)
            {
                onComplete(false, "Missing terrain, surface root, or request.");
                return;
            }

            var extent = request.SurfaceExtentMeters > 10f ? request.SurfaceExtentMeters : 220f;
            var center = ground.HasAnchor
                ? ground.Anchor.position
                : new Vector3(ground.Bounds.center.x, ground.SurfaceY, ground.Bounds.center.z);

            var session = new QueuedRepairSession
            {
                Ground = ground,
                Request = request,
                SurfaceRoot = surfaceRoot,
                CaveRoot = caveRoot,
                Terrain = ground.Terrain,
                Extent = extent,
                Center = center,
                OnComplete = onComplete,
            };

            CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunQueuedRepairStep(session));
        }

        static void RunQueuedRepairStep(QueuedRepairSession s)
        {
            if (s?.Ground?.Terrain == null || s.SurfaceRoot == null || s.Request == null)
            {
                s?.OnComplete?.Invoke(false, "Trail repair aborted (missing state).");
                return;
            }

            // Prepare once (may create trails + bake an initial surface navmesh).
            if (!s.Prepared)
            {
                s.Prepared = true;
                try
                {
                    PreparePrimaryRouteForProbe(s.Ground, s.SurfaceRoot, s.CaveRoot);
                }
                catch (System.Exception ex)
                {
                    s.OnComplete(false, "Trail repair prepare failed: " + ex.Message);
                    return;
                }
            }

            if (s.Pass >= MaxQueuedRepairPasses)
            {
                FinishQueuedRepair(s);
                return;
            }

            // One pass per frame.
            CaveBuildActionPacing.ScheduleHeavyChain(
                () =>
                {
                    try
                    {
                        s.TotalStripped += RunRepairPass(
                            s.Ground,
                            s.Request,
                            s.SurfaceRoot,
                            s.CaveRoot,
                            s.Terrain,
                            s.Center,
                            s.Extent,
                            ref s.TrailPolylines);

                        s.TotalStripped += StripPrimaryRouteBlockers(s.SurfaceRoot, s.CaveRoot, s.Ground);
                        s.Probe = SurfaceRouteProbeRunner.Run(s.CaveRoot);

                        if (s.Probe != null && s.Probe.Passed)
                        {
                            FinishQueuedRepair(s);
                            return;
                        }

                        FlattenNavFailedSegments(s.Terrain, s.SurfaceRoot, s.CaveRoot, s.Ground);
                        FlattenTerrainUnderProbeWaypoints(s.Terrain, s.SurfaceRoot, s.CaveRoot, s.Ground);
                        FlattenProbeRouteTailSegments(s.Terrain, s.SurfaceRoot, s.CaveRoot, s.Ground);
                        SnapSurfaceWaterToTerrain(s.SurfaceRoot, s.Ground);
                        SurfaceTerrainPlayRegion.FlushAllSurfaceTerrains(s.Terrain);

                        // Defer NavMesh rebake until final step for this queued repair attempt.
                    }
                    catch (System.Exception ex)
                    {
                        s.OnComplete(false, "Trail repair pass failed: " + ex.Message);
                        return;
                    }

                    s.Pass++;
                    CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunQueuedRepairStep(s));
                },
                CaveBuildPipelineDomains.SurfaceQueueLabel($"trail_walkability pass {s.Pass + 1}/{MaxQueuedRepairPasses}"));
        }

        static void FinishQueuedRepair(QueuedRepairSession s)
        {
            var navMsg = string.Empty;
            var envRoot = Object.FindAnyObjectByType<EnvironmentRoot>();
            if (envRoot != null && s?.Terrain != null)
            {
                var finalWaypoints =
                    SurfaceRouteProbeRunner.CollectSurfaceWaypointsForRepair(s.Ground, s.CaveRoot, s.SurfaceRoot);
                SurfaceNavMeshBaker.BakePhase(envRoot.transform, s.Terrain, s.SurfaceRoot, finalWaypoints, out navMsg);
            }

            // Re-probe after final bake so success reflects current navmesh.
            s.Probe = SurfaceRouteProbeRunner.Run(s.CaveRoot);

            var ok = s?.Probe != null && s.Probe.Passed;
            var msg =
                $"Trail repair (queued) — stripped {s?.TotalStripped ?? 0} blocker(s), " +
                $"benched {s?.TrailPolylines ?? 0} trail polyline(s). {navMsg}";
            if (s?.Probe != null)
                msg += ok ? " Route probe PASS." : $" Route probe: {s.Probe.Issues.Count} issue(s).";

            s?.OnComplete?.Invoke(ok, msg);
        }

        static int RunRepairPass(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            Transform caveRoot,
            Terrain terrain,
            Vector3 center,
            float extent,
            ref int trailPolylines)
        {
            var stripped = StripInvisibleSolidColliders(surfaceRoot);
            if (caveRoot != null)
            {
                CaveEntranceVolumeBuilder.StripEntranceOnionSlabs(caveRoot, ground);
                stripped += CaveInvisibleColliderUtility.StripForAdventure(caveRoot);
                stripped += StripNavFloorCollidersNearSurface(caveRoot, ground.SurfaceY);
            }

            SnapCaveOpeningsToTerrain(terrain, surfaceRoot);
            SnapSurfaceWaterToTerrain(surfaceRoot, ground);

            var trailsRoot = surfaceRoot.Find(SurfaceWorldPaths.TrailsName);
            var routeTarget = ResolveRouteTarget(surfaceRoot, caveRoot, ground);
            var corridorPoints = new List<Vector3>();

            if (trailsRoot != null)
            {
                var terrains = CollectTerrains(terrain);
                foreach (Transform trail in trailsRoot)
                {
                    if (trail == null)
                        continue;
                    SnapTrailWaypointsToTerrain(terrains, trail);
                    var pts = CollectWaypointPolyline(trail);
                    if (pts == null || pts.Length < 2)
                        continue;
                    corridorPoints.AddRange(pts);
                    FlattenTrailBenchMulti(terrain, pts, TrailHalfWidthMeters, TrailFlattenStrength);
                    trailPolylines++;
                }

                var primary = SelectPrimaryTrail(trailsRoot, routeTarget);
                if (primary != null)
                {
                    var primaryPts = CollectWaypointPolyline(primary);
                    if (primaryPts != null && primaryPts.Length >= 1)
                    {
                        corridorPoints.AddRange(primaryPts);
                        if (routeTarget.sqrMagnitude > 0.01f)
                        {
                            var mouthCorridor = DensifySegment(
                                primaryPts[primaryPts.Length - 1], routeTarget, 6f);
                            FlattenTrailBenchMulti(
                                terrain, mouthCorridor, TrailHalfWidthMeters + 1.5f, TrailFlattenStrength + 0.1f);
                            corridorPoints.Add(routeTarget);
                            ExtendPrimaryTrailTowardTarget(primary, terrain, routeTarget);
                        }
                    }
                }
            }

            if (corridorPoints.Count >= 1)
            {
                stripped += StripInvisibleAlongCorridor(
                    corridorPoints, CorridorBlockerRadiusMeters, caveRoot, surfaceRoot, ground.SurfaceY);
                stripped += StripInvisibleAlongCorridor(
                    corridorPoints, WaypointBlockerRadiusMeters, caveRoot, surfaceRoot, ground.SurfaceY);
            }

            SurfaceTerrainRefinement.TryRefineRoadsAndWater(
                terrain, surfaceRoot, center, extent, request.Seed, out _);

            SurfaceTerrainPlayRegion.FlushAllSurfaceTerrains(terrain);

            // Defer NavMesh rebake until final step for this repair pass.

            return stripped;
        }

        /// <summary>Trail whose end is closest to the cave opening / mouth (primary walk route).</summary>
        public static Transform SelectPrimaryTrail(Transform trailsRoot, Vector3 routeTarget)
        {
            if (trailsRoot == null || trailsRoot.childCount == 0)
                return null;

            Transform best = null;
            var bestDist = float.MaxValue;
            foreach (Transform trail in trailsRoot)
            {
                if (trail == null)
                    continue;
                var pts = CollectWaypointPolyline(trail);
                if (pts == null || pts.Length < 2)
                    continue;
                var endDist = Vector3.Distance(pts[pts.Length - 1], routeTarget);
                if (endDist < bestDist)
                {
                    bestDist = endDist;
                    best = trail;
                }
            }

            return best ?? (trailsRoot.childCount > 0 ? trailsRoot.GetChild(0) : null);
        }

        public static Vector3 ResolveRouteTarget(Transform surfaceRoot, Transform caveRoot) =>
            ResolveRouteTarget(surfaceRoot, caveRoot, null);

        public static Vector3 ResolveRouteTarget(Transform surfaceRoot, Transform caveRoot, SceneGroundInfo ground)
        {
            var near = ground != null && ground.HasAnchor
                ? ground.AnchorWorld
                : surfaceRoot != null
                    ? surfaceRoot.position
                    : Vector3.zero;
            var opening = ResolveNearestCaveOpening(surfaceRoot, near, float.MaxValue);
            if (caveRoot != null)
            {
                var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot, ground);
                if (mouth.sqrMagnitude > 0.01f)
                {
                    var lip = SurfaceRouteProbeRunner.ProjectMouthToSurfaceApproach(ground, mouth);
                    if (IsWalkableSurfacePoint(lip, ground))
                        return lip;
                }
            }

            if (opening.sqrMagnitude > 0.01f)
                return opening;

            return surfaceRoot != null ? surfaceRoot.position : Vector3.zero;
        }

        public static void AppendPrimaryTrailWaypoints(Transform surfaceRoot, Transform caveRoot, List<Vector3> list)
        {
            if (surfaceRoot == null || list == null)
                return;

            var trails = surfaceRoot.Find(SurfaceWorldPaths.TrailsName);
            if (trails == null)
                return;

            var ground = SceneGroundResolver.Resolve();
            var target = ResolveRouteTarget(surfaceRoot, caveRoot, ground);
            var primary = SelectPrimaryTrail(trails, target);
            if (primary != null)
                AppendTrailWaypoints(primary, list);
            else
                AppendAllTrailWaypoints(trails, list);
        }

        public static void AppendTrailWaypoints(Transform trailRoot, List<Vector3> list)
        {
            if (trailRoot == null || list == null)
                return;

            foreach (Transform child in trailRoot)
            {
                if (child != null && child.name.StartsWith("Waypoint_", System.StringComparison.Ordinal))
                    list.Add(child.position);
            }
        }

        static void AppendAllTrailWaypoints(Transform trailsRoot, List<Vector3> list)
        {
            foreach (Transform trail in trailsRoot)
                AppendTrailWaypoints(trail, list);
        }

        static void SnapTrailWaypointsToTerrain(Terrain terrain, Transform trail)
        {
            if (terrain == null || trail == null)
                return;

            var terrains = CollectTerrains(terrain);
            for (var i = 0; i < trail.childCount; i++)
            {
                var wp = trail.GetChild(i);
                if (wp == null || !wp.name.StartsWith("Waypoint_", System.StringComparison.Ordinal))
                    continue;

                Undo.RecordObject(wp, "Snap trail waypoint");
                var p = wp.position;
                if (TrySampleSurfaceY(terrains, p, out var surfaceY))
                    p.y = surfaceY + WaypointGroundOffsetMeters;
                else
                    p.y = terrain.SampleHeight(p) + terrain.transform.position.y + WaypointGroundOffsetMeters;
                wp.position = p;
            }
        }

        static void SnapTrailWaypointsToTerrain(List<Terrain> terrains, Transform trail)
        {
            if (terrains == null || terrains.Count == 0 || trail == null)
                return;

            for (var i = 0; i < trail.childCount; i++)
            {
                var wp = trail.GetChild(i);
                if (wp == null || !wp.name.StartsWith("Waypoint_", System.StringComparison.Ordinal))
                    continue;

                Undo.RecordObject(wp, "Snap trail waypoint");
                var p = wp.position;
                if (TrySampleSurfaceY(terrains, p, out var surfaceY))
                    p.y = surfaceY + WaypointGroundOffsetMeters;
                else if (terrains[0] != null &&
                         SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(terrains[0], p.x, p.z, out var tile))
                    p.y = tile.SampleHeight(p) + tile.transform.position.y + WaypointGroundOffsetMeters;
                wp.position = p;
            }
        }

        static void SnapCaveOpeningsToTerrain(Terrain terrain, Transform surfaceRoot)
        {
            if (terrain == null || surfaceRoot == null)
                return;

            var openings = surfaceRoot.Find(SurfaceWorldPaths.CaveOpeningsName);
            if (openings == null)
                return;

            var terrains = CollectTerrains(terrain);
            foreach (Transform opening in openings)
            {
                if (opening == null)
                    continue;

                Undo.RecordObject(opening, "Snap cave opening");
                var p = opening.position;
                if (TrySampleSurfaceY(terrains, p, out var surfaceY))
                    p.y = surfaceY + 0.05f;
                else
                    p.y = terrain.SampleHeight(p) + terrain.transform.position.y + 0.05f;
                opening.position = p;
            }
        }

        static void ExtendPrimaryTrailTowardTarget(Transform trail, Terrain terrain, Vector3 routeTarget)
        {
            if (trail == null || terrain == null)
                return;

            var pts = CollectWaypointPolyline(trail);
            if (pts == null || pts.Length < 1)
                return;

            var last = pts[pts.Length - 1];
            if (Vector3.Distance(last, routeTarget) < 4f)
                return;

            if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(terrain, routeTarget.x, routeTarget.z, out _))
                return;

            var idx = pts.Length;
            var wp = new GameObject($"Waypoint_{idx}");
            CaveEditorUndo.RegisterCreated(wp, "Trail extension");
            wp.transform.SetParent(trail, false);
            var terrains = CollectTerrains(terrain);
            var p = routeTarget;
            if (TrySampleSurfaceY(terrains, p, out var surfaceY))
                p.y = surfaceY + WaypointGroundOffsetMeters;
            else
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y + WaypointGroundOffsetMeters;
            wp.transform.position = p;
        }

        static void FlattenNavFailedSegments(
            Terrain terrain,
            Transform surfaceRoot,
            Transform caveRoot,
            SceneGroundInfo ground)
        {
            if (terrain == null || surfaceRoot == null)
                return;

            var probeWaypoints =
                SurfaceRouteProbeRunner.CollectSurfaceWaypointsForRepair(ground, caveRoot, surfaceRoot);
            if (probeWaypoints.Count >= 2)
            {
                for (var i = 1; i < probeWaypoints.Count; i++)
                {
                    if (NavSegmentComplete(probeWaypoints[i - 1], probeWaypoints[i]))
                        continue;

                    var segment = DensifySegment(probeWaypoints[i - 1], probeWaypoints[i], 5f);
                    FlattenTrailBenchMulti(
                        terrain, segment, NavFailHalfWidthMeters, NavFailFlattenStrength);
                }
            }

            var trails = surfaceRoot.Find(SurfaceWorldPaths.TrailsName);
            if (trails == null)
                return;

            var target = ResolveRouteTarget(surfaceRoot, caveRoot, ground);
            var primary = SelectPrimaryTrail(trails, target);
            if (primary == null)
                return;

            var pts = CollectWaypointPolyline(primary);
            if (pts == null || pts.Length < 2)
                return;

            for (var i = 1; i < pts.Length; i++)
            {
                if (NavSegmentComplete(pts[i - 1], pts[i]))
                    continue;

                var segment = DensifySegment(pts[i - 1], pts[i], 5f);
                FlattenTrailBenchMulti(
                    terrain, segment, NavFailHalfWidthMeters, NavFailFlattenStrength);
            }

            if (target.sqrMagnitude > 0.01f)
            {
                var mouthSeg = DensifySegment(pts[pts.Length - 1], target, 5f);
                FlattenTrailBenchMulti(
                    terrain, mouthSeg, NavFailHalfWidthMeters, NavFailFlattenStrength);
            }
        }

        static bool NavSegmentComplete(Vector3 from, Vector3 to)
        {
            const float eye = 1.1f;
            const float sampleRadius = 8f;
            if (!NavMesh.SamplePosition(from + Vector3.up * eye, out var startHit, sampleRadius, NavMesh.AllAreas))
                return false;
            if (!NavMesh.SamplePosition(to + Vector3.up * eye, out var endHit, sampleRadius, NavMesh.AllAreas))
                return false;

            var path = new NavMeshPath();
            return NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, path) &&
                   path.status == NavMeshPathStatus.PathComplete;
        }

        static Vector3[] DensifySegment(Vector3 a, Vector3 b, float spacingMeters)
        {
            var dist = Vector3.Distance(a, b);
            var steps = Mathf.Max(2, Mathf.CeilToInt(dist / Mathf.Max(1f, spacingMeters)) + 1);
            var pts = new Vector3[steps];
            for (var i = 0; i < steps; i++)
            {
                var t = i / (float)(steps - 1);
                pts[i] = Vector3.Lerp(a, b, t);
            }

            return pts;
        }

        static Vector3 ResolveNearestCaveOpening(Transform surfaceRoot, Vector3 near, float maxDist)
        {
            if (surfaceRoot == null)
                return Vector3.zero;

            var openings = surfaceRoot.Find(SurfaceWorldPaths.CaveOpeningsName);
            if (openings == null || openings.childCount == 0)
                return Vector3.zero;

            Vector3 best = Vector3.zero;
            var bestDist = maxDist;
            foreach (Transform child in openings)
            {
                if (child == null)
                    continue;
                var d = Vector3.Distance(child.position, near);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = child.position;
                }
            }

            return best;
        }

        static bool IsWalkableSurfacePoint(Vector3 world, SceneGroundInfo ground)
        {
            if (ground?.Terrain == null)
                return world.y <= 24f;

            var surfaceY = ground.SurfaceY;
            return world.y <= surfaceY + 4f && world.y >= surfaceY - 6f;
        }

        static int StripNavFloorCollidersNearSurface(Transform caveRoot, float surfaceY)
        {
            if (caveRoot == null)
                return 0;

            var removed = 0;
            foreach (var col in caveRoot.GetComponentsInChildren<Collider>(true))
            {
                if (col == null || col.isTrigger)
                    continue;
                if (!col.gameObject.name.Contains("NavFloor"))
                    continue;
                if (col.bounds.max.y < surfaceY - 1.5f)
                    continue;

                CaveEditorUndo.DestroyImmediate(col);
                removed++;
            }

            return removed;
        }

        static int StripPrimaryRouteBlockers(Transform surfaceRoot, Transform caveRoot, SceneGroundInfo ground)
        {
            if (surfaceRoot == null || ground == null)
                return 0;

            var terrain = ground.Terrain;
            var trails = surfaceRoot.Find(SurfaceWorldPaths.TrailsName);
            if (terrain != null && trails != null)
            {
                var target = ResolveRouteTarget(surfaceRoot, caveRoot, ground);
                var primary = SelectPrimaryTrail(trails, target);
                if (primary != null)
                    SnapTrailWaypointsToTerrain(CollectTerrains(terrain), primary);
            }

            var waypoints = SurfaceRouteProbeRunner.CollectSurfaceWaypointsForRepair(ground, caveRoot, surfaceRoot);
            if (waypoints.Count == 0)
                return 0;

            var surfaceY = ground.SurfaceY;
            var removed = StripInvisibleAlongCorridor(
                waypoints, WaypointBlockerRadiusMeters, caveRoot, surfaceRoot, surfaceY);

            for (var i = 1; i < waypoints.Count; i++)
            {
                var mid = Vector3.Lerp(waypoints[i - 1], waypoints[i], 0.5f);
                removed += StripInvisibleAlongCorridor(
                    new List<Vector3> { mid }, CorridorBlockerRadiusMeters, caveRoot, surfaceRoot, surfaceY);
            }

            return removed;
        }

        static void FlattenTerrainUnderProbeWaypoints(
            Terrain terrain,
            Transform surfaceRoot,
            Transform caveRoot,
            SceneGroundInfo ground)
        {
            if (terrain == null || surfaceRoot == null || ground == null)
                return;

            var waypoints = SurfaceRouteProbeRunner.CollectSurfaceWaypointsForRepair(ground, caveRoot, surfaceRoot);
            if (waypoints.Count == 0)
                return;

            FlattenTrailBenchMulti(
                terrain, waypoints.ToArray(), NavFailHalfWidthMeters * 0.65f, NavFailFlattenStrength + 0.12f);
        }

        /// <summary>Bench the last trail segments (opening → surface mouth approach) for NavMesh + clearance probes.</summary>
        static void FlattenProbeRouteTailSegments(
            Terrain terrain,
            Transform surfaceRoot,
            Transform caveRoot,
            SceneGroundInfo ground)
        {
            if (terrain == null || surfaceRoot == null || ground == null)
                return;

            var waypoints = SurfaceRouteProbeRunner.CollectSurfaceWaypointsForRepair(ground, caveRoot, surfaceRoot);
            if (waypoints.Count < 2)
                return;

            var tailStart = Mathf.Max(0, waypoints.Count - 3);
            for (var i = tailStart + 1; i < waypoints.Count; i++)
            {
                var segment = DensifySegment(waypoints[i - 1], waypoints[i], 4f);
                FlattenTrailBenchMulti(
                    terrain,
                    segment,
                    NavFailHalfWidthMeters,
                    NavFailFlattenStrength);
            }

            if (caveRoot != null)
            {
                var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot, ground);
                if (mouth.sqrMagnitude > 0.01f)
                {
                    var lip = SurfaceRouteProbeRunner.ProjectMouthToSurfaceApproach(ground, mouth);
                    var last = waypoints[waypoints.Count - 1];
                    var mouthSeg = DensifySegment(last, lip, 4f);
                    FlattenTrailBenchMulti(
                        terrain, mouthSeg, NavFailHalfWidthMeters + 2f, NavFailFlattenStrength + 0.08f);
                }
            }
        }

        static void SnapSurfaceWaterToTerrain(Transform surfaceRoot, SceneGroundInfo ground)
        {
            if (surfaceRoot == null || ground?.Terrain == null)
                return;

            var waterRoot = surfaceRoot.Find(SurfaceWorldPaths.WaterName);
            if (waterRoot == null)
                return;

            SurfaceWorldGenerator.SnapWaterFeaturesToTerrain(waterRoot, ground);
        }

        static int StripInvisibleAlongCorridor(
            List<Vector3> corridor,
            float radius,
            Transform caveRoot,
            Transform surfaceRoot,
            float surfaceY)
        {
            var removed = 0;
            var seen = new HashSet<Collider>();
            foreach (var p in corridor)
            {
                foreach (var col in Physics.OverlapSphere(p, radius, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!ShouldStripSurfaceBlocker(col, p, caveRoot, surfaceRoot, surfaceY, seen))
                        continue;

                    seen.Add(col);
                    CaveEditorUndo.DestroyImmediate(col);
                    removed++;
                }
            }

            return removed;
        }

        internal static bool ShouldStripSurfaceBlocker(
            Collider col,
            Vector3 walkPoint,
            Transform caveRoot,
            Transform surfaceRoot,
            float surfaceY,
            HashSet<Collider> seen = null)
        {
            if (col == null || col.isTrigger)
                return false;
            if (seen != null && seen.Contains(col))
                return false;
            if (col.GetComponent<TerrainCollider>() != null)
                return false;
            if (CaveColliderUtility.IsProtectedPlayCollider(col, caveRoot))
                return false;
            if (CaveRendererVisibility.HasVisibleRenderer(col, true))
                return false;
            if (!SurfaceRouteProbeRunner.IntersectsSurfaceWalkBand(col, walkPoint, surfaceY))
                return false;

            var onSurface = surfaceRoot != null && col.transform.IsChildOf(surfaceRoot);
            var onCave = caveRoot != null && col.transform.IsChildOf(caveRoot);
            return onSurface || onCave;
        }

        static Vector3[] CollectWaypointPolyline(Transform trail)
        {
            var list = new List<Vector3>();
            AppendTrailWaypoints(trail, list);
            return list.Count >= 2 ? list.ToArray() : null;
        }

        static int StripInvisibleSolidColliders(Transform root)
        {
            if (root == null)
                return 0;

            var removed = 0;
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
            {
                if (col == null || col.isTrigger)
                    continue;
                if (col.GetComponent<TerrainCollider>() != null)
                    continue;
                if (col.gameObject.name.Contains("NavFloor"))
                {
                    CaveEditorUndo.DestroyImmediate(col);
                    removed++;
                    continue;
                }

                if (!CaveRendererVisibility.HasVisibleRenderer(col, true))
                {
                    CaveEditorUndo.DestroyImmediate(col);
                    removed++;
                }
            }

            return removed;
        }
    }
}
#endif
