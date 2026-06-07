using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Tags a placed landmark mesh with manifest id + biome.</summary>
    public sealed class WorldLandmarkMarker : MonoBehaviour
    {
        [SerializeField] string landmarkId = string.Empty;
        [SerializeField] WorldSurfaceBiomeId biome;

        public string LandmarkId => landmarkId;

        public void Configure(string id, WorldSurfaceBiomeId biomeId)
        {
            landmarkId = id ?? string.Empty;
            biome = biomeId;
        }
    }
}
