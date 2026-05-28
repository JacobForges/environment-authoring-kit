using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.XR;
using UnityEditor;
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
                AssetDatabase.OpenAsset(sceneAsset);
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
    }
}
