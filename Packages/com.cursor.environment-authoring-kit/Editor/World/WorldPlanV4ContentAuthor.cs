#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Single entry for all Plan v4 world content (CC0, no Adobe).</summary>
    public static class WorldPlanV4ContentAuthor
    {
        [MenuItem("Window/Environment Kit/World/Apply Full Plan V4 Content (active terrain)")]
        public static void ApplyFromMenu()
        {
            var terrain = Object.FindAnyObjectByType<Terrain>();
            Apply(terrain, null);
        }

        public static void Apply(Terrain mainTerrain, WorldGenerationRequest request)
        {
            if (mainTerrain == null)
                return;

            WorldProjectTagSetup.EnsureBossStagePortalTag();

            request ??= new WorldGenerationRequest
            {
                ContentTier = WorldBuildContentTier.Aaa,
                Seed = 4242,
                SurfaceScope = SurfaceBuildScope.FullWorld,
            };

            WorldItemCatalogBuilder.BuildFromManifest();
            Cc0ContentImportUtility.EnsureAll(importItems: true);
            SurfaceWorldBiomeAuthor.TagAllSurfaceTiles(mainTerrain, request.Seed);
            if (request.SurfaceScope == SurfaceBuildScope.FullWorld)
                HollowTitanLandmarkAuthor.PlaceAaaBeforeTerraform(mainTerrain, request);
            WorldLandmarkSpawnDirector.Scatter(mainTerrain, request);
            WorldLootSpawnDirector.Scatter(mainTerrain, request);
            WorldNpcLayoutAuthor.Apply(mainTerrain, request);
            WorldTerrainV2ClosureAuthor.Apply(mainTerrain, request);
            WorldAnnexVisualPass.Apply(mainTerrain, request.Seed);
            HubSurfaceCombatWiring.WireSurfaceEnemies();
            WorldBossStageSceneAuthor.EnsureBossScene();
            WorldRuntimeResourcesAuthor.EnsureBossInResources();
            WorldPlayerSetupAuthor.WireActiveScenePlayer();
            WorldFxAndMediaAuthor.Apply(mainTerrain, request);
            WorldCinematicTimelineAuthor.BindAllTriggersInScene();
            WorldPlanV4RuntimeSystemsAuthor.EnsureInScene();
            BiomeGrassScatterAuthor.EnsureBiomePropsScattered(mainTerrain, request, mainTerrain.transform.parent);
            Debug.Log("[Surface] Full Plan v4 content applied.", mainTerrain);
        }
    }
}
#endif
