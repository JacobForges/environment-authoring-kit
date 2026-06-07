#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Editor.Generation;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Versioned JSON world recipe (Far Cry-style biome recipe, data not code).</summary>
    [Serializable]
    public class CaveBuildRecipeDefinition
    {
        public string schemaVersion = "1";
        public string id = "untitled";
        public string title = "Untitled recipe";
        public string productScope = "Florida karst surface + lava-tube cave for Unity XR";
        public string description =
            "hilly karst panhandle surface with walkable trail to lava-tube cave entrance";

        public int seed = 424242;
        public bool pinSeedWhileDebugging = true;
        /// <summary>When true, layout roll seed wins over recipe seed (randomize-each-build stays varied at AAA quality).</summary>
        public bool respectLayoutRollSeed = true;
        public string surfaceScope = "FullWorld";

        public float surfaceExtentMeters = 220f;
        public int surfaceDirectionCount = 8;
        public bool surfaceIncludeMountains = true;
        public bool surfaceIncludeWater = true;
        public bool surfaceIncludeRoads = true;
        public bool surfaceIncludeTrails = true;

        public string biome = "Cave";
        public string caveMode = "FullSystem";
        public bool useLayoutPrototype;
        public bool useIncrementalLadder = true;
        public bool showLiveScenePlacement = false;
        public bool skipAutonomousPolishLoop;
        public bool enforcePreBuildGate = true;
        public bool usePhasedCaveBuild = true;
        public bool constrainBotToResearchOnFailure = true;
        public bool runPostBuildResearchPhase = true;
        public bool autoRunPlaytestBotAfterBuild = false;

        /// <summary>ResearchCache entry IDs the bot may cite when this recipe's build fails.</summary>
        public string[] allowedResearchEntryIds =
        {
            "world-gen-pipeline-ladder-best-practices",
            "ubisoft-farcry5-freshwater-cliff-biome-order",
            "sidefx-houdini-heightfield-ladder",
        };

        public string[] floridaCounties = { "Bay", "Washington", "Jackson", "Calhoun" };

        public WorldGenerationRequest ToRequest()
        {
            Enum.TryParse(biome, true, out BiomeId biomeId);
            Enum.TryParse(caveMode, true, out CaveGenerationMode caveModeId);
            Enum.TryParse(surfaceScope, true, out SurfaceBuildScope scope);

            return new WorldGenerationRequest
            {
                RawDescription = description ?? string.Empty,
                Biome = biomeId,
                CaveMode = caveModeId,
                Seed = seed,
                SurfaceScope = scope,
                SurfaceExtentMeters = surfaceExtentMeters,
                SurfaceDirectionCount = surfaceDirectionCount,
                SurfaceIncludeMountains = surfaceIncludeMountains,
                SurfaceIncludeWater = surfaceIncludeWater,
                SurfaceIncludeRoads = surfaceIncludeRoads,
                SurfaceIncludeTrails = surfaceIncludeTrails,
                UseLayoutPrototype = useLayoutPrototype,
                UseSplineMesh = !useLayoutPrototype,
                UseTrue3DCaveSystem = !useLayoutPrototype,
                UseBlockTunnel = !useLayoutPrototype,
                UseTerrainCarve = !useLayoutPrototype,
                AllowCreateTerrain = true,
            };
        }

        public void ApplyToSettings(CaveBuildCursorSettings settings)
        {
            if (settings == null)
                return;
            settings.useIncrementalLadder = useIncrementalLadder;
            settings.showLiveScenePlacement = showLiveScenePlacement;
            settings.enforcePreBuildGate = enforcePreBuildGate;
            settings.enableAutonomousUntilShip = !skipAutonomousPolishLoop;
            settings.usePhasedCaveBuild = usePhasedCaveBuild;
            settings.constrainBotToResearchOnFailure = constrainBotToResearchOnFailure;
            settings.runPostBuildResearchPhase = runPostBuildResearchPhase;
            settings.autoRunPlaytestBotAfterBuild = autoRunPlaytestBotAfterBuild;
        }
    }
}
#endif
