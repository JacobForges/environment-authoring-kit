#if UNITY_EDITOR
using System;
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
            EnsureMinimalSceneDefaults(out _);
            ground = SceneGroundResolver.EnforceFullWorldGround(SceneGroundResolver.ResolveForFullWorld());
            return ground.HasAnchor && GameObject.Find("PortalFive") != null;
        }

        /// <summary>Fast path: Ground tag, portal, grid hosts — no npm. Safe to call every build.</summary>
        public static void EnsureMinimalSceneDefaults(out bool createdObjects)
        {
            createdObjects = false;
            if (!SceneManager.GetActiveScene().IsValid())
                return;

            EnsureGroundTagExists();
            EnsureSceneAnchorsInActiveScene(out var anchorsCreated);
            if (anchorsCreated)
                createdObjects = true;

            EnsureDefaultRenderSetup();

            var ground = SceneGroundResolver.ResolveForFullWorld();
            if (SurfaceTerrainTileExpansion.EnsureDefaultGridAnchors(ground))
                createdObjects = true;

            EnvironmentSceneUtility.GetOrCreateRoot(ground);

            if (ground.HasAnchor)
                SceneGroundResolver.SaveAssignedGround(ground.Anchor);

            CaveBuildPortalSettings.PromptIfNeeded(showDialog: false);

            if (createdObjects)
            {
                var scene = SceneManager.GetActiveScene();
                if (scene.IsValid())
                    EditorSceneManager.MarkSceneDirty(scene);
                EnvironmentKitSceneSafeguards.SaveActiveSceneSilentlyIfPossible();
            }
        }

        /// <summary>Ensure empty active scene has Ground + portal + grid before build.</summary>
        public static bool EnsureEmptySceneDefaults(ref SceneGroundInfo ground, out string log)
        {
            var lines = new System.Text.StringBuilder();
            EnsureMinimalSceneDefaults(out var created);
            if (created)
                lines.AppendLine("Created default Ground, PortalFive, and grid anchors in the active scene.");

            ground = SceneGroundResolver.EnforceFullWorldGround(SceneGroundResolver.ResolveForFullWorld());
            log = lines.ToString().TrimEnd();
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
            EnsureSceneAnchorsInActiveScene(out var sceneCreated);
            if (sceneCreated)
                Line("Ensured Ground + PortalFive in active scene.");

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

            CaveBuildSeedDefaults.EnsureVarietyForFreshGeneration();
            CaveBuildSessionPreset.ApplyAutomaticForFullWorld();
            if (sceneCreated)
                SaveStarterSceneTemplateIfMissing();
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
                var psi = new System.Diagnostics.ProcessStartInfo
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

                using var p = System.Diagnostics.Process.Start(psi);
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

        public static void EnsureSceneAnchorsInActiveScene(out bool createdObjects)
        {
            createdObjects = false;
            if (!SceneManager.GetActiveScene().IsValid())
                return;

            var ground = SceneGroundResolver.Resolve();
            var portal = GameObject.Find("PortalFive");

            if (!ground.HasAnchor)
            {
                CreateDefaultGroundPlane();
                createdObjects = true;
            }

            if (portal == null)
            {
                portal = CreateDefaultPortal();
                createdObjects = true;
            }

            EnsurePortalCandidateComponent(portal);

            if (createdObjects)
            {
                var scene = SceneManager.GetActiveScene();
                if (scene.IsValid())
                    EditorSceneManager.MarkSceneDirty(scene);
                EnvironmentKitSceneSafeguards.SaveActiveSceneSilentlyIfPossible();
            }
        }

        /// <summary>Legacy wrapper — never swaps away from the active scene.</summary>
        public static void EnsureSceneAnchors(out bool createdNewScene)
        {
            EnsureSceneAnchorsInActiveScene(out createdNewScene);
        }

        static void CreateDefaultGroundPlane()
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Ground";
            plane.transform.position = Vector3.zero;
            plane.transform.localScale = new Vector3(12f, 1f, 12f);
            TrySetTag(plane, "Ground");
            Undo.RegisterCreatedObjectUndo(plane, "Create Ground");
        }

        static GameObject CreateDefaultPortal()
        {
            var portal = new GameObject("PortalFive");
            portal.transform.position = new Vector3(0f, 0.5f, 8f);
            Undo.RegisterCreatedObjectUndo(portal, "Create PortalFive");
            return portal;
        }

        static void EnsurePortalCandidateComponent(GameObject portal)
        {
            if (portal == null || CaveBuildPortalSettings.IsPortalCandidate(portal))
                return;

            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType("CaveEntrancePortal");
                if (type == null || !typeof(Component).IsAssignableFrom(type))
                    continue;

                if (portal.GetComponent(type) == null)
                    portal.AddComponent(type);
                break;
            }
        }

        static void EnsureDefaultRenderSetup()
        {
            if (UnityEngine.Object.FindAnyObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("Directional Light");
                Undo.RegisterCreatedObjectUndo(lightGo, "Create Directional Light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            var cam = Camera.main;
            if (cam != null && cam.transform.position.sqrMagnitude < 0.01f)
            {
                Undo.RecordObject(cam.transform, "Position Main Camera");
                cam.transform.position = new Vector3(0f, 6f, -12f);
            }
        }

        static void SaveStarterSceneTemplateIfMissing()
        {
            if (File.Exists(StarterScenePath))
                return;

            var scene = SceneManager.GetActiveScene();
            var activePath = scene.path?.Replace('\\', '/') ?? string.Empty;
            if (!string.IsNullOrEmpty(activePath) &&
                !activePath.EndsWith(StarterScenePath, StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log(
                    "[EnvironmentKit.Setup] Skipped StarterWorld template save — active scene is not blank " +
                    $"(current: {activePath}).");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(StarterScenePath) ?? "Assets/EnvironmentKit/Scenes");
            EditorSceneManager.SaveScene(scene, StarterScenePath);
            Debug.Log("[EnvironmentKit.Setup] Saved starter template " + StarterScenePath);
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

        static void TrySetTag(GameObject go, string tag)
        {
            try
            {
                go.tag = tag;
            }
            catch
            {
                Debug.LogWarning(
                    "[EnvironmentKit.Setup] Could not assign Ground tag — add it in Project Settings > Tags.");
            }
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
