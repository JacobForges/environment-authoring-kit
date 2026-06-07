using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Serialized grid slot for a surface terrain tile — used by merge, stitch, props, and AI agents.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceTerrainGridIndex : MonoBehaviour
    {
        public const string PlayMainRing = "play_main";
        public const string PlayNeighborRing = "play_neighbor";
        public const string FoothillRing = "foothill";
        public const string PeakRing = "peak";
        public const string HorizonRing = "horizon";
        public const string LegacyWildernessRing = "wilderness_legacy";

        [Tooltip("Grid X offset from SurfaceTerrainMain (0,0).")]
        public int gridX;

        [Tooltip("Grid Z offset from SurfaceTerrainMain (0,0).")]
        public int gridZ;

        [Tooltip("play_main | play_neighbor | foothill | peak | horizon")]
        public string ring = PlayNeighborRing;

        [Tooltip("Stable id, e.g. play_1_0 or foothill_2_-1")]
        public string tileId = string.Empty;

        [Tooltip("Terrain this tile is edge-seeded / stitched from (inner ring).")]
        public string mergeTargetTileId = string.Empty;

        [Tooltip("Chebyshev distance from play disk center tile.")]
        public int chebyshevFromPlay;

        public void Apply(int x, int z, string ringId, string mergeTargetId)
        {
            gridX = x;
            gridZ = z;
            ring = ringId ?? PlayNeighborRing;
            mergeTargetTileId = mergeTargetId ?? string.Empty;
            chebyshevFromPlay = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            tileId = BuildTileId(ringId, x, z);
        }

        public static string BuildTileId(string ringId, int x, int z)
        {
            var prefix = ringId switch
            {
                PlayMainRing => "play",
                PlayNeighborRing => "play",
                FoothillRing => "foothill",
                PeakRing => "peak",
                HorizonRing => "horizon",
                LegacyWildernessRing => "wilderness",
                _ => "tile",
            };
            return $"{prefix}_{x}_{z}";
        }
    }
}
