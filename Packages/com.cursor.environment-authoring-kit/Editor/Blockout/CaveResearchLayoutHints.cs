#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Baked cues from <c>Tools/cave-grader/research-catalog.seed.json</c> (Lara Croft / adventure traversal).
    /// Used when ResearchCache is empty on public clones.
    /// </summary>
    public static class CaveResearchLayoutHints
    {
        public const string LaraCroftSystemsTitle = "One with Lara: The Croft of Systems Design";

        public static bool PreferTombRaiderCadence(WorldGenerationRequest request, System.Random rng)
        {
            if (request == null || rng == null)
                return false;

            if (request.UseTombRaiderLabyrinthCadence)
                return true;

            if (string.Equals(request.BuildVisualStyle, CaveBuildStylePalette.TombExplorer, StringComparison.Ordinal))
                return true;

            if (!string.IsNullOrEmpty(request.PropEmphasis) &&
                request.PropEmphasis.IndexOf("tomb", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return rng.NextDouble() < 0.72;
        }

        public static void ApplyTombRaiderGuidance(WorldGenerationRequest request, System.Random rng)
        {
            if (request == null || rng == null)
                return;

            request.UseTombRaiderLabyrinthCadence = true;
            request.MazeGenFlavor = (int)CaveMazeGenFlavor.WalkwayLabyrinthCavern;
            request.BuildVisualStyle = CaveBuildStylePalette.TombExplorer;
            request.CavePathYawVariance = Mathf.Max(request.CavePathYawVariance, 22f + (float)rng.NextDouble() * 18f);
            request.CaveChamberSizeMultiplier = Mathf.Max(request.CaveChamberSizeMultiplier, 2.4f + (float)rng.NextDouble() * 0.5f);
            request.SurfaceIncludeMountains = true;
            request.SurfaceIncludeTrails = true;
            request.HeightStyle = TerrainHeightStyle.Mountains;
            request.SurfaceTileLayoutVariant = request.SurfaceTileLayoutVariant < 0
                ? rng.Next(0, 48)
                : request.SurfaceTileLayoutVariant;

            if (string.IsNullOrEmpty(request.PropEmphasis) ||
                request.PropEmphasis.IndexOf("karst", StringComparison.OrdinalIgnoreCase) >= 0)
                request.PropEmphasis = rng.NextDouble() < 0.5 ? "tomb_arch" : "ancient_carvings";
        }
    }
}
#endif
