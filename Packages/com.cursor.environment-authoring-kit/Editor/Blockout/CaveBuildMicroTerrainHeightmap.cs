#if UNITY_EDITOR
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Paced terrain heightmap I/O — one row band per editor frame during long builds (full quality, no multi-second stalls).
    /// </summary>
    public static class CaveBuildMicroTerrainHeightmap
    {
        public delegate void MutateHeightBandDelegate(
            float[,] heights,
            int rowStart,
            int res,
            Vector3 origin,
            Vector3 size);

        /// <summary>Heightmap rows per queue step — capped at 1–2 under load/memory pressure.</summary>
        public static int BandRowsFor(int res)
        {
            if (CaveBuildLoadAwareBatching.PreferSingleItem())
                return 1;

            if (CaveBuildEditorResponsiveness.IsLongBuildActive ||
                CaveBuildSurfaceCompletionGate.IsFullWorldGridPipelineActive)
                return CaveBuildLoadAwareBatching.Clamp(2);

            return CaveBuildLoadAwareBatching.Clamp(2);
        }

        /// <summary>One SyncHeightmap after height commits — prevents SetResource ID overflow spam.</summary>
        public static void QueueFinalizeDelayedLod(Terrain terrain, System.Action onComplete) =>
            QueueFinalizeDelayedLod(
                terrain,
                onComplete,
                flushAllSurfaceTerrains: !CaveBuildTerrainHeightmapMemory.PreferSingleTileHeightmapFlush);

        /// <summary>Paced finalize — during long builds flush only the edited tile to avoid 49× SyncHeightmap hitches.</summary>
        public static void QueueFinalizeDelayedLod(
            Terrain terrain,
            System.Action onComplete,
            bool flushAllSurfaceTerrains)
        {
            CaveBuildActionPacing.ScheduleLight(() =>
            {
                if (flushAllSurfaceTerrains)
                    CaveBuildTerrainHeightmapMemory.FlushAllSurfaceTerrains(terrain);
                else
                    CaveBuildTerrainHeightmapMemory.CommitTerrainHeightmap(terrain);

                EnvironmentKitHardwareBudget.OnQueueStepCompletedThrottled();
                onComplete?.Invoke();
            }, CaveBuildPipelineDomains.QueueLabel("terrain height flush"));
        }

        public static void QueueWriteBand(
            Terrain terrain,
            float[,] heights,
            int yStart,
            int yEnd,
            bool delayLod,
            System.Action onDone)
        {
            CaveBuildActionPacing.ScheduleLight(() =>
            {
                if (terrain?.terrainData == null || heights == null || yEnd <= yStart)
                {
                    onDone?.Invoke();
                    return;
                }

                var res = heights.GetLength(1);
                var rowCount = yEnd - yStart;
                var slice = new float[rowCount, res];
                for (var y = 0; y < rowCount; y++)
                {
                    for (var x = 0; x < res; x++)
                        slice[y, x] = heights[yStart + y, x];
                }

                CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(terrain, 0, yStart, slice, delayLod);
                onDone?.Invoke();
            }, CaveBuildPipelineDomains.QueueLabel("terrain height rows"));
        }

        /// <summary>Fill terrain with a uniform normalized height, one row band per queued editor step.</summary>
        public static void QueueWriteUniformFlat(
            Terrain terrain,
            float normalizedHeight,
            string statusCategory,
            System.Action onComplete)
        {
            if (terrain?.terrainData == null)
            {
                onComplete?.Invoke();
                return;
            }

            var res = terrain.terrainData.heightmapResolution;
            var y = 0;
            const bool useDelay = true;

            void WriteNextBand()
            {
                var yEnd = Mathf.Min(res, y + BandRowsFor(res));
                CaveBuildActionPacing.ScheduleLight(
                    () =>
                    {
                        CaveBuildActionPacing.TouchQueueActivity();
                        var rowCount = yEnd - y;
                        var slice = new float[rowCount, res];
                        for (var ry = 0; ry < rowCount; ry++)
                        {
                            for (var x = 0; x < res; x++)
                                slice[ry, x] = normalizedHeight;
                        }

                        CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(terrain, 0, y, slice, useDelay);
                        CaveBuildRunStatusPublisher.PulseSubOperation(
                            statusCategory,
                            $"flat height rows {yEnd}/{res}");
                        y = yEnd;
                        if (y < res)
                            WriteNextBand();
                        else
                            QueueFinalizeDelayedLod(terrain, onComplete, flushAllSurfaceTerrains: false);
                    },
                    CaveBuildPipelineDomains.QueueLabel($"{statusCategory} — flat rows {yEnd}/{res}"));
            }

            WriteNextBand();
        }

        /// <summary>Write full height array in row bands (micro actions).</summary>
        public static void QueueWriteFull(
            Terrain terrain,
            float[,] heights,
            string statusCategory,
            string statusVerb,
            System.Action onComplete)
        {
            if (terrain?.terrainData == null || heights == null)
            {
                onComplete?.Invoke();
                return;
            }

            var res = heights.GetLength(0);
            var y = 0;
            var useDelay = !CaveBuildTerrainHeightmapMemory.PreferImmediateHeightCommits;

            void WriteNextBand()
            {
                var yEnd = Mathf.Min(res, y + BandRowsFor(res));
                CaveBuildRunStatusPublisher.PulseSubOperation(
                    statusCategory,
                    $"{statusVerb} rows {yEnd}/{res}");
                QueueWriteBand(terrain, heights, y, yEnd, useDelay, () =>
                {
                    y = yEnd;
                    if (y < res)
                        WriteNextBand();
                    else
                        QueueFinalizeDelayedLod(
                            terrain,
                            onComplete,
                            flushAllSurfaceTerrains: !CaveBuildTerrainHeightmapMemory.PreferSingleTileHeightmapFlush);
                });
            }

            WriteNextBand();
        }

        static void QueueWriteFullHeavy(
            Terrain terrain,
            float[,] heights,
            string statusCategory,
            string statusVerb,
            System.Action onComplete)
        {
            CaveBuildActionPacing.ScheduleHeavy(
                () =>
                {
                    CaveBuildActionPacing.TouchQueueActivity();
                    var res = heights.GetLength(0);
                    var band = BandRowsFor(res);
                    var useDelay = !CaveBuildTerrainHeightmapMemory.PreferImmediateHeightCommits;

                    for (var y = 0; y < res; y += band)
                    {
                        var yEnd = Mathf.Min(res, y + band);
                        var rowCount = yEnd - y;
                        var slice = new float[rowCount, res];
                        for (var ry = 0; ry < rowCount; ry++)
                        {
                            for (var x = 0; x < res; x++)
                                slice[ry, x] = heights[y + ry, x];
                        }

                        CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(terrain, 0, y, slice, useDelay);
                        CaveBuildRunStatusPublisher.PulseSubOperation(
                            statusCategory,
                            $"{statusVerb} rows {yEnd}/{res}");
                    }

                    if (CaveBuildTerrainHeightmapMemory.PreferSingleTileHeightmapFlush)
                        CaveBuildTerrainHeightmapMemory.FlushPendingTileHeightmap(terrain);
                    else
                        CaveBuildTerrainHeightmapMemory.CommitTerrainHeightmap(terrain);
                    EnvironmentKitHardwareBudget.OnQueueStepCompletedThrottled();
                    onComplete?.Invoke();
                },
                CaveBuildPipelineDomains.QueueLabel($"{statusCategory} — height upload batch"));
        }

        /// <summary>Load, mutate, and commit one heightmap row band per queued editor step.</summary>
        public static void QueueMutateRowBands(
            Terrain terrain,
            string undoLabel,
            string queueLabel,
            MutateHeightBandDelegate mutateBand,
            System.Action onComplete)
        {
            if (terrain?.terrainData == null || mutateBand == null)
            {
                onComplete?.Invoke();
                return;
            }

            var res = terrain.terrainData.heightmapResolution;
            var origin = terrain.transform.position;
            var size = terrain.terrainData.size;
            var row = 0;
            var recordedUndo = false;

            void RunBand()
            {
                var rowEnd = Mathf.Min(res, row + BandRowsFor(res));
                CaveBuildActionPacing.ScheduleLight(
                    () =>
                    {
                        CaveBuildActionPacing.TouchQueueActivity();
                        var bandRows = rowEnd - row;
                        var heights = terrain.terrainData.GetHeights(0, row, res, bandRows);
                        mutateBand(heights, row, res, origin, size);
                        if (!recordedUndo)
                        {
                            UnityEditor.Undo.RecordObject(terrain.terrainData, undoLabel);
                            recordedUndo = true;
                        }

                        CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(
                            terrain,
                            0,
                            row,
                            heights,
                            requestDelayLod: false);
                        row = rowEnd;
                        if (row < res)
                        {
                            CaveBuildActionPacing.ScheduleLight(RunBand, queueLabel);
                            return;
                        }

                        CaveBuildTerrainHeightmapMemory.FlushPendingTileHeightmap(terrain);
                        onComplete?.Invoke();
                    },
                    queueLabel);
            }

            RunBand();
        }
    }
}
#endif
