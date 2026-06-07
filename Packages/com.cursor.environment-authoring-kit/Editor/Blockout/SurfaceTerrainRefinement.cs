#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Meat-loop surface passes: re-stamp LiDAR, smooth outer ring, align roads/water to hillshade relief.
    /// Additive only — does not clear GeneratedSurfaceWorld.
    /// </summary>
    public static class SurfaceTerrainRefinement
    {
        const float RefineStampBlend = 0.38f;
        const float SmoothStrength = 0.22f;

        public static bool TryLidarRefineAndSmooth(
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            out string message)
        {
            message = string.Empty;
            if (terrain == null)
            {
                message = "No terrain for LiDAR refine.";
                return false;
            }

            var smoothed = SmoothOuterHeightRing(terrain, groundCenter, extentMeters, SmoothStrength);
            message =
                $"Hydro outer smooth ({smoothed} cells) — full DEM stamp runs in terrain phase 5 (paced queue).";
            return smoothed > 0;
        }

        public static bool TryRefineRoadsAndWater(
            Terrain terrain,
            Transform surfaceRoot,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            out string message)
        {
            message = string.Empty;
            if (terrain == null || surfaceRoot == null)
            {
                message = "Missing terrain or surface root.";
                return false;
            }

            var roads = surfaceRoot.Find(SurfaceWorldPaths.RoadsName);
            var trails = surfaceRoot.Find(SurfaceWorldPaths.TrailsName);
            var water = surfaceRoot.Find(SurfaceWorldPaths.WaterName);
            var roadLines = 0;
            var trailLines = 0;

            if (roads != null)
            {
                foreach (Transform child in roads)
                {
                    if (!TryRoadPolyline(child, out var pts) || pts.Length < 2)
                        continue;
                    SurfaceTerrainRadialAuthor.FlattenTrailBench(terrain, pts, 4.5f, 0.14f);
                    AlignPolylineToHillshade(terrain, pts, groundCenter, extentMeters, seed, flattenBright: true);
                    roadLines++;
                }
            }

            if (trails != null)
            {
                foreach (Transform trail in trails)
                {
                    var pts = CollectWaypointPolyline(trail);
                    if (pts == null || pts.Length < 2)
                        continue;
                    SurfaceTerrainRadialAuthor.FlattenTrailBench(terrain, pts, 3f, 0.1f);
                    trailLines++;
                }
            }

            var waterBasins = 0;
            if (water != null)
            {
                foreach (Transform child in water)
                {
                    if (TryDepressWaterBasin(terrain, child.position, groundCenter, extentMeters, seed))
                        waterBasins++;
                }
            }

            terrain.Flush();
            message =
                $"Roads/water LiDAR refine — {roadLines} roads, {trailLines} trails, {waterBasins} water basins.";
            CaveBuildPipelineLog.Info(message, "Surface-Meat");
            return roadLines + trailLines + waterBasins > 0;
        }

        public static int SmoothOuterHeightRingPublic(
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            float strength = -1f,
            float preserveRadiusFraction = -1f) =>
            SmoothOuterHeightRing(
                terrain,
                groundCenter,
                extentMeters,
                strength < 0f ? SmoothStrength : strength,
                preserveRadiusFraction < 0f
                    ? SurfaceLidarTerrainAuthor.MainLandPreserveRadiusFraction
                    : preserveRadiusFraction);

        /// <summary>Light uniform smooth on the full terrain tile — no radial ring (avoids visible circles on neighbor tiles).</summary>
        public static int SmoothTerrainFootprintUniform(Terrain terrain, float strength = -1f)
        {
            if (terrain == null || terrain.terrainData == null)
                return 0;

            var smoothStrength = strength < 0f ? SmoothStrength * 0.55f : strength;
            var data = terrain.terrainData;
            Undo.RecordObject(data, "Surface footprint smooth");
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var copy = (float[,])heights.Clone();
            var changed = ApplyFootprintLaplacianRows(heights, copy, res, smoothStrength, 1, res - 1);
            if (changed > 0)
                data.SetHeights(0, 0, heights);

            return changed;
        }

        /// <summary>Paced footprint smooth — row compute + row upload per editor frame (full quality, no main-thread stall).</summary>
        public static void QueueSmoothTerrainFootprintPaced(
            Terrain terrain,
            float strength,
            string progressLabel,
            float progress01,
            System.Action<int> onComplete)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                onComplete?.Invoke(0);
                return;
            }

            var session = new FootprintSmoothSession
            {
                Terrain = terrain,
                Strength = strength < 0f ? SmoothStrength * 0.55f : strength,
                Res = terrain.terrainData.heightmapResolution,
                ProgressLabel = progressLabel,
                Progress01 = progress01,
                OnComplete = onComplete,
            };
            CaveBuildActionPacing.ScheduleHeavyChain(
                () => CaveBuildActionPacing.ScheduleNextEditorFrame(
                    () => RunFootprintSmoothStep(session, FootprintSmoothStep.Load)),
                CaveBuildPipelineDomains.SurfaceQueueLabel("terrain footprint smooth load"));
        }

        static int ApplyFootprintLaplacianRows(
            float[,] heights,
            float[,] copy,
            int res,
            float smoothStrength,
            int yStart,
            int yEnd)
        {
            var changed = 0;
            yStart = Mathf.Max(1, yStart);
            yEnd = Mathf.Min(res - 1, yEnd);
            for (var y = yStart; y < yEnd; y++)
            {
                for (var x = 1; x < res - 1; x++)
                {
                    var avg = (copy[y - 1, x] + copy[y + 1, x] + copy[y, x - 1] + copy[y, x + 1]) * 0.25f;
                    var before = heights[y, x];
                    heights[y, x] = Mathf.Lerp(before, avg, smoothStrength);
                    if (Mathf.Abs(heights[y, x] - before) > 0.00005f)
                        changed++;
                }
            }

            return changed;
        }

        /// <summary>One terrain tile per queue step (avoids 9× full heightmap pulls in one frame / RAM spike).</summary>
        public static void QueueSmoothAllSurfaceTerrainsFootprint(
            Terrain mainTerrain,
            float strength,
            System.Action<int> onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke(0);
                return;
            }

            var terrains = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            QueueSmoothTerrainFootprintAtIndex(terrains, 0, strength, 0, onComplete);
        }

        /// <summary>Smooth play disk + trail benches only — skips cliff / outer mountain band.</summary>
        public static void QueueSelectiveSmoothPlayBand(
            Terrain mainTerrain,
            Vector3 playCenter,
            float playInnerRadiusMeters,
            System.Collections.Generic.IList<Vector3[]> trailPolylines,
            float worldMinX,
            float worldMaxX,
            float worldMinZ,
            float worldMaxZ,
            System.Action<int> onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke(0);
                return;
            }

            var terrains = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            var session = new SelectiveSmoothChainSession
            {
                Terrains = terrains,
                PlayCenter = playCenter,
                PlayInnerRadius = playInnerRadiusMeters,
                TrailPolylines = trailPolylines,
                WorldMinX = worldMinX,
                WorldMaxX = worldMaxX,
                WorldMinZ = worldMinZ,
                WorldMaxZ = worldMaxZ,
                Strength = SmoothStrength * 0.45f,
                OnComplete = onComplete,
            };
            QueueSelectiveSmoothTerrainAtIndex(session, 0, 0);
        }

        sealed class SelectiveSmoothChainSession
        {
            public List<Terrain> Terrains;
            public Vector3 PlayCenter;
            public float PlayInnerRadius;
            public System.Collections.Generic.IList<Vector3[]> TrailPolylines;
            public float WorldMinX;
            public float WorldMaxX;
            public float WorldMinZ;
            public float WorldMaxZ;
            public float Strength;
            public System.Action<int> OnComplete;
        }

        static void QueueSelectiveSmoothTerrainAtIndex(
            SelectiveSmoothChainSession chain,
            int terrainIndex,
            int cellsSoFar)
        {
            if (chain.Terrains == null || terrainIndex >= chain.Terrains.Count)
            {
                EditorUtility.ClearProgressBar();
                chain.OnComplete?.Invoke(cellsSoFar);
                return;
            }

            var terrain = chain.Terrains[terrainIndex];
            var label = terrain != null ? terrain.name : $"tile {terrainIndex + 1}";
            QueueSelectiveSmoothTerrainPaced(
                terrain,
                chain,
                $"{label} play-band ({terrainIndex + 1}/{chain.Terrains.Count})",
                0.46f,
                cells =>
                {
                    EnvironmentKitHardwareBudget.OnQueueStepCompleted();
                    CaveBuildActionPacing.ScheduleHeavyChain(
                        () => QueueSelectiveSmoothTerrainAtIndex(chain, terrainIndex + 1, cellsSoFar + cells),
                        CaveBuildPipelineDomains.SurfaceQueueLabel(
                            $"selective smooth {terrainIndex + 2}/{chain.Terrains.Count}"));
                });
        }

        static void QueueSelectiveSmoothTerrainPaced(
            Terrain terrain,
            SelectiveSmoothChainSession chain,
            string progressLabel,
            float progress01,
            System.Action<int> onComplete)
        {
            if (terrain?.terrainData == null)
            {
                onComplete?.Invoke(0);
                return;
            }

            var session = new SelectiveSmoothSession
            {
                Terrain = terrain,
                Chain = chain,
                Strength = chain.Strength,
                Res = terrain.terrainData.heightmapResolution,
                ProgressLabel = progressLabel,
                Progress01 = progress01,
                OnComplete = onComplete,
            };
            CaveBuildActionPacing.ScheduleHeavyChain(
                () => CaveBuildActionPacing.ScheduleNextEditorFrame(
                    () => RunSelectiveSmoothStep(session, SelectiveSmoothStep.Load)),
                CaveBuildPipelineDomains.SurfaceQueueLabel("selective smooth load"));
        }

        sealed class SelectiveSmoothSession
        {
            public Terrain Terrain;
            public SelectiveSmoothChainSession Chain;
            public float Strength;
            public int Res;
            public float[,] Heights;
            public float[,] Copy;
            public int RowY;
            public int Changed;
            public string ProgressLabel;
            public float Progress01;
            public System.Action<int> OnComplete;
        }

        enum SelectiveSmoothStep
        {
            Load,
            ProcessRows,
            UploadRows,
            Done,
        }

        static bool ShouldSmoothSelectiveCell(
            float wx,
            float wz,
            Vector3 tileOrigin,
            Vector3 tileSize,
            SelectiveSmoothChainSession chain)
        {
            var distIn = DistanceInsideWorldAabb(
                wx,
                wz,
                chain.WorldMinX,
                chain.WorldMaxX,
                chain.WorldMinZ,
                chain.WorldMaxZ);
            var outerBand = SurfaceOuterRingMountainsAuthor.OuterBandMeters;
            if (distIn < outerBand)
            {
                var edgeFactor = 1f - distIn / outerBand;
                if (edgeFactor > 0.2f)
                    return false;
            }

            var dist = Vector2.Distance(
                new Vector2(wx, wz),
                new Vector2(chain.PlayCenter.x, chain.PlayCenter.z));
            if (dist <= chain.PlayInnerRadius)
                return true;

            if (chain.TrailPolylines != null)
            {
                var p = new Vector3(wx, 0f, wz);
                foreach (var poly in chain.TrailPolylines)
                {
                    if (poly == null || poly.Length < 2)
                        continue;
                    if (SurfaceTerrainRadialAuthorDistance.DistanceToPolylineXZ(p, poly) <= 5.5f)
                        return true;
                }
            }

            return false;
        }

        static float DistanceInsideWorldAabb(float x, float z, float minX, float maxX, float minZ, float maxZ)
        {
            var dx = Mathf.Min(x - minX, maxX - x);
            var dz = Mathf.Min(z - minZ, maxZ - z);
            return Mathf.Max(0f, Mathf.Min(dx, dz));
        }

        static void RunSelectiveSmoothStep(SelectiveSmoothSession session, SelectiveSmoothStep step)
        {
            if (session?.Terrain?.terrainData == null)
            {
                session?.OnComplete?.Invoke(0);
                return;
            }

            var origin = session.Terrain.transform.position;
            var size = session.Terrain.terrainData.size;

            switch (step)
            {
                case SelectiveSmoothStep.Load:
                    CaveEditorUndo.RecordObject(session.Terrain.terrainData, "Selective play-band smooth");
                    session.Heights = session.Terrain.terrainData.GetHeights(0, 0, session.Res, session.Res);
                    session.Copy = (float[,])session.Heights.Clone();
                    session.RowY = 1;
                    session.Changed = 0;
                    CaveBuildActionPacing.ScheduleNextEditorFrame(
                        () => RunSelectiveSmoothStep(session, SelectiveSmoothStep.ProcessRows));
                    break;

                case SelectiveSmoothStep.ProcessRows:
                {
                    var chunk = SmoothRowChunk(session.Res);
                    var yEnd = Mathf.Min(session.Res - 1, session.RowY + chunk);
                    for (var y = session.RowY; y < yEnd; y++)
                    {
                        for (var x = 1; x < session.Res - 1; x++)
                        {
                            var wx = origin.x + x / (float)(session.Res - 1) * size.x;
                            var wz = origin.z + y / (float)(session.Res - 1) * size.z;
                            if (!ShouldSmoothSelectiveCell(wx, wz, origin, size, session.Chain))
                                continue;

                            var avg = (session.Copy[y - 1, x] + session.Copy[y + 1, x] +
                                       session.Copy[y, x - 1] + session.Copy[y, x + 1]) * 0.25f;
                            var before = session.Heights[y, x];
                            session.Heights[y, x] = Mathf.Lerp(before, avg, session.Strength);
                            if (Mathf.Abs(session.Heights[y, x] - before) > 0.00005f)
                                session.Changed++;
                        }
                    }

                    session.RowY = yEnd;
                    CaveBuildRunStatusPublisher.PulseSubOperation(
                        "Surface",
                        $"play-band smooth {session.ProgressLabel} rows {session.RowY}/{session.Res - 1}");
                    if (session.RowY < session.Res - 1)
                    {
                        CaveBuildActionPacing.ScheduleNextEditorFrame(
                            () => RunSelectiveSmoothStep(session, SelectiveSmoothStep.ProcessRows));
                        return;
                    }

                    session.RowY = 0;
                    CaveBuildActionPacing.ScheduleNextEditorFrame(
                        () => RunSelectiveSmoothStep(session, SelectiveSmoothStep.UploadRows));
                    break;
                }

                case SelectiveSmoothStep.UploadRows:
                {
                    var chunk = SmoothRowChunk(session.Res);
                    var yEnd = Mathf.Min(session.Res, session.RowY + chunk);
                    FlushHeightRows(session.Terrain, session.Heights, session.Res, session.RowY, yEnd);
                    session.RowY = yEnd;
                    if (session.RowY < session.Res)
                    {
                        CaveBuildActionPacing.ScheduleNextEditorFrame(
                            () => RunSelectiveSmoothStep(session, SelectiveSmoothStep.UploadRows));
                        return;
                    }

                    CaveBuildActionPacing.ScheduleNextEditorFrame(
                        () => RunSelectiveSmoothStep(session, SelectiveSmoothStep.Done));
                    break;
                }

                case SelectiveSmoothStep.Done:
                    if (session.Changed > 0)
                    {
                        CaveBuildMicroTerrainHeightmap.QueueFinalizeDelayedLod(
                            session.Terrain,
                            () => session.OnComplete?.Invoke(session.Changed));
                        break;
                    }

                    session.OnComplete?.Invoke(session.Changed);
                    break;
            }
        }

        static void QueueSmoothTerrainFootprintAtIndex(
            List<Terrain> terrains,
            int index,
            float strength,
            int cellsSoFar,
            System.Action<int> onComplete)
        {
            if (terrains == null || index >= terrains.Count)
            {
                EditorUtility.ClearProgressBar();
                onComplete?.Invoke(cellsSoFar);
                return;
            }

            var terrain = terrains[index];
            var label = terrain != null ? terrain.name : $"tile {index + 1}";
            var progress = 0.42f + index / (float)terrains.Count * 0.04f;
            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                $"[Surface] smoothing {label} ({index + 1}/{terrains.Count})…",
                progress);

            if (terrain == null)
            {
                QueueSmoothTerrainFootprintAtIndex(
                    terrains,
                    index + 1,
                    strength,
                    cellsSoFar,
                    onComplete);
                return;
            }

            QueueSmoothTerrainFootprintPaced(
                terrain,
                strength,
                $"{label} ({index + 1}/{terrains.Count})",
                progress,
                cells =>
                {
                    EnvironmentKitHardwareBudget.OnQueueStepCompleted();
                    CaveBuildActionPacing.ScheduleHeavyChain(
                        () => QueueSmoothTerrainFootprintAtIndex(
                            terrains,
                            index + 1,
                            strength,
                            cellsSoFar + cells,
                            onComplete),
                        CaveBuildPipelineDomains.SurfaceQueueLabel(
                            $"terrain smooth {index + 2}/{terrains.Count}"));
                });
        }

        /// <summary>Paced outer-ring smooth — one row band per editor frame (terrain footprint only).</summary>
        public static void QueueSmoothOuterHeightRingPublic(
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            float strength,
            System.Action<int> onComplete)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                onComplete?.Invoke(0);
                return;
            }

            var session = new SmoothRingSession
            {
                Terrain = terrain,
                PlayCenter = groundCenter,
                ExtentMeters = extentMeters,
                Strength = strength < 0f ? SmoothStrength : strength,
                Res = terrain.terrainData.heightmapResolution,
                OnComplete = onComplete,
            };
            CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunSmoothRingStep(session, SmoothRingStep.Load));
        }

        /// <summary>Light laplacian smooth on the playable annulus (grader sample band, not LiDAR preserve core).</summary>
        public static int SmoothGraderSampleBandPublic(
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            float strength = -1f) =>
            SmoothGraderSampleBand(terrain, groundCenter, extentMeters, strength < 0f ? SmoothStrength : strength);

        public static int BoxBlurGraderSampleBandPublic(
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            float blend = 0.45f) =>
            BoxBlurGraderSampleBand(terrain, groundCenter, extentMeters, blend);

        /// <summary>In-memory grader-band box blur — use inside single-pass ladder repair (one SetHeights at end).</summary>
        public static int BoxBlurGraderSampleBandHeights(
            float[,] heights,
            int res,
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            float blend = 0.45f)
        {
            if (heights == null || res < 3 || terrain?.terrainData == null)
                return 0;

            var copy = (float[,])heights.Clone();
            var changed = 0;

            for (var y = 1; y < res - 1; y++)
            {
                for (var x = 1; x < res - 1; x++)
                {
                    if (!InGraderSampleBand(terrain, x, y, res, groundCenter, extentMeters))
                        continue;

                    var sum = copy[y, x]
                        + copy[y - 1, x] + copy[y + 1, x]
                        + copy[y, x - 1] + copy[y, x + 1]
                        + copy[y - 1, x - 1] + copy[y - 1, x + 1]
                        + copy[y + 1, x - 1] + copy[y + 1, x + 1];
                    var before = heights[y, x];
                    heights[y, x] = Mathf.Lerp(before, sum / 9f, blend);
                    if (Mathf.Abs(heights[y, x] - before) > 0.00005f)
                        changed++;
                }
            }

            return changed;
        }

        static bool InGraderSampleBand(
            Terrain terrain,
            int x,
            int y,
            int res,
            Vector3 centerWorld,
            float extentMeters) =>
            SurfaceTerrainPlayRegion.InPlayAnnulusOnTerrain(
                terrain,
                x,
                y,
                res,
                centerWorld,
                extentMeters,
                innerFraction: 0.08f,
                outerFraction: 1.05f);

        static int SmoothGraderSampleBand(
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            float smoothStrength)
        {
            var data = terrain.terrainData;
            Undo.RecordObject(data, "Surface grader band smooth");
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var copy = (float[,])heights.Clone();
            var changed = 0;

            for (var y = 1; y < res - 1; y++)
            {
                for (var x = 1; x < res - 1; x++)
                {
                    if (!InGraderSampleBand(terrain, x, y, res, groundCenter, extentMeters))
                        continue;

                    var avg = (copy[y - 1, x] + copy[y + 1, x] + copy[y, x - 1] + copy[y, x + 1]) * 0.25f;
                    var before = heights[y, x];
                    heights[y, x] = Mathf.Lerp(before, avg, smoothStrength);
                    if (Mathf.Abs(heights[y, x] - before) > 0.00005f)
                        changed++;
                }
            }

            if (changed > 0)
                data.SetHeights(0, 0, heights);

            return changed;
        }

        static int BoxBlurGraderSampleBand(
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            float blend)
        {
            var data = terrain.terrainData;
            Undo.RecordObject(data, "Surface grader band box blur");
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var copy = (float[,])heights.Clone();
            var changed = 0;

            for (var y = 1; y < res - 1; y++)
            {
                for (var x = 1; x < res - 1; x++)
                {
                    if (!InGraderSampleBand(terrain, x, y, res, groundCenter, extentMeters))
                        continue;

                    var sum = copy[y, x]
                        + copy[y - 1, x] + copy[y + 1, x]
                        + copy[y, x - 1] + copy[y, x + 1]
                        + copy[y - 1, x - 1] + copy[y - 1, x + 1]
                        + copy[y + 1, x - 1] + copy[y + 1, x + 1];
                    var before = heights[y, x];
                    heights[y, x] = Mathf.Lerp(before, sum / 9f, blend);
                    if (Mathf.Abs(heights[y, x] - before) > 0.00005f)
                        changed++;
                }
            }

            if (changed > 0)
                data.SetHeights(0, 0, heights);

            return changed;
        }

        static int SmoothOuterHeightRing(
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            float smoothStrength,
            float preserveRadiusFraction = SurfaceLidarTerrainAuthor.MainLandPreserveRadiusFraction)
        {
            var data = terrain.terrainData;
            Undo.RecordObject(data, "Surface outer smooth");
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var preserveR = extentMeters * preserveRadiusFraction;
            var outerMax = extentMeters * 1.05f;
            var copy = (float[,])heights.Clone();
            var changed = 0;

            for (var y = 1; y < res - 1; y++)
            {
                for (var x = 1; x < res - 1; x++)
                {
                    if (!InSmoothRingOnTerrain(terrain, x, y, res, groundCenter, preserveR, outerMax))
                        continue;

                    var avg = (copy[y - 1, x] + copy[y + 1, x] + copy[y, x - 1] + copy[y, x + 1]) * 0.25f;
                    var before = heights[y, x];
                    heights[y, x] = Mathf.Lerp(before, avg, smoothStrength);
                    if (Mathf.Abs(heights[y, x] - before) > 0.00005f)
                        changed++;
                }
            }

            data.SetHeights(0, 0, heights);
            return changed;
        }

        static void AlignPolylineToHillshade(
            Terrain terrain,
            Vector3[] worldPoints,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            bool flattenBright)
        {
            if (!TryLoadHillshade(seed, out var tex))
                return;

            try
            {
                var data = terrain.terrainData;
                var res = data.heightmapResolution;
                var heights = data.GetHeights(0, 0, res, res);
                var size = data.size;
                var origin = terrain.transform.position;

                for (var y = 0; y < res; y++)
                {
                    for (var x = 0; x < res; x++)
                    {
                        var wx = origin.x + x / (float)(res - 1) * size.x;
                        var wz = origin.z + y / (float)(res - 1) * size.z;
                        var p = new Vector3(wx, 0f, wz);
                        var distLine = SurfaceTerrainRadialAuthorDistance.DistanceToPolylineXZ(p, worldPoints);
                        if (distLine > 6f)
                            continue;

                        var distCenter = Vector2.Distance(
                            new Vector2(wx, wz),
                            new Vector2(groundCenter.x, groundCenter.z));
                        if (distCenter < extentMeters * SurfaceLidarTerrainAuthor.MainLandPreserveRadiusFraction)
                            continue;

                        var u = x / (float)(res - 1);
                        var v = y / (float)(res - 1);
                        var lum = tex.GetPixelBilinear(u, v).grayscale;
                        var t = 1f - distLine / 6f;
                        var delta = flattenBright
                            ? (lum - 0.55f) * 0.012f * t
                            : (0.42f - lum) * 0.018f * t;
                        heights[y, x] = Mathf.Clamp01(heights[y, x] + delta);
                    }
                }

                data.SetHeights(0, 0, heights);
            }
            finally
            {
                if (tex != null)
                    Object.DestroyImmediate(tex);
            }
        }

        static bool TryDepressWaterBasin(
            Terrain terrain,
            Vector3 waterCenter,
            Vector3 groundCenter,
            float extentMeters,
            int seed)
        {
            if (!TryLoadHillshade(seed, out var tex))
                return false;

            try
            {
                var data = terrain.terrainData;
                var res = data.heightmapResolution;
                var heights = data.GetHeights(0, 0, res, res);
                var size = data.size;
                var origin = terrain.transform.position;
                var radius = 12f;
                var changed = false;

                for (var y = 0; y < res; y++)
                {
                    for (var x = 0; x < res; x++)
                    {
                        var wx = origin.x + x / (float)(res - 1) * size.x;
                        var wz = origin.z + y / (float)(res - 1) * size.z;
                        var d = Vector2.Distance(
                            new Vector2(wx, wz),
                            new Vector2(waterCenter.x, waterCenter.z));
                        if (d > radius)
                            continue;

                        var distFromPlay = Vector2.Distance(
                            new Vector2(waterCenter.x, waterCenter.z),
                            new Vector2(groundCenter.x, groundCenter.z));
                        if (distFromPlay < extentMeters * 0.52f)
                            continue;

                        var u = x / (float)(res - 1);
                        var v = y / (float)(res - 1);
                        var lum = tex.GetPixelBilinear(u, v).grayscale;
                        var bowl = (1f - d / radius) * (0.45f - lum) * 0.008f;
                        if (bowl <= 0.0001f)
                            continue;
                        heights[y, x] = Mathf.Clamp01(heights[y, x] - bowl);
                        changed = true;
                    }
                }

                if (changed)
                    data.SetHeights(0, 0, heights);
                return changed;
            }
            finally
            {
                if (tex != null)
                    Object.DestroyImmediate(tex);
            }
        }

        public static bool TryLoadHillshade(int seed, out Texture2D tex)
        {
            tex = null;
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var indexPath = Path.Combine(hub, SurfaceLidarTerrainAuthor.HillshadesIndexRel);
            if (!File.Exists(indexPath))
                return false;

            try
            {
                var index = JsonUtility.FromJson<HillshadeIndexWrapper>(File.ReadAllText(indexPath));
                if (index?.counties == null || index.counties.Length == 0)
                    return false;
                var pick = index.counties[Mathf.Abs(seed) % index.counties.Length];
                var rel = pick.path?.Replace('\\', '/');
                if (string.IsNullOrEmpty(rel))
                    return false;
                var pngPath = Path.Combine(hub, rel);
                if (!File.Exists(pngPath))
                    return false;
                var bytes = File.ReadAllBytes(pngPath);
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                return tex.LoadImage(bytes);
            }
            catch
            {
                return false;
            }
        }

        [System.Serializable]
        class HillshadeIndexWrapper
        {
            public CountyRow[] counties;
        }

        [System.Serializable]
        class CountyRow
        {
            public string countyId;
            public string path;
        }

        static bool TryRoadPolyline(Transform road, out Vector3[] pts)
        {
            pts = null;
            if (road == null)
                return false;
            var halfLen = road.localScale.z * 0.5f;
            var a = road.position - road.forward * halfLen;
            var b = road.position + road.forward * halfLen;
            pts = new[] { a, b };
            return true;
        }

        static Vector3[] CollectWaypointPolyline(Transform trail)
        {
            var list = new List<Vector3>();
            for (var i = 0; i < trail.childCount; i++)
            {
                var c = trail.GetChild(i);
                if (c.name.StartsWith("Waypoint", System.StringComparison.Ordinal))
                    list.Add(c.position);
            }

            return list.Count >= 2 ? list.ToArray() : null;
        }

        static bool InSmoothRingOnTerrain(
            Terrain terrain,
            int x,
            int y,
            int res,
            Vector3 playCenter,
            float preserveRadiusMeters,
            float outerMaxMeters)
        {
            if (terrain?.terrainData == null)
                return false;

            var size = terrain.terrainData.size;
            var origin = terrain.transform.position;
            var wx = origin.x + x / (float)Mathf.Max(1, res - 1) * size.x;
            var wz = origin.z + y / (float)Mathf.Max(1, res - 1) * size.z;
            if (!SurfaceTerrainPlayRegion.ContainsWorldXZ(terrain, wx, wz))
                return false;

            var dist = Vector2.Distance(
                new Vector2(wx, wz),
                new Vector2(playCenter.x, playCenter.z));
            return dist >= preserveRadiusMeters && dist <= outerMaxMeters;
        }

        sealed class FootprintSmoothSession
        {
            public Terrain Terrain;
            public float Strength;
            public int Res;
            public float[,] Heights;
            public float[,] Copy;
            public int RowY;
            public int Changed;
            public string ProgressLabel;
            public float Progress01;
            public System.Action<int> OnComplete;
        }

        enum FootprintSmoothStep
        {
            Load,
            ProcessRows,
            UploadRows,
            Done,
        }

        sealed class SmoothRingSession
        {
            public Terrain Terrain;
            public Vector3 PlayCenter;
            public float ExtentMeters;
            public float Strength;
            public int Res;
            public float[,] Heights;
            public float[,] Copy;
            public int RowY;
            public int Changed;
            public float PreserveRadiusMeters;
            public float OuterMaxMeters;
            public System.Action<int> OnComplete;
        }

        enum SmoothRingStep
        {
            Load,
            ProcessRows,
            UploadRows,
            Done,
        }

        static int SmoothRowChunk(int res)
        {
            if (CaveBuildEditorResponsiveness.IsLongBuildActive)
                return res >= 1025 ? 32 : res >= 513 ? 96 : 128;
            return res >= 1025 ? 4 : res >= 513 ? 8 : 16;
        }

        static void RunFootprintSmoothStep(FootprintSmoothSession session, FootprintSmoothStep step)
        {
            if (session?.Terrain?.terrainData == null)
            {
                session?.OnComplete?.Invoke(0);
                return;
            }

            switch (step)
            {
                case FootprintSmoothStep.Load:
                    Undo.RecordObject(session.Terrain.terrainData, "Surface footprint smooth");
                    session.Heights = session.Terrain.terrainData.GetHeights(0, 0, session.Res, session.Res);
                    session.Copy = (float[,])session.Heights.Clone();
                    session.RowY = 1;
                    session.Changed = 0;
                    CaveBuildActionPacing.TouchQueueActivity();
                    CaveBuildActionPacing.ScheduleNextEditorFrame(
                        () => RunFootprintSmoothStep(session, FootprintSmoothStep.ProcessRows));
                    break;

                case FootprintSmoothStep.ProcessRows:
                {
                    var chunk = SmoothRowChunk(session.Res);
                    var yEnd = Mathf.Min(session.Res - 1, session.RowY + chunk);
                    session.Changed += ApplyFootprintLaplacianRows(
                        session.Heights,
                        session.Copy,
                        session.Res,
                        session.Strength,
                        session.RowY,
                        yEnd);
                    session.RowY = yEnd;
                    CaveBuildRunStatusPublisher.PulseSubOperation(
                        "Surface",
                        $"smoothing {session.ProgressLabel} rows {session.RowY}/{session.Res - 1}");
                    EditorUtility.DisplayProgressBar(
                        "Environment Kit",
                        $"[Surface] smoothing {session.ProgressLabel} rows {session.RowY}/{session.Res - 1}…",
                        session.Progress01);
                    CaveBuildActionPacing.TouchQueueActivity();

                    if (session.RowY < session.Res - 1)
                    {
                        CaveBuildActionPacing.ScheduleNextEditorFrame(
                            () => RunFootprintSmoothStep(session, FootprintSmoothStep.ProcessRows));
                        return;
                    }

                    session.RowY = 0;
                    CaveBuildActionPacing.ScheduleNextEditorFrame(
                        () => RunFootprintSmoothStep(session, FootprintSmoothStep.UploadRows));
                    break;
                }

                case FootprintSmoothStep.UploadRows:
                {
                    var chunk = SmoothRowChunk(session.Res);
                    var yEnd = Mathf.Min(session.Res, session.RowY + chunk);
                    FlushHeightRows(session.Terrain, session.Heights, session.Res, session.RowY, yEnd);
                    session.RowY = yEnd;
                    CaveBuildActionPacing.TouchQueueActivity();

                    if (session.RowY < session.Res)
                    {
                        CaveBuildActionPacing.ScheduleNextEditorFrame(
                            () => RunFootprintSmoothStep(session, FootprintSmoothStep.UploadRows));
                        return;
                    }

                    CaveBuildActionPacing.ScheduleNextEditorFrame(
                        () => RunFootprintSmoothStep(session, FootprintSmoothStep.Done));
                    break;
                }

                case FootprintSmoothStep.Done:
                    if (session.Changed > 0)
                    {
                        CaveBuildMicroTerrainHeightmap.QueueFinalizeDelayedLod(
                            session.Terrain,
                            () => session.OnComplete?.Invoke(session.Changed));
                        break;
                    }

                    session.OnComplete?.Invoke(session.Changed);
                    break;
            }
        }

        static void RunSmoothRingStep(SmoothRingSession session, SmoothRingStep step)
        {
            if (session?.Terrain?.terrainData == null)
            {
                session?.OnComplete?.Invoke(0);
                return;
            }

            switch (step)
            {
                case SmoothRingStep.Load:
                    Undo.RecordObject(session.Terrain.terrainData, "Surface outer smooth");
                    session.Heights = session.Terrain.terrainData.GetHeights(0, 0, session.Res, session.Res);
                    session.Copy = (float[,])session.Heights.Clone();
                    session.PreserveRadiusMeters =
                        session.ExtentMeters * SurfaceLidarTerrainAuthor.MainLandPreserveRadiusFraction;
                    session.OuterMaxMeters = session.ExtentMeters * 1.05f;
                    session.RowY = 1;
                    session.Changed = 0;
                    CaveBuildActionPacing.TouchQueueActivity();
                    CaveBuildActionPacing.ScheduleNextEditorFrame(
                        () => RunSmoothRingStep(session, SmoothRingStep.ProcessRows));
                    break;

                case SmoothRingStep.ProcessRows:
                {
                    var chunk = SmoothRowChunk(session.Res);
                    var yEnd = Mathf.Min(session.Res - 1, session.RowY + chunk);
                    for (var y = session.RowY; y < yEnd; y++)
                    {
                        for (var x = 1; x < session.Res - 1; x++)
                        {
                            if (!InSmoothRingOnTerrain(
                                    session.Terrain,
                                    x,
                                    y,
                                    session.Res,
                                    session.PlayCenter,
                                    session.PreserveRadiusMeters,
                                    session.OuterMaxMeters))
                                continue;

                            var avg = (session.Copy[y - 1, x] + session.Copy[y + 1, x] +
                                       session.Copy[y, x - 1] + session.Copy[y, x + 1]) * 0.25f;
                            var before = session.Heights[y, x];
                            session.Heights[y, x] = Mathf.Lerp(before, avg, session.Strength);
                            if (Mathf.Abs(session.Heights[y, x] - before) > 0.00005f)
                                session.Changed++;
                        }
                    }

                    session.RowY = yEnd;
                    CaveBuildActionPacing.TouchQueueActivity();
                    EditorUtility.DisplayProgressBar(
                        "Environment Kit",
                        $"[Surface] outer smooth rows {session.RowY}/{session.Res - 1}",
                        0.42f);

                    if (session.RowY < session.Res - 1)
                    {
                        CaveBuildActionPacing.ScheduleNextEditorFrame(
                            () => RunSmoothRingStep(session, SmoothRingStep.ProcessRows));
                        return;
                    }

                    session.RowY = 0;
                    CaveBuildActionPacing.ScheduleNextEditorFrame(
                        () => RunSmoothRingStep(session, SmoothRingStep.UploadRows));
                    break;
                }

                case SmoothRingStep.UploadRows:
                {
                    var chunk = SmoothRowChunk(session.Res);
                    var yEnd = Mathf.Min(session.Res, session.RowY + chunk);
                    FlushHeightRows(session.Terrain, session.Heights, session.Res, session.RowY, yEnd);
                    session.RowY = yEnd;
                    CaveBuildActionPacing.TouchQueueActivity();

                    if (session.RowY < session.Res)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Environment Kit",
                            $"[Surface] outer smooth upload {session.RowY}/{session.Res}",
                            0.42f);
                        CaveBuildActionPacing.ScheduleNextEditorFrame(
                            () => RunSmoothRingStep(session, SmoothRingStep.UploadRows));
                        return;
                    }

                    CaveBuildActionPacing.ScheduleNextEditorFrame(
                        () => RunSmoothRingStep(session, SmoothRingStep.Done));
                    break;
                }

                case SmoothRingStep.Done:
                    if (session.Changed > 0)
                        session.Terrain.Flush();
                    session.OnComplete?.Invoke(session.Changed);
                    break;
            }
        }

        static void FlushHeightRows(Terrain terrain, float[,] heights, int res, int yStart, int yEnd)
        {
            var rowCount = yEnd - yStart;
            if (rowCount <= 0 || heights == null || terrain?.terrainData == null)
                return;

            var slice = new float[rowCount, res];
            for (var y = 0; y < rowCount; y++)
            {
                for (var x = 0; x < res; x++)
                    slice[y, x] = heights[yStart + y, x];
            }

            terrain.terrainData.SetHeights(0, yStart, slice);
        }
    }

    /// <summary>Exposes polyline distance for refinement without duplicating radial author internals.</summary>
    static class SurfaceTerrainRadialAuthorDistance
    {
        public static float DistanceToPolylineXZ(Vector3 p, Vector3[] points)
        {
            if (points == null || points.Length < 2)
                return float.MaxValue;
            var best = float.MaxValue;
            for (var i = 0; i < points.Length - 1; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                var ab = b - a;
                ab.y = 0f;
                var ap = p - a;
                ap.y = 0f;
                var t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
                var closest = a + ab * t;
                var d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(closest.x, closest.z));
                if (d < best)
                    best = d;
            }

            return best;
        }
    }
}
#endif
