#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Shapes the primary cave entrance bowl in the heightmap before surface props spawn.
    /// </summary>
    public static class SurfacePrimaryMouthTerrainAuthor
    {
        public const float PrimaryBowlRadiusMeters = 11f;
        public const float PrimaryBowlDepthMeters = 3.2f;
        public const float ApproachBenchRadiusMeters = 4.5f;

        public static bool TryApply(SceneGroundInfo ground, WorldGenerationRequest request, out string message)
        {
            message = string.Empty;
            if (ground?.Terrain == null)
            {
                message = "Primary mouth skipped — no terrain.";
                return false;
            }

            if (request != null && request.UseMountainWildernessCaveMouth)
            {
                message = "Primary play-disk bowl skipped — wilderness mouths on south peaks only.";
                return false;
            }

            var primary = FindPrimaryOpening();
            if (primary == null)
            {
                message = "Primary mouth skipped — no CaveOpening marker.";
                return false;
            }

            var mouthWorld = primary.transform.position;
            if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(ground.Terrain, mouthWorld.x, mouthWorld.z, out var tile) ||
                tile == null)
                tile = ground.Terrain;

            CaveTerrainCarveUtility.CarveEntranceDepression(
                tile,
                mouthWorld,
                PrimaryBowlRadiusMeters,
                PrimaryBowlDepthMeters);

            FlattenApproachBench(tile, mouthWorld, primary.transform.forward);
            tile.Flush();

            message =
                $"Primary cave mouth terrain — bowl r={PrimaryBowlRadiusMeters:F0}m depth={PrimaryBowlDepthMeters:F1}m on '{tile.name}'.";
            Debug.Log("[CaveBuild] " + message);
            return true;
        }

        static SurfaceCaveOpeningMarker FindPrimaryOpening()
        {
            SurfaceCaveOpeningMarker fallback = null;
            foreach (var marker in SurfaceWorldGenerator.FindCaveOpenings())
            {
                if (marker == null)
                    continue;
                if (marker.isPrimaryEntrance)
                    return marker;
                fallback ??= marker;
            }

            return fallback;
        }

        static void FlattenApproachBench(Terrain terrain, Vector3 mouthWorld, Vector3 mouthForward)
        {
            var approach = mouthForward;
            approach.y = 0f;
            if (approach.sqrMagnitude < 0.01f)
                approach = Vector3.forward;
            approach.Normalize();

            var points = new Vector3[4];
            for (var i = 0; i < points.Length; i++)
            {
                var t = (i + 1) / (float)(points.Length + 1);
                var p = mouthWorld + approach * Mathf.Lerp(10f, 24f, t);
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                points[i] = p;
            }

            SurfaceTerrainRadialAuthor.FlattenTrailBench(
                terrain,
                points,
                ApproachBenchRadiusMeters,
                flattenStrength: 0.4f);
        }
    }
}
#endif
