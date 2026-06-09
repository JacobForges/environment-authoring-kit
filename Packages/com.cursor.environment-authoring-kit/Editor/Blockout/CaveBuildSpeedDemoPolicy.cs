#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Concept 9 — playable recorded demo: 81-tile core, play-disk props, no Titan/CC0/research loops.
    /// </summary>
    public static class CaveBuildSpeedDemoPolicy
    {
        public static bool IsActive(WorldGenerationRequest request) =>
            FullWorldConceptLayoutCatalog.IsSpeedMinimal(request) ||
            CaveBuildSessionConfig.IsFloatingIslandsDemo(request);

        public static bool IsActiveHubSelection() =>
            FullWorldConceptLayoutCatalog.IsSpeedMinimal(FullWorldGenerationStylePreset.LoadSelectedIndex()) ||
            (CaveBuildSessionConfig.HasFinalizedActive && CaveBuildSessionConfig.IsFloatingIslandsDemo());

        public static void ApplySessionSettings(CaveBuildCursorSettings settings, bool savePrefs = true)
        {
            if (settings == null)
                return;

            settings.LoadFromPrefs();
            settings.usePhasedCaveBuild = true;
            settings.useIncrementalLadder = false;
            settings.enableEnhancementPhases = false;
            settings.requireHollowTitanOnSurface = false;
            settings.enforcePreBuildGate = false;
            settings.preBuildReloopUntilPass = false;
            settings.autoInvokePreBuildWorkflow = false;
            settings.autoInvokeEachMeatLoopPass = false;
            settings.autoInvokeTerrainAfterSurfaceBuild = false;
            settings.autoInvokeAfterEveryBuild = false;
            settings.runPostBuildResearchPhase = false;
            settings.invokeCursorOnResearchPhase = false;
            settings.enableAutonomousUntilShip = false;
            settings.skipAutonomousWhenGradeAtOrAbove = true;
            settings.fastGateMinOverallScore = 70;
            settings.skipResearchNetworkSyncWhenCachePresent = true;
            settings.editorQueueBatchSize = Mathf.Max(settings.editorQueueBatchSize, 2);
            settings.demSupersampleTargetDim = Mathf.Min(settings.demSupersampleTargetDim, 64);
            settings.mirrorPacedBuildLogsToConsole = false;

            SurfaceTerrainTileExpansion.PreferSequentialFullWorldTerrain = false;

            if (savePrefs)
                settings.SaveToPrefs();

            EditorUtility.SetDirty(settings);
        }

        public static void LogActive(int seed)
        {
            Debug.Log(
                "[CaveBuild] Speed demo (concept 9) — 81 tiles, fresh seed " + seed +
                ", play-disk props ON, Titan/CC0/research/agent loops OFF, faster queue batching.");
        }
    }
}
#endif
