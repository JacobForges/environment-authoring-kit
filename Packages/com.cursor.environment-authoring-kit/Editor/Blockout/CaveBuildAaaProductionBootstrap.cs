#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Applies AAA production ladder (contracts, metrics, research gates, recipes) to Build Complete Cave.
    /// </summary>
    public static class CaveBuildAaaProductionBootstrap
    {
        public const string FullProductionRecipeId = "aaa-full-cave-production";

        const string PrefRandomize = "CaveBuild_RandomizeEachTime";

        public static bool IsFullProductionBuild(SurfaceBuildScope scope, bool layoutPrototype) =>
            scope == SurfaceBuildScope.FullWorld && !layoutPrototype;

        /// <summary>Call before full-world build starts (menu or core).</summary>
        public static CaveBuildRecipeDefinition PrepareFullProductionBuild(
            CaveLayoutRoll roll,
            SurfaceBuildScope scope,
            bool layoutPrototype)
        {
            if (!IsFullProductionBuild(scope, layoutPrototype))
                return null;

            CaveBuildPhaseContractRegistry.ExportContractsCatalog();

            var recipe = CaveBuildRecipeLibrary.Load(FullProductionRecipeId) ??
                           CaveBuildRecipeLibrary.Load(CaveBuildShowcaseMenu.ShowcaseRecipeId);
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();

            if (recipe != null)
            {
                if (roll != null && recipe.respectLayoutRollSeed)
                    recipe.seed = roll.Seed;

                recipe.ApplyToSettings(settings);
                EditorPrefs.SetString("CaveBuild_ActiveRecipeId", recipe.id);
                CaveBuildResearchConstrainedGate.WriteAllowedEntriesForRecipe(recipe);

                if (recipe.pinSeedWhileDebugging && roll != null &&
                    !EditorPrefs.GetBool(PrefRandomize, true))
                    CaveBuildDeterminism.SetPinnedSeed(roll.Seed, enabled: true);
            }

            ApplyAaaProductionDefaults(settings);
            settings.SaveToPrefs();

            var sceneName = SceneManager.GetActiveScene().name;
            var seed = roll?.Seed ?? EditorPrefs.GetInt("CaveBuild_LastSeed", 7721001);
            CaveBuildLadderMetrics.BeginSession(sceneName, seed, recipe?.id ?? FullProductionRecipeId);
            CaveBuildRunStatusPublisher.BeginSession(sceneName, seed, additiveSurface: true);

            Debug.Log(
                "[CaveBuild] AAA production mode — FullWorld ladder: incremental rungs, phased queue, " +
                "pre-build gate, research-constrained repair, live Scene feedback, autonomous until ship.");

            return recipe;
        }

        /// <summary>Merge production guarantees onto the rolled request (roll keeps variety; recipe sets floors).</summary>
        public static void MergeRecipeIntoRequest(
            CaveBuildRecipeDefinition recipe,
            WorldGenerationRequest request,
            CaveLayoutRoll roll)
        {
            if (request == null)
                return;

            if (roll != null)
                roll.ApplyTo(request);

            var scope = request.SurfaceScope;

            if (recipe == null)
            {
                switch (scope)
                {
                    case SurfaceBuildScope.SurfaceOnly:
                        request.AllowCreateTerrain = true;
                        request.SurfaceIncludeTrails = true;
                        return;
                    case SurfaceBuildScope.CaveOnly:
                        request.CaveMode = CaveGenerationMode.FullSystem;
                        return;
                    default:
                        request.CaveMode = CaveGenerationMode.FullSystem;
                        request.SurfaceIncludeTrails = true;
                        return;
                }
            }

            var baseline = recipe.ToRequest();
            request.SurfaceScope = SurfaceBuildScope.FullWorld;
            request.CaveMode = CaveGenerationMode.FullSystem;
            request.UseLayoutPrototype = false;
            request.UseSplineMesh = true;
            request.UseTrue3DCaveSystem = true;
            request.UseBlockTunnel = true;
            request.UseTerrainCarve = true;
            request.AllowCreateTerrain = true;
            request.IncludeCaveWater = false;

            if (!string.IsNullOrWhiteSpace(baseline.RawDescription))
                request.RawDescription = baseline.RawDescription;

            request.SurfaceExtentMeters = Mathf.Max(request.SurfaceExtentMeters, baseline.SurfaceExtentMeters);
            request.SurfaceIncludeTrails = true;
            if (roll == null)
            {
                request.SurfaceDirectionCount = Mathf.Max(request.SurfaceDirectionCount, baseline.SurfaceDirectionCount);
                request.SurfaceIncludeMountains |= baseline.SurfaceIncludeMountains;
                request.SurfaceIncludeWater |= baseline.SurfaceIncludeWater;
                request.SurfaceIncludeRoads |= baseline.SurfaceIncludeRoads;
            }

            if (roll != null)
                request.Seed = roll.Seed;
            else
                request.Seed = baseline.Seed;
        }

        public static void OnPreBuildGatePassed(int seed)
        {
            CaveBuildPhaseContractRegistry.MarkRungComplete(
                CaveBuildPhaseContractRegistry.RungPreBuildGate, seed);
        }

        public static void OnFullProductionBuildFinished(int seed, float score, string letter)
        {
            CaveBuildLadderMetrics.RecordBuildGrade(score, letter);
            CaveBuildPhaseContractRegistry.MarkRungComplete(CaveBuildPhaseContractRegistry.RungPolish, seed);
            CaveBuildLadderMetrics.Save();
        }

        public static void EnsureBatchProductionSettings()
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            ApplyAaaProductionDefaults(settings);
            settings.SaveToPrefs();
        }

        static void ApplyAaaProductionDefaults(CaveBuildCursorSettings settings)
        {
            settings.usePhasedCaveBuild = true;
            settings.useIncrementalLadder = true;
            settings.showLiveScenePlacement = false;
            settings.constrainBotToResearchOnFailure = true;
            settings.enforcePreBuildGate = true;
            settings.runPostBuildResearchPhase = true;
            settings.invokeCursorOnResearchPhase = true;
            settings.autoRunPlaytestBotAfterBuild = false;
            settings.autoContinueAfterPreBuildCursor = true;
            settings.exportGenerationPrefabWhenFinished = true;
        }
    }
}
#endif
