#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Keeps pipeline flags, Hub phase labels, and step-counter segment budgets in sync.
    /// </summary>
    public static class CaveBuildPipelinePhaseTracker
    {
        public static string CurrentPhaseId { get; private set; } = "idle";

        public static string CurrentPhaseLabel { get; private set; } = string.Empty;

        public static int PipelineMacroStep { get; private set; } = -1;

        public static int PipelineMacroTotal { get; private set; } = CaveBuildQueuedPipelineSchedule.Total;

        public static void RefreshProvisionalExtended(WorldGenerationRequest request)
        {
            if (request == null || request.SurfaceScope != SurfaceBuildScope.FullWorld)
            {
                CaveBuildAaaSessionPolicy.MarkProvisionalFullWorldExtended(false);
                return;
            }

            CaveBuildAaaSessionPolicy.MarkProvisionalFullWorldExtended(request.UseExtendedOpenWorldGrid);
        }

        public static void OnBuildSessionStart(SurfaceBuildScope scope, WorldGenerationRequest request = null)
        {
            if (scope == SurfaceBuildScope.FullWorld && request != null)
                CaveBuildConceptSession.LockForBuild(request.ConceptLayoutIndex, request.Seed);

            var conceptIndex = request?.ConceptLayoutIndex >= 0
                ? request.ConceptLayoutIndex
                : CaveBuildConceptSession.ResolveLockedConceptIndex();
            var estimate = scope == SurfaceBuildScope.FullWorld
                ? FullWorldConceptLayoutCatalog.CreateHubBoundRequest(request?.Seed ?? 0, conceptIndex)
                : null;
            var provisionalExtended = scope == SurfaceBuildScope.FullWorld &&
                                      (estimate?.UseExtendedOpenWorldGrid ?? false);
            CaveBuildAaaSessionPolicy.MarkProvisionalFullWorldExtended(provisionalExtended);
            CaveBuildMicroProcessQueue.ApplyForBuildScope(scope);
            if (request != null)
                CaveBuildStepCounter.ConfigureForRequest(request);
            else if (estimate != null)
                CaveBuildStepCounter.ConfigureForRequest(estimate);
            else
                CaveBuildStepCounter.ConfigureForBuild(scope, extendedOpenWorldGrid: false);

            SetPhase("startup", "Build session queued");
        }

        public static void OnStartupDetail(string detail) => SetPhase("startup", detail);

        public static void OnSurfaceWorldStarted() =>
            SetPhase("surface_world", "Surface world (12 phases + finish chain)");

        public static void OnFlatGridPipelineStarted(int tilePlanCount)
        {
            if (tilePlanCount > 81)
                CaveBuildStepCounter.ConfigureForExtendedTilePlan(tilePlanCount);

            var label = CaveBuildSessionConfig.IsPlayDiskOnlyDemo()
                ? $"Play disk only — 3×3 ({tilePlanCount} terrains)"
                : tilePlanCount <= SurfaceTerrainTileExpansion.FloatingIslandsTerrainTileCount &&
                  CaveBuildSessionConfig.IsFloatingIslandsDemo()
                    ? $"Floating islands demo — 3×3 play disk + 4 cardinal tiles ({tilePlanCount} terrains)"
                    : CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid
                        ? $"Extended flat grid + terraform (~{tilePlanCount} tiles, Chebyshev 8)"
                        : $"FullWorld core grid + terraform ({tilePlanCount} tiles)";
            CaveBuildStepCounter.SetSegment(
                CaveBuildStepCounter.BuildSegment.FlatGrid,
                tilePlanCount * 2 + 320,
                0f,
                label);
            SetPhase("flat_grid", label);
        }

        public static void OnFlatGridPipelineFinished(bool success)
        {
            CaveBuildStepCounter.SetSegmentProgress(1f);
            if (success)
                PipelineContentPreservePolicy.MarkMidPipelineIntegrateMode(
                    "flat grid terraform complete — surface finish + cave merge into scene");
            SetPhase(
                success ? "surface_finish" : "surface_finish",
                success
                    ? "Post-grid surface finish (trails · mouth · NavMesh)"
                    : "Flat grid finished with issues");
        }

        public static void OnTerrainAiStarted() =>
            SetPhase("terrain_ai", "Terrain AI phases + grading ladder");

        public static void OnLateBuildBandEntered() =>
            SetPhase("late_build", "Titan · seams · props · cave macro (~20k–69k steps)");

        public static void OnTerrainGradingComplete()
        {
            CaveBuildStepCounter.SetSegment(
                CaveBuildStepCounter.BuildSegment.CaveMacro,
                CaveBuildQueuedPipelineSchedule.Total * 42,
                0f,
                "Cave macro pipeline");
            SetPhase("cave_unlocked", "Terrain grading done — cave macro queue unlocked");
        }

        public static void OnPreBuildGate(string detail) => SetPhase("pre_build", detail);

        public static void OnCaveMacroStep(int step, int total, string label)
        {
            PipelineMacroStep = step;
            PipelineMacroTotal = total;
            if (step >= CaveBuildQueuedPipelineSchedule.WorldFirst)
            {
                PipelineContentPreservePolicy.MarkMidPipelineIntegrateMode(
                    $"cave macro step {step}/{total} ({label})");
            }

            var frac = total > 0 ? step / (float)total : 0f;
            CaveBuildStepCounter.SetSegment(
                CaveBuildStepCounter.BuildSegment.CaveMacro,
                total * 40,
                frac,
                label);
        }

        public static void OnPostPolish(string detail) =>
            SetPhase("post_polish", detail);

        public static void OnSessionEnded()
        {
            CurrentPhaseId = "idle";
            CurrentPhaseLabel = string.Empty;
            PipelineMacroStep = -1;
        }

        static void SetPhase(string phaseId, string label)
        {
            CurrentPhaseId = phaseId ?? "idle";
            CurrentPhaseLabel = label ?? string.Empty;
            CaveBuildRunStatusPublisher.SetPhase(phaseId, label);
        }
    }
}
#endif
