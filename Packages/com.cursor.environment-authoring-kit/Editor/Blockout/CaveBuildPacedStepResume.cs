#if UNITY_EDITOR
using System;
using System.IO;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Resume an interrupted Hub build from CaveBuildPacedStepCheckpoint.json after playtest / Play Mode.
    /// </summary>
    public static class CaveBuildPacedStepResume
    {
        public static bool HasResumable
        {
            get
            {
                if (!TryLoad(out var doc))
                    return false;

                if (IsCheckpointStale(doc, out var staleReason))
                {
                    CaveBuildPacedStepPersistence.ClearCheckpoint();
                    CaveBuildEditorLog.LogSurface(
                        $"[Build] Discarded stale paced-step checkpoint ({staleReason}).",
                        forceUnityConsole: false);
                    return false;
                }

                return doc.pacedStep > 0 &&
                       SurfaceTerrainTileExpansion.FindMainTerrainInScene() != null;
            }
        }

        public static bool TryLoad(out CaveBuildPacedStepPersistence.PacedStepSnapshot doc) =>
            CaveBuildPacedStepPersistence.TryLoadSnapshot(out doc);

        public static bool TryResume(out string message)
        {
            message = string.Empty;
            if (!TryLoad(out var doc))
            {
                message = "No paced-step checkpoint on disk.";
                return false;
            }

            if (SurfaceTerrainTileExpansion.FindMainTerrainInScene() == null)
            {
                message = "Terrain missing from the active scene.";
                return false;
            }

            if (IsCheckpointStale(doc, out var staleReason))
            {
                CaveBuildPacedStepPersistence.ClearCheckpoint();
                message = $"Checkpoint stale ({staleReason}) — start a fresh build.";
                return false;
            }

            if (LavaTubeCaveBuilder.IsBuildInProgress || CaveBuildStartupCoordinator.IsActive)
            {
                message = "A build is already running.";
                return false;
            }

            return LavaTubeCaveBuilder.TryResumeFromPacedCheckpoint(doc, out message);
        }

        internal static bool IsCheckpointStale(
            CaveBuildPacedStepPersistence.PacedStepSnapshot doc,
            out string reason)
        {
            reason = string.Empty;
            if (doc == null || doc.pacedStep <= 0)
            {
                reason = "empty checkpoint";
                return true;
            }

            var request = new WorldGenerationRequest { SurfaceScope = SurfaceBuildScope.FullWorld };
            if (CaveBuildSessionConfig.HasFinalizedActive)
                CaveBuildSessionConfig.BindFullWorldRequest(request);
            else if (doc.seed > 0)
                request.Seed = doc.seed;

            var planned = Mathf.Max(400, CaveBuildPlannedStepBudget.ComputeForRequest(request));
            if (doc.pacedStep > planned * 2)
            {
                reason = $"step {doc.pacedStep} exceeds 2× planned budget ({planned})";
                return true;
            }

            if (CaveBuildSessionConfig.HasFinalizedActive &&
                !string.IsNullOrEmpty(doc.generationStyleId) &&
                !string.Equals(doc.generationStyleId, "session_config", StringComparison.Ordinal) &&
                doc.pacedStep > 200)
            {
                reason = "non-planner checkpoint with active planner session";
                return true;
            }

            return false;
        }
    }
}
#endif
