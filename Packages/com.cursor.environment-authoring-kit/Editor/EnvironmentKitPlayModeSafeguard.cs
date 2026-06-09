#if UNITY_EDITOR
using System.IO;
using EnvironmentAuthoringKit.Editor.Blockout;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor
{
    /// <summary>
    /// Play Mode must not wipe editor-built worlds. Disables scene reload during kit sessions,
    /// snapshots to disk before/after Play, and keeps player/inventory saves intact.
    /// </summary>
    [InitializeOnLoad]
    static class EnvironmentKitPlayModeSafeguard
    {
        const string PrefBlockDuringBuild = "EnvironmentKit_BlockPlayDuringBuild";

        static bool _savedPlayModeOptionsEnabled;
        static EnterPlayModeOptions _savedPlayModeOptions;
        static bool _appliedPreserveOptions;
        static int _terrainCountBeforePlay;

        static EnvironmentKitPlayModeSafeguard()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += () => PruneCorruptPlayModeBackups(log: false);
        }

        public static bool BlockPlayDuringBuild
        {
            get => EditorPrefs.GetBool(PrefBlockDuringBuild, true);
            set => EditorPrefs.SetBool(PrefBlockDuringBuild, value);
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                if (BlockPlayDuringBuild && EnvironmentKitSceneSafeguards.IsKitBuildSessionActive
                    && !CaveBuildPauseController.PlaytestBreakActive
                    && !CaveBuildPostBuildFinalizeGate.IsAwaitingPlayMode
                    && !CaveBuildPostBuildFinalizeGate.IsRecordingPlaythrough)
                {
                    Debug.LogWarning(
                        "[Environment Kit] Play blocked — build pipeline is still running. " +
                        "Use Hub → Playtest break (freezes queue, keeps recap recording), then Play.");
                    EditorApplication.isPlaying = false;
                    return;
                }

                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    Debug.LogWarning(
                        "[Environment Kit] Play blocked — Unity is still compiling or importing assets.");
                    EditorApplication.isPlaying = false;
                    return;
                }

                if (ShouldPreserveEditorWorld())
                {
                    _terrainCountBeforePlay = Object.FindObjectsByType<Terrain>().Length;
                    EnvironmentKitSceneSafeguards.EnsureActiveSceneHasSavePath();
                    EnvironmentKitSceneSafeguards.SaveBuildCheckpointSnapshot(
                        "before Play Mode",
                        out var detail,
                        flushAssets: true);
                    Debug.Log("[Environment Kit] Pre-Play snapshot — " + detail);
                    ApplyPreservePlayModeOptions();
                }

                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode)
            {
                if (ShouldPreserveEditorWorld())
                {
                    RestorePlayModeOptions();
                    EnvironmentKitSceneSafeguards.SaveBuildCheckpointSnapshot(
                        "after Play Mode",
                        out var detail,
                        flushAssets: true);
                    VerifyEditorWorldIntact();
                    Debug.Log("[Environment Kit] Post-Play snapshot — " + detail);
                }

                EditorApplication.delayCall += () => PruneCorruptPlayModeBackups(log: true);
            }
        }

        static bool ShouldPreserveEditorWorld() =>
            EnvironmentKitSceneSafeguards.PreserveWorldOnPlayEnabled;

        static void ApplyPreservePlayModeOptions()
        {
            if (_appliedPreserveOptions)
                return;

            _savedPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _savedPlayModeOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableSceneReload;
            _appliedPreserveOptions = true;
        }

        static void RestorePlayModeOptions()
        {
            if (!_appliedPreserveOptions)
                return;

            EditorSettings.enterPlayModeOptions = _savedPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = _savedPlayModeOptionsEnabled;
            _appliedPreserveOptions = false;
        }

        static void VerifyEditorWorldIntact()
        {
            var terrainNow = Object.FindObjectsByType<Terrain>().Length;
            if (_terrainCountBeforePlay <= 0 || terrainNow >= _terrainCountBeforePlay)
                return;

            Debug.LogError(
                $"[Environment Kit] Play Mode may have wiped editor terrain " +
                $"({_terrainCountBeforePlay} → {terrainNow}). " +
                "Use Hub → Resume FullWorld grid from checkpoint, or Cave Build → Diagnostics → Resume From Checkpoint.");

            if (CaveBuildFullWorldGridCheckpoint.HasResumable)
            {
                Debug.LogWarning(
                    "[Environment Kit] Resumable grid checkpoint found — Hub Build tab offers Resume.");
            }
        }

        /// <summary>Remove empty play-mode backup files only.</summary>
        public static void PruneCorruptPlayModeBackups(bool log)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                return;

            var backupDir = Path.Combine(projectRoot, "Temp", "__Backupscenes");
            if (!Directory.Exists(backupDir))
                return;

            foreach (var path in Directory.GetFiles(backupDir, "*.backup"))
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length <= 0)
                    {
                        File.Delete(path);
                        if (log)
                        {
                            Debug.Log(
                                "[Environment Kit] Removed empty play-mode backup: " + Path.GetFileName(path));
                        }
                    }
                }
                catch (IOException ex)
                {
                    Debug.LogWarning("[Environment Kit] Could not prune play backup: " + ex.Message);
                }
            }
        }
    }
}
#endif
