#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Cave-pipeline-style load delegation for surface/grid work (FullWorld, SurfaceOnly, CaveOnly).
    /// Extended ~289-tile grid: one tile per editor queue step; consolidate/import never bundled with tile batches.
    /// Cave macro continues in <see cref="LavaTubeCaveBuildPipeline"/> ScheduleBuildStep chain after surface completes.
    /// </summary>
    public static class CaveBuildMicroProcessQueue
    {
        public enum WorkKind
        {
            General,
            SurfaceSeam,
            SurfaceGridSnap,
            SurfaceGridWeld,
            SurfaceGridPlace,
            SurfacePolish,
        }

        /// <summary>True when every queue step should touch at most one grid tile.</summary>
        public static bool PreferOneTilePerQueueStep =>
            CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid ||
            CaveBuildSurfaceCompletionGate.IsFullWorldGridPipelineActive;

        /// <summary>Per-scope batch sizing — extended grid is always 1 tile/step on editor RAM ≤18GB.</summary>
        public static int ResolveBatchSize(WorkKind kind)
        {
            if (EnvironmentKitHardwareBudget.Active.ConserveGpuMemory)
                return 1;

            if (PreferOneTilePerQueueStep)
            {
                var ramGb = EnvironmentKitHardwareBudget.ResolveEditorRamBudgetGb();
                if (kind is WorkKind.SurfaceGridSnap or WorkKind.SurfaceGridWeld or WorkKind.SurfaceGridPlace)
                    return 1;

                return kind switch
                {
                    WorkKind.SurfaceSeam => ramGb >= 18f ? 2 : 1,
                    WorkKind.SurfacePolish => 1,
                    _ => 1,
                };
            }

            var pacing = CaveBuildCursorSettings.ResolveQueuePacing();
            var configured = Mathf.Max(1, pacing.batchSize);
            if (CaveBuildEditorResponsiveness.IsLongBuildActive && configured > 1)
                configured = 1;

            var ram = EnvironmentKitHardwareBudget.ResolveEditorRamBudgetGb();
            return kind switch
            {
                WorkKind.SurfaceGridWeld => ram >= 18f ? 8 : 4,
                WorkKind.SurfaceGridSnap => ram >= 18f ? 12 : 8,
                WorkKind.SurfaceSeam => Mathf.Min(4, configured),
                WorkKind.SurfacePolish => configured,
                _ => configured,
            };
        }

        static CaveBuildActionPacing.ActionWeight ResolveScheduleWeightInternal(WorkKind kind) =>
            kind switch
            {
                WorkKind.SurfaceGridWeld => CaveBuildActionPacing.ActionWeight.Normal,
                WorkKind.SurfaceSeam => CaveBuildActionPacing.ActionWeight.Light,
                WorkKind.SurfaceGridSnap => CaveBuildActionPacing.ActionWeight.Light,
                WorkKind.SurfaceGridPlace => CaveBuildActionPacing.ActionWeight.Light,
                WorkKind.SurfacePolish => CaveBuildActionPacing.ActionWeight.Heavy,
                _ => CaveBuildActionPacing.ActionWeight.Normal,
            };

        public static void ApplyForBuildScope(SurfaceBuildScope scope)
        {
            CaveBuildEditorQueueSafeguard.ResetForNewBuildSession();

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            settings.editorQueueBatchSize = 1;
            settings.mirrorPacedBuildLogsToConsole = false;
            settings.SaveToPrefs();
            CaveBuildEditorResponsiveness.ApplyForActiveBuild(settings);

            if (scope != SurfaceBuildScope.FullWorld)
                return;

            var tilePlan = CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid
                ? SurfaceOpenWorldGridExpansion.ExtendedBuildTileSlotCount
                : SurfaceTerrainTileExpansion.FullWorldTerrainTileCount;
            if (tilePlan > 81)
                CaveBuildMemoryGuard.ApplyExtendedGridMemoryProfile(tilePlan);
        }

        public static string FormatBatchLabel(string prefix, int endIndex, int total, int batchNumber, int totalBatches) =>
            totalBatches <= 1
                ? $"{prefix} {endIndex}/{total}"
                : $"{prefix} batch {batchNumber}/{totalBatches} ({endIndex}/{total})";

        /// <summary>One queue step for consolidate (FindObjects scan) — never inline in a tile batch.</summary>
        public static void ScheduleConsolidateScattered(
            Terrain mainTerrain,
            string label,
            Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.ScheduleBuildStep(
                () =>
                {
                    CaveBuildActionPacing.TouchQueueActivity();
                    SurfaceTerrainTileExpansion.ConsolidateScatteredFullWorldTerrains(mainTerrain);
                    onComplete?.Invoke();
                },
                CaveBuildPipelineDomains.QueueLabel(label),
                CaveBuildActionPacing.ActionWeight.Heavy);
        }

        /// <summary>Runs [startIndex, total) in paced batches — one editor tick per batch, yield between batches.</summary>
        public static void RunIndexedBatches(
            int total,
            int startIndex,
            WorkKind kind,
            string queueLabelPrefix,
            Action<int> processIndex,
            Action onComplete,
            Action<int, int> onBatchCompleted = null,
            Action<int, int, string> pulseSubOperation = null)
        {
            if (total <= 0 || processIndex == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (startIndex >= total)
            {
                onComplete?.Invoke();
                return;
            }

            RunIndexedBatchStep(
                total,
                startIndex,
                kind,
                queueLabelPrefix,
                processIndex,
                onComplete,
                onBatchCompleted,
                pulseSubOperation);
        }

        static void RunIndexedBatchStep(
            int total,
            int startIndex,
            WorkKind kind,
            string queueLabelPrefix,
            Action<int> processIndex,
            Action onComplete,
            Action<int, int> onBatchCompleted,
            Action<int, int, string> pulseSubOperation)
        {
            if (CaveBuildMemoryGuard.ShouldHoldQueueForMemory())
            {
                CaveBuildActionPacing.ScheduleLight(
                    () => RunIndexedBatchStep(
                        total,
                        startIndex,
                        kind,
                        queueLabelPrefix,
                        processIndex,
                        onComplete,
                        onBatchCompleted,
                        pulseSubOperation),
                    CaveBuildPipelineDomains.QueueLabel($"{queueLabelPrefix} — memory hold"));
                return;
            }

            var batchSize = ResolveBatchSize(kind);
            var endIndex = Mathf.Min(startIndex + batchSize, total);
            var batchNumber = startIndex / batchSize + 1;
            var totalBatches = (total + batchSize - 1) / batchSize;
            var label = CaveBuildPipelineDomains.QueueLabel(
                FormatBatchLabel(queueLabelPrefix, endIndex, total, batchNumber, totalBatches));
            var weight = ResolveScheduleWeightInternal(kind);

            CaveBuildActionPacing.ScheduleBuildStep(
                () =>
                {
                    CaveBuildActionPacing.TouchQueueActivity();
                    CaveBuildEditorResponsiveness.BeginSlice();
                    var processedEnd = startIndex;
                    for (var i = startIndex; i < endIndex; i++)
                    {
                        processIndex(i);
                        processedEnd = i + 1;
                        if (i + 1 < endIndex && CaveBuildEditorResponsiveness.ShouldYieldSlice())
                            break;
                    }
                    onBatchCompleted?.Invoke(processedEnd, total);
                    pulseSubOperation?.Invoke(
                        processedEnd,
                        total,
                        FormatBatchLabel(queueLabelPrefix, processedEnd, total, batchNumber, totalBatches));

                    if (CaveBuildSurfaceCompletionGate.IsFullWorldGridPipelineActive && total > 81)
                        CaveBuildMemoryGuard.OnFullWorldQueueStepCompleted(total);

                    if (processedEnd < total)
                    {
                        RunIndexedBatchStep(
                            total,
                            processedEnd,
                            kind,
                            queueLabelPrefix,
                            processIndex,
                            onComplete,
                            onBatchCompleted,
                            pulseSubOperation);
                        return;
                    }

                    onComplete?.Invoke();
                },
                label,
                weight);
        }
    }
}
#endif
