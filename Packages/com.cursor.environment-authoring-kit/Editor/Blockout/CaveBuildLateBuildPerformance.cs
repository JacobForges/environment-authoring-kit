#if UNITY_EDITOR
using System;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Steps ~20k–69k: Hollow Titan, post-grid weld/seams, surface props, cave macro queue.
    /// Central perf switches for 16 GB hitches (30s work / 2–3 min freeze).
    /// </summary>
    public static class CaveBuildLateBuildPerformance
    {
        public const int StepBandStart = 20_000;
        public const int StepBandEnd = 69_000;

        public static bool IsInLateBuildBand =>
            CaveBuildStepCounter.HasSession && CaveBuildStepCounter.Current >= StepBandStart;

        public static bool PreferLightweightTerrainFlush =>
            CaveBuildEditorResponsiveness.IsLongBuildActive &&
            CaveBuildMemoryGuard.SystemRamGb() is > 0f and <= 17f &&
            IsInLateBuildBand;

        public static bool ShouldSkipIntermediateTitanMilestoneSave() =>
            PreferLightweightTerrainFlush;

        public static void NotifyStepAdvanced(int step)
        {
            if (step != StepBandStart)
                return;

            CaveBuildStepCounter.SetSegment(
                CaveBuildStepCounter.BuildSegment.TerrainAi,
                StepBandEnd - StepBandStart,
                0f,
                "Post-grid · Titan · props · cave macro");
            CaveBuildPipelinePhaseTracker.OnLateBuildBandEntered();
        }
    }
}
#endif
