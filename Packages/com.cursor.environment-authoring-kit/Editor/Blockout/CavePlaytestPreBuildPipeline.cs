#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Sixty polish phases (research + terrain + cave + combat + QA) before playtest bot.
    /// </summary>
    public static class CavePlaytestPreBuildPipeline
    {
        public static bool Run(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out string summary,
            bool runFullPolishPhases = true)
        {
            summary = string.Empty;
            if (ground?.Terrain == null)
            {
                summary = "Pre-play skipped — no terrain.";
                return false;
            }

            if (!runFullPolishPhases)
            {
                summary = "Pre-play polish skipped (lightweight path).";
                return true;
            }

            var ok = PlaytestPolishPhaseRunner.RunAll(caveRoot, ground, request, out var polishMsg);
            summary = polishMsg;
            if (!ok)
                Debug.LogWarning("[CaveBuild] Pre-play polish pipeline issues — " + summary);
            else
                Debug.Log("[CaveBuild] Pre-play polish pipeline OK — " + summary);
            return ok;
        }
    }
}
#endif
