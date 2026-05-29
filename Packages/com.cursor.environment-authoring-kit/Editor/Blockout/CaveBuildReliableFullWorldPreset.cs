#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>One-click settings for a dependable FullWorld start → finish run.</summary>
    public static class CaveBuildReliableFullWorldPreset
    {
        public const string ProfileId = "reliable_full_world";

        public static void Apply(bool savePrefs = true)
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();

            settings.usePhasedCaveBuild = true;
            settings.useIncrementalLadder = false;
            settings.autoInvokeAfterEveryBuild = false;
            settings.autoInvokeEachMeatLoopPass = true;
            settings.autoInvokePreBuildWorkflow = true;
            settings.preBuildReloopUntilPass = true;
            settings.maxPreBuildReloopAttempts = CaveBuildPreBuildReloop.DefaultMaxLocalAttempts;
            settings.skipResearchNetworkSyncWhenCachePresent = true;
            settings.enableAutonomousUntilShip = false;
            settings.suppressMeatLoopCursorInvokes = false;
            settings.autoInvokeTerrainAfterSurfaceBuild = true;
            settings.autoRebuildSurfaceAfterTerrainAgent = false;
            settings.runPostBuildResearchPhase = true;
            settings.invokeCursorOnResearchPhase = true;
            settings.enforcePreBuildGate = true;
            settings.enableEnhancementPhases = true;
            settings.demSupersampleTargetDim = 128;
            settings.requirePreflightBeforeFullWorldBuild = false;
            settings.mirrorPacedBuildLogsToConsole = false;

            if (savePrefs)
                settings.SaveToPrefs();

            if (CaveBuildLayoutRollSession.LastRecordedSeed > 0)
            {
                EditorPrefs.SetBool("CaveBuild_RandomizeEachTime", false);
                CaveBuildLayoutRollSession.PinLastSeedForDebugging();
            }
            else
                CaveBuildSeedDefaults.EnsureVarietyForFreshGeneration();

            Selection.activeObject = settings;
            CaveBuildEnhancementRunner.ExportCatalogJson();
            Debug.Log(
                "[CaveBuild] Automated FullWorld preset — phased 121-step queue, Cursor on terrain meat + post-surface, " +
                "pre-build reloop until 88+, enhancements on, DEM×128.");
        }
    }
}
#endif
