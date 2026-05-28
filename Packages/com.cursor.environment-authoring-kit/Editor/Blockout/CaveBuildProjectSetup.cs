#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Makes a fresh clone buildable: scene anchors, starter module prefabs, npm, project root.
    /// Runs automatically before FullWorld preflight when you click Build Complete Cave.
    /// </summary>
    public static class CaveBuildProjectSetup
    {
        public const string StarterModulesFolder = "Assets/EnvironmentKit/StarterModules/Prefabs";
        public const string StarterScenePath = "Assets/EnvironmentKit/Scenes/StarterWorld.unity";

        const string PrefSetupVersion = "EnvironmentKit_CloneSetupVersion";
        const int SetupVersion = 1;

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Prepare Project For First Build (Clone Setup)", false, 4)]
        public static void MenuPrepareProject()
        {
            var summary = RunFullSetup(forceNpm: true);
            EditorUtility.DisplayDialog(
                "Environment Kit — Clone Setup",
                summary,
                "OK");
        }

        /// <summary>Call before preflight. Returns true if anchors exist after setup.</summary>
        public static bool EnsureCloneReady(ref SceneGroundInfo ground, out string log)
        {
            log = RunFullSetup(forceNpm: false);
            ground = SceneGroundResolver.Resolve();
            return ground.HasAnchor && GameObject.Find("PortalFive") != null;
        }

        public static string RunFullSetup(bool forceNpm)
        {
            var lines = new System.Text.StringBuilder();
            void Line(string s)
            {
                lines.AppendLine(s);
                Debug.Log("[EnvironmentKit.Setup] " + s);
            }

            Line("Clone setup — making project ready for Build Complete Cave…");

            EnsureHubRootIsThisProject();
            Line("Hub project root → " + CaveBuildCursorSettings.ResolveHubRoot());

            EnsureGroundTagExists();
            EnsureSceneAnchors(out var sceneCreated);
            if (sceneCreated)
                Line("Created/opened starter scene with Ground + PortalFive.");

            EnsureStarterModulePrefabs(out var modulesCreated);
            if (modulesCreated)
                Line("Created starter floor/wall/ceiling prefabs (replace with your licensed pack anytime).");

            LavaTubePrefabCatalog.Load(forceRefresh: true);
            var cat = LavaTubePrefabCatalog.Load();
            Line(LavaTubePrefabCatalog.LastDiscoverySummary);

            if (forceNpm || !IsTsxInstalled())
            {
                if (TryInstallCaveGrader(out var npmMsg))
                    Line(npmMsg);
                else
                    Line("WARN: " + npmMsg);
            }
            else
                Line("cave-grader tsx already installed.");

            CaveBuildSessionPreset.ApplyAutomaticForFullWorld();
            EditorPrefs.SetInt(PrefSetupVersion, SetupVersion);
            AssetDatabase.SaveAssets();

            if (!cat.IsValid)
                Line("WARN: Catalog still invalid — import 3D modular cave meshes for best results.");

            return lines.ToString().TrimEnd();
        }

        public static void EnsureHubRootIsThisProject()
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            var projectRoot = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(projectRoot))
                return;

            if (!string.Equals(settings.hubProjectRoot?.Trim().TrimEnd('/'), projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                settings.hubProjectRoot = projectRoot;
                settings.SaveToPrefs();
                EditorUtility.SetDirty(settings);
            }
        }

        public static bool IsTsxInstalled()
        {
            var toolsDir = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
                CaveBuildCursorAgentBridge.ToolsRelativePath);
            return File.Exists(Path.Combine(toolsDir, "node_modules", "tsx", "dist", "cli.mjs"));
        }

        public static bool TryInstallCaveGrader(out string message)
        {
            message = null;
            var toolsDir = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
                CaveBuildCursorAgentBridge.ToolsRelativePath);

            if (!Directory.Exists(toolsDir))
            {
                message = "Tools/cave-grader folder missing in package.";
                return false;
            }

            if (!CaveBuildCursorProcessResolver.TryResolveNode(out var node, out var nodeErr))
            {
                message = nodeErr ?? "Install Node 18+ (https://nodejs.org) then run setup again.";
                return false;
            }

            try
            {
                if (!TryResolveNpm(out var npmPath))
                {
                    message = "npm not found on PATH. Install Node 18+ from https://nodejs.org";
                    return false;
                }

                EditorUtility.DisplayProgressBar("Environment Kit", "Running npm install in Tools/cave-grader…", 0.4f);
                var psi = new ProcessStartInfo
                {
                    FileName = npmPath,
                    Arguments = "install",
                    WorkingDirectory = toolsDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                CaveBuildCursorProcessResolver.ApplyEnvironment(psi, Path.GetDirectoryName(Application.dataPath), null, null);

                using var p = Process.Start(psi);
                if (p == null)
                {
                    message = "Could not start npm install.";
                    return false;
                }

                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(120_000);
                EditorUtility.ClearProgressBar();

                if (p.ExitCode != 0)
                {
                    message = $"npm install failed (exit {p.ExitCode}).\n{stderr}\nRun manually: cd \"{toolsDir}\" && npm install";
                    return false;
                }

                message = "npm install completed in Tools/cave-grader.";
                if (!string.IsNullOrWhiteSpace(stdout))
                    Debug.Log("[EnvironmentKit.Setup] npm:\n" + stdout);
                return IsTsxInstalled();
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                message = "npm install error: " + ex.Message;
                return false;
            }
        }

        public static void EnsureGroundTagExists()
        {
            try
            {
                if (!string.IsNullOrEmpty(GameObject.FindGameObjectWithTag("Ground")?.name))
                    return;
            }
            catch
            {
                // tag missing
            }

            var asset = AssetDatabase.LoadMainAssetAtPath("ProjectSettings/TagManager.asset");
            if (asset == null)
                return;

            var so = new SerializedObject(asset);
            var tags = so.FindProperty("tags");
            for (var i = 0; i < tags.arraySize; i++)
            {
                if (tags.GetArrayElementAtIndex(i).stringValue == "Ground")
                    return;
            }

            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = "Ground";
            so.ApplyModifiedProperties();
        }

        public static void EnsureSceneAnchors(out bool createdNewScene)
        {
            createdNewScene = false;
            var ground = SceneGroundResolver.Resolve();
            var portal = GameObject.Find("PortalFive");

            if (ground.HasAnchor && portal != null)
                return;

            if (!ground.HasAnchor || portal == null)
            {
                if (!File.Exists(StarterScenePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(StarterScenePath) ?? "Assets/EnvironmentKit/Scenes");
                    var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                    createdNewScene = true;

                    var mainCam = Camera.main;
                    if (mainCam != null)
                        mainCam.transform.position = new Vector3(0f, 6f, -12f);

                    var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    plane.name = "Ground";
                    plane.transform.localScale = new Vector3(12f, 1f, 12f);
                    TrySetTag(plane, "Ground");

                    portal = new GameObject("PortalFive");
                    portal.transform.position = new Vector3(0f, 0.5f, 8f);

                    EditorSceneManager.SaveScene(scene, StarterScenePath);
                    LineSavedScene();
                }
                else
                {
                    EditorSceneManager.OpenScene(StarterScenePath, OpenSceneMode.Single);
                    ground = SceneGroundResolver.Resolve();
                    portal = GameObject.Find("PortalFive");

                    if (!ground.HasAnchor)
                    {
                        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                        plane.name = "Ground";
                        plane.transform.localScale = new Vector3(12f, 1f, 12f);
                        TrySetTag(plane, "Ground");
                    }

                    if (portal == null)
                    {
                        portal = new GameObject("PortalFive");
                        portal.transform.position = new Vector3(0f, 0.5f, 8f);
                    }

                    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                }
            }

            static void LineSavedScene() =>
                Debug.Log("[EnvironmentKit.Setup] Saved " + StarterScenePath);

            static void TrySetTag(GameObject go, string tag)
            {
                try
                {
                    go.tag = tag;
                }
                catch
                {
                    Debug.LogWarning("[EnvironmentKit.Setup] Could not assign Ground tag — add it in Project Settings > Tags.");
                }
            }
        }

        public static void EnsureStarterModulePrefabs(out bool created)
        {
            created = false;
            var catalog = LavaTubePrefabCatalog.Load(forceRefresh: true);
            if (catalog.IsValid)
                return;

            Directory.CreateDirectory(StarterModulesFolder);

            if (CreateAndSaveModulePrefab("EK_Starter_Floor", PrimitiveType.Cube, new Vector3(4f, 0.35f, 4f)))
                created = true;
            if (CreateAndSaveModulePrefab("EK_Starter_Wall", PrimitiveType.Cube, new Vector3(0.35f, 3.5f, 4f)))
                created = true;
            if (CreateAndSaveModulePrefab("EK_Starter_Ceiling", PrimitiveType.Cube, new Vector3(4f, 0.35f, 4f)))
                created = true;

            if (created)
            {
                AssetDatabase.Refresh();
                LavaTubePrefabCatalog.Load(forceRefresh: true);
            }
        }

        static bool CreateAndSaveModulePrefab(string prefabName, PrimitiveType primitive, Vector3 scale)
        {
            var path = $"{StarterModulesFolder}/{prefabName}.prefab";
            if (File.Exists(path))
                return false;

            var temp = GameObject.CreatePrimitive(primitive);
            temp.name = prefabName;
            temp.transform.localScale = scale;

            var prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
            UnityEngine.Object.DestroyImmediate(temp);
            return prefab != null;
        }

        static bool TryResolveNpm(out string npmPath)
        {
            npmPath = null;
            foreach (var dir in new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin" })
            {
                var candidate = Path.Combine(dir, "npm");
                if (File.Exists(candidate))
                {
                    npmPath = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
