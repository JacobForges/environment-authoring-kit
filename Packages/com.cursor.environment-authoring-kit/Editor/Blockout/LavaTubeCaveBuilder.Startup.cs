#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public static partial class LavaTubeCaveBuilder
    {
        internal static void StartupTryOpenMainScene() => TryOpenMainScene();

        internal static bool StartupEnsurePresets()
        {
            if (SamplePresetsExist())
                return true;
            SamplePresetsCreator.CreateAll();
            return SamplePresetsExist();
        }

        internal static void StartupClearInvalidGround() => ClearInvalidStoredGround();

        internal static Transform StartupLoadUserGround() => LoadUserGround();

        internal static CaveLayoutRoll StartupCreateLayoutRoll()
        {
            var roll = _unifiedBuildRoll ?? CreateLayoutRoll();
            _unifiedBuildRoll = null;
            return roll;
        }

        internal static void StartupLogLayoutRoll(CaveLayoutRoll roll) => LogLayoutRoll(roll);

        internal static bool StartupQueueCaveGeometry(
            string sceneName,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            bool hideLegacyBlockout,
            bool skipDialogs,
            bool layoutPrototype,
            CaveLayoutRoll roll) =>
            QueueOrRunCaveGeometry(
                sceneName,
                ground,
                request,
                hideLegacyBlockout,
                skipDialogs,
                layoutPrototype,
                roll);
    }
}
#endif
