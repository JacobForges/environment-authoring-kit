using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.World;
using EnvironmentAuthoringKit.World;
using EnvironmentAuthoringKit.Editor.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor
{
    /// <summary>
    /// Command-line: Unity -batchmode -projectPath /path/to/Hub -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.GenerateDefaultWorld -quit
    /// </summary>
    public static class EnvironmentKitBatch
    {
        const string DefaultDescription = "misty pine forest at dusk with a small clearing";
        const int DefaultSeed = 4242;

        const string CaveDescription =
            "hilly cave system with multiple tunnels, chambers, and a dramatic cave entrance, dense stalactites";

        [MenuItem("Window/Environment Kit/Cave Build/Advanced/Legacy/Build Lava Tube XR Cave (VITURE)")]
        public static void BuildLavaTubeCaveFromMenu() => LavaTubeCaveBuilder.BuildFromMenu();

        /// <summary>Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.GenerateLavaTubeCave</summary>
        public static void GenerateLavaTubeCave()
        {
            LavaTubeCaveBuilder.BuildInActiveScene(openMainSceneFirst: false);
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Unity -batchmode -projectPath &lt;Hub&gt; -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunShowcaseHeadless -quit
        /// </summary>
        public static void RunShowcaseHeadless() =>
            CaveBuildHeadlessRunner.RunRecipe(
                CaveBuildAaaProductionBootstrap.FullProductionRecipeId,
                exitEditorWhenDone: true);

        /// <summary>
        /// Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunHeadlessCaveLadder -quit
        /// Optional env CAVE_BUILD_RECIPE_ID (default showcase-florida-karst-xr).
        /// </summary>
        public static void RunHeadlessCaveLadder()
        {
            var recipeId = System.Environment.GetEnvironmentVariable("CAVE_BUILD_RECIPE_ID");
            if (string.IsNullOrWhiteSpace(recipeId))
                recipeId = CaveBuildShowcaseMenu.ShowcaseRecipeId;
            CaveBuildHeadlessRunner.RunRecipe(recipeId, exitEditorWhenDone: true);
        }

        /// <summary>Headless surface-only (faster than showcase). Not used by nightly CI.</summary>
        public static void RunSurfaceIterationHeadless() =>
            CaveBuildHeadlessRunner.RunRecipe(CaveBuildShowcaseMenu.SurfaceIterationRecipeId, exitEditorWhenDone: true);

        /// <summary>Regenerate Hub.sln and compile scripts for CodeQL (self-hosted CI). See docs/CODEQL_SELFHOSTED_INSTALL.md.</summary>
        public static void PrepareForCodeQl() => CodeQlUnityBootstrap.PrepareForCodeQl();

        /// <summary>
        /// Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.ExportCompileDiagnosticsForAgent -quit
        /// Exports CaveBuildCompileDiagnostics.json; exit 2 when verified compile errors remain.
        /// </summary>
        public static void ExportCompileDiagnosticsForAgent()
        {
            CaveBuildCompileGate.ExportDiagnostics();
            EditorApplication.Exit(CaveBuildCompileGate.HasBlockingErrors() ? 2 : 0);
        }

        /// <summary>
        /// Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunAgentPostPassForBot -quit
        /// Applies one ladder fix + re-grade after a Cursor agent session (terminal bot loop).
        /// </summary>
        public static void RunAgentPostPassForBot() => CaveBuildAgentPostPassBatch.Run();

        /// <summary>
        /// Production bot loop: multi-rung fixes + route probe + performance gate.
        /// </summary>
        public static void RunBotProductionPostPass() => CaveBuildBotProductionBatch.Run();

        /// <summary>Export Hub demo smoke checklist JSON for bot supervisor.</summary>
        public static void ExportHubDemoSmokeChecklist()
        {
            TryOpenWorldScene();
            var exporter = System.Type.GetType("HubDemoSmokeExporter, Hub.Editor");
            exporter?.GetMethod("Export", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                ?.Invoke(null, null);
            EditorApplication.Exit(0);
        }

        /// <summary>Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.BuildCinematicTimelines -quit</summary>
        public static void BuildCinematicTimelines()
        {
            TryOpenWorldScene();
            var triggers = Object.FindObjectsByType<EnvironmentAuthoringKit.World.WorldCinematicTrigger>(
                FindObjectsInactive.Include);
            if (triggers.Length == 0)
            {
                var terrain = Object.FindAnyObjectByType<Terrain>();
                WorldFxAndMediaAuthor.Apply(terrain, null);
            }

            WorldCinematicTimelineAuthor.BindAllTriggersInScene();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();
            EditorApplication.Exit(0);
        }

        /// <summary>Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.BuildHollowTitan -quit</summary>
        public static void BuildHollowTitan()
        {
            TryOpenWorldScene();
            var terrain = Object.FindAnyObjectByType<Terrain>();
            var request = new WorldGenerationRequest
            {
                ContentTier = WorldBuildContentTier.Aaa,
                Seed = DefaultSeed,
                SurfaceScope = SurfaceBuildScope.FullWorld,
            };
            HollowTitanLandmarkAuthor.PlaceAaaBeforeTerraform(terrain, request);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();
            EditorApplication.Exit(0);
        }

        /// <summary>Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.WireHubCombat -quit</summary>
        public static void WireHubCombat()
        {
            TryOpenWorldScene();
            HubSurfaceCombatWiring.WireFromMenu();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();
            EditorApplication.Exit(0);
        }

        static void TryOpenWorldScene()
        {
            foreach (var path in new[]
                     {
                         "Assets/TheStart.unity",
                         "Assets/MainScene.unity",
                         "Assets/NeonCity.unity",
                     })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                    continue;
                if (SceneManager.GetActiveScene().path != path)
                    EnvironmentKitSceneSafeguards.TryOpenSceneByPath(path);
                return;
            }

            Debug.LogWarning("[EnvironmentKitBatch] No known world scene — using active scene.");
        }

        [MenuItem("Window/Environment Kit/Cave Build/Advanced/Legacy/Build Cave World Now (Blockout)")]
        public static void BuildCaveWorldFromMenu()
        {
            if (!EditorUtility.DisplayDialog(
                    "Build Cave World",
                    "Generates caves under your scene ground/plane in the ACTIVE scene:\n\n\"" +
                    CaveDescription + "\"",
                    "Build",
                    "Cancel"))
                return;

            GenerateWorldInEditor(CaveDescription, 7721, openMainSceneFirst: true);
        }

        static void TryOpenMainScene()
        {
            const string mainScenePath = "Assets/MainScene.unity";
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(mainScenePath);
            if (sceneAsset == null)
            {
                Debug.LogWarning($"[Environment Kit] {mainScenePath} not found. Using the currently open scene.");
                return;
            }

            if (SceneManager.GetActiveScene().path != mainScenePath)
                EnvironmentKitSceneSafeguards.TryOpenSceneByPath(mainScenePath);
        }

        [MenuItem("Window/Environment Kit/Cave Build/Advanced/Legacy/Build Demo World Now")]
        public static void BuildDemoWorldFromMenu()
        {
            if (!EditorUtility.DisplayDialog(
                    "Build Demo World",
                    "Generates in the ACTIVE scene under your ground/plane:\n\n\"" + DefaultDescription + "\"",
                    "Build",
                    "Cancel"))
                return;

            GenerateWorldInEditor(DefaultDescription, DefaultSeed);
        }

        public static void GenerateDefaultWorld()
        {
            GenerateWorld(DefaultDescription, DefaultSeed);
        }

        public static void GenerateWorldInEditor(string description, int seed, bool openMainSceneFirst = false)
        {
            if (openMainSceneFirst)
                TryOpenMainScene();

            if (AssetDatabase.LoadAssetAtPath<BiomeCatalog>(
                    $"{EnvironmentKitSettings.PresetsFolder}/BiomeCatalog.asset") == null)
                SamplePresetsCreator.CreateAll();

            var catalog = AssetDatabase.LoadAssetAtPath<BiomeCatalog>(
                $"{EnvironmentKitSettings.PresetsFolder}/BiomeCatalog.asset");
            var xr = EnvironmentKitHardwareBudget.ResolveXrProfile(
                AssetDatabase.LoadAssetAtPath<XROptimizationProfile>(
                    $"{EnvironmentKitSettings.PresetsFolder}/VitureXRPro.asset"));

            if (catalog == null)
            {
                EditorUtility.DisplayDialog("Environment Kit", "Could not load BiomeCatalog.", "OK");
                return;
            }

            var result = WorldGenerator.Generate(description, catalog, seed, optimizeForXr: true, xr);

            if (!result.Success)
            {
                EditorUtility.DisplayDialog("Environment Kit", result.Message, "OK");
                return;
            }

            EditorUtility.DisplayDialog(
                "Environment Kit",
                $"{result.Message}\n\nSaved in active scene. Use Ctrl+S to persist.",
                "OK");

            AssetDatabase.SaveAssets();
            EnvironmentSceneUtility.MarkSceneDirty();
        }

        public static void GenerateWorld(string description, int seed)
        {
            GenerateWorldInEditor(description, seed);
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Unity -batchmode -projectPath &lt;Hub&gt; -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RepairLegacyModelImports -quit
        /// </summary>
        public static void RepairLegacyModelImports()
        {
            HubModelImportRepairUtility.RepairAll(forceReimport: true);
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
    }
}
