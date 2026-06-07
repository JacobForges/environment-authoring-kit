#if UNITY_EDITOR
using System;
using System.IO;
using EnvironmentAuthoringKit.Editor.Blockout;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor
{
    /// <summary>
    /// Prevents Unity "Scene(s) Have Been Modified" prompts during kit builds and scene switches.
    /// </summary>
    public static class EnvironmentKitSceneSafeguards
    {
        public const string AutoSaveBeforeBuildPrefKey = "EnvironmentKit_AutoSaveScenesBeforeBuild";
        public const string NeverSwitchSceneDuringBuildPrefKey = "EnvironmentKit_NeverSwitchSceneDuringBuild";

        public static bool AutoSaveBeforeBuildEnabled
        {
            get => EditorPrefs.GetBool(AutoSaveBeforeBuildPrefKey, true);
            set => EditorPrefs.SetBool(AutoSaveBeforeBuildPrefKey, value);
        }

        public static bool NeverSwitchSceneDuringBuildEnabled
        {
            get => EditorPrefs.GetBool(NeverSwitchSceneDuringBuildPrefKey, true);
            set => EditorPrefs.SetBool(NeverSwitchSceneDuringBuildPrefKey, value);
        }

        public static bool IsKitBuildSessionActive =>
            LavaTubeCaveBuilder.IsBuildInProgress ||
            CaveBuildStartupCoordinator.IsActive ||
            LavaTubeCaveBuildPipeline.IsPhasedBuildActive;

        /// <summary>Call at the start of Hub / menu builds — saves dirty scenes without prompting.</summary>
        public static void GuardActiveSceneForBuild()
        {
            if (!AutoSaveBeforeBuildEnabled)
            {
                if (HasAnyDirtyScenes())
                {
                    Debug.LogWarning(
                        "[Environment Kit] Scene has unsaved changes. Save manually or enable " +
                        "'Auto-save scenes before build' in Hub → Settings to avoid save dialogs.");
                }

                return;
            }

            var saved = SaveAllDirtyScenesSilently();
            if (saved > 0)
            {
                Debug.Log(
                    $"[Environment Kit] Auto-saved {saved} modified scene(s) before build (no save prompt).");
            }
        }

        public static int SaveAllDirtyScenesSilently()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return 0;

            var saved = 0;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isDirty)
                    continue;

                if (string.IsNullOrEmpty(scene.path))
                {
                    Debug.LogWarning(
                        $"[Environment Kit] Scene '{scene.name}' is dirty but unsaved — save it to disk once to enable auto-save.");
                    continue;
                }

                if (EditorSceneManager.SaveScene(scene))
                    saved++;
            }

            return saved;
        }

        public static bool SaveActiveSceneSilentlyIfPossible()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return false;

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isDirty || string.IsNullOrEmpty(scene.path))
                return false;

            return EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// Checkpoint snapshot: active scene (even if Unity did not mark dirty) + all dirty scenes + terrain/assets.
        /// </summary>
        public static bool SaveBuildCheckpointSnapshot(string context, out string detail)
        {
            detail = string.Empty;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                detail = "skipped (play mode)";
                return false;
            }

            var savedScenes = 0;
            var active = SceneManager.GetActiveScene();
            if (active.IsValid() && !string.IsNullOrEmpty(active.path))
            {
                if (EditorSceneManager.SaveScene(active))
                    savedScenes++;
            }

            savedScenes += SaveAllDirtyScenesSilently();
            AssetDatabase.SaveAssets();

            detail = savedScenes > 0
                ? $"saved {savedScenes} scene(s) + assets"
                : "flushed assets (active scene had no path or save failed)";

            if (!string.IsNullOrEmpty(context))
            {
                Debug.Log(
                    savedScenes > 0
                        ? $"[Environment Kit] Checkpoint scene save — {detail} ({context})"
                        : $"[Environment Kit] Checkpoint scene save — {detail} ({context})");
            }

            return savedScenes > 0;
        }

        public static bool HasAnyDirtyScenes()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                    return true;
            }

            return false;
        }

        /// <summary>Open scene without Unity save dialog when auto-save is enabled.</summary>
        public static bool TryOpenSceneByPath(
            string scenePath,
            bool allowUserPrompt = false,
            OpenSceneMode mode = OpenSceneMode.Single)
        {
            if (string.IsNullOrEmpty(scenePath))
                return false;

            scenePath = scenePath.Replace('\\', '/');
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                return false;

            var active = SceneManager.GetActiveScene();
            if (active.IsValid() && string.Equals(active.path, scenePath, StringComparison.OrdinalIgnoreCase))
                return true;

            if (NeverSwitchSceneDuringBuildEnabled && IsKitBuildSessionActive)
            {
                Debug.LogWarning(
                    $"[Environment Kit] Build in progress — staying in '{active.name}' (not switching to {scenePath}).");
                return false;
            }

            if (allowUserPrompt && HasAnyDirtyScenes())
            {
                var choice = EditorUtility.DisplayDialogComplex(
                    "Save scene changes?",
                    "Save modified scenes before switching?",
                    "Save",
                    "Cancel",
                    "Don't Save");
                if (choice == 1)
                    return false;
                if (choice == 0)
                    SaveAllDirtyScenesSilently();
            }
            else if (AutoSaveBeforeBuildEnabled)
            {
                SaveAllDirtyScenesSilently();
            }
            else if (HasAnyDirtyScenes())
            {
                Debug.LogWarning(
                    $"[Environment Kit] Cancelled switch to {scenePath} — save the active scene first or enable auto-save in Hub → Settings.");
                return false;
            }

            EditorSceneManager.OpenScene(scenePath, mode);
            return true;
        }

        /// <summary>Legacy AssetDatabase.OpenAsset(scene) replacement — never surprises with a modal.</summary>
        public static bool TryOpenSceneAsset(SceneAsset sceneAsset, bool allowUserPrompt = false)
        {
            if (sceneAsset == null)
                return false;

            var path = AssetDatabase.GetAssetPath(sceneAsset);
            return TryOpenSceneByPath(path, allowUserPrompt);
        }
    }
}
#endif
