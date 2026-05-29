#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
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
        const int CompileWaitMinutes = 45;

        public static void PrepareForCodeQl()
        {
            try
            {
                Debug.Log($"{LogPrefix} Refreshing assets…");
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                Debug.Log($"{LogPrefix} Requesting script compilation…");
                if (!EditorApplication.isCompiling)
                    CompilationPipeline.RequestScriptCompilation();

                WaitForEditorIdle($"{LogPrefix} compile");

                if (!TrySyncProjectFiles())
                {
                    Debug.LogWarning($"{LogPrefix} API sync failed; trying menu fallback.");
                    TrySyncViaMenu();
                    WaitForEditorIdle($"{LogPrefix} post-menu-sync");
                }

                if (!HasSolutionFile())
                {
                    Debug.LogError($"{LogPrefix} No .sln in project root after sync.");
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

        static void WaitForEditorIdle(string label)
        {
            var deadline = DateTime.UtcNow.AddMinutes(CompileWaitMinutes);
            while (DateTime.UtcNow < deadline)
            {
                if (!EditorApplication.isCompiling &&
                    !EditorApplication.isUpdating &&
                    !EditorApplication.isPlayingOrWillChangePlaymode)
                    return;

                Thread.Sleep(250);
            }

            throw new TimeoutException($"{label} timed out after {CompileWaitMinutes} minutes.");
        }

        static bool HasSolutionFile()
        {
            var root = Directory.GetParent(Application.dataPath)?.FullName;
            return !string.IsNullOrEmpty(root) &&
                   Directory.Exists(root) &&
                   Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly).Length > 0;
        }

        static void LogDiscoveredProjects()
        {
            var root = Directory.GetParent(Application.dataPath)?.FullName;
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
