#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Above-ground terrain meat-loop enrichment (separate from cave <see cref="CaveBuildMeatLoopPassPlan"/>).
    /// </summary>
    public static class SurfaceMeatLoopEnrichment
    {
        enum TerrainSurfaceTask
        {
            None = 0,
            LidarRefineSmooth = 1,
            RoadsWaterAccuracy = 2,
            IntelligentVegetation = 3,
            SurfaceNavAndPolish = 4,
            PlayableWorldCheck = 5,
        }

        static TerrainSurfaceTask TaskForPass(int pass)
        {
            var mod = ((pass % 16) + 16) % 16;
            return mod switch
            {
                1 or 15 => TerrainSurfaceTask.LidarRefineSmooth,
                4 or 12 => TerrainSurfaceTask.IntelligentVegetation,
                6 => TerrainSurfaceTask.RoadsWaterAccuracy,
                8 => TerrainSurfaceTask.SurfaceNavAndPolish,
                14 => TerrainSurfaceTask.PlayableWorldCheck,
                _ => TerrainSurfaceTask.None,
            };
        }

        public static bool TryPreparePass(int pass, out string message)
        {
            message = string.Empty;
            CaveBuildResearchCacheBridge.SyncTerrainResearchExecutionBrief("terrain_integration", pass, out var briefMsg);
            if (!CaveBuildPhasePromptBridge.ExportMeatPassPlan(pass, out var planMsg))
            {
                message = $"Meat pass {pass}: brief={briefMsg}; plan export failed: {planMsg}";
                return false;
            }

            message = $"Meat pass {pass}: research brief synced; execution plan written ({planMsg}).";
            return true;
        }

        public static bool TryApplySurfaceTask(
            int pass,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out string actionTaken)
        {
            actionTaken = string.Empty;
            var task = TaskForPass(pass);
            if (task == TerrainSurfaceTask.None)
                return false;

            TryPreparePass(pass, out var prep);
            if (!TryResolveSurfaceContext(ground, request, out var envRoot, out var surfaceRoot, out var terrain, out var center, out var extent))
            {
                actionTaken = prep + " Surface context missing (run FullWorld surface build first).";
                return false;
            }

            var seed = request?.Seed ?? 0;
            var ok = false;
            var detail = string.Empty;

            switch (task)
            {
                case TerrainSurfaceTask.LidarRefineSmooth:
                    if (CaveBuildWorkflowCoordinator.TryConsumeMeatSurfaceTerrainPass())
                        ok = SurfaceTerrainRefinement.TryLidarRefineAndSmooth(
                            terrain, center, extent, seed, out detail);
                    else
                        detail = "Surface terrain pass cap reached.";
                    break;

                case TerrainSurfaceTask.RoadsWaterAccuracy:
                    if (CaveBuildWorkflowCoordinator.TryConsumeMeatSurfaceTerrainPass())
                        ok = SurfaceTerrainRefinement.TryRefineRoadsAndWater(
                            terrain, surfaceRoot, center, extent, seed, out detail);
                    else
                        detail = "Surface terrain pass cap reached.";
                    break;

                case TerrainSurfaceTask.IntelligentVegetation:
                    var vegPass = pass % 16 == 12
                        ? SurfaceIntelligentPropPlacer.VegetationPass.Understory
                        : pass % 16 == 4
                            ? SurfaceIntelligentPropPlacer.VegetationPass.TreesFocus
                            : SurfaceIntelligentPropPlacer.VegetationPass.Mixed;
                    ok = SurfaceIntelligentPropPlacer.TryPlaceVegetationPass(
                        surfaceRoot, terrain, center, extent, pass, seed, vegPass, out detail);
                    break;

                case TerrainSurfaceTask.SurfaceNavAndPolish:
                    ok = SurfaceNavMeshBaker.BakePhase(envRoot.transform, terrain, surfaceRoot, out detail);
                    if (CaveBuildWorkflowCoordinator.TryConsumeMeatSurfaceTerrainPass())
                    {
                        SurfaceTerrainRefinement.TryLidarRefineAndSmooth(terrain, center, extent, seed + 7, out var touch);
                        detail += " " + touch;
                    }
                    break;

                case TerrainSurfaceTask.PlayableWorldCheck:
                    ok = PlayableWorldGate.EvaluateAndWrite(ground, request, pass, out detail);
                    break;
            }

            actionTaken = string.IsNullOrEmpty(prep) ? detail : prep + " " + detail;
            return ok;
        }

        static bool TryResolveSurfaceContext(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out GameObject envRoot,
            out Transform surfaceRoot,
            out Terrain terrain,
            out Vector3 center,
            out float extent)
        {
            envRoot = null;
            surfaceRoot = null;
            terrain = ground?.Terrain;
            center = Vector3.zero;
            if (ground != null && ground.HasAnchor)
                center = ground.AnchorWorld;

            extent = request != null ? Mathf.Clamp(request.SurfaceExtentMeters, 80f, 512f) : 160f;

            var caveRoot = CaveRouteProbeRunner.FindCaveRoot();
            if (caveRoot != null)
            {
                var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot);
                if (mouth.sqrMagnitude > 0.01f)
                    center = new Vector3(mouth.x, mouth.y, mouth.z);
            }

            var env = UnityEngine.Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            if (env != null)
            {
                envRoot = env.gameObject;
                surfaceRoot = envRoot.transform.Find(SurfaceWorldPaths.RootName);
            }

            if (terrain == null && ground != null)
                terrain = ground.Terrain;
            return surfaceRoot != null && terrain != null;
        }
    }
}
#endif
