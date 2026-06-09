#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Paced CC0 import for FullWorld builds — one mesh (or prep/finalize) per editor queue step
    /// so post-weld work does not beachball Unity on 16GB Macs.
    /// Finalize uses seam-phase only when NOT recording; recording keeps throttled Scene repaints for timelapse.
    /// </summary>
    public static class Cc0ContentImportPipeline
    {
        static bool _cc0SeamSuppressionActive;

        public static void QueueEnsureAll(bool importItems, Action onComplete)
        {
            if (onComplete == null)
                return;

            if (!CaveBuildEditorResponsiveness.IsLongBuildActive)
            {
                Cc0ContentImportUtility.EnsureAll(importItems, reimportFbx: false);
                onComplete();
                return;
            }

            _cc0SeamSuppressionActive = !CaveBuildDemoAutoRecorder.KeepSceneViewLiveForRecording;
            CaveBuildMemoryGuard.SetCc0ImportPhaseActive(true);
            if (_cc0SeamSuppressionActive)
                CaveBuildLiveSceneFlushUtility.EnterSeamPhase();

            var wrappedComplete = onComplete;
            onComplete = () =>
            {
                try
                {
                    wrappedComplete?.Invoke();
                }
                finally
                {
                    EndCc0ImportPhase();
                }
            };

            var itemPaths = Cc0ContentImportUtility.CollectItemMeshFullPaths();
            var characterPrefabs = new Dictionary<string, GameObject>();
            var itemPrefabs = new Dictionary<string, GameObject>();

            CaveBuildRunStatusPublisher.SetSubOperation("Full AAA", "CC0 import — prep");
            CaveBuildActionPacing.ScheduleBuildStep(
                () =>
                {
                    Cc0ContentImportUtility.RunEnsureAllPrep(reimportFbx: false);
                    if (importItems)
                        Cc0ContentImportUtility.FixAllItemModelImportSettings(reimport: false);
                    QueueCharacterImport(characterPrefabs, itemPaths, itemPrefabs, importItems, onComplete);
                },
                CaveBuildPipelineDomains.QueueLabel("Full AAA — CC0 import prep"),
                CaveBuildActionPacing.ActionWeight.Light);
        }

        static void QueueCharacterImport(
            Dictionary<string, GameObject> characterPrefabs,
            string[] itemPaths,
            Dictionary<string, GameObject> itemPrefabs,
            bool importItems,
            Action onComplete)
        {
            CaveBuildRunStatusPublisher.SetSubOperation("Full AAA", "CC0 import — characters");
            CaveBuildActionPacing.ScheduleBuildStep(
                () =>
                {
                    Cc0ContentImportUtility.RunEnsureAllCharacters(
                        characterPrefabs,
                        reimportFbx: false);
                    if (!importItems || itemPaths == null || itemPaths.Length == 0)
                    {
                        QueueFinalizePaced(itemPrefabs, onComplete);
                        return;
                    }

                    QueueItemImportBatch(
                        itemPaths,
                        0,
                        itemPrefabs,
                        onComplete);
                },
                CaveBuildPipelineDomains.QueueLabel("Full AAA — CC0 characters"),
                CaveBuildActionPacing.ActionWeight.Light);
        }

        static void QueueItemImportBatch(
            string[] itemPaths,
            int startIndex,
            Dictionary<string, GameObject> itemPrefabs,
            Action onComplete)
        {
            CaveBuildMicroProcessQueue.RunIndexedBatches(
                itemPaths.Length,
                startIndex,
                CaveBuildMicroProcessQueue.WorkKind.General,
                "CC0 item import",
                index =>
                {
                    Cc0ContentImportUtility.TryEnsureOneItem(
                        itemPaths[index],
                        reimportFbx: false,
                        itemPrefabs);
                },
                () => QueueFinalizePaced(itemPrefabs, onComplete),
                (done, total) =>
                    CaveBuildRunStatusPublisher.SetSubOperation(
                        "Full AAA",
                        $"CC0 import — items {done}/{total}"),
                (_, __, detail) =>
                    CaveBuildRunStatusPublisher.SetSubOperation("Full AAA", detail));
        }

        static void QueueFinalizePaced(
            Dictionary<string, GameObject> itemPrefabs,
            Action onComplete)
        {
            CaveBuildActionPacing.ScheduleBuildStep(
                () =>
                {
                    CaveBuildActionPacing.TouchQueueActivity();
                    CaveBuildRunStatusPublisher.SetSubOperation("Full AAA", "CC0 import — item registry");
                    Cc0ContentImportUtility.SaveItemRegistryIfNeeded(itemPrefabs);
                    TouchMemoryAfterCc0Step();
                    QueueCatalogSlices(0, onComplete);
                },
                CaveBuildPipelineDomains.QueueLabel("Full AAA — CC0 item registry"),
                CaveBuildActionPacing.ActionWeight.Light);
        }

        static void QueueCatalogSlices(int startIndex, Action onComplete)
        {
            if (!WorldItemCatalogBuilder.TryGetManifestItemCount(out var total, out var error))
            {
                if (!string.IsNullOrEmpty(error))
                    Debug.LogWarning("[CC0] Catalog skipped — " + error);
                QueueTitanResourceSteps(0, onComplete);
                return;
            }

            if (startIndex >= total)
            {
                CaveBuildActionPacing.ScheduleBuildStep(
                    () =>
                    {
                        CaveBuildRunStatusPublisher.SetSubOperation("Full AAA", "CC0 import — catalog commit");
                        WorldItemCatalogBuilder.CommitCatalogRebuildWithoutSave();
                        TouchMemoryAfterCc0Step();
                        QueueTitanResourceSteps(0, onComplete);
                    },
                    CaveBuildPipelineDomains.QueueLabel("Full AAA — CC0 catalog commit"),
                    CaveBuildActionPacing.ActionWeight.Light);
                return;
            }

            var batch = WorldItemCatalogBuilder.CatalogSliceBatchSizePublic;
            var end = Mathf.Min(startIndex + batch, total);
            CaveBuildRunStatusPublisher.SetSubOperation(
                "Full AAA",
                $"CC0 import — catalog {end}/{total}");
            CaveBuildActionPacing.ScheduleBuildStep(
                () =>
                {
                    CaveBuildActionPacing.TouchQueueActivity();
                    WorldItemCatalogBuilder.RebuildCatalogSlice(startIndex, batch);
                    TouchMemoryAfterCc0Step();
                    QueueCatalogSlices(end, onComplete);
                },
                CaveBuildPipelineDomains.QueueLabel($"Full AAA — CC0 catalog {end}/{total}"),
                CaveBuildActionPacing.ActionWeight.Light);
        }

        static void QueueTitanResourceSteps(int stepIndex, Action onComplete)
        {
            var total = WorldRuntimeResourcesAuthor.HollowTitanResourceStepCount;
            if (stepIndex >= total)
            {
                QueueFinalizeRegistry(onComplete);
                return;
            }

            CaveBuildRunStatusPublisher.SetSubOperation(
                "Full AAA",
                $"CC0 import — titan resources {stepIndex + 1}/{total}");
            CaveBuildActionPacing.ScheduleBuildStep(
                () =>
                {
                    CaveBuildActionPacing.TouchQueueActivity();
                    WorldRuntimeResourcesAuthor.RunHollowTitanResourceStep(stepIndex, flushSave: false);
                    TouchMemoryAfterCc0Step();
                    QueueTitanResourceSteps(stepIndex + 1, onComplete);
                },
                CaveBuildPipelineDomains.QueueLabel(
                    $"Full AAA — CC0 titan resource {stepIndex + 1}/{total}"),
                CaveBuildActionPacing.ActionWeight.Light);
        }

        static void QueueFinalizeRegistry(Action onComplete)
        {
            CaveBuildRunStatusPublisher.SetSubOperation("Full AAA", "CC0 import — persist registry");
            CaveBuildActionPacing.ScheduleBuildStep(
                () =>
                {
                    CaveBuildActionPacing.TouchQueueActivity();
                    Cc0ContentImportUtility.SaveItemRegistryAsset();
                    TouchMemoryAfterCc0Step();
                    QueueFinalizeHandoff(onComplete);
                },
                CaveBuildPipelineDomains.QueueLabel("Full AAA — CC0 persist registry"),
                CaveBuildActionPacing.ActionWeight.Light);
        }

        static void QueueFinalizeHandoff(Action onComplete)
        {
            CaveBuildRunStatusPublisher.SetSubOperation("Full AAA", "CC0 import — finalize");
            CaveBuildActionPacing.ScheduleHeavyChain(
                () =>
                {
                    CaveBuildActionPacing.TouchQueueActivity();
                    WorldItemCatalogBuilder.CommitCatalogRebuildWithoutSave();
                    CaveBuildEditorLog.LogSurface(
                        "[CC0] Import complete — prefabs in CC0Imports/Prefabs/. " +
                        "WorldItemCatalog persist deferred until FullWorld grid finishes.",
                        forceUnityConsole: true);
                    CaveBuildRunStatusPublisher.SetSubOperation("Full AAA", "CC0 import complete");
                    ScheduleGridHandoff(onComplete);
                },
                CaveBuildPipelineDomains.QueueLabel("Full AAA — CC0 finalize handoff"));
        }

        /// <summary>
        /// Let queue cooldown + player loop finish before grid snap / boss tree (avoids stacking with asset I/O).
        /// </summary>
        static void ScheduleGridHandoff(Action onComplete)
        {
            CaveBuildActionPacing.ScheduleLightChain(
                () =>
                {
                    CaveBuildRunStatusPublisher.PulseSubOperation(
                        "Full AAA",
                        "post-CC0 — Hollow Titan + terraform handoff");
                    CaveBuildFullWorldGridCheckpoint.SaveActiveSceneMilestone(
                        "CC0 import complete — post-CC0 handoff");
                    onComplete?.Invoke();
                },
                CaveBuildPipelineDomains.QueueLabel("Full AAA — CC0 grid handoff"));
        }

        static void EndCc0ImportPhase()
        {
            CaveBuildMemoryGuard.SetCc0ImportPhaseActive(false);
            if (_cc0SeamSuppressionActive)
            {
                CaveBuildLiveSceneFlushUtility.ExitSeamPhase();
                _cc0SeamSuppressionActive = false;
            }
        }

        static void TouchMemoryAfterCc0Step()
        {
            EnvironmentKitHardwareBudget.OnQueueStepCompletedThrottled();
            if (CaveBuildSurfaceCompletionGate.IsFullWorldGridPipelineActive)
                CaveBuildMemoryGuard.OnFullWorldQueueStepCompleted(289);
        }
    }
}
#endif
