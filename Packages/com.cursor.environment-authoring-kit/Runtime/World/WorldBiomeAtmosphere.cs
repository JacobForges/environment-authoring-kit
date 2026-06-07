using UnityEngine;
using UnityEngine.Rendering;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>URP-friendly fog tint, ambient color, and particle mood per biome.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldBiomeAtmosphere : MonoBehaviour
    {
        [SerializeField] float pollInterval = 1.5f;

        float _nextPoll;
        WorldSurfaceBiomeId _current = (WorldSurfaceBiomeId)(-1);
        WorldWeatherDirector _weather;

        void Awake()
        {
            _weather = GetComponent<WorldWeatherDirector>() ?? FindAnyObjectByType<WorldWeatherDirector>();
        }

        void Update()
        {
            if (Time.unscaledTime < _nextPoll)
                return;
            _nextPoll = Time.unscaledTime + pollInterval;

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
                return;

            var biome = WorldBiomeProbe.ResolveAt(player.transform.position);
            if (biome == _current)
                return;

            _current = biome;
            Apply(biome);
        }

        void Apply(WorldSurfaceBiomeId biome)
        {
            var profile = ResolveProfile(biome);

            RenderSettings.fog = true;
            RenderSettings.fogColor = profile.FogColor;
            RenderSettings.fogDensity = profile.FogDensity;
            RenderSettings.fogMode = FogMode.ExponentialSquared;

            if (RenderSettings.ambientMode == AmbientMode.Flat ||
                RenderSettings.ambientMode == AmbientMode.Trilight)
            {
                RenderSettings.ambientSkyColor = profile.AmbientSky;
                RenderSettings.ambientEquatorColor = profile.AmbientEquator;
                RenderSettings.ambientGroundColor = profile.AmbientGround;
            }

            _weather ??= GetComponent<WorldWeatherDirector>();
            _weather?.CaptureBiomeFogBaseline(profile.FogDensity, profile.FogColor);
        }

        readonly struct BiomeAtmosphereProfile
        {
            public readonly Color FogColor;
            public readonly float FogDensity;
            public readonly Color AmbientSky;
            public readonly Color AmbientEquator;
            public readonly Color AmbientGround;
            public readonly Color ParticleTint;

            public BiomeAtmosphereProfile(
                Color fogColor,
                float fogDensity,
                Color ambientSky,
                Color ambientEquator,
                Color ambientGround,
                Color particleTint)
            {
                FogColor = fogColor;
                FogDensity = fogDensity;
                AmbientSky = ambientSky;
                AmbientEquator = ambientEquator;
                AmbientGround = ambientGround;
                ParticleTint = particleTint;
            }
        }

        static BiomeAtmosphereProfile ResolveProfile(WorldSurfaceBiomeId biome) =>
            biome switch
            {
                WorldSurfaceBiomeId.PlayKarst => new BiomeAtmosphereProfile(
                    new Color(0.48f, 0.66f, 0.58f),
                    0.009f,
                    new Color(0.56f, 0.74f, 0.66f),
                    new Color(0.4f, 0.5f, 0.44f),
                    new Color(0.18f, 0.22f, 0.17f),
                    new Color(0.62f, 0.9f, 0.58f)),
                WorldSurfaceBiomeId.FoothillGreen => new BiomeAtmosphereProfile(
                    new Color(0.5f, 0.72f, 0.54f),
                    0.012f,
                    new Color(0.6f, 0.78f, 0.6f),
                    new Color(0.44f, 0.54f, 0.38f),
                    new Color(0.2f, 0.26f, 0.17f),
                    new Color(0.68f, 0.92f, 0.52f)),
                WorldSurfaceBiomeId.PeakStone => new BiomeAtmosphereProfile(
                    new Color(0.54f, 0.62f, 0.8f),
                    0.019f,
                    new Color(0.64f, 0.7f, 0.86f),
                    new Color(0.46f, 0.5f, 0.56f),
                    new Color(0.22f, 0.22f, 0.26f),
                    new Color(0.8f, 0.84f, 0.95f)),
                WorldSurfaceBiomeId.HorizonMist => new BiomeAtmosphereProfile(
                    new Color(0.78f, 0.84f, 0.92f),
                    0.038f,
                    new Color(0.74f, 0.8f, 0.88f),
                    new Color(0.58f, 0.62f, 0.68f),
                    new Color(0.3f, 0.32f, 0.34f),
                    new Color(0.9f, 0.94f, 0.99f)),
                WorldSurfaceBiomeId.AnnexLabyrinth => new BiomeAtmosphereProfile(
                    new Color(0.28f, 0.34f, 0.44f),
                    0.028f,
                    new Color(0.32f, 0.38f, 0.48f),
                    new Color(0.24f, 0.28f, 0.32f),
                    new Color(0.12f, 0.13f, 0.16f),
                    new Color(0.42f, 0.5f, 0.66f)),
                WorldSurfaceBiomeId.BossThreshold => new BiomeAtmosphereProfile(
                    new Color(0.46f, 0.18f, 0.24f),
                    0.018f,
                    new Color(0.52f, 0.22f, 0.28f),
                    new Color(0.34f, 0.16f, 0.2f),
                    new Color(0.12f, 0.07f, 0.09f),
                    new Color(0.98f, 0.32f, 0.4f)),
                WorldSurfaceBiomeId.MixedTransition => new BiomeAtmosphereProfile(
                    new Color(0.52f, 0.66f, 0.56f),
                    0.014f,
                    new Color(0.56f, 0.7f, 0.58f),
                    new Color(0.42f, 0.5f, 0.4f),
                    new Color(0.2f, 0.23f, 0.19f),
                    new Color(0.72f, 0.84f, 0.6f)),
                WorldSurfaceBiomeId.ConceptPreset00 or
                WorldSurfaceBiomeId.ConceptPreset01 or
                WorldSurfaceBiomeId.ConceptPreset02 => new BiomeAtmosphereProfile(
                    new Color(0.46f, 0.64f, 0.52f),
                    0.011f,
                    new Color(0.54f, 0.7f, 0.58f),
                    new Color(0.4f, 0.48f, 0.4f),
                    new Color(0.18f, 0.2f, 0.17f),
                    new Color(0.66f, 0.88f, 0.56f)),
                WorldSurfaceBiomeId.ConceptPreset03 or
                WorldSurfaceBiomeId.ConceptPreset04 or
                WorldSurfaceBiomeId.ConceptPreset05 => new BiomeAtmosphereProfile(
                    new Color(0.5f, 0.58f, 0.72f),
                    0.016f,
                    new Color(0.58f, 0.64f, 0.78f),
                    new Color(0.42f, 0.46f, 0.52f),
                    new Color(0.2f, 0.21f, 0.24f),
                    new Color(0.74f, 0.8f, 0.9f)),
                WorldSurfaceBiomeId.ConceptPreset06 or
                WorldSurfaceBiomeId.ConceptPreset07 or
                WorldSurfaceBiomeId.ConceptPreset08 or
                WorldSurfaceBiomeId.ConceptPreset09 => new BiomeAtmosphereProfile(
                    new Color(0.72f, 0.78f, 0.86f),
                    0.026f,
                    new Color(0.68f, 0.74f, 0.82f),
                    new Color(0.52f, 0.56f, 0.6f),
                    new Color(0.26f, 0.28f, 0.3f),
                    new Color(0.86f, 0.9f, 0.96f)),
                _ => new BiomeAtmosphereProfile(
                    new Color(0.46f, 0.62f, 0.54f),
                    0.01f,
                    new Color(0.52f, 0.68f, 0.78f),
                    new Color(0.38f, 0.44f, 0.4f),
                    new Color(0.18f, 0.18f, 0.16f),
                    new Color(0.72f, 0.86f, 0.68f)),
            };
    }
}
