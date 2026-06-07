#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Skip completed ladder rungs when incremental mode is on (invalidate downstream only).</summary>
    public static class CaveBuildIncrementalLadder
    {
        public static bool ShouldSkipQueuedStep(int step, WorldGenerationRequest request)
        {
            if (request == null)
                return false;

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (!settings.useIncrementalLadder)
                return false;

            if (step == 0 || LavaTubeCaveBuildPipeline.IsValidateSubPipelineActive)
                return false;

            // Commercial manifest + finalize always run (never skip past step 115).
            if (step >= CaveBuildQueuedPipelineSchedule.AaaManifest)
                return false;

            // Post-meat/research/finalize-polish sequencing is fragile; always run for deterministic resumes.
            if (step >= CaveBuildQueuedPipelineSchedule.PostMeatFirst &&
                step < CaveBuildQueuedPipelineSchedule.AaaManifest)
                return false;

            // Never skip underground work when the scene has no full cave (blocks/shell/tube).
            if (step >= 1 &&
                step < CaveBuildQueuedPipelineSchedule.FinalizePolishFirst &&
                !CaveBuildPhaseContractRegistry.HasPlayableCaveLayoutInScene())
            {
                CaveBuildPipelineLog.Info(
                    $"Incremental ladder: forcing step {step} — no full cave geometry in scene.",
                    "Incremental");
                return false;
            }

            var rung = CaveBuildPhaseContractRegistry.MapQueuedStepToRung(step);
            if (string.IsNullOrEmpty(rung))
                return false;

            if (CaveBuildPhaseContractRegistry.IsRungComplete(rung, request.Seed))
            {
                CaveBuildLadderMetrics.RecordRungSkipped(rung, true);
                CaveBuildPipelineLog.Info(
                    $"Incremental ladder: skip step {step} ({rung}) — artifacts OK for seed {request.Seed}.",
                    "Incremental");
                return true;
            }

            return false;
        }

        public static int AdvanceSkippingComplete(int fromStep, int maxStep, WorldGenerationRequest request)
        {
            var step = fromStep;
            var guard = 0;
            while (step < maxStep && guard++ < maxStep + 5)
            {
                if (!ShouldSkipQueuedStep(step, request))
                    return step;
                step++;
            }

            return step;
        }
    }
}
#endif
