#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public static class CaveBuildShowcaseMenu
    {
        public const string ShowcaseRecipeId = "showcase-florida-karst-xr";
        public const string SurfaceIterationRecipeId = "surface-only-iteration";

        [MenuItem(CaveBuildMenuPaths.Root + "Run Showcase Build (Florida Karst XR)", false, 5)]
        public static void RunShowcaseFromMenu()
        {
            var recipe = CaveBuildRecipeLibrary.Load(ShowcaseRecipeId);
            if (recipe == null)
            {
                EditorUtility.DisplayDialog(
                    "Showcase recipe missing",
                    $"Expected:\n{CaveBuildRecipeLibrary.RecipesFolderRel}/{ShowcaseRecipeId}.json\n\n" +
                    "Create it from the package template or re-sync the Hub.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Showcase build — Florida Karst XR",
                    $"Product: {recipe.productScope}\n\n" +
                    $"Seed: {recipe.seed} (pinned while debugging)\n" +
                    $"Counties: {string.Join(", ", recipe.floridaCounties)}\n\n" +
                    "Runs full surface + cave ladder with incremental skips where artifacts exist.",
                    "Build",
                    "Cancel"))
                return;

            LavaTubeCaveBuilder.BuildInActiveScene(
                hideLegacyBlockout: true,
                skipDialogs: false,
                layoutPrototype: recipe.useLayoutPrototype,
                surfaceScope: SurfaceBuildScope.FullWorld);
        }

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Apply Showcase Recipe Settings Only", false, 16)]
        public static void ApplyShowcaseSettingsOnly()
        {
            var recipe = CaveBuildRecipeLibrary.Load(ShowcaseRecipeId);
            if (recipe != null)
                CaveBuildRecipeLibrary.ApplyRecipe(recipe);
        }

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Pin Showcase Seed (deterministic debug)", false, 17)]
        public static void PinShowcaseSeed()
        {
            var recipe = CaveBuildRecipeLibrary.Load(ShowcaseRecipeId);
            if (recipe != null)
                CaveBuildDeterminism.SetPinnedSeed(recipe.seed, enabled: true);
        }

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Unpin Seed (shipping variety)", false, 18)]
        public static void UnpinSeed() => CaveBuildDeterminism.Unpin();

        [MenuItem(CaveBuildMenuPaths.CaveBuild + "Run Surface Iteration Recipe (fast)", false, 12)]
        public static void RunSurfaceIterationRecipe()
        {
            var recipe = CaveBuildRecipeLibrary.Load(SurfaceIterationRecipeId);
            if (recipe == null)
            {
                EditorUtility.DisplayDialog(
                    "Recipe missing",
                    $"Expected {CaveBuildRecipeLibrary.RecipesFolderRel}/{SurfaceIterationRecipeId}.json",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Surface iteration build",
                    "Surface only — skips cave pipeline, pre-build gate, and autonomous polish.\n\n" +
                    "Uses incremental ladder when artifacts exist (faster re-runs).",
                    "Build surface",
                    "Cancel"))
                return;

            CaveBuildRecipeLibrary.ApplyRecipe(recipe);
            var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            CaveBuildLadderMetrics.BeginSession(sceneName, recipe.seed, recipe.id);
            CaveBuildRunStatusPublisher.BeginSession(sceneName, recipe.seed, additiveSurface: true);

            LavaTubeCaveBuilder.BuildInActiveScene(
                hideLegacyBlockout: true,
                skipDialogs: true,
                layoutPrototype: false,
                skipPreBuildGate: true,
                surfaceScope: SurfaceBuildScope.SurfaceOnly);
        }
    }
}
#endif
