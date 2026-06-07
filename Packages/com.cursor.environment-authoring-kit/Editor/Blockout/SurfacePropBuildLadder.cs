#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Prop placement grading — one vegetation category at a time with artifact freeze.</summary>
    public static class SurfacePropBuildLadder
    {
        public const string ReportPath =
            CaveBuildAgentContextExporter.Folder + "/SurfacePropBuildLadderReport.json";
        public const string ActivePromptPath =
            CaveBuildAgentContextExporter.Folder + "/SurfacePropActiveRungPrompt.md";

        public static readonly string[] RungOrder =
        {
            "prop_trees",
            "prop_grass",
            "prop_bushes",
            "prop_ground_cover",
        };

        public static TerrainStageGrade GradeOneRung(
            string rungId,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            ref SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog vegCatalog)
        {
            if (!CaveBuildGradedArtifactGate.TryBegin(CaveBuildGradedArtifactGate.Track.Prop, rungId))
            {
                return new TerrainStageGrade
                {
                    StageId = rungId,
                    StageName = rungId,
                    Score = 75,
                    Passed = true,
                    Issues = new List<string> { "Another graded task is active — deferred." },
                };
            }

            try
            {
                foreach (var def in SurfaceTerrainBuildLadder.RungOrder)
                {
                    if (def.Id != rungId)
                        continue;
                    var g = SurfaceTerrainBuildLadder.GradeOneRung(
                        def,
                        ground,
                        request,
                        surfaceRoot,
                        ref vegCatalog);
                    if (g.Passed)
                        CaveBuildGradedArtifactGate.CompleteAndRelease(
                            CaveBuildGradedArtifactGate.Track.Prop,
                            rungId,
                            request?.Seed ?? 0);
                    else
                        CaveBuildGradedArtifactGate.Release(CaveBuildGradedArtifactGate.Track.Prop, rungId);
                    return g;
                }

                CaveBuildGradedArtifactGate.Release(CaveBuildGradedArtifactGate.Track.Prop, rungId);
                return new TerrainStageGrade { StageId = rungId, Score = 50, Passed = false };
            }
            catch
            {
                CaveBuildGradedArtifactGate.Release(CaveBuildGradedArtifactGate.Track.Prop, rungId);
                throw;
            }
        }

        public static string PickActiveRung(IReadOnlyList<TerrainStageGrade> stages, ISet<string> skip = null)
        {
            var map = new Dictionary<string, TerrainStageGrade>();
            if (stages != null)
            {
                foreach (var s in stages)
                {
                    if (s != null && !string.IsNullOrEmpty(s.StageId))
                        map[s.StageId] = s;
                }
            }

            foreach (var id in RungOrder)
            {
                if (skip != null && skip.Contains(id))
                    continue;
                if (!map.TryGetValue(id, out var g) || !g.Passed)
                    return id;
            }

            return null;
        }
    }
}
#endif
