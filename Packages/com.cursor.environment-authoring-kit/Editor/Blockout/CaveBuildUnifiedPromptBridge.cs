#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Single entry: sync research briefs + regenerate AI prompts from every Generated JSON on disk.
    /// Call before any terrain phase, cave queued step, or surface build — not hand-written one-off MD.
    /// </summary>
    public static class CaveBuildUnifiedPromptBridge
    {
        public const string ManifestRel =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildGeneratedJsonManifest.json";

        public const string UnifiedPromptRel =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildUnifiedAgentPrompt.md";

        public const string ResearchAgentPromptRel =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildResearchAgentPrompt.md";

        enum AsyncRefreshStep
        {
            Unified = 0,
            ResearchAgent,
            ActionPlan,
            PhasePrompt,
            NextSteps,
        }

        const int AsyncRefreshStepCount = 5;

        static bool _asyncRefreshInFlight;
        static string _lastDebouncedWarnKey = string.Empty;

        public static bool ExportResearchAgentPrompt(string phaseId, string rung, out string message) =>
            CaveBuildPhasePromptBridge.ExportResearchAgentPrompt(phaseId, rung, out message);

        public static bool RefreshForPhase(
            string phaseId,
            string rung,
            int meatPass,
            int queuedStep,
            int seed,
            out string message) =>
            RefreshForPhase(phaseId, rung, meatPass, queuedStep, seed, out message, out _);

        /// <summary>
        /// Refreshes terrain + cave execution briefs, JSON manifest, unified prompt, phase prompt, and action plan.
        /// During active build: prefers on-disk cache; otherwise kicks off non-blocking tsx (see awaitingPromptTsx).
        /// </summary>
        public static bool RefreshForPhase(
            string phaseId,
            string rung,
            int meatPass,
            int queuedStep,
            int seed,
            out string message,
            out bool awaitingPromptTsx)
        {
            message = string.Empty;
            awaitingPromptTsx = false;
            if (string.IsNullOrWhiteSpace(phaseId))
                phaseId = "research";

            var exportRung = string.IsNullOrWhiteSpace(rung) ? PhaseRungForExport(phaseId) : rung;
            var warnKey = $"{phaseId}:{queuedStep}:{meatPass}";

            CaveBuildResearchCacheBridge.SyncTerrainResearchExecutionBrief(phaseId, meatPass, out var terrainBrief);
            CaveBuildResearchCacheBridge.SyncCaveResearchExecutionBrief(phaseId, meatPass, out var caveBrief);

            if (CaveBuildPhasePromptBridge.RequiresNonBlockingTsx)
            {
                if (CaveBuildPromptExportSession.HasValidateResearchPromptArtifactsOnDisk())
                {
                    message =
                        $"Prompts on disk for `{phaseId}` — skipped blocking tsx during active build. " +
                        $"terrain brief: {terrainBrief} | cave brief: {caveBrief}";
                    CaveBuildEditorLog.LogSurface("[Surface] " + message, forceUnityConsole: true);
                    CaveBuildPromptExportSession.MarkFresh(seed);
                    return true;
                }

                if (_asyncRefreshInFlight)
                {
                    awaitingPromptTsx = true;
                    message = $"Prompt tsx in flight for `{phaseId}` — gate will resume when exports finish.";
                    return true;
                }

                if (TryBeginAsyncRefresh(phaseId, exportRung, meatPass, queuedStep, seed, warnKey, out message))
                {
                    awaitingPromptTsx = queuedStep >= 0;
                    return true;
                }

                if (CaveBuildPromptExportSession.HasValidateResearchPromptArtifactsOnDisk())
                {
                    message =
                        $"Prompts on disk for `{phaseId}` (per-script skip). " +
                        $"terrain brief: {terrainBrief} | cave brief: {caveBrief}";
                    CaveBuildPromptExportSession.MarkFresh(seed);
                    return true;
                }

                LogDebouncedWarning(
                    warnKey,
                    "[CaveBuild] Prompt refresh could not start async tsx: " + message);
                message =
                    $"Prompt export deferred (async unavailable): {message}. " +
                    $"terrain brief: {terrainBrief} | cave brief: {caveBrief}";
                return true;
            }

            var unifiedOk = CaveBuildPhasePromptBridge.ExportUnifiedAgentContext(phaseId, out var unifiedMsg);
            if (!unifiedOk)
                LogDebouncedWarning(warnKey + ":unified", "[CaveBuild] Unified JSON prompt export: " + unifiedMsg);

            var researchAgentOk = ExportResearchAgentPrompt(phaseId, exportRung, out var researchAgentMsg);
            if (!researchAgentOk)
                LogDebouncedWarning(
                    warnKey + ":research-agent",
                    "[CaveBuild] Research agent prompt: " + researchAgentMsg);

            var planOk = CaveBuildPhasePromptBridge.ExportResearchActionPlan(
                phaseId,
                queuedStep,
                seed,
                out var planMsg);
            if (!planOk)
                LogDebouncedWarning(warnKey + ":plan", "[CaveBuild] Research action plan: " + planMsg);

            var phaseOk = CaveBuildPhasePromptBridge.ExportPhasePrompt(
                phaseId,
                exportRung,
                meatPass,
                0,
                out var phaseMsg);
            if (!phaseOk)
                LogDebouncedWarning(warnKey + ":phase", "[CaveBuild] Phase prompt: " + phaseMsg);

            CaveBuildPhasePromptBridge.ExportNextStepsAndDoNot(phaseId, out _);

            message =
                $"Prompts refreshed for `{phaseId}` — manifest + unified MD + research agent prompt + phase prompt + action plan. " +
                $"terrain brief: {terrainBrief} | cave brief: {caveBrief}";
            CaveBuildEditorLog.LogSurface("[Surface] " + message, forceUnityConsole: true);
            CaveBuildPromptExportSession.MarkFresh(seed);
            return unifiedOk && researchAgentOk && planOk && phaseOk;
        }

        /// <summary>Resume queued pipeline after non-blocking gate prompt tsx.</summary>
        internal static void CompleteGatePromptRefresh(bool ok, string msg, int resumeQueuedStep)
        {
            _asyncRefreshInFlight = false;
            if (resumeQueuedStep >= 0)
                LavaTubeCaveBuildPipeline.CompleteQueuedResearchPromptTsx(ok, msg, resumeQueuedStep);
            else if (!ok)
                LogDebouncedWarning("meat-prompt-tsx", "[CaveBuild] Meat-loop prompt tsx: " + msg);
            else if (!string.IsNullOrEmpty(msg))
                CaveBuildEditorLog.LogCave(msg, forceUnityConsole: true);
        }

        static bool TryBeginAsyncRefresh(
            string phaseId,
            string rung,
            int meatPass,
            int queuedStep,
            int seed,
            string warnKey,
            out string message)
        {
            message = string.Empty;
            for (var i = 0; i < AsyncRefreshStepCount; i++)
            {
                var step = (AsyncRefreshStep)i;
                if (ShouldSkipAsyncStep(step))
                    continue;

                if (!TryBeginAsyncStep(
                        step,
                        phaseId,
                        rung,
                        meatPass,
                        queuedStep,
                        seed,
                        warnKey,
                        out message))
                    continue;

                _asyncRefreshInFlight = true;
                return true;
            }

            message = "All prompt outputs already on disk.";
            CaveBuildPromptExportSession.MarkFresh(seed);
            return false;
        }

        static bool ShouldSkipAsyncStep(AsyncRefreshStep step)
        {
            switch (step)
            {
                case AsyncRefreshStep.Unified:
                    return CaveBuildPromptExportSession.ShouldSkipPromptTsxDuringValidate(
                        "generate-unified-agent-prompt.ts");
                case AsyncRefreshStep.ResearchAgent:
                    return CaveBuildPromptExportSession.ShouldSkipPromptTsxDuringValidate(
                        "generate-research-agent-prompt.ts");
                case AsyncRefreshStep.ActionPlan:
                    return CaveBuildPromptExportSession.ShouldSkipPromptTsxDuringValidate(
                        "generate-research-action-plan.ts");
                case AsyncRefreshStep.PhasePrompt:
                    return CaveBuildPromptExportSession.ShouldSkipPromptTsxDuringValidate(
                        "generate-phase-prompts.ts");
                case AsyncRefreshStep.NextSteps:
                    return true;
                default:
                    return true;
            }
        }

        static bool TryBeginAsyncStep(
            AsyncRefreshStep step,
            string phaseId,
            string rung,
            int meatPass,
            int queuedStep,
            int seed,
            string warnKey,
            out string message)
        {
            message = string.Empty;
            var handled = false;
            Action<bool, string> onComplete = (ok, msg) =>
            {
                handled = true;
                if (!ok)
                    LogDebouncedWarning(warnKey + ":" + step, "[CaveBuild] Prompt export (" + step + "): " + msg);
                ScheduleNextAsyncStep(step, phaseId, rung, meatPass, queuedStep, seed, warnKey, ok, msg);
            };

            bool started;
            switch (step)
            {
                case AsyncRefreshStep.Unified:
                    started = CaveBuildPhasePromptBridge.TryBeginRunTsx(
                        "generate-unified-agent-prompt.ts",
                        null,
                        null,
                        120_000,
                        onComplete,
                        $"CAVE_ACTIVE_PHASE={phaseId}",
                        CaveBuildPromptExportSession.ShouldSkipManifestRebuild(seed)
                            ? "CAVE_SKIP_MANIFEST_REBUILD=1"
                            : null);
                    break;

                case AsyncRefreshStep.ResearchAgent:
                    started = CaveBuildPhasePromptBridge.TryBeginRunTsx(
                        "generate-research-agent-prompt.ts",
                        string.IsNullOrEmpty(rung) ? null : $"--rung={rung}",
                        null,
                        90_000,
                        onComplete,
                        $"CAVE_ACTIVE_PHASE={phaseId}",
                        string.IsNullOrEmpty(rung) ? null : $"CAVE_ACTIVE_RUNG={rung}");
                    break;

                case AsyncRefreshStep.ActionPlan:
                    started = CaveBuildPhasePromptBridge.TryBeginRunTsx(
                        "generate-research-action-plan.ts",
                        null,
                        null,
                        90_000,
                        onComplete,
                        $"CAVE_ACTIVE_PHASE={phaseId}",
                        $"CAVE_QUEUED_STEP={queuedStep}",
                        $"CAVE_BUILD_SEED={seed}");
                    break;

                case AsyncRefreshStep.PhasePrompt:
                    started = CaveBuildPhasePromptBridge.TryBeginRunTsx(
                        "generate-phase-prompts.ts",
                        $"--rung={rung}",
                        meatPass >= 0 ? $"--meat-pass={meatPass}" : null,
                        90_000,
                        onComplete,
                        $"CAVE_ACTIVE_PHASE={phaseId}",
                        "CAVE_AUTONOMOUS_ITERATION=0",
                        CaveBuildPromptExportSession.ShouldSkipManifestRebuild(seed)
                            ? "CAVE_SKIP_MANIFEST_REBUILD=1"
                            : null);
                    break;

                default:
                    return false;
            }

            return started || handled;
        }

        static void ScheduleNextAsyncStep(
            AsyncRefreshStep completed,
            string phaseId,
            string rung,
            int meatPass,
            int queuedStep,
            int seed,
            string warnKey,
            bool ok,
            string msg)
        {
            if (!ok)
            {
                _asyncRefreshInFlight = false;
                CompleteGatePromptRefresh(false, msg, queuedStep);
                return;
            }

            var next = (int)completed + 1;
            for (; next < AsyncRefreshStepCount; next++)
            {
                var step = (AsyncRefreshStep)next;
                if (ShouldSkipAsyncStep(step))
                    continue;

                if (TryBeginAsyncStep(step, phaseId, rung, meatPass, queuedStep, seed, warnKey, out _))
                    return;

                LogDebouncedWarning(
                    warnKey + ":" + step,
                    "[CaveBuild] Prompt export could not begin async step " + step);
            }

            if (next >= AsyncRefreshStepCount &&
                !CaveBuildPhasePromptBridge.RequiresNonBlockingTsx)
                CaveBuildPhasePromptBridge.ExportNextStepsAndDoNot(phaseId, out _);
            else if (next >= AsyncRefreshStepCount)
            {
                CaveBuildPhasePromptBridge.TryBeginRunTsx(
                    "generate-research-action-plan.ts",
                    "--next-steps-only",
                    null,
                    45_000,
                    (_, _) => { },
                    $"CAVE_ACTIVE_PHASE={phaseId}");
            }

            _asyncRefreshInFlight = false;
            CaveBuildPromptExportSession.MarkFresh(seed);
            CompleteGatePromptRefresh(
                true,
                $"Prompt tsx chain finished for `{phaseId}`.",
                queuedStep);
        }

        static void LogDebouncedWarning(string key, string warning)
        {
            if (_lastDebouncedWarnKey == key)
                return;
            _lastDebouncedWarnKey = key;
            Debug.LogWarning(warning);
        }

        public static void RefreshForTerrainPhase(
            int terrainPhaseIndex,
            WorldGenerationRequest request,
            int seed)
        {
            if (terrainPhaseIndex < 0 ||
                terrainPhaseIndex >= SurfaceTerrainAiPhases.PhaseIds.Length)
                return;

            var phaseId = SurfaceTerrainAiPhases.PhaseIds[terrainPhaseIndex];
            RefreshForPhase(
                phaseId,
                "terrain_integration",
                terrainPhaseIndex,
                37 + terrainPhaseIndex,
                seed,
                out _);
        }

        static string PhaseRungForExport(string phaseId)
        {
            if (phaseId != null && phaseId.StartsWith("surface_"))
                return "terrain_integration";
            if (phaseId == "layout_platforms" || phaseId == "moving_platforms")
                return "floor_collision";
            if (phaseId == "fog_layout" || phaseId == "atmosphere_fog")
                return "materials";
            if (phaseId == "surface_route_bot")
                return "ground_placement";
            return "research";
        }
    }
}
#endif
