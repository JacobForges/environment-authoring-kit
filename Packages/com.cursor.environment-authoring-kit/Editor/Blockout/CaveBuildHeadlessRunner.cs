#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Batchmode / farm entry for full ladder without blocking UI dialogs.</summary>
    public static class CaveBuildHeadlessRunner
    {
        public static void RunRecipe(
            string recipeId = CaveBuildAaaProductionBootstrap.FullProductionRecipeId,
            bool exitEditorWhenDone = false)
        {
            CaveBuildPhaseContractRegistry.ExportContractsCatalog();
            // Full invalidation only for CI/farm exits — keeps local/editor headless runs incremental.
            if (exitEditorWhenDone || System.Environment.GetEnvironmentVariable("CAVE_BUILD_FORCE_FULL") == "1")
                CaveBuildPhaseContractRegistry.InvalidateAll();

            var recipe = CaveBuildRecipeLibrary.Load(recipeId);
            if (recipe == null)
            {
                Debug.LogError("[CaveBuild] Headless: recipe not found: " + recipeId);
                if (exitEditorWhenDone)
                    EditorApplication.Exit(1);
                return;
            }

            CaveBuildRecipeLibrary.ApplyRecipe(recipe);
            var request = recipe.ToRequest();
            request.Seed = CaveBuildDeterminism.ResolveSeed(request.Seed);

            var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            CaveBuildLadderMetrics.BeginSession(sceneName, request.Seed, recipe.id);
            CaveBuildRunStatusPublisher.BeginSession(sceneName, request.Seed, additiveSurface: true);

            Debug.Log(
                $"[CaveBuild] Headless ladder start — recipe={recipe.id} seed={request.Seed} scope={request.SurfaceScope}");

            LavaTubeCaveBuilder.BuildInActiveScene(
                openMainSceneFirst: false,
                hideLegacyBlockout: true,
                skipDialogs: true,
                layoutPrototype: recipe.useLayoutPrototype,
                skipPreBuildGate: !recipe.enforcePreBuildGate);

            if (exitEditorWhenDone)
            {
                Debug.Log("[CaveBuild] Headless complete — exiting editor.");
                EditorApplication.Exit(0);
            }
        }
    }
}
#endif
