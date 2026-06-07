using System;
using UnityEngine;

namespace EnvironmentAuthoringKit
{
    [DisallowMultipleComponent]
    public sealed class GeneratedWorldMetadata : MonoBehaviour
    {
        [SerializeField] string description;
        [SerializeField] string biomeId;
        [SerializeField] string timeOfDay;
        [SerializeField] string weather;
        [SerializeField] int seed;
        [SerializeField] bool xrOptimized;
        [SerializeField] string generatedAtUtc;

        public string Description => description;
        public string BiomeId => biomeId;
        public string TimeOfDay => timeOfDay;
        public string Weather => weather;
        public int Seed => seed;
        public bool XrOptimized => xrOptimized;
        public string GeneratedAtUtc => generatedAtUtc;

        public void Apply(
            string worldDescription,
            string biome,
            string time,
            string weatherValue,
            int generationSeed,
            bool optimizedForXr)
        {
            description = worldDescription ?? string.Empty;
            biomeId = biome ?? string.Empty;
            timeOfDay = time ?? string.Empty;
            weather = weatherValue ?? string.Empty;
            seed = generationSeed;
            xrOptimized = optimizedForXr;
            generatedAtUtc = DateTime.UtcNow.ToString("o");
        }
    }
}
