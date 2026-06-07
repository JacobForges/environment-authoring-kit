#if UNITY_EDITOR
using UnityEditor;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>First-build / clone defaults: unpinned, fresh layout roll each FullWorld run.</summary>
    public static class CaveBuildSeedDefaults
    {
        const string PrefRandomize = "CaveBuild_RandomizeEachTime";

        public static void EnsureVarietyForFreshGeneration()
        {
            EditorPrefs.SetBool(PrefRandomize, true);
            EditorPrefs.DeleteKey("CaveBuild_FixedSeed");
            CaveBuildDeterminism.Unpin();
            CaveBuildLayoutRollSession.ClearPreserveRequest();
        }

        public static void ForceNewSeedBeforeLayoutRoll()
        {
            EnsureVarietyForFreshGeneration();
            EditorPrefs.DeleteKey("CaveBuild_LastSeed");
        }
    }
}
#endif
