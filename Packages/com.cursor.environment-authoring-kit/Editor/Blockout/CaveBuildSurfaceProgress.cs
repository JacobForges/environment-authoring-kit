#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Weighted 0–1 progress for the FullWorld surface pipeline (terrain, props, ladder).
    /// Props use contract targets; terrain phases and ladder rungs use fixed weights.
    /// </summary>
    public static class CaveBuildSurfaceProgress
    {
        const int WeightPerTerrainPhase = 120;
        const int WeightPropsSetup = 45;
        const int WeightPropPolishPerCategory = 180;
        const int WeightPropWideSpreadPerCategory = 140;
        const int WeightCraterTilePaced = 14;
        const int WeightLadderRung = 38;
        const int WeightLadderIterationBuffer = 220;

        static int _totalWeight;
        static int _doneWeight;
        static string _detail = string.Empty;
        static int _propCategoryBudget;
        static int _propCategoryReported;

        public static float Progress01 =>
            _totalWeight > 0 ? Mathf.Clamp01(_doneWeight / (float)_totalWeight) : 0f;

        public static void BeginSession(int terrainTileCount)
        {
            terrainTileCount = Mathf.Max(1, terrainTileCount);
            _doneWeight = 0;
            _detail = "Surface pipeline starting…";
            _propCategoryBudget = 0;
            _propCategoryReported = 0;

            var propTargets = 0;
            foreach (var cat in PropCategories)
                propTargets += SurfaceTerrainPropPlacementRegion.TargetCountForCategory(cat, terrainTileCount);

            _totalWeight =
                SurfaceTerrainAiPhases.PhaseCount * WeightPerTerrainPhase +
                WeightPropsSetup +
                propTargets +
                PropCategories.Length * WeightPropPolishPerCategory +
                PropCategories.Length * WeightPropWideSpreadPerCategory +
                terrainTileCount * WeightCraterTilePaced * 2 +
                SurfaceTerrainBuildLadder.RungOrder.Length * WeightLadderRung +
                WeightLadderIterationBuffer;
        }

        static readonly SurfacePropCategory[] PropCategories =
        {
            SurfacePropCategory.Trees,
            SurfacePropCategory.Grass,
            SurfacePropCategory.Bushes,
            SurfacePropCategory.GroundCover,
        };

        public static void CompleteTerrainPhase(int phaseIndex)
        {
            if (phaseIndex < 0 || phaseIndex >= SurfaceTerrainAiPhases.PhaseTitles.Length)
                return;

            Advance(
                WeightPerTerrainPhase,
                SurfaceTerrainAiPhases.PhaseTitles[phaseIndex]);
        }

        public static void CompletePropsSetup(string detail)
        {
            Advance(WeightPropsSetup, detail);
        }

        public static void BeginPropCategory(SurfacePropCategory category, int targetCount)
        {
            _propCategoryBudget = Mathf.Max(1, targetCount);
            _propCategoryReported = 0;
            _detail = $"Props {category} 0/{targetCount}";
            Show();
        }

        public static void ReportPropCategoryProgress(SurfacePropCategory category, int placed, int target)
        {
            target = Mathf.Max(1, target);
            placed = Mathf.Clamp(placed, 0, target);
            var budget = _propCategoryBudget > 0 ? _propCategoryBudget : target;
            var delta = placed - _propCategoryReported;
            if (delta <= 0)
            {
                _detail = $"Props {category} {placed}/{target}";
                Show();
                return;
            }

            _propCategoryReported = placed;
            _doneWeight += delta;
            _detail = $"Props {category} {placed}/{target}";
            Show();
        }

        public static void CompletePropCategory(SurfacePropCategory category, int placed, int target)
        {
            target = Mathf.Max(1, target);
            placed = Mathf.Clamp(placed, 0, target);
            var budget = _propCategoryBudget > 0 ? _propCategoryBudget : target;
            var remaining = budget - _propCategoryReported;
            if (remaining > 0)
                _doneWeight += remaining;

            _propCategoryReported = 0;
            _propCategoryBudget = 0;
            _detail = $"Props {category} done ({placed}/{target})";
            Show();
        }

        public static void CompletePropPolish(SurfacePropCategory category)
        {
            Advance(WeightPropPolishPerCategory, $"Polish {category}");
        }

        public static void CompletePropWideSpread(SurfacePropCategory category)
        {
            Advance(WeightPropWideSpreadPerCategory, $"Wide spread {category}");
        }

        public static void CompleteCraterTile(int tileIndex, int tileCount, string context)
        {
            Advance(
                WeightCraterTilePaced,
                $"[{context}] crater repair tile {tileIndex}/{tileCount}");
        }

        public static void CompleteLadderRung(string rungId, int rungIndex, int rungCount)
        {
            Advance(
                WeightLadderRung,
                $"Terrain ladder {rungIndex + 1}/{rungCount} — {rungId}");
        }

        public static void Show(string detail = null)
        {
            if (!string.IsNullOrEmpty(detail))
                _detail = detail;

            var t = 0.08f + Progress01 * 0.84f;
            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                $"[Surface] {_detail} ({Mathf.RoundToInt(Progress01 * 100f)}%)",
                t);
            CaveBuildRunStatusPublisher.PulseSubOperation("surface build", _detail);
        }

        static void Advance(int units, string detail)
        {
            if (units > 0)
                _doneWeight = Mathf.Min(_totalWeight, _doneWeight + units);
            _detail = detail;
            Show();
        }
    }
}
#endif
