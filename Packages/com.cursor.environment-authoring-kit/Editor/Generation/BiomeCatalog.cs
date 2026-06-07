using EnvironmentAuthoringKit.Editor.Atmosphere;
using EnvironmentAuthoringKit.Editor.Scatter;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Generation
{
    [System.Serializable]
    public sealed class BiomeEntry
    {
        public BiomeId biomeId;
        public TerrainDressingPreset terrainPreset;
        public ScatterProfile scatterProfile;
        public AtmospherePreset atmospherePreset;
        public Texture2D heightmapStamp;
        public BlockoutLayoutKind defaultBlockout = BlockoutLayoutKind.None;
    }

    [CreateAssetMenu(fileName = "BiomeCatalog", menuName = "Environment Kit/Biome Catalog")]
    public sealed class BiomeCatalog : ScriptableObject
    {
        public BiomeEntry[] biomes = System.Array.Empty<BiomeEntry>();

        public bool TryGet(BiomeId id, out BiomeEntry entry)
        {
            foreach (var biome in biomes)
            {
                if (biome != null && biome.biomeId == id)
                {
                    entry = biome;
                    return true;
                }
            }

            entry = null;
            return false;
        }

        public BiomeEntry GetOrDefault(BiomeId id)
        {
            if (TryGet(id, out var entry))
                return entry;

            return biomes is { Length: > 0 } ? biomes[0] : null;
        }
    }
}
