#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor
{
    /// <summary>
    /// Headless prep for GitHub CodeQL (self-hosted runner with Unity installed).
    /// Unity -batchmode -projectPath &lt;Hub&gt; -executeMethod EnvironmentAuthoringKit.Editor.CodeQlUnityBootstrap.PrepareForCodeQl -quit
    /// </summary>
    public static class CodeQlUnityBootstrap
    {
        const string LogPrefix = "[CodeQL]";

        public static void PrepareForCodeQl()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;

            Debug.Log($"{LogPrefix} Refreshing assets…");
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            if (!EditorApplication.isCompiling)
            {
                Debug.Log($"{LogPrefix} Requesting script compilation…");
                CompilationPipeline.RequestScriptCompilation();
            }
        }

        static void OnEditorUpdate()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            EditorApplication.update -= OnEditorUpdate;

            try
            {
                if (!TrySyncProjectFiles())
                    Debug.LogWarning($"{LogPrefix} Project sync API unavailable; using existing Hub.sln if present.");

                LogDiscoveredProjects();
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogPrefix} Prepare failed: {ex}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"{LogPrefix} Ready for msbuild/dotnet.");
            EditorApplication.Exit(0);
        }

        static void LogDiscoveredProjects()
        {
            var root = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return;

            foreach (var sln in Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly))
                Debug.Log($"{LogPrefix} Solution: {sln}");

            var kitEditor = Path.Combine(root, "EnvironmentAuthoringKit.Editor.csproj");
            if (File.Exists(kitEditor))
                Debug.Log($"{LogPrefix} Package editor project: {kitEditor}");
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
                    Debug.LogWarning($"{LogPrefix} ProjectGeneration.Sync failed: {ex.Message}");
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
    }
}
#endif
