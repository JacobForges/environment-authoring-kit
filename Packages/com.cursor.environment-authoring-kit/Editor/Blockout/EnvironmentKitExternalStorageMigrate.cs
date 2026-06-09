#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Moves heavy Environment Kit folders to /Volumes/*/EnvironmentKit-Hub and symlinks back into the Hub project.
    /// </summary>
    public static class EnvironmentKitExternalStorageMigrate
    {
        public const string MigrateScriptRel =
            "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/migrate-envkit-to-external.sh";

        public static bool IsGeneratedOnExternalVolume()
        {
            var generated = EnvironmentKitDataRoot.ResolveProjectGeneratedRoot();
            return EnvironmentKitDataRoot.IsPathOnExternalVolume(generated);
        }

        public static bool IsResearchCacheOnExternalVolume()
        {
            var cache = EnvironmentKitDataRoot.ResolveProjectResearchCacheRoot();
            return EnvironmentKitDataRoot.IsPathOnExternalVolume(cache);
        }

        public static string DescribeProjectHeavyStorage()
        {
            var gen = IsGeneratedOnExternalVolume() ? "external" : "Mac disk";
            var research = IsResearchCacheOnExternalVolume() ? "external" : "Mac disk";
            return $"Generated ({gen}) · ResearchCache ({research}) · captures ({EnvironmentKitDataRoot.DescribeStorage()})";
        }

        public static void RunMigration(bool quitUnityFirst = true)
        {
            if (LavaTubeCaveBuilder.IsBuildInProgress || CaveBuildHubSessionReconcile.IsPacedWorkActive())
            {
                EditorUtility.DisplayDialog(
                    "Environment Kit — storage migration",
                    "Stop the active build before moving Generated / ResearchCache to the external drive.",
                    "OK");
                return;
            }

            if (quitUnityFirst)
            {
                var proceed = EditorUtility.DisplayDialog(
                    "Move heavy data to external drive",
                    "This rsyncs Assets/EnvironmentKit/Generated and ResearchCache to your external " +
                    "EnvironmentKit-Hub folder, replaces them with symlinks, and moves Unity Library caches.\n\n" +
                    "Quit Unity when the script finishes, then reopen the project from the same Hub folder.",
                    "Run migration",
                    "Cancel");
                if (!proceed)
                    return;
            }

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var script = Path.Combine(hub, MigrateScriptRel);
            if (!File.Exists(script))
            {
                EditorUtility.DisplayDialog(
                    "Environment Kit — storage migration",
                    $"Migration script not found:\n{script}",
                    "OK");
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"\"{script}\"",
                    WorkingDirectory = hub,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = false,
                };
                psi.EnvironmentVariables["HUB_ROOT"] = hub;
                EnvironmentKitDataRoot.ApplyToProcessEnvironment(psi);

                using var proc = Process.Start(psi);
                var stdout = proc?.StandardOutput.ReadToEnd() ?? string.Empty;
                var stderr = proc?.StandardError.ReadToEnd() ?? string.Empty;
                proc?.WaitForExit(600_000);

                Debug.Log(
                    "[EnvironmentKit] External storage migration finished.\n" +
                    stdout +
                    (string.IsNullOrEmpty(stderr) ? string.Empty : "\n" + stderr));

                EditorUtility.DisplayDialog(
                    "Environment Kit — storage migration",
                    "Migration script finished.\n\n" +
                    DescribeProjectHeavyStorage() +
                    "\n\nQuit and reopen Unity so AssetDatabase picks up symlinks cleanly.",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(
                    "Environment Kit — storage migration failed",
                    ex.Message,
                    "OK");
            }
        }

        [MenuItem("Environment Kit/Storage/Move heavy data to external drive")]
        public static void RunMigrationMenu() => RunMigration();

        [MenuItem("Environment Kit/Storage/Show active data root")]
        public static void LogStorage()
        {
            Debug.Log(
                "[EnvironmentKit] " + DescribeProjectHeavyStorage() + "\n" +
                $"Generated: {EnvironmentKitDataRoot.ResolveProjectGeneratedRoot()}\n" +
                $"ResearchCache: {EnvironmentKitDataRoot.ResolveProjectResearchCacheRoot()}");
        }
    }
}
#endif
