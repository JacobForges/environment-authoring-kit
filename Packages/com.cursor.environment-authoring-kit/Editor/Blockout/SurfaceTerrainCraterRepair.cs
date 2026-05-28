#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Fills bowl/crater artifacts in the heightmap (radial water stamp, bad carve, etc.).</summary>
    public static class SurfaceTerrainCraterRepair
    {
        // Must match SurfaceTerrainHeightAnalyzer thresholds so grader + fixer agree.
        // SurfaceTerrainHeightAnalyzer currently grades:
        // - crater when delta < -0.018
        // - spike  when delta >  0.022
        const float CraterDepthThreshold = 0.018f;
        const float SpikeRaiseThreshold = 0.022f;
        const float DeepCraterDepthThreshold = 0.036f;
        const int RingInner = 2;
        // Must match SurfaceTerrainHeightAnalyzer ring neighborhood sizing.
        const int RingOuter = 6;
        const int DeepRingInner = 5;
        const int DeepRingOuter = 12;
        const int MegaDipRingInner = 8;
        const int MegaDipRingOuter = 18;
        const float MegaDipDepthThreshold = 0.08f;
        // Full relaxation to neighborhood mean clears residual grader outliers in one shot.
        const float FillStrength = 1f;

        public static int RepairCraterCells(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            float preserveCenterRadiusMeters = 0f)
        {
            if (terrain == null || terrain.terrainData == null)
                return 0;

            var data = terrain.terrainData;
            Undo.RecordObject(data, "Repair terrain craters");

            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;
            var fixedCount = 0;

            for (var y = RingOuter; y < res - RingOuter; y++)
            {
                for (var x = RingOuter; x < res - RingOuter; x++)
                {
                    if (!SurfaceTerrainPlayRegion.InPlayAnnulusOnTerrain(
                            terrain,
                            x,
                            y,
                            res,
                            centerWorld,
                            extentMeters,
                            innerFraction: preserveCenterRadiusMeters > 0f
                                ? preserveCenterRadiusMeters / Mathf.Max(1f, extentMeters)
                                : 0.08f,
                            outerFraction: 1.05f))
                        continue;

                    var centerH = heights[y, x];
                    var ringSum = 0f;
                    var ringN = 0;
                    for (var dy = -RingOuter; dy <= RingOuter; dy++)
                    {
                        for (var dx = -RingOuter; dx <= RingOuter; dx++)
                        {
                            var r2 = dx * dx + dy * dy;
                            if (r2 < RingInner * RingInner || r2 > RingOuter * RingOuter)
                                continue;
                            ringSum += heights[y + dy, x + dx];
                            ringN++;
                        }
                    }

                    if (ringN < 4)
                        continue;

                    var ringAvg = ringSum / ringN;
                    if (centerH >= ringAvg - CraterDepthThreshold)
                        continue;

                    heights[y, x] = Mathf.Lerp(centerH, ringAvg, FillStrength);
                    fixedCount++;
                }
            }

            if (fixedCount > 0)
                data.SetHeights(0, 0, heights);

            return fixedCount;
        }

        public static int RepairSpikeCells(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            float preserveCenterRadiusMeters = 0f)
        {
            if (terrain == null || terrain.terrainData == null)
                return 0;

            var data = terrain.terrainData;
            Undo.RecordObject(data, "Repair terrain spikes");

            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var fixedCount = 0;

            for (var y = RingOuter; y < res - RingOuter; y++)
            {
                for (var x = RingOuter; x < res - RingOuter; x++)
                {
                    if (!SurfaceTerrainPlayRegion.InPlayAnnulusOnTerrain(
                            terrain,
                            x,
                            y,
                            res,
                            centerWorld,
                            extentMeters,
                            innerFraction: preserveCenterRadiusMeters > 0f
                                ? preserveCenterRadiusMeters / Mathf.Max(1f, extentMeters)
                                : 0.08f,
                            outerFraction: 1.05f))
                        continue;

                    var centerH = heights[y, x];
                    var ringSum = 0f;
                    var ringN = 0;
                    for (var dy = -RingOuter; dy <= RingOuter; dy++)
                    {
                        for (var dx = -RingOuter; dx <= RingOuter; dx++)
                        {
                            var r2 = dx * dx + dy * dy;
                            if (r2 < RingInner * RingInner || r2 > RingOuter * RingOuter)
                                continue;
                            ringSum += heights[y + dy, x + dx];
                            ringN++;
                        }
                    }

                    if (ringN < 4)
                        continue;

                    var ringAvg = ringSum / ringN;
                    if (centerH <= ringAvg + SpikeRaiseThreshold)
                        continue;

                    heights[y, x] = Mathf.Lerp(centerH, ringAvg, FillStrength);
                    fixedCount++;
                }
            }

            if (fixedCount > 0)
                data.SetHeights(0, 0, heights);

            return fixedCount;
        }

        /// <summary>Wider-ring fill for deep bowls that survive the grader-matched kernel (DEM / radial stamp compounding).</summary>
        public static int RepairDeepBowlCells(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            float preserveCenterRadiusMeters = 0f)
        {
            if (terrain == null || terrain.terrainData == null)
                return 0;

            var data = terrain.terrainData;
            Undo.RecordObject(data, "Repair deep terrain bowls");

            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var fixedCount = 0;

            for (var y = DeepRingOuter; y < res - DeepRingOuter; y++)
            {
                for (var x = DeepRingOuter; x < res - DeepRingOuter; x++)
                {
                    if (!SurfaceTerrainPlayRegion.InPlayAnnulusOnTerrain(
                            terrain,
                            x,
                            y,
                            res,
                            centerWorld,
                            extentMeters,
                            innerFraction: preserveCenterRadiusMeters > 0f
                                ? preserveCenterRadiusMeters / Mathf.Max(1f, extentMeters)
                                : 0.08f,
                            outerFraction: 1.05f))
                        continue;

                    var centerH = heights[y, x];
                    var ringSum = 0f;
                    var ringN = 0;
                    for (var dy = -DeepRingOuter; dy <= DeepRingOuter; dy++)
                    {
                        for (var dx = -DeepRingOuter; dx <= DeepRingOuter; dx++)
                        {
                            var r2 = dx * dx + dy * dy;
                            if (r2 < DeepRingInner * DeepRingInner || r2 > DeepRingOuter * DeepRingOuter)
                                continue;
                            ringSum += heights[y + dy, x + dx];
                            ringN++;
                        }
                    }

                    if (ringN < 8)
                        continue;

                    var ringAvg = ringSum / ringN;
                    if (centerH >= ringAvg - DeepCraterDepthThreshold)
                        continue;

                    heights[y, x] = Mathf.Lerp(centerH, ringAvg, FillStrength);
                    fixedCount++;
                }
            }

            if (fixedCount > 0)
                data.SetHeights(0, 0, heights);

            return fixedCount;
        }

        /// <summary>Very wide kernel for DEM / tile-seam dips that exceed the deep-bowl pass (max depth &gt; ~0.08 norm).</summary>
        public static int RepairMegaDipCells(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            float preserveCenterRadiusMeters = 0f)
        {
            if (terrain == null || terrain.terrainData == null)
                return 0;

            var data = terrain.terrainData;
            Undo.RecordObject(data, "Repair mega terrain dips");

            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var fixedCount = 0;

            for (var y = MegaDipRingOuter; y < res - MegaDipRingOuter; y++)
            {
                for (var x = MegaDipRingOuter; x < res - MegaDipRingOuter; x++)
                {
                    if (!SurfaceTerrainPlayRegion.InPlayAnnulusOnTerrain(
                            terrain,
                            x,
                            y,
                            res,
                            centerWorld,
                            extentMeters,
                            innerFraction: preserveCenterRadiusMeters > 0f
                                ? preserveCenterRadiusMeters / Mathf.Max(1f, extentMeters)
                                : 0.08f,
                            outerFraction: 1.05f))
                        continue;

                    var centerH = heights[y, x];
                    var ringSum = 0f;
                    var ringN = 0;
                    for (var dy = -MegaDipRingOuter; dy <= MegaDipRingOuter; dy++)
                    {
                        for (var dx = -MegaDipRingOuter; dx <= MegaDipRingOuter; dx++)
                        {
                            var r2 = dx * dx + dy * dy;
                            if (r2 < MegaDipRingInner * MegaDipRingInner || r2 > MegaDipRingOuter * MegaDipRingOuter)
                                continue;
                            ringSum += heights[y + dy, x + dx];
                            ringN++;
                        }
                    }

                    if (ringN < 12)
                        continue;

                    var ringAvg = ringSum / ringN;
                    if (centerH >= ringAvg - MegaDipDepthThreshold)
                        continue;

                    heights[y, x] = ringAvg;
                    fixedCount++;
                }
            }

            if (fixedCount > 0)
                data.SetHeights(0, 0, heights);

            return fixedCount;
        }

        /// <summary>Wide-ring fill for FBM/DEM compounding that survives the grader-matched 6-cell kernel.</summary>
        public static int RepairWideContextCells(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            float preserveCenterRadiusMeters = 0f)
        {
            if (terrain == null || terrain.terrainData == null)
                return 0;

            const int wideInner = 9;
            const int wideOuter = 20;
            const float wideCraterThreshold = 0.014f;
            const float wideSpikeThreshold = 0.018f;

            var data = terrain.terrainData;
            Undo.RecordObject(data, "Repair wide terrain context");

            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var fixedCount = 0;

            for (var y = wideOuter; y < res - wideOuter; y++)
            {
                for (var x = wideOuter; x < res - wideOuter; x++)
                {
                    if (!SurfaceTerrainPlayRegion.InPlayAnnulusOnTerrain(
                            terrain,
                            x,
                            y,
                            res,
                            centerWorld,
                            extentMeters,
                            innerFraction: preserveCenterRadiusMeters > 0f
                                ? preserveCenterRadiusMeters / Mathf.Max(1f, extentMeters)
                                : 0.08f,
                            outerFraction: 1.05f))
                        continue;

                    var centerH = heights[y, x];
                    var ringSum = 0f;
                    var ringN = 0;
                    for (var dy = -wideOuter; dy <= wideOuter; dy++)
                    {
                        for (var dx = -wideOuter; dx <= wideOuter; dx++)
                        {
                            var r2 = dx * dx + dy * dy;
                            if (r2 < wideInner * wideInner || r2 > wideOuter * wideOuter)
                                continue;
                            ringSum += heights[y + dy, x + dx];
                            ringN++;
                        }
                    }

                    if (ringN < 12)
                        continue;

                    var ringAvg = ringSum / ringN;
                    var delta = centerH - ringAvg;
                    if (delta >= -wideCraterThreshold && delta <= wideSpikeThreshold)
                        continue;

                    heights[y, x] = ringAvg;
                    fixedCount++;
                }
            }

            if (fixedCount > 0)
                data.SetHeights(0, 0, heights);

            return fixedCount;
        }

        /// <summary>Iterative crater + spike repair until stable or pass cap (heightfield ladder / post-sculpt).</summary>
        public static int RepairHeightfieldPlayable(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            int maxPasses = 10)
        {
            if (terrain == null)
                return 0;

            var total = 0;
            maxPasses = Mathf.Clamp(maxPasses, 1, 28);
            var innerBowlPasses = Mathf.Min(maxPasses, 8);

            // Ladder fix path skips sculpt-time de-checkerboard — run it here for checkerboard spikes.
            total += SurfaceTerrainHeightSmoothing.DeCheckerboardOnTerrain(
                terrain, centerWorld, extentMeters, strength: 0.48f);
            total += SurfaceTerrainRefinement.BoxBlurGraderSampleBandPublic(
                terrain, centerWorld, extentMeters, blend: 0.38f);
            total += SurfaceTerrainRefinement.BoxBlurGraderSampleBandPublic(
                terrain, centerWorld, extentMeters, blend: 0.32f);
            total += SurfaceTerrainRefinement.BoxBlurGraderSampleBandPublic(
                terrain, centerWorld, extentMeters, blend: 0.26f);
            total += SurfaceTerrainRefinement.BoxBlurGraderSampleBandPublic(
                terrain, centerWorld, extentMeters, blend: 0.20f);
            total += RepairMegaDipCells(terrain, centerWorld, extentMeters, preserveCenterRadiusMeters: 0f);

            for (var pass = 0; pass < maxPasses; pass++)
            {
                var n = RepairCraterCells(terrain, centerWorld, extentMeters, preserveCenterRadiusMeters: 0f);
                n += RepairSpikeCells(terrain, centerWorld, extentMeters, preserveCenterRadiusMeters: 0f);
                if (pass < 8)
                    n += RepairDeepBowlCells(terrain, centerWorld, extentMeters, preserveCenterRadiusMeters: 0f);
                if (pass < 6)
                    n += RepairMegaDipCells(terrain, centerWorld, extentMeters, preserveCenterRadiusMeters: 0f);
                if (pass < 8)
                    n += RepairWideContextCells(terrain, centerWorld, extentMeters, preserveCenterRadiusMeters: 0f);
                // Radial bowls often linger in the disk — revisit for the first passes, not once.
                if (pass < innerBowlPasses)
                    n += RepairCenterBowl(terrain, centerWorld, extentMeters * 0.55f);
                total += n;
                if (n == 0)
                    break;
            }

            total += PrefilterGraderBand(terrain, centerWorld, extentMeters, passes: 10, strength: 0.48f);
            // Outer-ring smooth is playable_slopes rung — it reintroduces bowl/spike clusters against the 6-cell grader kernel.
            for (var tail = 0; tail < 18; tail++)
            {
                var n = RepairCraterCells(terrain, centerWorld, extentMeters, preserveCenterRadiusMeters: 0f);
                n += RepairSpikeCells(terrain, centerWorld, extentMeters, preserveCenterRadiusMeters: 0f);
                if (tail < 8)
                    n += RepairMegaDipCells(terrain, centerWorld, extentMeters, preserveCenterRadiusMeters: 0f);
                if (tail < 7)
                    n += RepairWideContextCells(terrain, centerWorld, extentMeters, preserveCenterRadiusMeters: 0f);
                if (tail < 3)
                    n += RepairDeepBowlCells(terrain, centerWorld, extentMeters, preserveCenterRadiusMeters: 0f);
                total += n;
                if (n == 0)
                    break;
            }

            terrain.Flush();
            return total;
        }

        /// <summary>Extra pass on the inner disk where radial water bowls often punch holes.</summary>
        public static int RepairCenterBowl(
            Terrain terrain,
            Vector3 centerWorld,
            float innerRadiusMeters)
        {
            if (terrain == null || innerRadiusMeters <= 1f)
                return 0;

            return RepairCraterCells(terrain, centerWorld, innerRadiusMeters * 2.5f, preserveCenterRadiusMeters: 0f);
        }

        public static int PrefilterGraderBand(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            int passes = 3,
            float strength = 0.24f)
        {
            if (terrain == null)
                return 0;

            var total = 0;
            passes = Mathf.Clamp(passes, 1, 8);
            for (var i = 0; i < passes; i++)
            {
                total += SurfaceTerrainRefinement.SmoothGraderSampleBandPublic(
                    terrain, centerWorld, extentMeters, strength);
            }

            return total;
        }

        public static int RepairHeightfieldLadderPass(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters)
        {
            if (terrain == null)
                return 0;

            return RepairHeightfieldPlayable(terrain, centerWorld, extentMeters, maxPasses: 18);
        }

        /// <summary>
        /// Paced ladder/post-prop repair — one light pass per editor tick (avoids 18× full-map sync freeze).
        /// </summary>
        public static void QueueRepairHeightfieldLadderTile(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            System.Action<int> onComplete)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                onComplete?.Invoke(0);
                return;
            }

            var session = new LadderTileSession
            {
                Terrain = terrain,
                CenterWorld = centerWorld,
                ExtentMeters = extentMeters,
                OnComplete = onComplete,
                Step = LadderTileStep.DeCheckerboard,
            };
            CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunLadderTileStep(session));
        }

        sealed class LadderTileSession
        {
            public Terrain Terrain;
            public Vector3 CenterWorld;
            public float ExtentMeters;
            public int Total;
            public LadderTileStep Step;
            public System.Action<int> OnComplete;
        }

        enum LadderTileStep
        {
            DeCheckerboard,
            BlurBand,
            CraterCells,
            SpikePass,
            Done,
        }

        static void RunLadderTileStep(LadderTileSession session)
        {
            if (session?.Terrain == null)
            {
                session?.OnComplete?.Invoke(0);
                return;
            }

            CaveBuildActionPacing.TouchQueueActivity();

            switch (session.Step)
            {
                case LadderTileStep.DeCheckerboard:
                    session.Total += SurfaceTerrainHeightSmoothing.DeCheckerboardOnTerrain(
                        session.Terrain, session.CenterWorld, session.ExtentMeters, strength: 0.42f);
                    session.Step = LadderTileStep.BlurBand;
                    CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunLadderTileStep(session));
                    break;

                case LadderTileStep.BlurBand:
                    session.Total += SurfaceTerrainRefinement.BoxBlurGraderSampleBandPublic(
                        session.Terrain, session.CenterWorld, session.ExtentMeters, blend: 0.32f);
                    session.Step = LadderTileStep.CraterCells;
                    CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunLadderTileStep(session));
                    break;

                case LadderTileStep.CraterCells:
                    QueueRepairCraterCells(
                        session.Terrain,
                        session.CenterWorld,
                        session.ExtentMeters,
                        preserveCenterRadiusMeters: 0f,
                        count =>
                        {
                            session.Total += count;
                            session.Step = LadderTileStep.SpikePass;
                            CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunLadderTileStep(session));
                        });
                    break;

                case LadderTileStep.SpikePass:
                    session.Total += RepairSpikeCells(
                        session.Terrain, session.CenterWorld, session.ExtentMeters, preserveCenterRadiusMeters: 0f);
                    session.Total += RepairMegaDipCells(
                        session.Terrain, session.CenterWorld, session.ExtentMeters, preserveCenterRadiusMeters: 0f);
                    session.Step = LadderTileStep.Done;
                    CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunLadderTileStep(session));
                    break;

                case LadderTileStep.Done:
                    session.Terrain.Flush();
                    session.OnComplete?.Invoke(session.Total);
                    break;
            }
        }

        /// <summary>Paced crater repair for terrain ladder fix pass 1 (one upload band per frame).</summary>
        public static void QueueRepairCraterCells(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            float preserveCenterRadiusMeters,
            System.Action<int> onComplete)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                onComplete?.Invoke(0);
                return;
            }

            var session = new RepairSession
            {
                Terrain = terrain,
                CenterWorld = centerWorld,
                ExtentMeters = extentMeters,
                PreserveCenterRadiusMeters = preserveCenterRadiusMeters,
                Res = terrain.terrainData.heightmapResolution,
                OnComplete = onComplete,
            };
            CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunRepairStep(session, RepairStep.Prepare));
        }

        sealed class RepairSession
        {
            public Terrain Terrain;
            public Vector3 CenterWorld;
            public float ExtentMeters;
            public float PreserveCenterRadiusMeters;
            public int Res;
            public float[,] Heights;
            public int RowY;
            public int FixedCount;
            public System.Action<int> OnComplete;
        }

        enum RepairStep
        {
            Prepare,
            UploadRows,
            Done,
        }

        static int RepairRowChunk(int res) =>
            res >= 1025 ? 4 : res >= 513 ? 6 : 16;

        static void RunRepairStep(RepairSession session, RepairStep step)
        {
            if (session?.Terrain?.terrainData == null)
            {
                session?.OnComplete?.Invoke(0);
                return;
            }

            switch (step)
            {
                case RepairStep.Prepare:
                    Undo.RecordObject(session.Terrain.terrainData, "Repair terrain craters");
                    session.Heights = session.Terrain.terrainData.GetHeights(0, 0, session.Res, session.Res);
                    var innerFrac = session.PreserveCenterRadiusMeters > 0f
                        ? session.PreserveCenterRadiusMeters / Mathf.Max(1f, session.ExtentMeters)
                        : 0.08f;

                    for (var y = RingOuter; y < session.Res - RingOuter; y++)
                    {
                        if ((y & 63) == 0)
                            CaveBuildActionPacing.TouchQueueActivity();

                        for (var x = RingOuter; x < session.Res - RingOuter; x++)
                        {
                            if (!SurfaceTerrainPlayRegion.InPlayAnnulusOnTerrain(
                                    session.Terrain,
                                    x,
                                    y,
                                    session.Res,
                                    session.CenterWorld,
                                    session.ExtentMeters,
                                    innerFrac,
                                    1.05f))
                                continue;

                            var centerH = session.Heights[y, x];
                            var ringSum = 0f;
                            var ringN = 0;
                            for (var dy = -RingOuter; dy <= RingOuter; dy++)
                            {
                                for (var dx = -RingOuter; dx <= RingOuter; dx++)
                                {
                                    var r2 = dx * dx + dy * dy;
                                    if (r2 < RingInner * RingInner || r2 > RingOuter * RingOuter)
                                        continue;
                                    ringSum += session.Heights[y + dy, x + dx];
                                    ringN++;
                                }
                            }

                            if (ringN < 4)
                                continue;

                            var ringAvg = ringSum / ringN;
                            if (centerH >= ringAvg - CraterDepthThreshold)
                                continue;

                            session.Heights[y, x] = Mathf.Lerp(centerH, ringAvg, FillStrength);
                            session.FixedCount++;
                        }
                    }

                    session.RowY = 0;
                    if (session.FixedCount <= 0)
                    {
                        session.OnComplete?.Invoke(0);
                        return;
                    }

                    RunRepairStep(session, RepairStep.UploadRows);
                    break;

                case RepairStep.UploadRows:
                {
                    var chunk = RepairRowChunk(session.Res);
                    var yEnd = Mathf.Min(session.Res, session.RowY + chunk);
                    FlushRepairRows(session, session.RowY, yEnd);
                    session.RowY = yEnd;
                    if (session.RowY < session.Res)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Environment Kit",
                            $"[Surface] crater repair rows {session.RowY}/{session.Res}",
                            0.44f);
                        CaveBuildActionPacing.ScheduleNextEditorFrame(() =>
                            RunRepairStep(session, RepairStep.UploadRows));
                        return;
                    }

                    CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunRepairStep(session, RepairStep.Done));
                    break;
                }

                case RepairStep.Done:
                    session.Terrain.Flush();
                    session.OnComplete?.Invoke(session.FixedCount);
                    break;
            }
        }

        static void FlushRepairRows(RepairSession session, int yStart, int yEnd)
        {
            var rowCount = yEnd - yStart;
            if (rowCount <= 0 || session.Heights == null)
                return;

            var slice = new float[rowCount, session.Res];
            for (var y = 0; y < rowCount; y++)
            {
                for (var x = 0; x < session.Res; x++)
                    slice[y, x] = session.Heights[yStart + y, x];
            }

            session.Terrain.terrainData.SetHeights(0, yStart, slice);
        }
    }
}
#endif
