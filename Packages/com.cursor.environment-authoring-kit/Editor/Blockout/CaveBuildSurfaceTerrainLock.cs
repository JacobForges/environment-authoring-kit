#if UNITY_EDITOR
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>After underground cave generation starts, block further surface terrain/prop/sculpt edits.</summary>
    public static class CaveBuildSurfaceTerrainLock
    {
        static bool _lockedForCave;
        static string _reason = string.Empty;

        public static bool IsLocked => _lockedForCave;

        public static void ResetForNewBuildSession()
        {
            _lockedForCave = false;
            _reason = string.Empty;
        }

        public static void LockForUndergroundCave(string reason)
        {
            if (_lockedForCave)
                return;

            _lockedForCave = true;
            _reason = reason ?? "underground cave generation";
            CaveBuildEditorLog.LogCave(
                $"[Surface] Locked — {_reason}. Terrain/prop/sculpt on surface is blocked until build ends.",
                forceUnityConsole: true);
        }

        public static bool TryBlockSurfaceMutation(string operation, bool logOnce = true)
        {
            if (!_lockedForCave)
                return false;

            if (logOnce)
                CaveBuildEditorLog.LogSurfaceWarning(
                    $"[Surface] Skipped '{operation}' — surface locked ({_reason}).",
                    forceUnityConsole: false);
            return true;
        }
    }
}
#endif
