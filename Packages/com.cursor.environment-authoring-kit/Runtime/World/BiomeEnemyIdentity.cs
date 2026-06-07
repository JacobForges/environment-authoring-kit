using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Marks which surface biome an enemy belongs to; wrong-biome presence is treated as legendary.</summary>
    public sealed class BiomeEnemyIdentity : MonoBehaviour
    {
        public WorldSurfaceBiomeId nativeBiome = WorldSurfaceBiomeId.PlayKarst;
        public bool isLegendaryWanderer;

        public void Configure(WorldSurfaceBiomeId native, bool legendary)
        {
            nativeBiome = native;
            isLegendaryWanderer = legendary;
            BiomeEnemyCombatScaling.Apply(this);
        }

        void OnEnable() => BiomeEnemyCombatScaling.Apply(this);
    }
}
