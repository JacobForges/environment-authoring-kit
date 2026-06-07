using System;
using System.Linq;

namespace EnvironmentAuthoringKit.Editor.Generation
{
    public static class DescriptionParser
    {
        public static WorldGenerationRequest Parse(string description, int seed)
        {
            var request = new WorldGenerationRequest
            {
                RawDescription = description ?? string.Empty,
                Seed = seed
            };

            var text = request.RawDescription.ToLowerInvariant();

            request.Biome = ParseBiome(text);
            request.Time = ParseTime(text);
            request.Weather = ParseWeather(text);
            request.Density = ParseDensity(text);
            request.HeightStyle = ParseHeight(text);
            request.BlockoutLayout = ParseBlockout(text);
            request.AllowCreateTerrain = ContainsAny(text, "terrain", "landscape", "heightmap", "hills");
            ApplyCaveIntent(text, request);

            if (ContainsAny(text, "fog", "misty", "mist", "hazy"))
                request.Weather = WeatherKind.Foggy;

            return request;
        }

        static void ApplyCaveIntent(string text, WorldGenerationRequest request)
        {
            var isCave = request.Biome == BiomeId.Cave ||
                         ContainsAny(text, "cave", "cavern", "grotto", "tunnel", "underground", "stalactite", "stalagmite");

            if (!isCave)
                return;

            request.AllowCreateTerrain = false;
            request.Biome = BiomeId.Cave;
            request.Time = TimeOfDay.Night;
            request.Weather = WeatherKind.Foggy;
            request.FogDensityMultiplier = 1.8f;
            request.HeightStyle = TerrainHeightStyle.Hilly;

            if (ContainsAny(text, "entrance", "mouth", "opening", "gateway") &&
                !ContainsAny(text, "system", "network", "tunnels", "maze", "complex"))
            {
                request.CaveMode = CaveGenerationMode.EntranceOnly;
                request.BlockoutLayout = BlockoutLayoutKind.CaveEntrance;
            }
            else
            {
                request.CaveMode = CaveGenerationMode.FullSystem;
                request.BlockoutLayout = BlockoutLayoutKind.CaveSystem;
            }

            if (ContainsAny(text, "large", "huge", "extensive", "maze"))
            {
                request.CaveTunnelSegments = 18;
                request.CaveChamberCount = 5;
            }
            else if (ContainsAny(text, "small", "tight", "narrow"))
            {
                request.CaveTunnelSegments = 6;
                request.CaveChamberCount = 2;
            }

            if (ContainsAny(text, "sparse", "empty"))
                request.Density = ScatterDensityLevel.Sparse;
            else if (ContainsAny(text, "dense", "cluttered", "stalactite"))
                request.Density = ScatterDensityLevel.Dense;
        }

        static BiomeId ParseBiome(string text)
        {
            if (ContainsAny(text, "cave", "cavern", "grotto", "stalactite", "stalagmite")) return BiomeId.Cave;
            if (ContainsAny(text, "jungle", "tropical")) return BiomeId.Jungle;
            if (ContainsAny(text, "desert", "sand", "dune")) return BiomeId.Desert;
            if (ContainsAny(text, "snow", "winter", "ice", "arctic")) return BiomeId.Snow;
            if (ContainsAny(text, "beach", "coast", "ocean", "shore")) return BiomeId.Beach;
            if (ContainsAny(text, "city", "urban", "neon", "street")) return BiomeId.City;
            if (ContainsAny(text, "dungeon", "underground")) return BiomeId.Dungeon;
            if (ContainsAny(text, "forest", "woods", "pine", "trees")) return BiomeId.Forest;
            return BiomeId.Forest;
        }

        static TimeOfDay ParseTime(string text)
        {
            if (ContainsAny(text, "night", "midnight")) return TimeOfDay.Night;
            if (ContainsAny(text, "dawn", "sunrise", "morning")) return TimeOfDay.Dawn;
            if (ContainsAny(text, "dusk", "sunset", "evening")) return TimeOfDay.Dusk;
            if (ContainsAny(text, "overcast", "cloudy", "grey", "gray")) return TimeOfDay.Overcast;
            if (ContainsAny(text, "noon", "day", "afternoon")) return TimeOfDay.Day;
            return TimeOfDay.Day;
        }

        static WeatherKind ParseWeather(string text)
        {
            if (ContainsAny(text, "storm", "thunder", "lightning")) return WeatherKind.Stormy;
            if (ContainsAny(text, "rain", "rainy", "wet")) return WeatherKind.Rainy;
            if (ContainsAny(text, "fog", "foggy", "misty", "mist", "haze")) return WeatherKind.Foggy;
            if (ContainsAny(text, "clear", "sunny")) return WeatherKind.Clear;
            return WeatherKind.Clear;
        }

        static ScatterDensityLevel ParseDensity(string text)
        {
            if (ContainsAny(text, "empty", "barren", "bare")) return ScatterDensityLevel.Empty;
            if (ContainsAny(text, "sparse", "scattered", "few")) return ScatterDensityLevel.Sparse;
            if (ContainsAny(text, "dense", "thick", "lush", "crowded")) return ScatterDensityLevel.Dense;
            return ScatterDensityLevel.Normal;
        }

        static TerrainHeightStyle ParseHeight(string text)
        {
            if (ContainsAny(text, "flat", "plains", "plateau")) return TerrainHeightStyle.Flat;
            if (ContainsAny(text, "mountain", "mountains", "peaks", "alpine")) return TerrainHeightStyle.Mountains;
            if (ContainsAny(text, "hill", "hilly", "rolling")) return TerrainHeightStyle.Hilly;
            return TerrainHeightStyle.Hilly;
        }

        static BlockoutLayoutKind ParseBlockout(string text)
        {
            if (ContainsAny(text, "arena", "combat zone", "battle")) return BlockoutLayoutKind.Arena;
            if (ContainsAny(text, "path", "paths", "trail", "road")) return BlockoutLayoutKind.Paths;
            if (ContainsAny(text, "room", "rooms", "interior", "corridor")) return BlockoutLayoutKind.Rooms;
            if (ContainsAny(text, "cave system", "tunnel network", "cave network", "caverns")) return BlockoutLayoutKind.CaveSystem;
            if (ContainsAny(text, "cave entrance", "cavern entrance", "cave mouth")) return BlockoutLayoutKind.CaveEntrance;
            if (ContainsAny(text, "clearing", "platform", "plaza")) return BlockoutLayoutKind.Arena;
            return BlockoutLayoutKind.None;
        }

        static bool ContainsAny(string text, params string[] tokens)
        {
            return tokens.Any(token => text.Contains(token, StringComparison.Ordinal));
        }
    }
}
