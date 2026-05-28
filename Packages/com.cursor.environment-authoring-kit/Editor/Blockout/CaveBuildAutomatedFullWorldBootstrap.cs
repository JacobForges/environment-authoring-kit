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
        /// <summary>Applied to the active build session request (startup + queued pipeline).</summary>
        public static bool SessionActive { get; private set; }

        public static int SessionDemSupersampleDim { get; private set; } = 128;

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

            CaveBuildReliableFullWorldPreset.Apply(savePrefs: true);
            EnsureAutomatedCursorInvokesEnabled();

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

            if (invalidateEntireLadder)
                CaveBuildPhaseContractRegistry.InvalidateAll();
            else if (!CaveBuildPhaseContractRegistry.HasPlayableCaveLayoutInScene() ||
                     !CaveBuildPhaseContractRegistry.IsRungComplete(
                         CaveBuildPhaseContractRegistry.RungCaveLayout,
                         layoutSeed))
            {
                CaveBuildPhaseContractRegistry.InvalidateCaveGeometryLadderRungs();
                if (!CaveBuildPhaseContractRegistry.HasPlayableCaveLayoutInScene())
                {
                    Debug.LogWarning(
                        "[CaveBuild] Scene has no full cave (blocks/shell/tube) — cave_layout ladder invalidated; " +
                        "queued geo will rebuild underground layout.");
                }
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

        /// <summary>Enable agent invokes when credentials exist; otherwise stay procedural-only.</summary>
        static void EnsureAutomatedCursorInvokesEnabled()
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();

            if (!CaveBuildCursorSettings.HasCredentialsForActiveProvider())
            {
                settings.suppressMeatLoopCursorInvokes = true;
                settings.autoInvokeEachMeatLoopPass = false;
                settings.autoInvokeTerrainAfterSurfaceBuild = false;
                settings.autoInvokePreBuildWorkflow = false;
                settings.preBuildReloopUntilPass = false;
                settings.invokeCursorOnResearchPhase = false;
                settings.SaveToPrefs();
                EditorUtility.SetDirty(settings);
                Debug.LogWarning(
                    "[CaveBuild] No credentials for active AI provider — FullWorld runs procedural steps only. " +
                    "Hub → Apply Offline (No API) preset, or set provider + keys / local Ollama.");
                return;
            }

            settings.suppressMeatLoopCursorInvokes = false;
            settings.autoInvokeEachMeatLoopPass = true;
            settings.autoInvokeTerrainAfterSurfaceBuild = true;
            settings.autoRebuildSurfaceAfterTerrainAgent = false;
            settings.invokeCursorOnResearchPhase = true;
            settings.SaveToPrefs();
            EditorUtility.SetDirty(settings);
        }
    }
}
#endif
