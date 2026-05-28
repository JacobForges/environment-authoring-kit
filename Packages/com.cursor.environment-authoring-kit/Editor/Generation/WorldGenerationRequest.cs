namespace EnvironmentAuthoringKit.Editor.Generation
{
    public enum BiomeId
    {
        Forest,
        Jungle,
        Desert,
        Snow,
        Beach,
        City,
        Dungeon,
        Cave
    }

    public enum CaveGenerationMode
    {
        None,
        EntranceOnly,
        FullSystem
    }

    public enum TimeOfDay
    {
        Day,
        Night,
        Dawn,
        Dusk,
        Overcast
    }

    public enum WeatherKind
    {
        Clear,
        Foggy,
        Rainy,
        Stormy
    }

    public enum ScatterDensityLevel
    {
        Empty,
        Sparse,
        Normal,
        Dense
    }

    public enum TerrainHeightStyle
    {
        Flat,
        Hilly,
        Mountains
    }

    public enum BlockoutLayoutKind
    {
        None,
        Arena,
        Paths,
        Rooms,
        CaveSystem,
        CaveEntrance
    }

    public sealed class WorldGenerationRequest
    {
        public string RawDescription = string.Empty;
        public BiomeId Biome = BiomeId.Forest;
        public TimeOfDay Time = TimeOfDay.Day;
        public WeatherKind Weather = WeatherKind.Clear;
        public ScatterDensityLevel Density = ScatterDensityLevel.Normal;
        public TerrainHeightStyle HeightStyle = TerrainHeightStyle.Hilly;
        public BlockoutLayoutKind BlockoutLayout = BlockoutLayoutKind.None;
        public CaveGenerationMode CaveMode = CaveGenerationMode.None;
        public int CaveTunnelSegments = 10;
        public int CaveChamberCount = 3;
        /// <summary>Organic enclosed spline mesh (default). When false, legacy prefab ring tunnels (not recommended).</summary>
        public bool UseSplineMesh = true;
        /// <summary>Minecraft-style block shell with morphing along the spline (primary cave walls).</summary>
        public bool UseBlockTunnel = true;
        /// <summary>Carve terrain heightmap along tunnels and water basin.</summary>
        public bool UseTerrainCarve = true;
        /// <summary>Use a single true 3D cave mesh as primary geometry (recommended for stable enclosed caves).</summary>
        public bool UseTrue3DCaveSystem = true;
        /// <summary>Underground lava/water pools and branch tubes (off by default — material often breaks).</summary>
        public bool IncludeCaveWater = false;
        /// <summary>Layout + flat floor + markers only — no block tunnel, no route ceiling meshes (for art pass / Terrain sculpt).</summary>
        public bool UseLayoutPrototype = false;
        public bool AllowCreateTerrain;

        /// <summary>Surface vs cave orchestration. Default <see cref="SurfaceBuildScope.CaveOnly"/> keeps legacy builds unchanged until set.</summary>
        public SurfaceBuildScope SurfaceScope = SurfaceBuildScope.CaveOnly;

        /// <summary>Radial extent from ground anchor (meters) for trails, water, and mouth markers.</summary>
        public float SurfaceExtentMeters = 220f;

        /// <summary>Directional complement pass count after Florida DEM (4–16; recipe default 8). Each pass is one compass axis.</summary>
        public int SurfaceDirectionCount = 8;

        /// <summary>Blend steps toward world-space FBM target (default 12; not additive noise layers).</summary>
        public int SurfaceTerrainBuildPasses = 12;

        public bool SurfaceIncludeMountains = true;
        public bool SurfaceIncludeWater = true;
        public bool SurfaceIncludeRoads = true;
        public bool SurfaceIncludeTrails = true;

        public float FogDensityMultiplier = 1f;
        public float ColorMood = 0.5f;
        public string PropEmphasis = string.Empty;
        public int Seed = 12345;

        public float CavePathStepLength;
        public float CavePathDropPerStep;
        public float CavePathYawVariance;
        public float CaveChamberSizeMultiplier = 2.35f;
        public int CavePropScatterCount;
        public int CaveMinableTarget;
        public int CaveWaterBranchSegment = -1;
        public float CaveWaterBranchYaw;
        public float CaveEntranceYawDegrees;

        /// <summary>Serialized style id from <see cref="CaveBuildStylePalette"/> (per-build roll).</summary>
        public string BuildVisualStyle = string.Empty;

        /// <summary>Maze generator flavor index (0–4). -1 = pick from seed at generate time.</summary>
        public int MazeGenFlavor = -1;

        /// <summary>Surface opening sector index, or -1 to pick a random marker each build.</summary>
        public int PreferredCaveOpeningSector = -1;

        /// <summary>DEM elevation-grid supersample target (0 = use CaveBuildCursorSettings). 64–256.</summary>
        public int DemSupersampleTargetDim;

        /// <summary>Run enhancement catalog hooks (speed/quality/creative) during this build.</summary>
        public bool RunEnhancementPhases = true;

        public WorldGenerationRequest Clone()
        {
            return new WorldGenerationRequest
            {
                RawDescription = RawDescription,
                Biome = Biome,
                Time = Time,
                Weather = Weather,
                Density = Density,
                HeightStyle = HeightStyle,
                BlockoutLayout = BlockoutLayout,
                CaveMode = CaveMode,
                CaveTunnelSegments = CaveTunnelSegments,
                CaveChamberCount = CaveChamberCount,
                UseSplineMesh = UseSplineMesh,
                UseBlockTunnel = UseBlockTunnel,
                UseTerrainCarve = UseTerrainCarve,
                UseTrue3DCaveSystem = UseTrue3DCaveSystem,
                IncludeCaveWater = IncludeCaveWater,
                AllowCreateTerrain = AllowCreateTerrain,
                SurfaceScope = SurfaceScope,
                SurfaceExtentMeters = SurfaceExtentMeters,
                SurfaceDirectionCount = SurfaceDirectionCount,
                SurfaceTerrainBuildPasses = SurfaceTerrainBuildPasses,
                SurfaceIncludeMountains = SurfaceIncludeMountains,
                SurfaceIncludeWater = SurfaceIncludeWater,
                SurfaceIncludeRoads = SurfaceIncludeRoads,
                SurfaceIncludeTrails = SurfaceIncludeTrails,
                FogDensityMultiplier = FogDensityMultiplier,
                ColorMood = ColorMood,
                PropEmphasis = PropEmphasis,
                Seed = Seed,
                CavePathStepLength = CavePathStepLength,
                CavePathDropPerStep = CavePathDropPerStep,
                CavePathYawVariance = CavePathYawVariance,
                CaveChamberSizeMultiplier = CaveChamberSizeMultiplier,
                CavePropScatterCount = CavePropScatterCount,
                CaveMinableTarget = CaveMinableTarget,
                CaveWaterBranchSegment = CaveWaterBranchSegment,
                CaveWaterBranchYaw = CaveWaterBranchYaw,
                CaveEntranceYawDegrees = CaveEntranceYawDegrees,
                BuildVisualStyle = BuildVisualStyle,
                MazeGenFlavor = MazeGenFlavor,
                PreferredCaveOpeningSector = PreferredCaveOpeningSector,
                DemSupersampleTargetDim = DemSupersampleTargetDim,
                RunEnhancementPhases = RunEnhancementPhases,
            };
        }

        public static WorldGenerationRequest LoadOrDefault()
        {
            return new WorldGenerationRequest
            {
                Seed = UnityEditor.EditorPrefs.GetInt("CaveBuild_LastSeed", 12345),
                SurfaceExtentMeters = UnityEditor.EditorPrefs.GetFloat("CaveBuild_SurfaceExtent", 220f),
                SurfaceIncludeTrails = true,
                SurfaceIncludeRoads = true,
                UseTerrainCarve = true,
            };
        }
    }
}
