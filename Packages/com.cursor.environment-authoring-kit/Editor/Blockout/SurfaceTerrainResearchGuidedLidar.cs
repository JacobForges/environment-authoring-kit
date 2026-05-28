#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Grade → research plan → terrain ladder prompt → execute LiDAR stamp (same rhythm as cave meat loop).
    /// </summary>
    static class SurfaceTerrainResearchGuidedLidar
    {
        const string PhaseId = "terrain_phase_dem";
        const int QueuedStep = 41;

        public static void QueueDemStamp(
            SurfaceTerrainAiPhases.TerrainDemStampContext context,
            Action<string> onStampComplete)
        {
            if (context.Ground?.Terrain == null)
            {
                onStampComplete?.Invoke("Terrain phases aborted — terrain missing.");
                return;
            }

            CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunGradeAndPlan(context, onStampComplete));
        }

        static void RunGradeAndPlan(
            SurfaceTerrainAiPhases.TerrainDemStampContext context,
            Action<string> onStampComplete)
        {
            var request = context.Request;
            var seed = request?.Seed ?? 0;
            var gateMsg = string.Empty;

            if (request != null && request.SurfaceScope != SurfaceBuildScope.FullWorld)
            {
                CaveBuildPhaseResearchGate.EnsureBeforeQueuedStep(QueuedStep, request, out gateMsg);
            }

            ExportResearchArtifacts(context, seed, gateMsg);

            var preCells = SurfaceTerrainHeightSmoothing.DeCheckerboardOnTerrain(
                context.Ground.Terrain,
                context.Center,
                context.Extent,
                strength: 0.28f);

            CaveBuildEditorLog.LogSurface(
                $"[Surface] LiDAR prep — de-checkerboard {preCells} cells; research plan exported (grade→plan→execute).",
                forceUnityConsole: true);

            SurfaceDemGeoreferenceAuthor.QueueApplyGeoreferencedStamp(
                context.Ground.Terrain,
                context.Center,
                context.Extent,
                seed,
                phaseMsg => OnStampComplete(context, phaseMsg, onStampComplete));
        }

        static void ExportResearchArtifacts(
            SurfaceTerrainAiPhases.TerrainDemStampContext context,
            int seed,
            string gateMsg)
        {
            CaveBuildUnifiedPromptBridge.RefreshForPhase(
                PhaseId,
                "terrain_integration",
                1,
                QueuedStep,
                seed,
                out var promptMsg);
            if (!string.IsNullOrEmpty(promptMsg))
                CaveBuildEditorLog.LogSurface($"[Surface] {promptMsg}", forceUnityConsole: true);

            var activeRung = "heightfield_no_craters";
            if (SurfaceTerrainBuildLadder.TryTakeCachedGradedReport(seed, out var cachedReport))
            {
                activeRung = SurfaceTerrainBuildLadder.PickActiveRung(cachedReport) ?? activeRung;
                SurfaceTerrainBuildLadder.ExportRungPrompt(activeRung, cachedReport, seed, exportTsxPrompts: true);
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] DEM plan — cached ladder overall {cachedReport.OverallScore}, rung `{activeRung}`. Gate: {gateMsg}",
                    forceUnityConsole: true);
            }
            else
            {
                SurfaceTerrainBuildLadder.ExportRungPrompt(activeRung, null, seed, exportTsxPrompts: true);
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] DEM plan — prompts exported (full terrain ladder runs after surface phases). Gate: {gateMsg}",
                    forceUnityConsole: true);
            }
        }

        static void OnStampComplete(
            SurfaceTerrainAiPhases.TerrainDemStampContext context,
            string phaseMsg,
            Action<string> onStampComplete)
        {
            if (context.Ground?.Terrain != null)
            {
                var main = context.Ground.Terrain;
                var unifiedExtent = SurfaceTerrainPlayRegion.ResolveUnifiedSurfaceExtent(
                    main,
                    context.Center,
                    context.Extent);
                var multiTile = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(main).Count > 1;
                var postCells = 0;

                SurfaceTerrainPlayRegion.ForEachSurfaceTerrainUnified(
                    main,
                    context.Center,
                    (terrain, playCenter) =>
                    {
                        postCells += SurfaceTerrainHeightSmoothing.DeCheckerboardOnTerrain(
                            terrain,
                            playCenter,
                            unifiedExtent,
                            strength: 0.32f);
                        SurfaceTerrainRefinement.SmoothTerrainFootprintUniform(terrain, 0.08f);
                        terrain.Flush();
                    });

                if (!multiTile)
                {
                    SurfaceTerrainRefinement.SmoothGraderSampleBandPublic(
                        main,
                        context.Center,
                        unifiedExtent,
                        strength: 0.12f);
                }

                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Post-LiDAR polish — de-checkerboard {postCells} cells (all terrains, no per-tile circles). {phaseMsg}",
                    forceUnityConsole: true);

                var request = context.Request;
                var creativePasses = SurfaceTerrainCenteredAuthor.ResolvePassCountAfterFloridaDem(
                    request?.SurfaceTerrainBuildPasses ?? SurfaceTerrainCenteredAuthor.DefaultPassCount,
                    demStamped: true);
                if (creativePasses > 0 && request != null)
                {
                    CaveBuildEditorLog.LogSurface(
                        $"[Surface] Queueing {creativePasses} creative sculpt pass(es) after LiDAR guide…",
                        forceUnityConsole: true);
                    var preserve = context.Extent * 0.12f;
                    SurfaceTerrainCenteredAuthor.QueueCenteredPasses(
                        main,
                        context.Center,
                        context.Extent,
                        request.Seed,
                        request.SurfaceIncludeMountains,
                        request.SurfaceIncludeWater,
                        request.SurfaceIncludeRoads,
                        preserve,
                        creativePasses,
                        refinementAfterAuthoritativeDem: true,
                        onComplete: () => onStampComplete?.Invoke(phaseMsg));
                    return;
                }
            }

            onStampComplete?.Invoke(phaseMsg);
        }
    }
}
#endif
