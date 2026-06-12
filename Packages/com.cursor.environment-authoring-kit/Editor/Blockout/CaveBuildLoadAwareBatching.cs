#if UNITY_EDITOR
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Central cap for paced work: 1–2 items per editor queue batch, tightened under memory/load pressure.
    /// </summary>
    public static class CaveBuildLoadAwareBatching
    {
        public const int MaxItemsPerBatch = 2;

        public static int Clamp(int requested)
        {
            requested = Mathf.Clamp(requested, 1, MaxItemsPerBatch);
            if (PreferSingleItem())
                return 1;
            return requested;
        }

        public static bool PreferSingleItem()
        {
            if (CaveBuildMemoryGuard.ShouldHoldQueueForMemory())
                return true;
            if (EnvironmentKitHardwareBudget.Active.ConserveGpuMemory)
                return true;
            if (CaveBuildEditorResponsiveness.IsLongBuildActive)
                return true;
            if (CaveBuildSurfaceCompletionGate.IsFullWorldGridPipelineActive)
                return true;
            if (CaveBuildPipelineScope.CaveOnlyContinuation)
                return true;

            var backlog = CaveBuildActionPacing.QueuedCount;
            if (backlog > 6)
                return true;
            if (backlog > 3)
                return true;

            if (CaveBuildMemoryGuard.PressureRatio01() >= 0.72f)
                return true;

            return false;
        }
    }
}
#endif
