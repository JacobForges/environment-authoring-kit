#if UNITY_EDITOR
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Per-tile edge sculpt: each side pushes relief along its outward normal; corners blend both axes.
    /// World-space sampling keeps relief continuous for streamed tiles.
    /// </summary>
    public static class SurfaceTerrainDirectionalEdgeSculpt
    {
        public struct EdgeWeights
        {
            public float West;
            public float East;
            public float South;
            public float North;
            public float OutX;
            public float OutZ;
            public float MaxWeight;
        }

        /// <summary>0 at tile center, 1 on the given edge, smooth corner blend.</summary>
        public static EdgeWeights ComputeTileEdgeWeights(
            float wx,
            float wz,
            Vector3 tileOrigin,
            Vector3 tileSize,
            float bandMeters)
        {
            var band = Mathf.Max(4f, bandMeters);
            var lx = wx - tileOrigin.x;
            var lz = wz - tileOrigin.z;
            var sx = Mathf.Max(tileSize.x, 1f);
            var sz = Mathf.Max(tileSize.z, 1f);

            var west = EdgeFalloff(lx, band);
            var east = EdgeFalloff(sx - lx, band);
            var south = EdgeFalloff(lz, band);
            var north = EdgeFalloff(sz - lz, band);

            var outX = -west + east;
            var outZ = -south + north;
            var mag = Mathf.Sqrt(outX * outX + outZ * outZ);
            if (mag > 0.001f)
            {
                outX /= mag;
                outZ /= mag;
            }

            var max = Mathf.Max(Mathf.Max(west, east), Mathf.Max(south, north));
            return new EdgeWeights
            {
                West = west,
                East = east,
                South = south,
                North = north,
                OutX = outX,
                OutZ = outZ,
                MaxWeight = max,
            };
        }

        /// <summary>Stronger sculpt on world exterior; lighter on interior tile seams.</summary>
        public static float ExteriorStrength(
            float wx,
            float wz,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            float marginMeters,
            in EdgeWeights edges)
        {
            var interior = 0.32f;
            var exterior = 1f;
            var strength = interior;

            if (wx - minX < marginMeters && edges.West > 0.05f)
                strength = Mathf.Max(strength, Mathf.Lerp(interior, exterior, edges.West));
            if (maxX - wx < marginMeters && edges.East > 0.05f)
                strength = Mathf.Max(strength, Mathf.Lerp(interior, exterior, edges.East));
            if (wz - minZ < marginMeters && edges.South > 0.05f)
                strength = Mathf.Max(strength, Mathf.Lerp(interior, exterior, edges.South));
            if (maxZ - wz < marginMeters && edges.North > 0.05f)
                strength = Mathf.Max(strength, Mathf.Lerp(interior, exterior, edges.North));

            return strength;
        }

        static float EdgeFalloff(float distFromEdge, float bandMeters) =>
            Mathf.Clamp01(1f - distFromEdge / Mathf.Max(bandMeters, 0.01f));
    }
}
#endif
