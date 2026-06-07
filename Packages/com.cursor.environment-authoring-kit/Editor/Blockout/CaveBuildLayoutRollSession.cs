#if UNITY_EDITOR
using UnityEditor;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Keeps the current layout roll across agent auto-rebuilds so overnight Cursor loops do not churn seeds.
    /// </summary>
    public static class CaveBuildLayoutRollSession
    {
        const string PrefLastSeed = "CaveBuild_LastSeed";

        static CaveLayoutRoll _lastRoll;
        static bool _preserveNextBuild;

        public static int LastRecordedSeed =>
            _lastRoll?.Seed ?? EditorPrefs.GetInt(PrefLastSeed, 0);

        public static void Record(CaveLayoutRoll roll)
        {
            if (roll == null)
                return;

            _lastRoll = roll;
            EditorPrefs.SetInt(PrefLastSeed, roll.Seed);
        }

        public static void RequestPreserveNextBuild() => _preserveNextBuild = true;

        public static void ClearPreserveRequest() => _preserveNextBuild = false;

        public static bool TryConsumePreservedRoll(out CaveLayoutRoll roll)
        {
            roll = null;
            if (!_preserveNextBuild || _lastRoll == null)
                return false;

            _preserveNextBuild = false;
            roll = _lastRoll;
            return true;
        }

        public static void PinLastSeedForDebugging()
        {
            var seed = LastRecordedSeed;
            if (seed <= 0)
                return;

            CaveBuildDeterminism.SetPinnedSeed(seed, enabled: true);
            EditorPrefs.SetBool("CaveBuild_RandomizeEachTime", false);
        }
    }
}
#endif
