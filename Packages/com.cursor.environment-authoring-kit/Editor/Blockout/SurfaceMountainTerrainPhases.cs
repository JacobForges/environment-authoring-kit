#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Light add-ons after unified outer-ring mountains: perimeter trails + play-band smooth only.
    /// Does not replace neighbor seed merge, seam stitch, or outer-ring sculpt.
    /// </summary>
    public static class SurfaceMountainTerrainPhases
    {
        const float PlaySmoothInnerFraction = 0.4f;
        const float TrailBenchWidthMeters = 3.2f;
        const float TrailBenchDepthNorm = 0.32f;
        /// <summary>~12% max rise/run — Appalachian bench trail grade cap.</summary>
        const float MaxTrailRisePerMeter = 0.12f;

        /// <summary>Row-paced cliff accent on existing outer band — does not re-run full outer ring sculpt.</summary>
        public static void QueueCliffAccent(
            Terrain mainTerrain,
            Action onComplete)
        {
            if (mainTerrain?.terrainData == null)
            {
                onComplete?.Invoke();
                return;
            }

            var nine = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            SurfaceOuterRingMountainsAuthor.ComputeNineTileWorldBounds(
                nine,
                out var minX,
                out var maxX,
                out var minZ,
                out var maxZ);
            var wilderness = SurfaceTerrainTileExpansion.CollectMountainWildernessTiles(mainTerrain);
            var queue = new Queue<Terrain>(wilderness);
            ProcessNextCliffTile(queue, minX, maxX, minZ, maxZ, onComplete);
        }

        static void ProcessNextCliffTile(
            Queue<Terrain> queue,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            Action onComplete)
        {
            if (queue.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var terrain = queue.Dequeue();
            SurfaceOuterRingMountainsAuthor.QueueCliffAccentOnTerrain(
                terrain,
                minX,
                maxX,
                minZ,
                maxZ,
                OnCliffTileDone);

            void OnCliffTileDone()
            {
                CaveBuildActionPacing.ScheduleLight(
                    () => ProcessNextCliffTile(queue, minX, maxX, minZ, maxZ, onComplete),
                    CaveBuildPipelineDomains.QueueLabel("wilderness cliffs - next tile"));
            }
        }

        public static void QueueAfterOuterRing(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            Vector3 center,
            float extent,
            Vector3 primaryForward,
            int seed,
            Action onComplete)
        {
            if (mainTerrain == null || request == null || !request.SurfaceIncludeMountains)
            {
                onComplete?.Invoke();
                return;
            }

            var terrains = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            if (terrains.Count < 9)
            {
                onComplete?.Invoke();
                return;
            }

            ComputeWorldBounds(terrains, out var minX, out var maxX, out var minZ, out var maxZ);

            CaveBuildEditorLog.LogSurface(
                "[Surface] Mountain polish — perimeter trails + play-band smooth (mountains unchanged).",
                forceUnityConsole: true);

            var trailPolylines = RunTrailsOnly(
                surfaceRoot,
                mainTerrain,
                center,
                extent,
                primaryForward,
                seed,
                request);

            SurfaceTerrainRefinement.QueueSelectiveSmoothPlayBand(
                mainTerrain,
                center,
                extent * PlaySmoothInnerFraction,
                trailPolylines,
                minX,
                maxX,
                minZ,
                maxZ,
                _ =>
                {
                    CaveBuildEditorLog.LogSurface(
                        "[Surface] Mountain polish complete.",
                        forceUnityConsole: true);
                    onComplete?.Invoke();
                });
        }

        public static void QueuePerimeterTrails(
            Transform surfaceRoot,
            Terrain mainTerrain,
            Vector3 center,
            float extent,
            Vector3 primaryForward,
            int seed,
            WorldGenerationRequest request,
            Action<List<Vector3[]>> onComplete)
        {
            if (onComplete == null)
                return;

            CaveBuildActionPacing.ScheduleHeavy(
                () =>
                {
                    var polylines = RunTrailsOnly(
                        surfaceRoot,
                        mainTerrain,
                        center,
                        extent,
                        primaryForward,
                        seed,
                        request);
                    CaveBuildActionPacing.QueueWhenIdle(
                        () => onComplete(polylines),
                        CaveBuildPipelineDomains.QueueLabel("mountain trails — done"));
                },
                CaveBuildPipelineDomains.QueueLabel("mountain trails — carve benches"));
        }

        public static List<Vector3[]> RunTrailsOnly(
            Transform surfaceRoot,
            Terrain mainTerrain,
            Vector3 center,
            float extent,
            Vector3 primaryForward,
            int seed,
            WorldGenerationRequest request)
        {
            var polylines = new List<Vector3[]>();
            if (!request.SurfaceIncludeTrails || surfaceRoot == null)
                return polylines;

            var trailsRoot = EnvironmentSceneUtility.GetOrCreateChild(
                surfaceRoot,
                SurfaceWorldPaths.TrailsName);
            var rng = new System.Random(seed + 8801);
            var dirs = new[]
            {
                primaryForward,
                Quaternion.Euler(0f, 90f, 0f) * primaryForward,
                Quaternion.Euler(0f, 180f, 0f) * primaryForward,
                Quaternion.Euler(0f, 270f, 0f) * primaryForward,
            };

            for (var i = 0; i < dirs.Length; i++)
            {
                var forward = dirs[i];
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.01f)
                    continue;
                forward.Normalize();

                var polyline = BuildPerimeterTrail(
                    trailsRoot,
                    mainTerrain,
                    center,
                    forward,
                    extent,
                    seed + i * 131,
                    rng);
                if (polyline == null || polyline.Length < 2)
                    continue;

                polylines.Add(polyline);
                if (!SurfaceTerrainTileExpansion.IsPlayDiskGameplayTerrainPublic(mainTerrain) &&
                    polyline.Length >= 2)
                {
                    SurfaceTerrainRadialAuthor.FlattenTrailBench(
                        mainTerrain,
                        polyline,
                        TrailBenchWidthMeters,
                        TrailBenchDepthNorm * 0.65f);
                }
            }

            var connector = BuildMountainMainMouthConnectorTrail(
                trailsRoot,
                mainTerrain,
                center,
                extent,
                seed,
                request);
            if (connector != null && connector.Length >= 2)
                polylines.Add(connector);

            var mouthTrails = BuildSouthPeakMouthTrailSystem(
                trailsRoot,
                mainTerrain,
                center,
                extent,
                seed,
                request);
            polylines.AddRange(mouthTrails);

            if (CaveBuildAaaSessionPolicy.IsFullAaaRebuild)
                polylines.AddRange(BuildFoothillMazeTrails(trailsRoot, mainTerrain, center, seed, request));

            if (polylines.Count > 0)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Mountain trails — {polylines.Count} route(s) (perimeter + play labyrinth → 3 south peak mouths).",
                    forceUnityConsole: true);
            }

            return polylines;
        }

        /// <summary>From play-disk labyrinth south exits through foothill/peak to each wilderness cave mouth.</summary>
        static List<Vector3[]> BuildSouthPeakMouthTrailSystem(
            Transform trailsRoot,
            Terrain mainTerrain,
            Vector3 center,
            float extent,
            int seed,
            WorldGenerationRequest request)
        {
            var result = new List<Vector3[]>();
            if (trailsRoot == null || mainTerrain == null || request == null || !request.UseMountainWildernessCaveMouth)
                return result;

            var surfaceRoot = trailsRoot.parent;
            if (surfaceRoot == null)
                return result;

            var nine = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            SurfaceOuterRingMountainsAuthor.ComputeNineTileWorldBounds(
                nine,
                out var playMinX,
                out var playMaxX,
                out var playMinZ,
                out var playMaxZ);

            var exits = SurfaceMountainPlayLabyrinthRegion.ResolveLabyrinthExitWorldPoints(
                mainTerrain,
                playMinX,
                playMaxX,
                playMinZ,
                playMaxZ,
                seed);

            var routes = new[]
            {
                (SurfaceMountainPlayLabyrinthRegion.ExitWest, SurfaceMountainWildernessCaveMouthAuthor.SouthPeakMouthWest, "Cave W"),
                (SurfaceMountainPlayLabyrinthRegion.ExitCenter, SurfaceMountainWildernessCaveMouthAuthor.SouthPeakMouthCenter, "Cave C"),
                (SurfaceMountainPlayLabyrinthRegion.ExitEast, SurfaceMountainWildernessCaveMouthAuthor.SouthPeakMouthEast, "Cave E"),
            };

            foreach (var (exitIndex, mouthIndex, label) in routes)
            {
                if (exitIndex >= exits.Length)
                    continue;

                if (!SurfaceMountainWildernessCaveMouthAuthor.TryFindSouthPeakMouthWorld(
                        surfaceRoot,
                        mouthIndex,
                        out var mouthWorld))
                {
                    CaveBuildEditorLog.LogSurfaceWarning(
                        $"[Surface] Play → {label} trail skipped — mouth marker {mouthIndex} not found (run mountain_wilderness_cave_mouth first).");
                    continue;
                }

                var trail = BuildTrailFromPlayExitToMouth(
                    trailsRoot,
                    mainTerrain,
                    exits[exitIndex],
                    mouthWorld,
                    seed + mouthIndex * 977,
                    mouthIndex,
                    label);
                if (trail != null && trail.Length >= 2)
                {
                    result.Add(trail);
                    CaveBuildEditorLog.LogSurface(
                        $"[Surface] Play labyrinth → {label} (east peak annex) trail carved — {trail.Length} waypoint(s).",
                        forceUnityConsole: true);
                }
                else
                {
                    CaveBuildEditorLog.LogSurfaceWarning(
                        $"[Surface] Play → {label} trail failed — could not bench path from play south exit to mouth.");
                }
            }

            if (result.Count > 0)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Play labyrinth → south peak mouth trails — {result.Count}/3 (W, C, E) on foothill/peak.",
                    forceUnityConsole: true);
            }

            if (exits.Length > SurfaceMountainPlayLabyrinthRegion.ExitTitan &&
                HollowTitanLandmarkSitePlanner.TryResolvePeakSite(mainTerrain, seed, out _, out var titanSite))
            {
                var titanTrail = BuildTrailFromPlayExitToMouth(
                    trailsRoot,
                    mainTerrain,
                    exits[SurfaceMountainPlayLabyrinthRegion.ExitTitan],
                    titanSite,
                    seed + 8803,
                    99,
                    "Hollow Titan");
                if (titanTrail != null && titanTrail.Length >= 2)
                {
                    result.Add(titanTrail);
                    CaveBuildEditorLog.LogSurface(
                        $"[Surface] Play labyrinth → Hollow Titan peak trail — {titanTrail.Length} waypoint(s).",
                        forceUnityConsole: true);
                }
            }

            return result;
        }

        static List<Vector3[]> BuildFoothillMazeTrails(
            Transform trailsRoot,
            Terrain mainTerrain,
            Vector3 center,
            int seed,
            WorldGenerationRequest request)
        {
            var result = new List<Vector3[]>();
            if (trailsRoot == null || mainTerrain == null || request == null)
                return result;

            var nine = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            SurfaceOuterRingMountainsAuthor.ComputeNineTileWorldBounds(
                nine,
                out var playMinX,
                out var playMaxX,
                out var playMinZ,
                out var playMaxZ);
            SurfaceTerrainTileExpansion.ComputeFoothillRingWorldBounds(
                mainTerrain,
                out var fhMinX,
                out var fhMaxX,
                out var fhMinZ,
                out var fhMaxZ);

            var layout = SurfaceMountainLabyrinthLayout.Generate(
                seed,
                playMinX,
                playMaxX,
                playMinZ,
                playMaxZ,
                fhMinX,
                fhMaxX,
                fhMinZ,
                fhMaxZ,
                mainTerrain);
            var spines = layout.BuildCarvePolylines(playMinX, playMaxX, playMinZ, playMaxZ);
            var foothills = SurfaceTerrainTileExpansion.CollectMountainFoothillTiles(mainTerrain);
            var trailIndex = 0;

            foreach (var spine in spines)
            {
                if (spine == null || spine.Length < 2)
                    continue;

                var mazePoints = new List<Vector3>();
                for (var i = 0; i < spine.Length; i++)
                {
                    var p = spine[i];
                    if (!TrySampleFoothillTrailPoint(mainTerrain, foothills, p.x, p.z, out var sampled))
                        continue;

                    mazePoints.Add(sampled);
                }

                if (mazePoints.Count < 2)
                    continue;

                EnforceTrailGradeLimit(mainTerrain, mazePoints, MaxTrailRisePerMeter);
                CarveFoothillMazeBenchSegments(mainTerrain, mazePoints);
                var trailGo = new GameObject($"FoothillMazeTrail_{trailIndex++}_{seed % 1000}");
                CaveEditorUndo.RegisterCreated(trailGo, "Foothill maze trail");
                trailGo.transform.SetParent(trailsRoot, false);
                for (var w = 0; w < mazePoints.Count; w++)
                {
                    var wp = new GameObject($"Waypoint_{w}");
                    CaveEditorUndo.RegisterCreated(wp, "Waypoint");
                    wp.transform.SetParent(trailGo.transform, false);
                    wp.transform.position = mazePoints[w];
                }

                result.Add(mazePoints.ToArray());
            }

            if (result.Count > 0)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Foothill maze trails — {result.Count} bench route(s) from play labyrinth spines.",
                    forceUnityConsole: true);
            }

            return result;
        }

        static bool TrySampleFoothillTrailPoint(
            Terrain mainTerrain,
            Terrain[] foothills,
            float wx,
            float wz,
            out Vector3 world)
        {
            world = default;
            if (foothills == null || foothills.Length == 0)
                return false;

            for (var i = 0; i < foothills.Length; i++)
            {
                var tile = foothills[i];
                if (tile?.terrainData == null ||
                    !SurfaceTerrainPlayRegion.ContainsWorldXZ(tile, wx, wz))
                    continue;

                world = new Vector3(
                    wx,
                    tile.SampleHeight(new Vector3(wx, 0f, wz)) + tile.transform.position.y + 0.45f,
                    wz);
                return true;
            }

            return false;
        }

        static void CarveFoothillMazeBenchSegments(Terrain mainTerrain, List<Vector3> points)
        {
            for (var i = 0; i < points.Count - 1; i++)
            {
                var seg = new[] { points[i], points[i + 1] };
                if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(mainTerrain, seg[0].x, seg[0].z, out var tile) ||
                    tile == null ||
                    SurfaceTerrainTileExpansion.IsPlayDiskGameplayTerrainPublic(tile))
                    continue;

                SurfaceTerrainRadialAuthor.FlattenTrailBench(
                    tile,
                    seg,
                    TrailBenchWidthMeters + 2.4f,
                    TrailBenchDepthNorm * 0.85f);
            }
        }

        static Vector3[] BuildTrailFromPlayExitToMouth(
            Transform trailsRoot,
            Terrain mainTerrain,
            Vector3 startXZ,
            Vector3 mouthWorld,
            int seed,
            int mouthIndex,
            string label = "")
        {
            startXZ.y = 0f;
            mouthWorld.y = 0f;
            if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(mainTerrain, startXZ.x, startXZ.z, out var startTile) ||
                startTile == null)
                return null;

            startXZ.y = startTile.SampleHeight(startXZ) + startTile.transform.position.y + 0.4f;
            if (SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(mainTerrain, mouthWorld.x, mouthWorld.z, out var mouthTile) &&
                mouthTile != null)
                mouthWorld.y = mouthTile.SampleHeight(mouthWorld) + mouthTile.transform.position.y + 0.2f;

            var dist = Vector3.Distance(
                new Vector3(startXZ.x, 0f, startXZ.z),
                new Vector3(mouthWorld.x, 0f, mouthWorld.z));
            if (dist < 18f || dist > 640f)
                return null;

            var rng = new System.Random(seed);
            var steps = Mathf.Clamp(Mathf.CeilToInt(dist / 16f), 6, 32);
            var points = new List<Vector3>(steps + 1);
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var p = Vector3.Lerp(startXZ, mouthWorld, t);
                var lateral = ((float)rng.NextDouble() - 0.5f) * 8f * Mathf.Sin(t * Mathf.PI);
                var forward = mouthWorld - startXZ;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.01f)
                {
                    forward.Normalize();
                    var right = Vector3.Cross(Vector3.up, forward);
                    p += right * lateral;
                }

                if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(mainTerrain, p.x, p.z, out var tile) || tile == null)
                    break;

                p.y = tile.SampleHeight(p) + tile.transform.position.y + Mathf.Lerp(0.35f, 1.1f, t);
                points.Add(p);
            }

            if (points.Count < 2)
                return null;

            EnforceTrailGradeLimit(mainTerrain, points, MaxTrailRisePerMeter);

            var polyline = points.ToArray();
            for (var i = 0; i < polyline.Length - 1; i++)
            {
                var seg = new[] { polyline[i], polyline[i + 1] };
                if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(mainTerrain, seg[0].x, seg[0].z, out var tile) ||
                    tile == null ||
                    SurfaceTerrainTileExpansion.IsPlayDiskGameplayTerrainPublic(tile))
                    continue;

                SurfaceTerrainRadialAuthor.FlattenTrailBench(
                    tile,
                    seg,
                    TrailBenchWidthMeters + 1.2f,
                    TrailBenchDepthNorm * 0.7f);
            }

            var suffix = string.IsNullOrEmpty(label) ? mouthIndex.ToString() : label.Replace(" ", "");
            var trailGo = new GameObject($"PlayLabyrinthToMouth_{suffix}_{seed % 1000}");
            CaveEditorUndo.RegisterCreated(trailGo, "Play labyrinth to mouth trail");
            trailGo.transform.SetParent(trailsRoot, false);
            for (var i = 0; i < points.Count; i++)
            {
                var wp = new GameObject($"Waypoint_{i}");
                CaveEditorUndo.RegisterCreated(wp, "Waypoint");
                wp.transform.SetParent(trailGo.transform, false);
                wp.transform.position = points[i];
            }

            return polyline;
        }

        static void EnforceTrailGradeLimit(Terrain mainTerrain, List<Vector3> points, float maxRisePerMeter)
        {
            if (mainTerrain == null || points == null || points.Count < 2 || maxRisePerMeter <= 0.0001f)
                return;

            for (var i = 1; i < points.Count; i++)
            {
                var prev = points[i - 1];
                var cur = points[i];
                var horiz = Vector2.Distance(
                    new Vector2(prev.x, prev.z),
                    new Vector2(cur.x, cur.z));
                if (horiz < 0.05f)
                    continue;

                var maxRise = horiz * maxRisePerMeter;
                if (cur.y <= prev.y + maxRise)
                    continue;

                cur.y = prev.y + maxRise;
                if (SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(mainTerrain, cur.x, cur.z, out var tile) &&
                    tile != null)
                {
                    cur.y = Mathf.Max(
                        cur.y,
                        tile.SampleHeight(cur) + tile.transform.position.y + 0.2f);
                }

                points[i] = cur;
            }
        }

        static Vector3[] BuildPerimeterTrail(
            Transform trailsRoot,
            Terrain terrain,
            Vector3 center,
            Vector3 forward,
            float extent,
            int seed,
            System.Random rng)
        {
            var points = new List<Vector3>();
            var steps = 7 + rng.Next(0, 3);
            var maxDist = extent * 0.85f;
            for (var s = 0; s <= steps; s++)
            {
                var t = s / (float)steps;
                var dist = Mathf.Lerp(extent * 0.22f, maxDist, t);
                var lateral = ((float)rng.NextDouble() - 0.5f) * 10f * (1f - t * 0.55f);
                var right = Vector3.Cross(Vector3.up, forward).normalized;
                var p = center + forward * dist + right * lateral;
                if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(terrain, p.x, p.z, out var tile) || tile == null)
                    break;

                p.y = tile.SampleHeight(p) + Mathf.Lerp(0.3f, 1.8f, t);
                points.Add(p);
            }

            if (points.Count < 2)
                return null;

            var trailGo = new GameObject($"MountainPerimeterTrail_{seed % 1000}");
            CaveEditorUndo.RegisterCreated(trailGo, "Mountain perimeter trail");
            trailGo.transform.SetParent(trailsRoot, false);
            for (var i = 0; i < points.Count; i++)
            {
                var wp = new GameObject($"Waypoint_{i}");
                CaveEditorUndo.RegisterCreated(wp, "Waypoint");
                wp.transform.SetParent(trailGo.transform, false);
                wp.transform.position = points[i];
            }

            return points.ToArray();
        }

        static Vector3[] BuildMountainMainMouthConnectorTrail(
            Transform trailsRoot,
            Terrain mainTerrain,
            Vector3 center,
            float extent,
            int seed,
            WorldGenerationRequest request)
        {
            if (trailsRoot == null || mainTerrain == null || request == null || !request.UseMountainWildernessCaveMouth)
                return null;

            var surfaceRoot = trailsRoot.parent;
            if (surfaceRoot == null ||
                !SurfaceMountainWildernessCaveMouthAuthor.TryFindMainMountainMouthWorld(surfaceRoot, out var mouthWorld))
                return null;

            var start = center + Vector3.back * (extent * 0.28f);
            if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(mainTerrain, start.x, start.z, out var startTile) ||
                startTile == null)
                return null;

            start.y = startTile.SampleHeight(start) + startTile.transform.position.y + 0.35f;
            if (SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(mainTerrain, mouthWorld.x, mouthWorld.z, out var mouthTile) &&
                mouthTile != null)
                mouthWorld.y = mouthTile.SampleHeight(mouthWorld) + mouthTile.transform.position.y + 0.15f;

            var dist = Vector3.Distance(
                new Vector3(start.x, 0f, start.z),
                new Vector3(mouthWorld.x, 0f, mouthWorld.z));
            if (dist < 12f || dist > 520f)
                return null;

            var steps = Mathf.Clamp(Mathf.CeilToInt(dist / 14f), 4, 24);
            var points = new List<Vector3>(steps + 1);
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var p = Vector3.Lerp(start, mouthWorld, t);
                if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(mainTerrain, p.x, p.z, out var tile) || tile == null)
                    break;

                p.y = tile.SampleHeight(p) + tile.transform.position.y + Mathf.Lerp(0.25f, 0.9f, t);
                points.Add(p);
            }

            if (points.Count < 2)
                return null;

            EnforceTrailGradeLimit(mainTerrain, points, MaxTrailRisePerMeter);

            var polyline = points.ToArray();
            for (var i = 0; i < polyline.Length - 1; i++)
            {
                var seg = new[] { polyline[i], polyline[i + 1] };
                if (SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(mainTerrain, seg[0].x, seg[0].z, out var tile) &&
                    tile != null &&
                    !SurfaceTerrainTileExpansion.IsPlayDiskGameplayTerrainPublic(tile))
                {
                    SurfaceTerrainRadialAuthor.FlattenTrailBench(
                        tile,
                        seg,
                        TrailBenchWidthMeters + 0.8f,
                        TrailBenchDepthNorm * 0.55f);
                }
            }

            var trailGo = new GameObject($"MountainMainMouthTrail_{seed % 1000}");
            CaveEditorUndo.RegisterCreated(trailGo, "Mountain main mouth trail");
            trailGo.transform.SetParent(trailsRoot, false);
            for (var i = 0; i < points.Count; i++)
            {
                var wp = new GameObject($"Waypoint_{i}");
                CaveEditorUndo.RegisterCreated(wp, "Waypoint");
                wp.transform.SetParent(trailGo.transform, false);
                wp.transform.position = points[i];
            }

            CaveBuildEditorLog.LogSurface(
                "[Surface] Mountain main-mouth connector trail — play fields → center south peak cave.",
                forceUnityConsole: true);
            return polyline;
        }

        static void ComputeWorldBounds(
            IReadOnlyList<Terrain> terrains,
            out float minX,
            out float maxX,
            out float minZ,
            out float maxZ)
        {
            minX = minZ = float.PositiveInfinity;
            maxX = maxZ = float.NegativeInfinity;
            foreach (var t in terrains)
            {
                if (t?.terrainData == null)
                    continue;
                var o = t.transform.position;
                var s = t.terrainData.size;
                minX = Mathf.Min(minX, o.x);
                minZ = Mathf.Min(minZ, o.z);
                maxX = Mathf.Max(maxX, o.x + s.x);
                maxZ = Mathf.Max(maxZ, o.z + s.z);
            }
        }
    }
}
#endif
