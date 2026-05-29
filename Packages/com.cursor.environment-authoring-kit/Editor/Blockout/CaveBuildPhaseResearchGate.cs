#if UNITY_EDITOR
// Research gate uses WorldGenerationRequest only (not LavaTubeCaveBuildPipeline.QueuedPipelineContext).
using System.IO;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Mandatory research + action plan before every queued step / phase. Full cache pull on phase boundaries only.
    /// </summary>
    public static class CaveBuildPhaseResearchGate
    {
        public const string GateRel = CaveBuildAgentContextExporter.Folder + "/CaveBuildPhaseResearchGate.json";
        public const string ActionPlanRel = CaveBuildAgentContextExporter.Folder + "/CaveBuildResearchActionPlan.json";

        [System.Serializable]
        public class GateFile
        {
            public bool passed;
            public string completedUtc;
            public string phaseId;
            public int queuedStep;
            public int seed;
            public string message;
            public bool fullResearchPull;
        }

        public static bool EnsureBeforeQueuedStep(
            int step,
            WorldGenerationRequest request,
            out string message) =>
            EnsureBeforeQueuedStep(step, request, out message, out _);

        public static bool EnsureBeforeQueuedStep(
            int step,
            WorldGenerationRequest request,
            out string message,
            out bool awaitingPromptTsx)
        {
            message = string.Empty;
            awaitingPromptTsx = false;
            if (request == null)
            {
                message = "Missing build request.";
                return false;
            }

            if (CaveBuildPipelineScope.CaveOnlyContinuation &&
                request.SurfaceScope == SurfaceBuildScope.CaveOnly)
            {
                message = "CaveOnly align continuation — phase research gate skipped (surface already built).";
                return true;
            }

            var seed = request.Seed;

            var phaseId = ResolvePhaseId(step);
            var fullPull = RequiresFullResearchPull(step);

            if (step == 0 && CaveBuildPrePlacementResearch.IsGatePassedForSeed(seed))
            {
                message = "Research gate OK (pre-placement research already completed for this seed).";
                CaveBuildRunStatusPublisher.SetResearchPhase(
                    message,
                    CaveBuildRunStatusPublisher.ResearchGateState.Passed);
                return true;
            }

            if (!fullPull && IsRecentlyPassed(phaseId, step, seed))
            {
                message = $"Research gate OK (cached): {phaseId} step {step}.";
                return true;
            }

            CaveBuildRunStatusPublisher.SetResearchPhase(
                $"Research gate: {phaseId} (step {step}){(fullPull ? " — full pull" : "")}…",
                CaveBuildRunStatusPublisher.ResearchGateState.InProgress);

            if (fullPull)
            {
                if (CaveBuildResearchCacheBridge.TryFastPathResearchPull(PhaseRung(phaseId), out var fastMsg))
                {
                    message = fastMsg;
                    WriteGate(true, phaseId, step, seed, message, fullPull: false);
                    CaveBuildRunStatusPublisher.SetResearchPhase(
                        message,
                        CaveBuildRunStatusPublisher.ResearchGateState.Passed);
                    return true;
                }

                if (!CaveBuildResearchCacheBridge.SyncFullResearchPull(PhaseRung(phaseId), out var pullMsg))
                {
                    if (!CaveBuildResearchCacheBridge.HasUsableLocalResearchCache())
                    {
                        message = pullMsg;
                        WriteGate(false, phaseId, step, seed, message, fullPull);
                        CaveBuildRunStatusPublisher.SetResearchPhase(
                            message,
                            CaveBuildRunStatusPublisher.ResearchGateState.Failed);
                        return false;
                    }

                    pullMsg += " | Using existing ResearchCache (offline/degraded).";
                    Debug.LogWarning("[CaveBuild] Phase research: " + pullMsg);
                }
            }
            else
            {
                if (!RefreshPromptsForGate(
                        phaseId,
                        step,
                        seed,
                        MeatPassForStep(step),
                        out message,
                        out awaitingPromptTsx))
                    return false;

                if (awaitingPromptTsx)
                    return true;

                WriteGate(true, phaseId, step, seed, message, false);
                CaveBuildRunStatusPublisher.SetResearchPhase(
                    message,
                    CaveBuildRunStatusPublisher.ResearchGateState.Passed);
                return true;
            }

            if (!RefreshPromptsForGate(
                    phaseId,
                    step,
                    seed,
                    MeatPassForStep(step),
                    out var refreshMsg,
                    out awaitingPromptTsx))
                return false;

            if (awaitingPromptTsx)
            {
                message = refreshMsg;
                return true;
            }

            message = $"{phaseId}: full research + all JSON → AI prompts. {refreshMsg}";
            WriteGate(true, phaseId, step, seed, message, true);
            CaveBuildRunStatusPublisher.SetResearchPhase(
                message,
                CaveBuildRunStatusPublisher.ResearchGateState.Passed);
            return true;
        }

        public static bool EnsureBeforeMeatPass(int meatPass, int seed, out string message) =>
            EnsureBeforeMeatPass(meatPass, seed, out message, out _);

        public static bool EnsureBeforeMeatPass(
            int meatPass,
            int seed,
            out string message,
            out bool awaitingPromptTsx)
        {
            var phaseId = PhaseForMeatPass(meatPass);
            message = string.Empty;
            awaitingPromptTsx = false;

            if (!RefreshPromptsForGate(
                    phaseId,
                    -1,
                    seed,
                    meatPass,
                    out var refreshMsg,
                    out awaitingPromptTsx))
                return false;

            message = $"Meat pass {meatPass} ({phaseId}): {refreshMsg}";

            if (!awaitingPromptTsx)
                WriteGate(true, phaseId, -1, seed, message, false);
            return true;
        }

        static bool RefreshPromptsForGate(
            string phaseId,
            int queuedStep,
            int seed,
            int meatPass,
            out string message,
            out bool awaitingPromptTsx)
        {
            awaitingPromptTsx = false;
            var ok = CaveBuildUnifiedPromptBridge.RefreshForPhase(
                phaseId,
                PhaseRung(phaseId),
                meatPass,
                queuedStep,
                seed,
                out message,
                out awaitingPromptTsx);

            if (awaitingPromptTsx)
            {
                message =
                    $"Research gate awaiting prompt tsx for `{phaseId}`" +
                    (queuedStep >= 0 ? $" (step {queuedStep})" : $" (meat pass {meatPass})") +
                    (string.IsNullOrEmpty(message) ? "." : $" — {message}");
                CaveBuildRunStatusPublisher.SetResearchPhase(
                    message,
                    CaveBuildRunStatusPublisher.ResearchGateState.InProgress);
                CaveBuildEditorLog.LogCave(message, forceUnityConsole: true);
                return true;
            }

            return ok;
        }

        static bool IsRecentlyPassed(string phaseId, int step, int seed)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, GateRel);
            if (!File.Exists(path))
                return false;
            try
            {
                var gate = JsonUtility.FromJson<GateFile>(File.ReadAllText(path));
                return gate.passed && gate.seed == seed && gate.phaseId == phaseId && gate.queuedStep == step;
            }
            catch
            {
                return false;
            }
        }

        static void WriteGate(bool passed, string phaseId, int step, int seed, string message, bool fullPull)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, GateRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            var gate = new GateFile
            {
                passed = passed,
                completedUtc = System.DateTime.UtcNow.ToString("o"),
                phaseId = phaseId,
                queuedStep = step,
                seed = seed,
                message = message,
                fullResearchPull = fullPull,
            };
            File.WriteAllText(path, JsonUtility.ToJson(gate, true));
        }

        /// <summary>Macro boundaries for bot reports / prompt refresh — excludes manifest + finalize (115+).</summary>
        public static bool IsPhaseBoundary(int step) =>
            step == 0 ||
            step == CaveBuildQueuedPipelineSchedule.PlayabilityFirst - 1 ||
            step == CaveBuildQueuedPipelineSchedule.ValidationFirst - 1 ||
            step == CaveBuildQueuedPipelineSchedule.GroundPolishFirst - 1 ||
            step == CaveBuildQueuedPipelineSchedule.WorldFirst - 1 ||
            step == CaveBuildQueuedPipelineSchedule.Meat ||
            (step >= CaveBuildQueuedPipelineSchedule.ResearchFirst &&
             step < CaveBuildQueuedPipelineSchedule.AaaManifest);

        /// <summary>Blocking cache pull only at true phase starts — not every post-meat substep or AAA manifest.</summary>
        public static bool RequiresFullResearchPull(int step) =>
            step == 0 ||
            step == CaveBuildQueuedPipelineSchedule.PlayabilityFirst - 1 ||
            step == CaveBuildQueuedPipelineSchedule.ValidationFirst - 1 ||
            step == CaveBuildQueuedPipelineSchedule.GroundPolishFirst - 1 ||
            step == CaveBuildQueuedPipelineSchedule.WorldFirst - 1 ||
            step == CaveBuildQueuedPipelineSchedule.Meat ||
            step == CaveBuildQueuedPipelineSchedule.ResearchFirst;

        static int MeatPassForStep(int step)
        {
            if (step == CaveBuildQueuedPipelineSchedule.Meat)
                return 0;
            return -1;
        }

        public static string PhaseForMeatPass(int pass)
        {
            if (pass == 5)
                return "layout_platforms";
            if (pass == 6)
                return "atmosphere_fog";
            if (pass == 1)
                return "cave_mouth_seal";
            if (pass == 4)
                return "organic_mesh";
            if (pass == 14)
                return "packaging_ship";
            return "meat_loop_additive";
        }

        static string PhaseRung(string phaseId)
        {
            if (phaseId.StartsWith("surface_"))
                return "terrain_integration";
            if (phaseId == "layout_platforms" || phaseId == "moving_platforms")
                return "floor_collision";
            if (phaseId == "fog_layout" || phaseId == "atmosphere_fog")
                return "materials";
            if (phaseId == "surface_route_bot")
                return "ground_placement";
            return "other";
        }

        public static string ResolvePhaseId(int step)
        {
            if (step == 0)
                return "research";
            if (step >= CaveBuildQueuedPipelineSchedule.GeoFirst &&
                step < CaveBuildQueuedPipelineSchedule.PlayabilityFirst)
            {
                if (step == 5)
                    return "layout_platforms";
                if (step == 6 || step == 7 || step == 8)
                    return "moving_platforms";
                if (step == 13)
                    return "cave_mouth_seal";
                return "visual_shell";
            }

            if (step >= CaveBuildQueuedPipelineSchedule.PlayabilityFirst &&
                step < CaveBuildQueuedPipelineSchedule.ValidationFirst)
                return step < 21 ? "floor_collision" : "ground_placement";
            if (step >= CaveBuildQueuedPipelineSchedule.ValidationFirst &&
                step < CaveBuildQueuedPipelineSchedule.GroundPolishFirst)
                return "floor_collision";
            if (step >= CaveBuildQueuedPipelineSchedule.GroundPolishFirst &&
                step < CaveBuildQueuedPipelineSchedule.WorldFirst)
            {
                var w = step - CaveBuildQueuedPipelineSchedule.GroundPolishFirst;
                return w switch
                {
                    0 => "visual_shell",
                    1 => "materials_lighting",
                    2 => "cinematic_lighting",
                    3 => "materials_lighting",
                    4 => "cinematic_lighting",
                    5 => "atmosphere_fog",
                    6 => "performance",
                    7 => "navmesh",
                    8 => "floor_collision",
                    9 => "materials_lighting",
                    10 => "packaging_ship",
                    _ => "meat_loop_additive",
                };
            }

            if (step >= CaveBuildQueuedPipelineSchedule.WorldFirst &&
                step < CaveBuildQueuedPipelineSchedule.Meat)
            {
                var w = step - CaveBuildQueuedPipelineSchedule.WorldFirst;
                return w switch
                {
                    0 => "meat_loop_additive",
                    _ => w < 8 ? "fog_layout" : "packaging_ship",
                };
            }

            if (step == CaveBuildQueuedPipelineSchedule.Meat)
                return "meat_loop_additive";
            if (step >= CaveBuildQueuedPipelineSchedule.PostMeatFirst &&
                step < CaveBuildQueuedPipelineSchedule.ResearchFirst)
                return "fog_layout";
            if (step >= CaveBuildQueuedPipelineSchedule.ResearchFirst &&
                step < CaveBuildQueuedPipelineSchedule.FinalizePolishFirst)
                return "research";
            if (step >= CaveBuildQueuedPipelineSchedule.FinalizePolishFirst &&
                step < 1000)
                return "packaging_ship";
            if (step >= PlaytestPolishPhaseRunner.QueuedStepBase &&
                step < PlaytestPolishPhaseRunner.QueuedStepBase + PlaytestPolishPhaseRunner.PhaseCount)
            {
                var idx = step - PlaytestPolishPhaseRunner.QueuedStepBase;
                return idx >= 0 && idx < PlaytestPolishPhaseRunner.PhaseIds.Length
                    ? PlaytestPolishPhaseRunner.PhaseIds[idx]
                    : "polish_60_bot_schedule_ready";
            }

            if (step >= CaveGenerationQualityPhaseRunner.QueuedStepBase &&
                step < CaveGenerationQualityPhaseRunner.QueuedStepBase + CaveGenerationQualityPhaseRunner.PhaseCount)
            {
                var g = step - CaveGenerationQualityPhaseRunner.QueuedStepBase;
                return g >= 0 && g < CaveGenerationQualityPhaseRunner.PhaseIds.Length
                    ? CaveGenerationQualityPhaseRunner.PhaseIds[g]
                    : "gen_30_generation_complete";
            }

            return "meat_loop_additive";
        }
    }
}
#endif
