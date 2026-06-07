using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Marks a planned cave mouth on the open-sky surface for underground builds to align to.
    /// </summary>
    public sealed class SurfaceCaveOpeningMarker : MonoBehaviour
    {
        public int sectorIndex;
        public float distanceFromCenterMeters;
        [Tooltip("Primary mega-cave at PortalFive; satellites are smaller POI systems.")]
        public bool isPrimaryEntrance;
        [Tooltip("Grid offset from SurfaceTerrainMain (0,0) when using 9-tile square.")]
        public Vector2Int terrainTileOffset;
        [Tooltip("Suggested underground depth for this entrance.")]
        public float suggestedDepthMeters = 12f;
    }
}
