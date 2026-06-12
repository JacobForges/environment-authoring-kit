#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.World;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Computes the paced editor-queue step budget for Hub progress and ETC (one Advance per ScheduleBuildStep).
    /// </summary>
    public static class CaveBuildPlannedStepBudget
    {
        const int DefaultHeightmapResolution = 513;

        public static int Compute(SurfaceBuildScope scope, int tileCount, bool extendedGrid)
        {
            tileCount = Mathf.Max(1, tileCount);

            if (scope == SurfaceBuildScope.FullWorld && extendedGrid && tileCount > 81)
                return ComputeExtendedFullWorld(tileCount);

            if (scope == SurfaceBuildScope.FullWorld)
                return ComputeCoreFullWorld(tileCount);

            return scope switch
            {
                SurfaceBuildScope.SurfaceOnly => ComputeSurfaceOnly(tileCount),
                SurfaceBuildScope.CaveOnly => ComputeCaveOnly(),
                _ => ComputeCoreFullWorld(tileCount),
            };
        }

        /// <summary>Hub concept + request flags — accurate paced-step denominator for ETC.</summary>
        public static int ComputeForRequest(WorldGenerationRequest request)
        {
            if (request == null)
                return Compute(SurfaceBuildScope.FullWorld, 289, extendedGrid: true);

            var tileCount = FullWorldConceptLayoutCatalog.ExpectedTerrainTileCount(request);
            if (CaveBuildSessionConfig.IsFloatingIslandsDemo(request))
                return ComputeFloatingIslands(tileCount, request);

            var extended = request.SurfaceScope == SurfaceBuildScope.FullWorld &&
                           request.UseExtendedOpenWorldGrid &&
                           tileCount > 81;
            var total = Compute(request.SurfaceScope, tileCount, extended);

            if (!request.UseTrue3DCaveSystem)
                total -= ComputeCavePacedSteps(fullValidate: true) - 90;

            if (CaveBuildSessionConfig.SkipTerrainHelperScripts(request))
                total -= 320;

            if (!request.SurfaceIncludeTrails)
                total -= 96;

            if (request.ForceNineTileSquareGrid && tileCount >= 9)
                total += 36;

            total += EstimateSurfaceTerrainSculptMicroSteps(tileCount, request);

            return Mathf.Max(480, total);
        }

        static int ComputeFloatingIslands(int tileCount, WorldGenerationRequest request)
        {
            tileCount = Mathf.Max(tileCount, SurfaceTerrainTileExpansion.FloatingIslandsTerrainTileCount);
            var passes = SurfaceTerrainCenteredAuthor.ResolvePassCount(request?.SurfaceTerrainBuildPasses ?? 4);
            var sculpt = EstimateSurfaceTerrainSculptMicroSteps(tileCount, passes);
            var total =
                ComputePreGridSteps() +
                ComputeCoreGridSteps(tileCount) +
                sculpt +
                ComputePostGridSteps(tileCount, includeTitan: false, lightweightSeams: true) +
                ComputeCavePacedSteps(fullValidate: true);

            if (!request.UseTrue3DCaveSystem)
                total -= ComputeCavePacedSteps(fullValidate: true) - 90;

            if (CaveBuildSessionConfig.SkipTerrainHelperScripts(request))
                total -= 280;

            return Mathf.Max(900, total);
        }

        /// <summary>Micro sculpt queue: load bands + 1 paced step per heightmap row × pass × tile.</summary>
        public static int EstimateSurfaceTerrainSculptMicroSteps(int tileCount, WorldGenerationRequest request)
        {
            var passes = SurfaceTerrainCenteredAuthor.ResolvePassCount(request?.SurfaceTerrainBuildPasses ?? 4);
            return EstimateSurfaceTerrainSculptMicroSteps(tileCount, passes);
        }

        public static int EstimateSurfaceTerrainSculptMicroSteps(int tileCount, int passCount)
        {
            tileCount = Mathf.Max(1, tileCount);
            passCount = Mathf.Max(1, passCount);
            const int res = DefaultHeightmapResolution;
            const int loadBand = 2;
            var loadSteps = (res + loadBand - 1) / loadBand;
            var sculptStepsPerPass = res;
            var perTile = loadSteps + passCount * sculptStepsPerPass + 3;
            return tileCount * perTile;
        }

        public static int ComputeExtendedFullWorld(int tileCount) =>
            ComputePreGridSteps() +
            ComputeExtendedGridSteps(tileCount) +
            ComputePostGridSteps(tileCount, includeTitan: true) +
            ComputeCavePacedSteps(fullValidate: true);

        static int ComputeCoreFullWorld(int tileCount)
        {
            var tiles = Mathf.Max(tileCount, 9);
            return ComputePreGridSteps() +
                   ComputeCoreGridSteps(tiles) +
                   ComputePostGridSteps(tiles, includeTitan: false) +
                   ComputeCavePacedSteps(fullValidate: true);
        }

        static int ComputeSurfaceOnly(int tileCount) =>
            ComputePreGridSteps() +
            ComputeCoreGridSteps(Mathf.Max(tileCount, 9)) +
            ComputePostGridSteps(tileCount, includeTitan: false) +
            900;

        static int ComputeCaveOnly() =>
            120 + ComputeCavePacedSteps(fullValidate: false);

        static int ComputePreGridSteps() =>
            28 +
            SurfaceWorldGenerator.SurfacePhaseCount * 4 +
            140;

        static int ComputeCoreGridSteps(int tileCount)
        {
            var place = IndexedBatchSteps(tileCount, CaveBuildMicroProcessQueue.WorkKind.SurfaceGridPlace);
            var snap = IndexedBatchSteps(tileCount, CaveBuildMicroProcessQueue.WorkKind.SurfaceGridSnap);
            var weld = IndexedBatchSteps(tileCount, CaveBuildMicroProcessQueue.WorkKind.SurfaceGridWeld);
            return 8 + place + snap + weld + tileCount + 18;
        }

        static int ComputeExtendedGridSteps(int tileCount)
        {
            var place = IndexedBatchSteps(tileCount, CaveBuildMicroProcessQueue.WorkKind.SurfaceGridPlace);
            var snap = IndexedBatchSteps(tileCount, CaveBuildMicroProcessQueue.WorkKind.SurfaceGridSnap);
            var weld = IndexedBatchSteps(tileCount, CaveBuildMicroProcessQueue.WorkKind.SurfaceGridWeld);
            var terraform = tileCount * (1 + EstimateTerraformQueueStepsPerTile(tileCount));
            return 8 + place + snap + weld + 36 + terraform;
        }

        static int ComputePostGridSteps(int tileCount, bool includeTitan, bool lightweightSeams = false)
        {
            var seam = lightweightSeams
                ? Mathf.Max(12, tileCount * 6)
                : IndexedBatchSteps(tileCount, CaveBuildMicroProcessQueue.WorkKind.SurfaceSeam);
            var snap = IndexedBatchSteps(tileCount, CaveBuildMicroProcessQueue.WorkKind.SurfaceGridSnap);
            var surfaceFinish =
                SurfaceTerrainAiPhases.PhaseCount * 14 +
                PropCategoryPassSteps(tileCount) +
                SurfaceTerrainBuildLadder.RungOrder.Length * 6 +
                420;

            var titan = includeTitan
                ? 8 + HollowTitanConceptCatalog.PhaseCount + HollowTitanStumpSculptPhases.PhaseCount
                : 0;

            var cc0 = 160;
            var finalize = snap + seam + 52;

            return finalize + surfaceFinish + titan + cc0;
        }

        static int PropCategoryPassSteps(int tileCount)
        {
            var categories = 4;
            var chunksPerCategory = Mathf.Max(8, tileCount / 6);
            return categories * chunksPerCategory;
        }

        static int ComputeCavePacedSteps(bool fullValidate)
        {
            const int validateSubSteps = 13;
            const int validateSubStepsCaveOnly = 4;
            var validate = fullValidate ? validateSubSteps : validateSubStepsCaveOnly;

            var macro = CaveBuildQueuedPipelineSchedule.Total;
            var geoSubSteps =
                CaveAdventureCaveGenerator.ShellSubStepCount +
                CaveAdventureCaveGenerator.LabyrinthSubStepCount +
                CaveAdventureCaveGenerator.GrandCavernSubStepCount +
                CaveAdventureCaveGenerator.FeaturesSubStepCount +
                CaveAdventureCaveGenerator.PropsWaterSubStepCount +
                CaveAdventureCaveGenerator.AddTerrainSubStepCount +
                24;

            var playability = CaveAdventurePlayabilityPipeline.StepCount * 2;
            var validation = CaveBuildAutomatedValidation.StepCount * 2;
            var research = CaveBuildQueuedPipelineSchedule.ResearchCount * 2;
            var finalizePolish = CaveBuildQueuedPipelineSchedule.FinalizePolishCount * 2;
            var postMeat = CaveBuildQueuedPipelineSchedule.PostMeatCount * 2;
            var groundPolish = CaveBuildQueuedPipelineSchedule.GroundPolishCount * 2;
            var world = CaveBuildQueuedPipelineSchedule.WorldCount * 2;

            return validate +
                   macro +
                   geoSubSteps +
                   playability +
                   validation +
                   research +
                   finalizePolish +
                   postMeat +
                   groundPolish +
                   world +
                   180;
        }

        static int EstimateTerraformQueueStepsPerTile(int tileCount)
        {
            var res = DefaultHeightmapResolution;
            var bandRows = TerraformBandRowsForBudget(res);
            var rowBands = (res + bandRows - 1) / bandRows;
            var heightmapOps = rowBands * 4 + 8;
            const int playTile = 6;
            const int centerMain = 2;
            var wilderness = 2 + heightmapOps + 14;
            const int playCount = 8;
            var wildernessCount = Mathf.Max(1, tileCount - playCount - 1);
            return (centerMain + playCount * playTile + wildernessCount * wilderness) /
                   Mathf.Max(1, playCount + wildernessCount);
        }

        /// <summary>Matches extended FullWorld terraform on 16 GB (see CaveBuildMicroTerrainHeightmap).</summary>
        static int TerraformBandRowsForBudget(int res) =>
            res >= 1025 ? 8 : res >= 513 ? 12 : res >= 257 ? 16 : 24;

        static int IndexedBatchSteps(int total, CaveBuildMicroProcessQueue.WorkKind kind)
        {
            if (total <= 0)
                return 0;

            var batch = Mathf.Max(1, CaveBuildMicroProcessQueue.ResolveBatchSize(kind));
            return (total + batch - 1) / batch;
        }
    }
}
#endif
