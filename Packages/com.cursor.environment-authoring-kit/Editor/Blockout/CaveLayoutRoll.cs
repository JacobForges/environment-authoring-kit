using System;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Randomized parameters for one cave generation — unique layout, surface, and visual style each roll.</summary>
    public sealed class CaveLayoutRoll
    {
        public int Seed;
        public int TunnelSegments;
        public int ChamberCount;
        public float StepLength;
        public float DropPerStep;
        public float YawVarianceDegrees;
        public float ChamberSizeMultiplier;
        public int PropScatterCount;
        public int MinableTarget;
        public int WaterBranchAtSegment;
        public float WaterBranchYaw;
        public float EntranceYawDegrees;
        public ScatterDensityLevel Density;

        public float SurfaceExtentMeters;
        public int SurfaceDirectionCount;
        public bool SurfaceIncludeMountains;
        public bool SurfaceIncludeWater;
        public bool SurfaceIncludeRoads;
        public bool SurfaceIncludeTrails;
        public TimeOfDay SurfaceTime;
        public WeatherKind SurfaceWeather;
        public TerrainHeightStyle SurfaceHeightStyle;

        public string VisualStyleId;
        public int MazeGenFlavor;
        public int PreferredOpeningSector;
        public float FogDensityMultiplier;
        public float ColorMood;
        public string PropEmphasis;

        public static CaveLayoutRoll CreateRandom(int? forcedSeed = null)
        {
            var seed = forcedSeed ?? NewSeed();
            var rng = new System.Random(seed);

            var segments = rng.Next(10, 22);
            var chambers = rng.Next(2, 8);
            var dirChoices = new[] { 1, 2, 3, 4, 6, 8 };

            return new CaveLayoutRoll
            {
                Seed = seed,
                TunnelSegments = segments,
                ChamberCount = chambers,
                StepLength = 7.5f + (float)rng.NextDouble() * 7f,
                DropPerStep = 0.16f + (float)rng.NextDouble() * 0.28f,
                YawVarianceDegrees = 8f + (float)rng.NextDouble() * 28f,
                ChamberSizeMultiplier = 1.85f + (float)rng.NextDouble() * 1.1f,
                PropScatterCount = rng.Next(10, 34),
                MinableTarget = rng.Next(6, 22),
                WaterBranchAtSegment = rng.Next(Mathf.Max(2, segments / 4), Mathf.Max(3, segments - 2)),
                WaterBranchYaw = rng.NextDouble() > 0.5 ? 65f + (float)rng.NextDouble() * 50f : -65f - (float)rng.NextDouble() * 50f,
                EntranceYawDegrees = (float)(rng.NextDouble() * 360),
                Density = (ScatterDensityLevel)rng.Next(1, 4),

                SurfaceExtentMeters = 165f + (float)rng.NextDouble() * 180f,
                SurfaceDirectionCount = dirChoices[rng.Next(dirChoices.Length)],
                SurfaceIncludeMountains = rng.NextDouble() > 0.12,
                SurfaceIncludeWater = rng.NextDouble() > 0.18,
                SurfaceIncludeRoads = rng.NextDouble() > 0.25,
                SurfaceIncludeTrails = rng.NextDouble() > 0.08,
                SurfaceTime = (TimeOfDay)rng.Next(0, 5),
                SurfaceWeather = (WeatherKind)rng.Next(0, 4),
                SurfaceHeightStyle = rng.NextDouble() > 0.38
                    ? TerrainHeightStyle.Mountains
                    : TerrainHeightStyle.Hilly,

                VisualStyleId = CaveBuildStylePalette.PickVisualStyle(rng),
                MazeGenFlavor = rng.Next(0, 6),
                SurfaceTileLayoutVariant = rng.Next(0, 48),
                PreferredOpeningSector = -1,
                FogDensityMultiplier = 0.65f + (float)rng.NextDouble() * 0.9f,
                ColorMood = (float)rng.NextDouble(),
                PropEmphasis = PickPropEmphasis(rng),
            };
        }

        static string PickPropEmphasis(System.Random rng)
        {
            var options = new[]
            {
                "mossy_boulders",
                "crystals_sparse",
                "roots_and_stalactites",
                "ruined_pillars",
                "lava_glow_accents",
                "flooded_stones",
                "ancient_carvings",
            };
            return options[rng.Next(options.Length)];
        }

        public static int NewSeed()
        {
            var guid = Guid.NewGuid().ToByteArray();
            var a = BitConverter.ToInt32(guid, 0);
            var b = BitConverter.ToInt32(guid, 4);
            var c = BitConverter.ToInt32(guid, 8);
            return unchecked(a ^ b ^ c ^ Environment.TickCount ^ (int)DateTime.UtcNow.Ticks);
        }

        public void ApplyTo(WorldGenerationRequest request)
        {
            request.Seed = Seed;
            request.CaveTunnelSegments = TunnelSegments;
            request.CaveChamberCount = ChamberCount;
            request.Density = Density;
            request.CavePathStepLength = StepLength;
            request.CavePathDropPerStep = DropPerStep;
            request.CavePathYawVariance = YawVarianceDegrees;
            request.CaveChamberSizeMultiplier = ChamberSizeMultiplier;
            request.CavePropScatterCount = PropScatterCount;
            request.CaveMinableTarget = MinableTarget;
            request.CaveWaterBranchSegment = WaterBranchAtSegment;
            request.CaveWaterBranchYaw = WaterBranchYaw;
            request.CaveEntranceYawDegrees = EntranceYawDegrees;

            request.SurfaceExtentMeters = EnvironmentKitHardwareBudget.ClampSurfaceExtent(SurfaceExtentMeters);
            request.SurfaceDirectionCount = Mathf.Max(1, SurfaceDirectionCount);
            request.SurfaceTerrainBuildPasses = SurfaceTerrainCenteredAuthor.DefaultPassCount;
            request.SurfaceIncludeMountains = SurfaceIncludeMountains;
            request.SurfaceIncludeWater = SurfaceIncludeWater;
            request.SurfaceIncludeRoads = SurfaceIncludeRoads;
            request.SurfaceIncludeTrails = SurfaceIncludeTrails;
            request.Time = SurfaceTime;
            request.Weather = SurfaceWeather;
            request.HeightStyle = SurfaceHeightStyle;
            request.FogDensityMultiplier = FogDensityMultiplier;
            request.ColorMood = ColorMood;
            request.PropEmphasis = PropEmphasis ?? string.Empty;
            request.BuildVisualStyle = VisualStyleId ?? CaveBuildStylePalette.Classic;
            request.MazeGenFlavor = MazeGenFlavor;
            request.SurfaceTileLayoutVariant = SurfaceTileLayoutVariant;
            request.PreferredCaveOpeningSector = PreferredOpeningSector;
        }

        public override string ToString() =>
            $"seed={Seed} maze={MazeGenFlavor} style={VisualStyleId} surface={SurfaceExtentMeters:F0}m passes=5 " +
            $"seg={TunnelSegments} ch={ChamberCount} step={StepLength:F1}m yaw±{YawVarianceDegrees:F0}°";

        /// <summary>Cave pipeline only — omits surface sector fields (surface already built).</summary>
        public string ToCaveOnlyString() =>
            $"seed={Seed} maze={MazeGenFlavor} style={VisualStyleId} " +
            $"seg={TunnelSegments} ch={ChamberCount} step={StepLength:F1}m yaw±{YawVarianceDegrees:F0}°";
    }
}
