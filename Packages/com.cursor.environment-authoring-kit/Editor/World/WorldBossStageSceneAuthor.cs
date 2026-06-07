#if UNITY_EDITOR
using System.IO;
using System.Linq;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.World
{
    public static class WorldBossStageSceneAuthor
    {
        const string ScenePath = "Assets/Scenes/BossStage_01.unity";

        [MenuItem("Window/Environment Kit/World/Create Boss Stage Scene")]
        public static void EnsureBossScene()
        {
            if (File.Exists(ScenePath))
            {
                Debug.Log($"[Boss] Scene already exists: {ScenePath}");
                EnsureInBuildSettings(ScenePath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var arena = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arena.name = "BossArena";
            arena.transform.localScale = new Vector3(28f, 0.5f, 28f);

            var spawn = new GameObject("BossSpawn");
            spawn.transform.position = new Vector3(0f, 1f, 6f);

            var playerSpawn = new GameObject("PlayerSpawn");
            playerSpawn.transform.position = new Vector3(0f, 1f, -8f);

            var controllerGo = new GameObject("BossFightController");
            controllerGo.AddComponent<WorldBossFightController>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureInBuildSettings(ScenePath);
            WorldRuntimeResourcesAuthor.EnsureBossInResources();
            Debug.Log($"[Boss] Created {ScenePath} and added to Build Settings.");
        }

        static void EnsureInBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == scenePath))
                return;

            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
