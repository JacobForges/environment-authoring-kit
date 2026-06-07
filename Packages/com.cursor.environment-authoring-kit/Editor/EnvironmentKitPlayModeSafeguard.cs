#if UNITY_EDITOR
using System.IO;
using EnvironmentAuthoringKit.Editor.Blockout;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor
{
    /// <summary>
    /// Blocks Play during active kit builds / compiles and prunes corrupt play-mode scene backups.
    /// Never saves scenes or deletes backups during ExitingEditMode — that corrupts Unity's backup writer.
    /// </summary>
    [InitializeOnLoad]
    static class EnvironmentKitPlayModeSafeguard
    {
        const string PrefBlockDuringBuild = "EnvironmentKit_BlockPlayDuringBuild";
        const long CorruptBackupSizeBytes = 150_000_000;

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
                if (BlockPlayDuringBuild && EnvironmentKitSceneSafeguards.IsKitBuildSessionActive)
                {
                    Debug.LogWarning(
                        "[Environment Kit] Play blocked — build pipeline is still running. " +
                        "Wait for Hub to go idle (or Pause), then try Play again.");
                    EditorApplication.isPlaying = false;
                    return;
                }

                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    Debug.LogWarning(
                        "[Environment Kit] Play blocked — Unity is still compiling or importing assets.");
                    EditorApplication.isPlaying = false;
                }

                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += () => PruneCorruptPlayModeBackups(log: true);
        }

        /// <summary>Remove oversized/partial play-mode backups (Unity fatals if it loads them).</summary>
        public static void PruneCorruptPlayModeBackups(bool log)
        {
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
                    if (info.Length <= 0 || info.Length >= CorruptBackupSizeBytes)
                    {
                        File.Delete(path);
                        if (log)
                        {
                            Debug.Log(
                                "[Environment Kit] Removed stale play-mode backup: " +
                                $"{Path.GetFileName(path)} ({info.Length / (1024 * 1024)} MB)");
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
