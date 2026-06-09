#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Prepare a brand-new Hub build — fresh seed and optional checkpoint wipe.</summary>
    public static class CaveBuildFreshBuildSession
    {
        public static void PrepareForNewBuild(bool randomizeSeed, bool speedDemo)
        {
            if (randomizeSeed)
            {
                CaveBuildSeedDefaults.ForceNewSeedBeforeLayoutRoll();
                CaveBuildDeterminism.Unpin();
                CaveBuildLayoutRollSession.ClearPreserveRequest();
                ClearIncrementalArtifacts();
            }

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();

            if (speedDemo)
                CaveBuildSpeedDemoPolicy.ApplySessionSettings(settings);
            else if (randomizeSeed)
                CaveBuildSeedDefaults.EnsureVarietyForFreshGeneration();

            settings.SaveToPrefs();
        }

        public static void ClearIncrementalArtifacts()
        {
            CaveBuildFullWorldGridCheckpoint.Clear("fresh build");
            CaveBuildPacedStepPersistence.ClearCheckpoint();
            CaveBuildPlaytestSessionSnapshot.Clear();
            CaveBuildConceptSession.ClearLock();
            CaveBuildPauseController.ClearStalePauseIfOrphaned();
        }

        public static string DescribeSeedMode()
        {
            if (CaveBuildDeterminism.IsPinned)
                return $"pinned seed {CaveBuildDeterminism.PinnedSeed}";

            if (FullWorldConceptLayoutCatalog.RandomOnBuildEnabled)
                return "random seed each new build";

            var fixedSeed = EditorPrefs.GetInt("CaveBuild_FixedSeed", 0);
            if (fixedSeed > 0)
                return $"fixed seed {fixedSeed}";

            var last = EditorPrefs.GetInt("CaveBuild_LastSeed", 0);
            return last > 0 ? $"reusing last seed {last}" : "random seed";
        }
    }
}
#endif
