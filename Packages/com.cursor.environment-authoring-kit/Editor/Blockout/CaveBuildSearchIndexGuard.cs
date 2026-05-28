#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Unity Quick Search expects Library/Search on disk; races during heavy refreshes can delete or
    /// lock files mid-write. Ensures the folder exists before any batched AssetDatabase.Refresh.
    /// </summary>
    [InitializeOnLoad]
    static class CaveBuildSearchIndexGuard
    {
        static CaveBuildSearchIndexGuard() => EnsureSearchFolder();

        public static string SearchFolderPath =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, "Library", "Search");

        public static void EnsureSearchFolder()
        {
            try
            {
                if (!Directory.Exists(SearchFolderPath))
                    Directory.CreateDirectory(SearchFolderPath);
            }
            catch (IOException ex)
            {
                Debug.LogWarning("[CaveBuild] Library/Search not writable: " + ex.Message);
            }
        }

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Ensure Library/Search Folder")]
        public static void EnsureSearchFolderMenu()
        {
            EnsureSearchFolder();
            EditorUtility.DisplayDialog(
                "Unity Search folder",
                $"Ensured:\n{SearchFolderPath}\n\nIf Search errors continue: quit Unity, delete Library/Search, reopen the project.",
                "OK");
        }
    }
}
#endif
