#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Blocks cave / pre-build continuation until open-sky surface work for this seed is finished.</summary>
    public static class CaveBuildSurfaceCompletionGate
    {
        static bool _surfaceBuildActive;
        static bool _surfacePipelineStarted;
        static bool _surfaceWorldGeneratorFinished;
        static bool _terrainGradingComplete;
        static bool _surfacePipelineFailed;
        static int _completedSeed = int.MinValue;
        static SurfaceBuildScope _completedScope;

        public static bool IsSurfaceBuildActive => _surfaceBuildActive;
        public static bool WasSurfacePipelineQueued => _surfacePipelineStarted;

        public static bool IsSurfaceWorldGeneratorFinished => _surfaceWorldGeneratorFinished;

        /// <summary>FullWorld: terrain ladder + meat loop finished for this build session.</summary>
        public static bool IsTerrainGradingComplete => _terrainGradingComplete;

        public static void ResetForNewBuildSession()
        {
            SurfaceNavMeshBaker.ClearSurfaceNavMeshData("new build session reset");
            _surfaceBuildActive = false;
            _surfacePipelineStarted = false;
            _surfaceWorldGeneratorFinished = false;
            _surfacePipelineFailed = false;
            _terrainGradingComplete = false;
            _completedSeed = int.MinValue;
            _completedScope = SurfaceBuildScope.CaveOnly;
            SurfaceTerrainCenteredAuthor.ResetQueuedPassesState();
        }

        public static void MarkSurfaceBuildStarted()
        {
            _surfaceBuildActive = true;
            _surfacePipelineStarted = true;
            _surfaceWorldGeneratorFinished = false;
            _surfacePipelineFailed = false;
            _terrainGradingComplete = false;
            _completedSeed = int.MinValue;
            SurfaceTerrainCenteredAuthor.ResetQueuedPassesState();
            // Do not reset Florida DEM / deferral here — that re-stamps LiDAR and clears handoff flags
            // while the cave pipeline is already waiting at meat-loop (see ResetForNewBuildSession at build start).
            SurfaceTerrainPropPlacementRegion.ResetLock();
            SurfaceTerrainBuildLadder.ClearCachedGradedReport();
            CaveBuildFullWorldSurfaceDeferral.NotifySurfacePipelineQueued();
            LogState("surface build started");
        }

        /// <summary>12-phase surface world done (trails, NavMesh, manifest) — terrain AI phases may still be queued.</summary>
        public static void MarkSurfaceWorldGeneratorFinished(WorldGenerationRequest request, bool success)
        {
            _surfaceWorldGeneratorFinished = success && request != null;
            SurfaceTerrainCenteredAuthor.ResetQueuedPassesState();
            LogState(success
                ? "surface world generator finished (OK)"
                : "surface world generator finished (failed)");
        }

        public static void MarkSurfaceBuildFinished(WorldGenerationRequest request, bool success)
        {
            _surfaceBuildActive = false;
            SurfaceTerrainCenteredAuthor.ResetQueuedPassesState();

            if (!success || request == null)
            {
                _surfaceWorldGeneratorFinished = false;
                _terrainGradingComplete = false;
                LogState("surface build finished (failed or cancelled)");
                return;
            }

            if (request.SurfaceScope is SurfaceBuildScope.FullWorld or SurfaceBuildScope.SurfaceOnly)
            {
                _completedSeed = request.Seed;
                _completedScope = request.SurfaceScope;
                _surfaceWorldGeneratorFinished = true;
            }

            LogState("surface world + terrain phases finished (cave meat unlocks after terrain grading complete)");
        }

        /// <summary>Marks terrain ladder + meat loop + surface validation done for FullWorld cave grading.</summary>
        public static void MarkTerrainGradingComplete(WorldGenerationRequest request)
        {
            if (request == null)
                return;

            _terrainGradingComplete = true;
            if (request.SurfaceScope is SurfaceBuildScope.FullWorld or SurfaceBuildScope.SurfaceOnly)
            {
                _completedSeed = request.Seed;
                _completedScope = request.SurfaceScope;
            }

            LogState("terrain grading complete (cave meat loop unlocked)");
        }

        public static bool SurfacePipelineFailed => _surfacePipelineFailed;

        /// <summary>FullWorld: cave meat loop waits until above-ground terrain pipeline finished.</summary>
        public static bool IsReadyForCaveMeatLoop(WorldGenerationRequest request, SceneGroundInfo ground)
        {
            if (request == null)
                return true;

            if (request.SurfaceScope != SurfaceBuildScope.FullWorld)
                return true;

            if (SurfaceTerrainAiPhases.IsPipelineActive || _surfaceBuildActive)
                return false;

            if (SurfaceTerrainCenteredAuthor.IsQueuedPassesActive)
                return false;

            return _terrainGradingComplete &&
                   IsCompleteForSeed(request) &&
                   HasUsableSurfaceInScene(ground);
        }

        /// <summary>Human-readable blockers for orchestrator logs (FullWorld meat gate).</summary>
        public static string DescribeCaveMeatLoopBlockers(
            WorldGenerationRequest request,
            SceneGroundInfo ground)
        {
            if (request == null || request.SurfaceScope != SurfaceBuildScope.FullWorld)
                return "ready (not FullWorld)";

            if (IsReadyForCaveMeatLoop(request, ground))
                return "ready";

            var parts = new System.Collections.Generic.List<string>();
            if (_surfacePipelineFailed)
                parts.Add("surface pipeline failed");
            if (_surfaceBuildActive)
                parts.Add("surfaceBuildActive");
            if (SurfaceTerrainAiPhases.IsPipelineActive)
                parts.Add("terrainAiPhasesActive");
            if (SurfaceTerrainCenteredAuthor.IsQueuedPassesActive)
                parts.Add("queuedSculptPasses");
            if (!_surfacePipelineStarted && !_surfaceBuildActive)
                parts.Add("surface/terrain pipeline never started (run Build Complete Cave)");
            else if (_surfaceBuildActive || _surfacePipelineStarted)
                parts.Add("Florida LiDAR + terrain pipeline running");
            if (!IsCompleteForSeed(request))
            {
                if (_surfaceBuildActive || SurfaceTerrainCenteredAuthor.IsQueuedPassesActive ||
                    SurfaceTerrainAiPhases.IsPipelineActive)
                    parts.Add("terrain pipeline in progress (seed gate opens when surface finishes)");
                else
                    parts.Add($"seed not complete (gate={_completedSeed}, want={request.Seed})");
            }
            else if (!_terrainGradingComplete)
                parts.Add("terrain grading not marked complete");
            if (!HasUsableSurfaceInScene(ground))
                parts.Add("no usable surface terrain in scene");

            return parts.Count > 0 ? string.Join(", ", parts) : "waiting (unknown)";
        }

        /// <summary>Failure in queued surface steps — always clears active so cave/pre-build cannot deadlock.</summary>
        public static void MarkSurfacePipelineFailed(WorldGenerationRequest request)
        {
            _surfaceBuildActive = false;
            _surfaceWorldGeneratorFinished = false;
            _terrainGradingComplete = false;
            _surfacePipelineFailed = true;
            SurfaceTerrainCenteredAuthor.ResetQueuedPassesState();
            if (request != null &&
                request.SurfaceScope is SurfaceBuildScope.FullWorld or SurfaceBuildScope.SurfaceOnly)
            {
                _completedSeed = int.MinValue;
            }

            LogState("surface pipeline failed (flags cleared)");
        }

        /// <summary>
        /// When the surface world report succeeded but the gate was not updated (exception path), sync flags before pre-build/cave.
        /// </summary>
        public static void EnsureHandoffAfterSuccessfulSurfaceWorld(
            WorldGenerationRequest request,
            SceneGroundInfo ground,
            bool surfaceWorldSuccess)
        {
            if (request == null || !surfaceWorldSuccess)
                return;

            if (!_surfaceWorldGeneratorFinished)
                MarkSurfaceWorldGeneratorFinished(request, true);

            if (request.SurfaceScope is SurfaceBuildScope.FullWorld or SurfaceBuildScope.SurfaceOnly &&
                !IsCompleteForSeed(request) &&
                HasUsableSurfaceInScene(ground))
            {
                _completedSeed = request.Seed;
                _completedScope = request.SurfaceScope;
                _surfaceBuildActive = false;
                LogState("handoff flags synced from successful surface world");
            }
        }

        public static bool IsCompleteForSeed(WorldGenerationRequest request) =>
            request != null &&
            _completedSeed == request.Seed &&
            _completedScope == request.SurfaceScope;

        /// <summary>FullWorld: surface + terrain grading must finish before cave geometry (Ground anchor up, cave last).</summary>
        public static bool MustFinishSurfaceBeforeCave(WorldGenerationRequest request) =>
            request != null && request.SurfaceScope == SurfaceBuildScope.FullWorld;

        /// <summary>True when cave geometry or pre-build Cursor workflow may proceed.</summary>
        public static bool IsReadyForCave(WorldGenerationRequest request, SceneGroundInfo ground)
        {
            if (request == null)
                return true;

            if (!MustFinishSurfaceBeforeCave(request))
            {
                if (request.SurfaceScope == SurfaceBuildScope.CaveOnly)
                    return HasUsableSurfaceInScene(ground);
                return true;
            }

            if (SurfaceTerrainCenteredAuthor.IsQueuedPassesActive)
                return false;

            if (SurfaceTerrainAiPhases.IsPipelineActive)
                return false;

            switch (request.SurfaceScope)
            {
                case SurfaceBuildScope.CaveOnly:
                    return HasUsableSurfaceInScene(ground);
                case SurfaceBuildScope.SurfaceOnly:
                    return IsCompleteForSeed(request) || _surfaceWorldGeneratorFinished;
                case SurfaceBuildScope.FullWorld:
                    if (IsCompleteForSeed(request))
                        return true;
                    return !_surfaceBuildActive && HasUsableSurfaceInScene(ground);
                default:
                    return true;
            }
        }

        /// <summary>Used by pre-build deferral and pending cave continuation.</summary>
        public static bool CanStartCaveGeometryNow(WorldGenerationRequest request, SceneGroundInfo ground)
        {
            if (request == null)
                return true;

            if (!MustFinishSurfaceBeforeCave(request))
                return true;

            if (request.SurfaceScope == SurfaceBuildScope.FullWorld)
                return IsReadyForCaveMeatLoop(request, ground);

            return IsReadyForCave(request, ground);
        }

        /// <summary>
        /// Clears stale sculpt/active flags when surface work succeeded but cave startup is blocked (common after editor freeze).
        /// </summary>
        public static void ReleaseStuckHandoffForStartup(
            WorldGenerationRequest request,
            SceneGroundInfo ground,
            bool surfaceWorldSuccess)
        {
            if (request == null || !surfaceWorldSuccess)
                return;

            if (SurfaceTerrainCenteredAuthor.IsQueuedPassesActive)
            {
                SurfaceTerrainCenteredAuthor.ResetQueuedPassesState();
                LogState("cleared stale queued sculpt passes for cave handoff");
            }

            if (SurfaceTerrainAiPhases.IsPipelineActive)
            {
                LogState(
                    "terrain AI phases still marked active after surface success — cave will wait until they finish");
            }

            if (_surfaceBuildActive)
                MarkSurfaceBuildFinished(request, true);

            EnsureHandoffAfterSuccessfulSurfaceWorld(request, ground, true);
        }

        public static void LogHandoffBlocker(WorldGenerationRequest request, SceneGroundInfo ground, string context)
        {
            var reasons = new System.Text.StringBuilder();
            if (_surfaceBuildActive)
                reasons.Append(" surfaceBuildActive");
            if (_surfaceWorldGeneratorFinished)
                reasons.Append(" worldGenDone");
            if (SurfaceTerrainCenteredAuthor.IsQueuedPassesActive)
                reasons.Append(" queuedSculptPasses");
            if (SurfaceTerrainAiPhases.IsPipelineActive)
                reasons.Append(" terrainAiPhasesActive");
            if (request != null && IsCompleteForSeed(request))
                reasons.Append(" completeForSeed");
            if (ground != null && HasUsableSurfaceInScene(ground))
                reasons.Append(" usableTerrain");
            if (reasons.Length == 0)
                reasons.Append(" (no flags set)");

            CaveBuildEditorLog.LogCave(
                $"[Handoff] {context} — CanStartCave={CanStartCaveGeometryNow(request, ground)}{reasons}",
                forceUnityConsole: true);
        }

        static void LogState(string detail)
        {
            CaveBuildPipelineLog.Info(
                $"Surface gate: {detail} (active={_surfaceBuildActive}, worldDone={_surfaceWorldGeneratorFinished}, seed={_completedSeed})",
                "Surface-Gate");
        }

        static bool HasUsableSurfaceInScene(SceneGroundInfo ground)
        {
            if (ground == null || !ground.HasAnchor)
                return false;

            var envRoot = EnvironmentSceneUtility.GetOrCreateRoot(ground);
            if (envRoot == null)
                return false;

            var surfaceRoot = envRoot.transform.Find(SurfaceWorldPaths.RootName);
            if (surfaceRoot == null)
                return false;

            var budget = EnvironmentKitHardwareBudget.Active;
            var terrain = EnvironmentSceneUtility.FindTerrainInActiveScene(
                envRoot.transform,
                ground,
                allowCreate: false,
                budget.TerrainMaxSizeMeters,
                budget.TerrainMaxHeightMeters);
            return terrain != null && terrain.terrainData != null;
        }
    }
}
#endif
