#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Procedural FullWorld with no cloud/local LLM agent invokes (no API keys required).</summary>
    public static class CaveBuildOfflineNoApiPreset
    {
        public const string ProfileId = "offline_no_api";

        public static void Apply(bool savePrefs = true)
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();

            settings.usePhasedCaveBuild = true;
            settings.useIncrementalLadder = false;
            settings.autoInvokeAfterEveryBuild = false;
            settings.autoInvokeOnDud = false;
            settings.autoInvokeEachMeatLoopPass = false;
            settings.autoInvokePreBuildWorkflow = false;
            settings.autoInvokeTerrainAfterSurfaceBuild = false;
            settings.autoRebuildSurfaceAfterTerrainAgent = false;
            settings.preBuildReloopUntilPass = false;
            settings.invokeCursorOnResearchPhase = false;
            settings.runPostBuildResearchPhase = false;
            settings.suppressMeatLoopCursorInvokes = true;
            settings.enforcePreBuildGate = false;
            settings.enableEnhancementPhases = true;
            settings.demSupersampleTargetDim = 64;
            settings.requirePreflightBeforeFullWorldBuild = false;

            if (savePrefs)
                settings.SaveToPrefs();

            CaveBuildEnhancementRunner.ExportCatalogJson();
            Debug.Log(
                "[CaveBuild] Offline / no-API preset — 120-step procedural pipeline only (no grade-and-fix agent). " +
                "For local LLM grading use Hub → Active provider → Local Ollama + run Ollama on your machine.");
        }
    }
}
#endif
