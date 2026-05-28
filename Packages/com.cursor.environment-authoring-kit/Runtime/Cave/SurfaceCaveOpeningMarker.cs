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
        [Tooltip("Suggested underground depth for this entrance.")]
        public float suggestedDepthMeters = 12f;
    }
}
