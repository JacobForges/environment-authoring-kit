#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// During long paced builds, Unity Console shows errors only unless mirror is enabled on CaveBuildCursorSettings.
    /// Pipeline Console + Hub activity feed still receive all Info/Warn via <see cref="CaveBuildPipelineLog"/>.
    /// </summary>
    [InitializeOnLoad]
    static class CaveBuildUnityConsoleQuietMode
    {
        static LogType _savedFilter = LogType.Log;
        static bool _filterApplied;

        static CaveBuildUnityConsoleQuietMode() =>
            EditorApplication.update += OnEditorUpdate;

        static void OnEditorUpdate()
        {
            var quiet = ShouldQuietUnityConsole();
            if (quiet && !_filterApplied)
            {
                _savedFilter = Debug.unityLogger.filterLogType;
                Debug.unityLogger.filterLogType = LogType.Error;
                _filterApplied = true;
            }
            else if (!quiet && _filterApplied)
            {
                Debug.unityLogger.filterLogType = _savedFilter;
                _filterApplied = false;
            }
        }

        public static bool ShouldQuietUnityConsole() =>
            CaveBuildEditorResponsiveness.IsLongBuildActive && !CaveBuildEditorLog.MirrorPacedLogsToConsole;
    }
}
#endif
