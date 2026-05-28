#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Runs automatically when you use Build Complete Cave (FullWorld) — not a separate operator workflow.
    /// </summary>
    public static class CaveBuildAutomatedFullWorldBootstrap
    {
        const string PrefFullWorldCompletedOnce = "CaveBuild_FullWorldCompletedOnce";

        /// <summary>Applied to the active build session request (startup + queued pipeline).</summary>
        public static bool SessionActive { get; private set; }

        public static int SessionDemSupersampleDim { get; private set; } = 128;

        /// <summary>First-time / empty-scene builds get AAA-style invalidate without a separate button.</summary>
        public static bool ShouldAutoInvalidateEntireLadder(SceneGroundInfo ground)
        {
            if (!EditorPrefs.GetBool(PrefFullWorldCompletedOnce, false))
                return true;
            if (!CaveBuildPhaseContractRegistry.HasPlayableCaveLayoutInScene())
                return true;
            return false;
        }

        public static void MarkFullWorldCompletedOnce()
        {
            if (EditorPrefs.GetBool(PrefFullWorldCompletedOnce, false))
                return;
            EditorPrefs.SetBool(PrefFullWorldCompletedOnce, true);
            CaveBuildEditorLog.LogCave(
                "First FullWorld completed — later builds may reuse incremental ladder steps unless you use Full AAA Rebuild.",
                forceUnityConsole: true);
        }

        /// <summary>
        /// FullWorld automated prep: preset, preflight, ladder hygiene, enhancement session flags.
        /// Returns false only on hard blockers (missing ground, catalog, node, build already running).
        /// </summary>
        public static bool Prepare(
            SceneGroundInfo ground,
            int layoutSeed,
            bool invalidateEntireLadder,
            out string blockMessage)
        {
            blockMessage = null;
            SessionActive = false;
            CaveBuildPreBuildReloop.ResetSession();

            CaveBuildProjectSetup.EnsureCloneReady(ref ground, out var setupLog);
            if (!string.IsNullOrEmpty(setupLog))
                Debug.Log("[CaveBuild] " + setupLog.Replace("\n", "\n[CaveBuild] "));

            CaveBuildSessionPreset.ApplyAutomaticForFullWorld();

            var request = new WorldGenerationRequest
            {
                Seed = layoutSeed,
                SurfaceScope = SurfaceBuildScope.FullWorld,
                RunEnhancementPhases = true,
                DemSupersampleTargetDim = CaveBuildCursorSettings.LoadOrCreate().demSupersampleTargetDim,
            };

            var preflight = CaveBuildFullRunPreflight.Run(ground, request, writeMarkdown: true);
            if (!preflight.CanStartFullWorld)
            {
                blockMessage =
                    $"Automated FullWorld blocked ({preflight.blockCount} issue(s)). " +
                    $"See Assets/EnvironmentKit/Generated/CaveBuildPreflightReport.md";
                CaveBuildCompletionSummary.ShowBlocked(blockMessage, CaveBuildFullRunPreflight.ReportRel);
                return false;
            }

            var fullInvalidate = invalidateEntireLadder || ShouldAutoInvalidateEntireLadder(ground);
            if (fullInvalidate)
            {
                CaveBuildPhaseContractRegistry.InvalidateAll();
                if (!invalidateEntireLadder)
                {
                    CaveBuildEditorLog.LogCave(
                        "[CaveBuild] First run or empty scene — cleared incremental cache automatically " +
                        "(same as Full AAA Rebuild; use Build Complete Cave only).",
                        forceUnityConsole: true);
                }
            }
            else if (!CaveBuildPhaseContractRegistry.IsRungComplete(
                         CaveBuildPhaseContractRegistry.RungCaveLayout,
                         layoutSeed))
            {
                CaveBuildPhaseContractRegistry.InvalidateCaveGeometryLadderRungs();
            }

            SessionActive = true;
            SessionDemSupersampleDim = request.DemSupersampleTargetDim;
            SurfaceDemGeoreferenceAuthor.SetSupersampleTargetDim(SessionDemSupersampleDim);
            CaveBuildEnhancementRunner.ExportCatalogJson();

            CaveBuildRunStatusPublisher.SetPhase(
                "automated_full_world",
                "Build Complete Cave — preset + preflight OK — queued pipeline starting");
            CaveBuildEditorLog.LogCave(
                $"Automated FullWorld ready — seed {layoutSeed}, " +
                $"{CaveBuildUnifiedFlow.QueuedPipelineStepCount} queued steps, " +
                $"enhancements ON, DEM×{SessionDemSupersampleDim}, " +
                $"preflight warns={preflight.warnCount}. " +
                "Watch Cave Build → Diagnostics → Pipeline Console until Build 120/120.",
                forceUnityConsole: true);
            if (preflight.warnCount > 0)
            {
                Debug.LogWarning(
                    $"[CaveBuild] Preflight passed with {preflight.warnCount} warning(s) — details in CaveBuildPreflightReport.md");
            }

            return true;
        }

        public static void ApplyToRequest(WorldGenerationRequest request)
        {
            if (request == null || !SessionActive)
                return;

            request.SurfaceScope = SurfaceBuildScope.FullWorld;
            request.RunEnhancementPhases = true;
            if (request.DemSupersampleTargetDim <= 0)
                request.DemSupersampleTargetDim = SessionDemSupersampleDim;
            SurfaceDemGeoreferenceAuthor.SetSupersampleTargetDim(request.DemSupersampleTargetDim);
        }

        public static void ClearSession() => SessionActive = false;
    }
}
#endif
