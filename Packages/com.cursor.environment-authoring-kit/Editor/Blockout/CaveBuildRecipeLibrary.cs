#if UNITY_EDITOR
using System;
using System.IO;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public static class CaveBuildRecipeLibrary
    {
        public const string RecipesFolderRel = "Assets/EnvironmentKit/Recipes";

        public static string RecipesFolderAbs =>
            Path.Combine(CaveBuildCursorSettings.ResolveHubRoot(), RecipesFolderRel);

        public static CaveBuildRecipeDefinition Load(string recipeId)
        {
            if (string.IsNullOrWhiteSpace(recipeId))
                return null;

            var path = Path.Combine(RecipesFolderAbs, recipeId.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? recipeId
                : recipeId + ".json");
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[CaveBuild] Recipe not found: {path}");
                return null;
            }

            try
            {
                return JsonUtility.FromJson<CaveBuildRecipeDefinition>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CaveBuild] Recipe parse failed: " + ex.Message);
                return null;
            }
        }

        public static void Save(CaveBuildRecipeDefinition recipe)
        {
            if (recipe == null || string.IsNullOrEmpty(recipe.id))
                return;

            Directory.CreateDirectory(RecipesFolderAbs);
            var path = Path.Combine(RecipesFolderAbs, recipe.id + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(recipe, true));
            AssetDatabase.Refresh();
        }

        public static void ApplyRecipe(CaveBuildRecipeDefinition recipe, bool persistPrefs = true)
        {
            if (recipe == null)
                return;

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            recipe.ApplyToSettings(settings);
            if (persistPrefs)
                settings.SaveToPrefs();

            if (recipe.pinSeedWhileDebugging && !recipe.respectLayoutRollSeed)
                CaveBuildDeterminism.SetPinnedSeed(recipe.seed, enabled: true);

            LavaTubeCaveBuilder.LastSurfaceScope = recipe.ToRequest().SurfaceScope;
            EditorPrefs.SetInt("CaveBuild_LastSeed", recipe.seed);
            EditorPrefs.SetString("CaveBuild_ActiveRecipeId", recipe.id);

            CaveBuildResearchConstrainedGate.WriteAllowedEntriesForRecipe(recipe);
            Debug.Log($"[CaveBuild] Applied recipe '{recipe.id}' — seed {recipe.seed}, scope {recipe.surfaceScope}.");
        }

        public static WorldGenerationRequest BuildRequestFromActiveRecipe(out CaveBuildRecipeDefinition recipe)
        {
            var id = EditorPrefs.GetString(
                "CaveBuild_ActiveRecipeId",
                CaveBuildAaaProductionBootstrap.FullProductionRecipeId);
            recipe = Load(id) ??
                     Load(CaveBuildAaaProductionBootstrap.FullProductionRecipeId) ??
                     Load(CaveBuildShowcaseMenu.ShowcaseRecipeId);
            if (recipe == null)
                return WorldGenerationRequest.LoadOrDefault();

            ApplyRecipe(recipe, persistPrefs: false);
            return recipe.ToRequest();
        }
    }
}
#endif
