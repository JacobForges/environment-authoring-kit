#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Ten numbered FullWorld concept layouts (0–9). Each has a guide PNG — similar per seed, not a clone.
    /// </summary>
    public static class FullWorldConceptLayoutCatalog
    {
        public const int ConceptCount = 10;
        public const string ConceptsRootRel =
            "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts";
        public const string RandomOnBuildPrefKey = "EnvironmentKit_FullWorldConceptRandomOnBuild";

        public static string GetDisplayName(int index) =>
            index >= 0 && index < ConceptCount ? Definitions[index].DisplayName : "Unknown";

        public static string GetStyleId(int index) =>
            index >= 0 && index < ConceptCount ? Definitions[index].Id : Definitions[0].Id;

        public static string GetConceptImageRel(int index)
        {
            if (index < 0 || index >= ConceptCount)
                index = 0;
            return $"{ConceptsRootRel}/{index:D2}/concept.png";
        }

        public static void ApplyByIndex(WorldGenerationRequest request, int index)
        {
            if (request == null || index < 0 || index >= ConceptCount)
                return;

            request.GenerationStyleId = Definitions[index].Id;
            request.ConceptLayoutIndex = index;
            request.EnsureFullWorldSurfaceContract();
            Definitions[index].Apply?.Invoke(request);
        }

        /// <summary>Deterministic roll from build seed (guide index only — geometry still varies by seed).</summary>
        public static int RollIndex(int seed) =>
            Mathf.Abs(seed * 1103515245 + 12345) % ConceptCount;

        public static int RollRandomIndex() => RollIndex(UnityEngine.Random.Range(1, int.MaxValue));

        public static bool RandomOnBuildEnabled =>
            UnityEditor.EditorPrefs.GetBool(RandomOnBuildPrefKey, true);

        public static void SetRandomOnBuild(bool enabled) =>
            UnityEditor.EditorPrefs.SetBool(RandomOnBuildPrefKey, enabled);

        public static int ResolveIndexForBuild(int layoutSeed, int? manualOverride = null)
        {
            if (manualOverride.HasValue)
                return Mathf.Clamp(manualOverride.Value, 0, ConceptCount - 1);

            if (!RandomOnBuildEnabled)
                return FullWorldGenerationStylePreset.LoadSelectedIndex();

            var rolled = RollIndex(layoutSeed);
            FullWorldGenerationStylePreset.SaveSelectedIndex(rolled);
            return rolled;
        }

        public static readonly ConceptDefinition[] Definitions =
        {
            new("concept_00_ideal_labyrinth", "0 — Ideal labyrinth (~289 tiles, play + maze + peaks)", ApplyIdeal),
            new("concept_01_classic_rings", "1 — Classic open rings (~289 tiles, minimal maze)", ApplyClassic),
            new("concept_02_florida_karst", "2 — Florida karst (flat play + water)", ApplyFlorida),
            new("concept_03_appalachian_peaks", "3 — Appalachian tall peaks (~289 tiles)", ApplyAppalachian),
            new("concept_04_coastal_fog", "4 — Coastal fog low mountains", ApplyCoastalFog),
            new("concept_05_tomb_raider_vertical", "5 — Tomb Raider vertical cave + maze", ApplyTombRaider),
            new("concept_06_sparse_trails", "6 — Sparse jumps + trail network", ApplySparseTrails),
            new("concept_07_water_labyrinth", "7 — Water basins + foothill maze", ApplyWaterLabyrinth),
            new("concept_08_perimeter_trails", "8 — Perimeter trail ring", ApplyPerimeterTrails),
            new("concept_09_speed_minimal", "9 — Speed minimal (81-tile core only, fast)", ApplySpeedMinimal),
        };

        static void ApplyIdeal(WorldGenerationRequest r)
        {
            r.UseTombRaiderLabyrinthCadence = true;
            r.SurfaceIncludeMountainLabyrinth = true;
            r.UsePeakSummitCap = true;
            r.UseFlatSummitPlaza = false;
            r.UseMountainWildernessCaveMouth = true;
            r.UseOuterRingMountains = true;
            r.MazeGenFlavor = (int)CaveMazeGenFlavor.WalkwayLabyrinthCavern;
            r.PropEmphasis = AppendProp(r.PropEmphasis, "ideal_49_labyrinth_foothills");
        }

        static void ApplyClassic(WorldGenerationRequest r)
        {
            r.UseTombRaiderLabyrinthCadence = false;
            r.SurfaceIncludeMountainLabyrinth = false;
            r.UseOuterRingMountains = true;
            r.MazeGenFlavor = (int)CaveMazeGenFlavor.OrganicCellular;
        }

        static void ApplyFlorida(WorldGenerationRequest r)
        {
            r.SurfaceIncludeWater = true;
            r.UseMountainWildernessCaveMouth = false;
            r.HeightStyle = TerrainHeightStyle.Hilly;
            r.MazeGenFlavor = (int)CaveMazeGenFlavor.OrganicCellular;
            r.PropEmphasis = AppendProp(r.PropEmphasis, "florida_karst");
        }

        static void ApplyAppalachian(WorldGenerationRequest r)
        {
            r.HeightStyle = TerrainHeightStyle.Mountains;
            r.SurfaceIncludeMountainLabyrinth = true;
            r.UsePeakSummitCap = true;
            r.UseFlatSummitPlaza = false;
            r.PropEmphasis = AppendProp(r.PropEmphasis, "appalachian_ridge");
        }

        static void ApplyCoastalFog(WorldGenerationRequest r)
        {
            r.Weather = WeatherKind.Foggy;
            r.FogDensityMultiplier = 1.35f;
            r.SurfaceIncludeWater = true;
            r.HeightStyle = TerrainHeightStyle.Hilly;
        }

        static void ApplyTombRaider(WorldGenerationRequest r)
        {
            r.UseTombRaiderLabyrinthCadence = true;
            r.MazeGenFlavor = (int)CaveMazeGenFlavor.VerticalClimb;
            r.SurfaceIncludeMountainLabyrinth = true;
        }

        static void ApplySparseTrails(WorldGenerationRequest r)
        {
            r.MazeGenFlavor = (int)CaveMazeGenFlavor.SparseJumps;
            r.SurfaceIncludeTrails = true;
            r.CaveChamberCount = Mathf.Max(r.CaveChamberCount, 4);
        }

        static void ApplyWaterLabyrinth(WorldGenerationRequest r)
        {
            r.SurfaceIncludeWater = true;
            r.SurfaceIncludeMountainLabyrinth = true;
            r.UseMountainWildernessCaveMouth = true;
        }

        static void ApplyPerimeterTrails(WorldGenerationRequest r)
        {
            r.SurfaceIncludeTrails = true;
            r.SurfaceIncludeRoads = false;
            r.SurfaceIncludeMountainLabyrinth = true;
        }

        static void ApplySpeedMinimal(WorldGenerationRequest r)
        {
            r.UseExtendedOpenWorldGrid = false;
            r.RunEnhancementPhases = false;
            r.SurfaceDirectionCount = 4;
            r.SurfaceTerrainBuildPasses = 8;
            r.SurfaceIncludeRoads = false;
            r.SatelliteCaveCount = 3;
        }

        static string AppendProp(string existing, string token) =>
            string.IsNullOrEmpty(existing) ? token : existing + "," + token;

        public readonly struct ConceptDefinition
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly Action<WorldGenerationRequest> Apply;

            public ConceptDefinition(string id, string displayName, Action<WorldGenerationRequest> apply)
            {
                Id = id;
                DisplayName = displayName;
                Apply = apply;
            }
        }
    }
}
#endif
