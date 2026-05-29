#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor
{
    /// <summary>
    /// Headless prep for GitHub CodeQL (self-hosted runner with Unity installed).
    /// Unity -batchmode -projectPath &lt;repo&gt; -executeMethod EnvironmentAuthoringKit.Editor.CodeQlUnityBootstrap.PrepareForCodeQl
    /// Do not pass -quit on the CLI; this class calls EditorApplication.Exit when done.
    /// </summary>
    public static class CodeQlUnityBootstrap
    {
        const string LogPrefix = "[CodeQL]";
        const int InitialLoadWaitMinutes = 90;
        const int LightRefreshWaitMinutes = 15;

        public static void PrepareForCodeQl()
        {
            try
            {
                // Batchmode already runs Initial Refresh + first script compile before -executeMethod.
                // Do not call RequestScriptCompilation again — full Hub clones can hang for 45+ minutes.
                Debug.Log($"{LogPrefix} Waiting for editor idle after initial load…");
                WaitForEditorIdle($"{LogPrefix} initial load", InitialLoadWaitMinutes);

                SyncProjectFiles();

                if (!HasCodeQlBuildArtifacts())
                {
                    Debug.LogWarning($"{LogPrefix} No .sln / kit .csproj yet; light refresh (no forced recompile).");
                    AssetDatabase.Refresh();
                    WaitForEditorIdle($"{LogPrefix} light refresh", LightRefreshWaitMinutes);
                    SyncProjectFiles();
                }

                if (!HasCodeQlBuildArtifacts())
                {
                    Debug.LogError($"{LogPrefix} No .sln or EnvironmentAuthoringKit.*.csproj after sync.");
                    EditorApplication.Exit(1);
                    return;
                }

                LogDiscoveredProjects();
                Debug.Log($"{LogPrefix} Ready for dotnet/msbuild.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogPrefix} Prepare failed: {ex}");
                EditorApplication.Exit(1);
            }
        }

        static void SyncProjectFiles()
        {
            if (TrySyncProjectFiles())
                return;

            Debug.LogWarning($"{LogPrefix} API sync failed; trying menu fallback.");
            TrySyncViaMenu();
            WaitForEditorIdle($"{LogPrefix} post-menu-sync", LightRefreshWaitMinutes);
        }

        static void WaitForEditorIdle(string label, int maxMinutes)
        {
            var deadline = DateTime.UtcNow.AddMinutes(maxMinutes);
            while (DateTime.UtcNow < deadline)
            {
                if (!EditorApplication.isCompiling &&
                    !EditorApplication.isUpdating &&
                    !EditorApplication.isPlayingOrWillChangePlaymode)
                    return;

                Thread.Sleep(250);
            }

            throw new TimeoutException($"{label} timed out after {maxMinutes} minutes.");
        }

        static bool HasCodeQlBuildArtifacts()
        {
            return HasSolutionFile() || HasKitProjectFile();
        }

        static bool HasSolutionFile()
        {
            var root = ProjectRoot();
            return !string.IsNullOrEmpty(root) &&
                   Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly).Length > 0;
        }

        static bool HasKitProjectFile()
        {
            var root = ProjectRoot();
            return !string.IsNullOrEmpty(root) &&
                   File.Exists(Path.Combine(root, "EnvironmentAuthoringKit.Editor.csproj"));
        }

        static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath)?.FullName;
        }

        static void LogDiscoveredProjects()
        {
            var root = ProjectRoot();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return;

            foreach (var sln in Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly))
                Debug.Log($"{LogPrefix} Solution: {sln}");

            foreach (var name in new[] { "EnvironmentAuthoringKit.Editor.csproj", "EnvironmentAuthoringKit.Runtime.csproj" })
            {
                var path = Path.Combine(root, name);
                if (File.Exists(path))
                    Debug.Log($"{LogPrefix} Project: {path}");
            }
        }

        static bool TrySyncProjectFiles()
        {
            var genType = Type.GetType("UnityEditor.ProjectGeneration.ProjectGeneration, UnityEditor");
            if (genType != null)
            {
                try
                {
                    var instance = Activator.CreateInstance(genType);
                    var sync = genType.GetMethod("Sync", BindingFlags.Instance | BindingFlags.Public);
                    if (sync != null)
                    {
                        sync.Invoke(instance, null);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{LogPrefix} ProjectGeneration.Sync: {ex.Message}");
                }
            }

            var syncVs = Type.GetType("UnityEditor.SyncVS.SyncVS, UnityEditor");
            var syncSolution = syncVs?.GetMethod("SyncSolution", BindingFlags.Public | BindingFlags.Static);
            if (syncSolution != null)
            {
                syncSolution.Invoke(null, null);
                return true;
            }

            return false;
        }

        static void TrySyncViaMenu()
        {
            try
            {
                EditorApplication.ExecuteMenuItem("Assets/Open C# Project");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} Menu sync: {ex.Message}");
            }
        }
    }
}
#endif
