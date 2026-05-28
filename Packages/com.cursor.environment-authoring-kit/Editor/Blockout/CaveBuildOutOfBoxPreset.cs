#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Default for fresh clones: complete the 120-step procedural pipeline without any LLM/API.
    /// AI grading is optional (Hub → provider + keys, or local Ollama).
    /// </summary>
    public static class CaveBuildOutOfBoxPreset
    {
        public const string ProfileId = "out_of_box";

        public static void Apply(bool savePrefs = true, bool log = true)
        {
            CaveBuildOfflineNoApiPreset.Apply(savePrefs: false, log: false);

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            settings.usePhasedCaveBuild = true;
            settings.useIncrementalLadder = false;
            settings.enableEnhancementPhases = true;
            settings.demSupersampleTargetDim = 96;
            CaveBuildEditorResponsiveness.ApplyForActiveBuild(settings);

            if (savePrefs)
                settings.SaveToPrefs();

            CaveBuildEnhancementRunner.ExportCatalogJson();
            if (log)
            {
                Debug.Log(
                    "[CaveBuild] Out-of-box preset — FullWorld runs procedurally to 120/120 with no API keys. " +
                    "Optional: Hub → Active provider (Ollama/Gemini/etc.) + keys for AI grading passes.");
            }
        }
    }
}
#endif
