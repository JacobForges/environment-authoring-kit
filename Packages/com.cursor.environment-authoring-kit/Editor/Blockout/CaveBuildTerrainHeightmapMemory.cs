#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Limits managed/native churn and Unity terrain SetResource ID overflow during multi-tile builds.
    /// </summary>
    static class CaveBuildTerrainHeightmapMemory
    {
        /// <summary>289-tile grid allows 513² on wilderness tiles (was 257 at 1225 scale).</summary>
        public const int WildernessHeightmapResolutionCap = 513;

        /// <summary>Unity internal SetResource table overflows near 2^20−1 without frequent SyncHeightmap.</summary>
        const int MaxDelayLodWritesBeforeGlobalFlush = 2;
        const int ImmediateCommitBatch16Gb = 6;

        static int _delayLodWritesSinceFlush;
        static int _immediateCommitsSinceSync;

        public static void Release(ref float[,] buffer) => buffer = null;

        public static bool PreferImmediateHeightCommits =>
            CaveBuildEditorResponsiveness.IsLongBuildActive;

        /// <summary>16 GB extended grid — sync only the edited tile, not all ~289 terrains per micro step.</summary>
        public static bool PreferSingleTileHeightmapFlush =>
            CaveBuildEditorResponsiveness.IsLongBuildActive &&
            CaveBuildMemoryGuard.SystemRamGb() is > 0f and <= 17f &&
            (SurfaceTerrainTileExpansion.IsLiveFullWorldTerraformPhase ||
             SurfaceTerrainTileExpansion.PreferSequentialFullWorldTerrain ||
             CaveBuildLateBuildPerformance.IsInLateBuildBand);

        static bool PreferBatchedImmediateCommits =>
            PreferImmediateHeightCommits && PreferSingleTileHeightmapFlush;

        public static void CommitTerrainHeightmap(Terrain terrain)
        {
            if (terrain?.terrainData == null)
                return;

            terrain.terrainData.SyncHeightmap();
            terrain.Flush();
        }

        /// <summary>Write one height slice — avoids DelayLOD pile-up during FullWorld / 25-tile passes.</summary>
        public static void ApplyHeightSlice(
            Terrain terrain,
            int xBase,
            int yBase,
            float[,] slice,
            bool requestDelayLod)
        {
            if (terrain?.terrainData == null || slice == null)
                return;

            var data = terrain.terrainData;
            var useDelay = requestDelayLod && !PreferImmediateHeightCommits;

            if (useDelay)
            {
                data.SetHeightsDelayLOD(xBase, yBase, slice);
                _delayLodWritesSinceFlush++;
                if (_delayLodWritesSinceFlush >= MaxDelayLodWritesBeforeGlobalFlush)
                {
                    CommitTerrainHeightmap(terrain);
                    _delayLodWritesSinceFlush = 0;
                }
            }
            else
            {
                data.SetHeights(xBase, yBase, slice);
                if (PreferImmediateHeightCommits)
                {
                    _immediateCommitsSinceSync++;
                    if (!PreferBatchedImmediateCommits ||
                        _immediateCommitsSinceSync >= ImmediateCommitBatch16Gb)
                    {
                        CommitTerrainHeightmap(terrain);
                        _immediateCommitsSinceSync = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Seam stitch band — defer SyncHeightmap until tile finalize (513² SyncHeightmap can block 30–120s).
        /// </summary>
        public static void ApplySeamHeightBand(Terrain terrain, int xBase, int yBase, float[,] band)
        {
            if (terrain?.terrainData == null || band == null)
                return;

            if (CaveBuildEditorResponsiveness.IsLongBuildActive)
            {
                terrain.terrainData.SetHeightsDelayLOD(xBase, yBase, band);
                return;
            }

            ApplyHeightSlice(terrain, xBase, yBase, band, requestDelayLod: true);
        }

        public static void FlushAllSurfaceTerrains(Terrain mainTerrain)
        {
            _delayLodWritesSinceFlush = 0;
            _immediateCommitsSinceSync = 0;

            if (mainTerrain != null)
                CommitTerrainHeightmap(mainTerrain);

            foreach (var tile in SurfaceTerrainTileExpansion.CollectGameplayTiles(mainTerrain))
            {
                if (tile != null)
                    CommitTerrainHeightmap(tile);
            }

            foreach (var wild in SurfaceTerrainTileExpansion.CollectMountainWildernessTiles(mainTerrain))
            {
                if (wild != null)
                    CommitTerrainHeightmap(wild);
            }
        }

        /// <summary>289× SyncHeightmap in one frame blocks 2–3 min on 16 GB — pace during late build band.</summary>
        public static void QueueFlushAllSurfaceTerrainsPaced(Terrain mainTerrain, Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (!CaveBuildLateBuildPerformance.PreferLightweightTerrainFlush)
            {
                FlushAllSurfaceTerrains(mainTerrain);
                onComplete?.Invoke();
                return;
            }

            var tiles = new System.Collections.Generic.List<Terrain> { mainTerrain };
            tiles.AddRange(SurfaceTerrainTileExpansion.CollectGameplayTiles(mainTerrain));
            tiles.AddRange(SurfaceTerrainTileExpansion.CollectMountainWildernessTiles(mainTerrain));

            var index = 0;
            const int batch = 8;

            void FlushBatch()
            {
                if (index >= tiles.Count)
                {
                    ResetDelayLodBudget();
                    onComplete?.Invoke();
                    return;
                }

                var end = Mathf.Min(index + batch, tiles.Count);
                CaveBuildActionPacing.ScheduleLight(
                    () =>
                    {
                        for (var i = index; i < end; i++)
                        {
                            if (tiles[i] != null)
                                CommitTerrainHeightmap(tiles[i]);
                        }

                        index = end;
                        FlushBatch();
                    },
                    CaveBuildPipelineDomains.QueueLabel($"terrain paced flush {end}/{tiles.Count}"));
            }

            FlushBatch();
        }

        public static void FlushPendingTileHeightmap(Terrain terrain)
        {
            _immediateCommitsSinceSync = 0;
            ResetDelayLodBudget();
            if (terrain != null)
                CommitTerrainHeightmap(terrain);
        }

        public static void AfterTileHeightmapPass(Terrain terrain)
        {
            if (PreferSingleTileHeightmapFlush)
            {
                FlushPendingTileHeightmap(terrain);
                ReleaseWorkingSet();
                return;
            }

            FlushAllSurfaceTerrains(terrain);
            ReleaseWorkingSet();
        }

        public static void ReleaseWorkingSet(bool force = false)
        {
            ResetDelayLodBudget();
            if (!force && !CaveBuildEditorResponsiveness.IsLongBuildActive)
                return;

            GC.Collect(0, GCCollectionMode.Optimized);
        }

        public static void ResetDelayLodBudget()
        {
            _delayLodWritesSinceFlush = 0;
            _immediateCommitsSinceSync = 0;
        }

        /// <summary>
        /// Full flat heightmap via DelayLOD — no SyncHeightmap until <see cref="CommitGroundLayBatch"/>.
        /// </summary>
        public static void ApplyUniformFlatGroundLayDefer(Terrain terrain, float normalizedHeight)
        {
            if (terrain?.terrainData == null)
                return;

            var res = terrain.terrainData.heightmapResolution;
            var heights = new float[res, res];
            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                    heights[z, x] = normalizedHeight;
            }

            terrain.terrainData.SetHeightsDelayLOD(0, 0, heights);
        }

        /// <summary>Sync only tiles written this ground-lay batch — avoids per-tile or all-terrain flush storms.</summary>
        public static void CommitGroundLayBatch(IReadOnlyList<Terrain> tiles)
        {
            if (tiles == null || tiles.Count == 0)
                return;

            for (var i = 0; i < tiles.Count; i++)
                CommitTerrainHeightmap(tiles[i]);

            ResetDelayLodBudget();
        }

        public static int WildernessHeightmapResolution(TerrainData playDiskTemplate)
        {
            if (playDiskTemplate == null)
                return WildernessHeightmapResolutionCap;

            var playRes = playDiskTemplate.heightmapResolution;
            if (playRes <= WildernessHeightmapResolutionCap)
                return playRes;

            return WildernessHeightmapResolutionCap;
        }
    }
}
#endif
