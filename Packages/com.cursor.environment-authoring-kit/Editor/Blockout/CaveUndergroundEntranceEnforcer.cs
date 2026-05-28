#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Forces surface mouth at walkable ground, underground route below, trail link, and descent rebuild
    /// (research: Floridan karst entrances + DS 926 structural depth; not water-table geometry).
    /// </summary>
    public static class CaveUndergroundEntranceEnforcer
    {
        public const float MinUndergroundDropMeters = 5.5f;

        public static int Enforce(
            Transform cavesRoot,
            Transform geometry,
            CaveMazeLayout layout,
            Material floorMat,
            Material rockMat,
            SceneGroundInfo ground,
            LavaTubePrefabCatalog catalog,
            int seed)
        {
            if (cavesRoot == null || geometry == null || layout == null || ground == null || !ground.HasAnchor)
                return 0;

            var placed = 0;
            SurfaceCaveOpeningAligner.TryAlignCaveRootToOpening(cavesRoot, ground);
            CaveGroundPlacementUtility.FinalizeGroundPlacement(cavesRoot, ground, out _, seed);
            CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(cavesRoot, ground, out _);

            EnsureCaveRootUnderSurface(cavesRoot, geometry, layout, ground);

            placed += CaveSurfaceEntranceBuilder.Build(
                cavesRoot, geometry, layout, floorMat, rockMat, ground, catalog, seed);

            var envRoot = cavesRoot.parent;
            var surfaceRoot = envRoot != null ? envRoot.Find(SurfaceWorldPaths.RootName) : null;
            if (surfaceRoot != null)
                placed += SurfaceTrailCaveMouthConnector.ConnectPrimaryTrailToCaveMouth(surfaceRoot, cavesRoot, ground);

            CaveEntranceVolumeBuilder.CarveTerrainBowlAtMouth(cavesRoot, ground, radiusMeters: 11f);
            CavePlayabilityFix.ExtendAtmosphereForSurfaceDescent(cavesRoot);
            CaveSceneMaterialRepair.RepairCaveRoot(cavesRoot);

            if (placed > 0)
                Debug.Log($"[CaveBuild] Underground entrance enforcer: {placed} piece(s); mouth on surface, route below.");
            return placed;
        }

        static void EnsureCaveRootUnderSurface(
            Transform cavesRoot,
            Transform geometry,
            CaveMazeLayout layout,
            SceneGroundInfo ground)
        {
            var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(cavesRoot, ground);
            if (mouth.sqrMagnitude < 0.01f)
                return;

            var surfaceY = CaveGroundPlacementUtility.SampleWalkableSurfaceWorldY(ground, mouth);
            if (layout?.SolutionPath != null && layout.SolutionPath.Count > 0 && geometry != null)
            {
                var start = layout.SolutionPath[0];
                var routeStart = geometry.TransformPoint(layout.GetFloorSurfaceLocal(start.x, start.y));
                var drop = surfaceY - routeStart.y;
                if (drop < MinUndergroundDropMeters)
                {
                    var delta = MinUndergroundDropMeters - drop;
                    CaveEditorUndo.RecordObject(cavesRoot, "Lower cave for underground drop");
                    cavesRoot.position -= new Vector3(0f, delta, 0f);
                }
            }

            var mouthErr = CaveGroundPlacementUtility.MeasureEntranceMouthSurfaceError(cavesRoot, ground);
            if (Mathf.Abs(mouthErr) > 0.55f)
                CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(cavesRoot, ground, out _);
        }
    }
}
#endif
