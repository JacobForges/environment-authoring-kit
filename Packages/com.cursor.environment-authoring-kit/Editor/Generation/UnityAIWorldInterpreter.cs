using System;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Generation
{
    /// <summary>
    /// Optional enrichment when Unity AI packages are present. Falls back silently otherwise.
    /// </summary>
    public static class UnityAIWorldInterpreter
    {
        public static bool IsAvailable => FindAssistantType() != null;

        public static void TryEnrich(WorldGenerationRequest request)
        {
            if (request == null || !IsAvailable)
                return;

            try
            {
                ApplyHeuristicEnrichment(request);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Environment Kit] Unity AI enrichment skipped: {ex.Message}");
            }
        }

        static void ApplyHeuristicEnrichment(WorldGenerationRequest request)
        {
            var text = request.RawDescription.ToLowerInvariant();

            if (text.Contains("cozy") || text.Contains("warm"))
                request.ColorMood = 0.75f;
            if (text.Contains("cold") || text.Contains("bleak"))
                request.ColorMood = 0.2f;
            if (text.Contains("neon") || text.Contains("cyber"))
            {
                request.Biome = BiomeId.City;
                request.Time = TimeOfDay.Night;
                request.PropEmphasis = "neon";
            }
            if (text.Contains("clearing"))
                request.BlockoutLayout = BlockoutLayoutKind.Arena;
            if (text.Contains("moody") || text.Contains("eerie"))
            {
                request.Weather = WeatherKind.Foggy;
                request.FogDensityMultiplier = 1.4f;
            }

            if (text.Contains("cave") || text.Contains("cavern") || text.Contains("grotto"))
            {
                request.Biome = BiomeId.Cave;
                request.CaveMode = text.Contains("entrance") && !text.Contains("system")
                    ? CaveGenerationMode.EntranceOnly
                    : CaveGenerationMode.FullSystem;
                request.Time = TimeOfDay.Night;
                request.FogDensityMultiplier = 1.6f;
            }
        }

        static Type FindAssistantType()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.GetName().Name.StartsWith("Unity.AI", StringComparison.Ordinal) &&
                    !assembly.GetName().Name.StartsWith("com.unity.ai", StringComparison.Ordinal))
                    continue;

                var type = assembly.GetType("Unity.AI.Assistant.Assistant");
                if (type != null)
                    return type;

                foreach (var t in assembly.GetTypes())
                {
                    if (t.Name.Contains("Assistant", StringComparison.Ordinal))
                        return t;
                }
            }

            return null;
        }
    }
}
