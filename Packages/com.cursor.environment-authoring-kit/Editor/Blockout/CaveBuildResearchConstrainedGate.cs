#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// When a rung fails, export which ResearchCache entries the bot may use for fixes (style guide).
    /// </summary>
    public static class CaveBuildResearchConstrainedGate
    {
        public const string BriefRel =
            CaveBuildAgentContextExporter.Folder + "/CaveBuildConstrainedRepairBrief.json";

        [System.Serializable]
        public class ConstrainedBrief
        {
            public string failedRungId;
            public string failedPhaseId;
            public int seed;
            public string message;
            public string[] allowedResearchEntryIds;
            public string productScope;
            public string ladderDoc =
                "Packages/com.cursor.environment-authoring-kit/docs/WORLD-GENERATION-PIPELINE-LADDER.md";
        }

        public static void WriteAllowedEntriesForRecipe(CaveBuildRecipeDefinition recipe)
        {
            if (recipe?.allowedResearchEntryIds == null || recipe.allowedResearchEntryIds.Length == 0)
                return;

            WriteBrief(
                "recipe_active",
                "recipe",
                recipe.seed,
                $"Active recipe '{recipe.id}' — bot constrained to recipe research list.",
                recipe.allowedResearchEntryIds,
                recipe.productScope);
        }

        public static void OnRungFailed(string rungId, string phaseId, int seed, string message)
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (!settings.constrainBotToResearchOnFailure)
                return;

            var recipe = CaveBuildRecipeLibrary.Load(
                EditorPrefs.GetString("CaveBuild_ActiveRecipeId", "showcase-florida-karst-xr"));
            var allowed = recipe?.allowedResearchEntryIds ?? DefaultAllowedIds(rungId);
            WriteBrief(
                rungId,
                phaseId,
                seed,
                message,
                allowed,
                recipe?.productScope ??
                "Florida karst surface + lava-tube cave for Unity XR");
        }

        static string[] DefaultAllowedIds(string rungId) => rungId switch
        {
            CaveBuildPhaseContractRegistry.RungTrailsNav or "surface_route_bot" => new[]
            {
                "world-gen-pipeline-ladder-best-practices",
                "ubisoft-farcry5-freshwater-cliff-biome-order",
                "sidefx-houdini-heightfield-ladder",
            },
            CaveBuildPhaseContractRegistry.RungValidation => new[]
            {
                "nvidia-fly-fail-fix-iterative-pcg-repair",
                "ea-seed-aaa-testing",
                "sony-ps5-automated-gameplay",
            },
            CaveBuildPhaseContractRegistry.RungCaveLayout or CaveBuildPhaseContractRegistry.RungShellMaterials => new[]
            {
                "guerrilla-horizon-gpu-procedural-placement",
                "epic-ue5-pcg-topological-execution",
                "fromsoftware-cave-lighting",
            },
            _ => new[]
            {
                "world-gen-pipeline-ladder-best-practices",
                "hello-games-nms-deterministic-seed-pipeline",
            },
        };

        static void WriteBrief(
            string rungId,
            string phaseId,
            int seed,
            string message,
            string[] allowedIds,
            string productScope)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, BriefRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            var brief = new ConstrainedBrief
            {
                failedRungId = rungId,
                failedPhaseId = phaseId,
                seed = seed,
                message = message,
                allowedResearchEntryIds = allowedIds,
                productScope = productScope,
            };
            File.WriteAllText(path, JsonUtility.ToJson(brief, true));
            Debug.Log(
                "[CaveBuild] Constrained repair brief written — bot limited to " +
                $"{allowedIds.Length} ResearchCache entries.");
        }
    }
}
#endif
