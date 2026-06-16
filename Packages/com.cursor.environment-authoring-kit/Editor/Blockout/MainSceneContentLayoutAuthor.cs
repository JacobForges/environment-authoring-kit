#if UNITY_EDITOR
using System.IO;
using EnvironmentAuthoringKit.WorldContent;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Applies CaveBuildContentLayoutBrief.json to the active editor scene (intended: MainScene).</summary>
    public static class MainSceneContentLayoutAuthor
    {
        public const string BriefRelPath = "Assets/EnvironmentKit/Generated/CaveBuildContentLayoutBrief.json";
        const string MainScenePath = "Assets/MainScene.unity";

        [MenuItem("Window/Environment Kit/Apply Content Layout (MainScene)")]
        public static void ApplyFromMenu() => ApplyToActiveScene(promptOpenMainScene: true);

        [MenuItem("Window/Environment Kit/Open MainScene && Apply Content Layout")]
        public static void OpenMainSceneAndApply()
        {
            if (File.Exists(MainScenePath))
                EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            ApplyToActiveScene(promptOpenMainScene: false);
        }

        /// <summary>Pipeline auto-apply — opens MainScene when present, no dialogs.</summary>
        public static bool TryApplyMainSceneSilent(out string message)
        {
            message = null;
            if (File.Exists(MainScenePath))
                EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            var scene = SceneManager.GetActiveScene();
            if (!TryLoad(out var file, out var error))
            {
                message = error;
                return false;
            }

            var root = GetOrCreateRoot();
            var placed = Apply(file, root);
            EditorSceneManager.MarkSceneDirty(scene);
            message =
                $"Surface content: {placed.npcs} NPC(s), {placed.enemies} enemy route(s), {placed.props} prop(s) in \"{scene.name}\".";
            Debug.Log("[ContentLayout] " + message);
            return true;
        }

        static void ApplyToActiveScene(bool promptOpenMainScene)
        {
            var scene = SceneManager.GetActiveScene();
            if (promptOpenMainScene && !scene.name.Contains("Main") && File.Exists(MainScenePath))
            {
                if (EditorUtility.DisplayDialog(
                        "Content Layout",
                        $"Active scene is \"{scene.name}\", not MainScene.\n\nOpen MainScene and apply there?",
                        "Open MainScene",
                        "Apply to current scene"))
                {
                    EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
                    scene = SceneManager.GetActiveScene();
                }
            }

            if (!TryLoad(out var file, out var error))
            {
                EditorUtility.DisplayDialog("Content Layout", error, "OK");
                return;
            }

            var root = GetOrCreateRoot();
            var placed = Apply(file, root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorUtility.DisplayDialog(
                "Content Layout",
                $"Applied to scene \"{scene.name}\":\n" +
                $"{placed.npcs} NPC(s), {placed.enemies} enemy route(s), {placed.props} prop(s).\n\n" +
                $"Brief: {file.contentPlan?.npcs?.Length ?? 0} NPC defs, " +
                $"{file.contentPlan?.enemies?.Length ?? 0} spawners, " +
                $"{file.contentPlan?.props?.Length ?? 0} props.\n\n" +
                "Save the scene (Ctrl/Cmd+S). Enter Play Mode to wire dialog, shops, spawners, and smoke test.\n" +
                "Or use Game → Apply Surface Content Layout (MainScene).",
                "OK");
        }

        public static bool TryLoad(out ContentLayoutBriefFile file, out string error)
        {
            file = null;
            error = null;
            var path = Path.Combine(Application.dataPath, "EnvironmentKit/Generated/CaveBuildContentLayoutBrief.json");

            if (!File.Exists(path))
            {
                error = $"Brief not found.\nFinalize the Content Layout tab in AI Build Wizard first.\n\nExpected:\n{BriefRelPath}";
                return false;
            }

            var json = File.ReadAllText(path);
            file = JsonUtility.FromJson<ContentLayoutBriefFile>(json);
            if (file?.contentPlan == null)
            {
                error = "Brief JSON missing contentPlan.";
                return false;
            }

            return true;
        }

        static (int npcs, int enemies, int props) Apply(ContentLayoutBriefFile file, Transform root)
        {
            var stats = ContentLayoutSceneGraph.Build(root, file, clearExisting: true);
            return (stats.Npcs, stats.Enemies, stats.Props);
        }

        static Transform GetOrCreateRoot()
        {
            var root = ContentLayoutSceneGraph.EnsureRoot();
            if (root.GetComponent<Transform>() != null && root.gameObject.scene.IsValid())
                Undo.RegisterCreatedObjectUndo(root.gameObject, "Content layout root");
            return root;
        }
    }
}
#endif
