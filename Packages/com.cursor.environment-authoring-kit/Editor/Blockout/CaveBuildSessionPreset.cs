#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Chooses FullWorld settings from available credentials — no separate Hub buttons.</summary>
    public static class CaveBuildSessionPreset
    {
        public static bool HasUsableAiProvider => CaveBuildCursorSettings.HasCredentialsForActiveProvider();

        public static bool HasLocalResearchCache => CaveBuildResearchCacheBridge.HasUsableLocalResearchCache();

        /// <summary>No API — terrain may use Florida/DEM defaults without ResearchCache or hillshade PNGs.</summary>
        public static bool AllowProceduralTerrainWithoutResearch => !HasUsableAiProvider;

        /// <summary>Called at start of every FullWorld build (Hub button or menu).</summary>
        public static void ApplyAutomaticForFullWorld()
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();

            if (HasUsableAiProvider)
            {
                CaveBuildReliableFullWorldPreset.Apply(savePrefs: false);
                settings.LoadFromPrefs();
                settings.suppressMeatLoopCursorInvokes = false;
                settings.autoInvokeEachMeatLoopPass = true;
                settings.autoInvokeTerrainAfterSurfaceBuild = true;
                settings.autoInvokePreBuildWorkflow = true;
                settings.preBuildReloopUntilPass = true;
                settings.invokeCursorOnResearchPhase = true;
                settings.enforcePreBuildGate = true;
                settings.skipResearchNetworkSyncWhenCachePresent = true;
                settings.SaveToPrefs();
                Debug.Log(
                    $"[CaveBuild] AI provider ready ({CaveBuildCursorSettings.ResolveActiveProvider()}) — " +
                    "FullWorld includes agent grading when steps request it.");
            }
            else
            {
                CaveBuildOutOfBoxPreset.Apply(savePrefs: false, log: false);
                ConfigureProceduralResearchFallback();
                var cacheNote = HasLocalResearchCache
                    ? "using on-disk ResearchCache"
                    : "no ResearchCache — Florida/DEM procedural defaults only";
                Debug.Log(
                    "[CaveBuild] No API keys — FullWorld runs procedurally (" + cacheNote + ").");
            }

            EditorUtility.SetDirty(CaveBuildCursorSettings.LoadOrCreate());
        }

        static void ConfigureProceduralResearchFallback()
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.skipResearchNetworkSyncWhenCachePresent = true;
            settings.runPostBuildResearchPhase = HasLocalResearchCache;
            settings.invokeCursorOnResearchPhase = false;
            settings.suppressMeatLoopCursorInvokes = true;
            settings.autoInvokeEachMeatLoopPass = false;
            settings.autoInvokeTerrainAfterSurfaceBuild = false;
            settings.autoInvokePreBuildWorkflow = false;
            settings.preBuildReloopUntilPass = false;
            settings.enforcePreBuildGate = false;
            settings.SaveToPrefs();
        }
    }
}
#endif
