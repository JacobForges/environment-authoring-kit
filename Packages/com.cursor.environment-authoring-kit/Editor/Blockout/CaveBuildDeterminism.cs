#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Pin seed while debugging (UE5 PCG pattern); unpin for shipping variety.</summary>
    public static class CaveBuildDeterminism
    {
        const string PrefPinEnabled = "CaveBuild_PinSeedEnabled";
        const string PrefPinnedSeed = "CaveBuild_PinnedSeed";

        public static bool IsPinned => EditorPrefs.GetBool(PrefPinEnabled, false);

        public static int PinnedSeed => EditorPrefs.GetInt(PrefPinnedSeed, 424242);

        public static void SetPinnedSeed(int seed, bool enabled = true)
        {
            EditorPrefs.SetBool(PrefPinEnabled, enabled);
            EditorPrefs.SetInt(PrefPinnedSeed, seed);
            EditorPrefs.SetInt("CaveBuild_FixedSeed", seed);
            EditorPrefs.SetInt("CaveBuild_LastSeed", seed);
            Debug.Log($"[CaveBuild] Determinism: seed {(enabled ? "pinned" : "unpinned")} = {seed}.");
        }

        public static void Unpin()
        {
            EditorPrefs.SetBool(PrefPinEnabled, false);
            Debug.Log("[CaveBuild] Determinism: seed unpinned — builds may vary.");
        }

        public static int ResolveSeed(int proposedSeed)
        {
            if (!IsPinned)
                return proposedSeed;
            return PinnedSeed;
        }

        public static void ApplyToSettings(CaveBuildCursorSettings settings)
        {
            if (settings == null || !IsPinned)
                return;
            settings.LoadFromPrefs();
        }
    }
}
#endif
