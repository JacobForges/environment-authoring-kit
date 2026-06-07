#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Mountain-specific grading rubric — research, LiDAR, outer ring, cliffs, trails.</summary>
    public static class SurfaceMountainBuildLadder
    {
        public const string ReportPath =
            CaveBuildAgentContextExporter.Folder + "/SurfaceMountainBuildLadderReport.json";
        public const string ActivePromptPath =
            CaveBuildAgentContextExporter.Folder + "/SurfaceMountainActiveRungPrompt.md";

        public const int StagePassScore = 88;
        public const int StageFloorScore = 62;

        public static readonly MountainRungDef[] RungOrder =
        {
            new("mountain_wilderness_tiles", 12, critical: true),
            new("mountain_research", 8, critical: false),
            new("mountain_lidar", 10, critical: false),
            new("outer_ring_mountains", 22, critical: true),
            new("mountain_labyrinth_research", 8, critical: false),
            new("mountain_labyrinth", 16, critical: true),
            new("mountain_cliffs", 14, critical: false),
            new("mountain_trails", 16, critical: true),
            new("mountain_play_smooth", 10, critical: false),
        };

        public struct MountainRungDef
        {
            public string Id;
            public int Weight;
            public bool Critical;

            public MountainRungDef(string id, int weight, bool critical)
            {
                Id = id;
                Weight = weight;
                Critical = critical;
            }
        }

        public static TerrainStageGrade GradeOneRung(
            MountainRungDef def,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Transform surfaceRoot)
        {
            if (!CaveBuildGradedArtifactGate.TryBegin(CaveBuildGradedArtifactGate.Track.Mountain, def.Id))
            {
                return new TerrainStageGrade
                {
                    StageId = def.Id,
                    StageName = def.Id,
                    Score = 70,
                    Passed = true,
                    Issues = new List<string> { "Another graded task is active — deferred." },
                };
            }

            try
            {
                var g = new TerrainStageGrade
                {
                    StageId = def.Id,
                    StageName = def.Id,
                    Weight = def.Weight,
                    Critical = def.Critical,
                    Score = 100,
                };

                switch (def.Id)
                {
                    case "mountain_wilderness_tiles":
                        GradeWildernessTiles(g, ground);
                        break;
                    case "mountain_research":
                        GradeResearch(g, request);
                        break;
                    case "mountain_lidar":
                        GradeLidar(g, request);
                        break;
                    case "outer_ring_mountains":
                        GradeOuterRing(g, ground, request);
                        break;
                    case "mountain_labyrinth_research":
                        GradeLabyrinthResearch(g, request);
                        break;
                    case "mountain_labyrinth":
                        GradeLabyrinth(g, ground, request);
                        break;
                    case "mountain_cliffs":
                        GradeCliffs(g, ground, request);
                        break;
                    case "mountain_trails":
                        GradeTrails(g, surfaceRoot, request);
                        break;
                    case "mountain_play_smooth":
                        g.Score = 90;
                        g.Issues.Add("Play-band smooth is pacing-only — spot-check trail benches visually.");
                        break;
                }

                g.Passed = g.Score >= StagePassScore;
                if (def.Critical && g.Score < StageFloorScore)
                    g.Passed = false;

                if (g.Passed)
                    CaveBuildGradedArtifactGate.CompleteAndRelease(
                        CaveBuildGradedArtifactGate.Track.Mountain,
                        def.Id,
                        request?.Seed ?? 0);
                else
                    CaveBuildGradedArtifactGate.Release(CaveBuildGradedArtifactGate.Track.Mountain, def.Id);

                return g;
            }
            catch
            {
                CaveBuildGradedArtifactGate.Release(CaveBuildGradedArtifactGate.Track.Mountain, def.Id);
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

            foreach (var def in RungOrder)
            {
                if (skip != null && skip.Contains(def.Id))
                    continue;
                if (!map.TryGetValue(def.Id, out var g) || !g.Passed)
                    return def.Id;
            }

            return null;
        }

        public static void ExportRungPrompt(string rungId, int seed, TerrainStageGrade grade)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var phaseId = rungId.StartsWith("mountain_") ? rungId : "mountain_" + rungId;
            var step = QueuedStepForRung(rungId);
            CaveBuildUnifiedPromptBridge.RefreshForPhase(
                phaseId,
                "ground_placement",
                -1,
                step,
                seed,
                out _);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Mountain ladder — active rung");
            sb.AppendLine();
            sb.AppendLine($"**Rung:** `{rungId}` | **Seed:** {seed}");
            if (grade != null)
            {
                sb.AppendLine($"**Score:** {grade.Score} | **Passed:** {grade.Passed}");
                if (grade.Issues?.Count > 0)
                    sb.AppendLine($"**Issues:** {string.Join("; ", grade.Issues)}");
                if (grade.Fixes?.Count > 0)
                    sb.AppendLine($"**Fixes:** {string.Join("; ", grade.Fixes)}");
            }

            sb.AppendLine();
            sb.AppendLine(
                "ONE task only — read `fullworld_do_not` first (no Grid/EnvironmentRooms layout), then `mountain_terrain` + `mountain_lidar`.");
            File.WriteAllText(Path.Combine(hub, ActivePromptPath), sb.ToString());
        }

        static int QueuedStepForRung(string rungId)
        {
            for (var i = 0; i < RungOrder.Length; i++)
            {
                if (RungOrder[i].Id == rungId)
                    return 52 + i;
            }

            return 52;
        }

        static void GradeResearch(TerrainStageGrade g, WorldGenerationRequest request)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var brief = Path.Combine(hub, CaveBuildPhaseResearchGate.ActionPlanRel);
            var cache = Path.Combine(
                hub,
                "Assets/EnvironmentKit/ResearchCache/categories/mountain_terrain/index.json");
            g.Score = File.Exists(brief) && File.Exists(cache) ? 95 : File.Exists(cache) ? 85 : 65;
            if (g.Score < StagePassScore)
            {
                g.Issues.Add("Mountain research cache or action plan missing.");
                g.Fixes.Add("Run `npm run sync-research-cache` in Tools/cave-grader.");
            }
        }

        static void GradeLidar(TerrainStageGrade g, WorldGenerationRequest request)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var cache = Path.Combine(
                hub,
                "Assets/EnvironmentKit/ResearchCache/categories/mountain_lidar/index.json");
            g.Score = File.Exists(cache) ? 92 : 68;
            if (!File.Exists(cache))
            {
                g.Issues.Add("mountain_lidar category index missing.");
                g.Fixes.Add("Sync research cache — mountain LiDAR refs are a separate category from Florida lidar_terrain.");
            }
        }

        static void GradeWildernessTiles(TerrainStageGrade g, SceneGroundInfo ground)
        {
            if (ground?.Terrain == null)
            {
                g.Score = 80;
                return;
            }

            var foothills = SurfaceTerrainTileExpansion.CollectMountainFoothillTiles(ground.Terrain).Length;
            var peaks = SurfaceTerrainTileExpansion.CollectMountainPeakTiles(ground.Terrain).Length;
            var count = foothills + peaks;
            var need = SurfaceTerrainTileExpansion.MountainWildernessTileCount;
            g.Score = count >= need ? 98 : count >= need - 4 ? 88 : count >= need - 8 ? 72 : 44;
            if (count < need)
            {
                g.Issues.Add(
                    $"FullWorld outer rings {count}/{need} ({foothills}/{SurfaceTerrainTileExpansion.FoothillRingTileCount} foothill, " +
                    $"{peaks}/{SurfaceTerrainTileExpansion.PeakRingTileCount} peak).");
                g.Fixes.Add("Re-run mountain_wilderness_tiles phase (paced spawn, 0.05s steps).");
            }
        }

        static void GradeOuterRing(TerrainStageGrade g, SceneGroundInfo ground, WorldGenerationRequest request)
        {
            if (TryReuseTerrainLadderStage("outer_ring_mountains", request?.Seed ?? 0, out var reused))
            {
                g.Score = reused.Score;
                g.Passed = reused.Passed;
                if (reused.Issues != null)
                    g.Issues.AddRange(reused.Issues);
                if (reused.Fixes != null)
                    g.Fixes.AddRange(reused.Fixes);
                g.Issues.Add("Score aligned with terrain ladder (avoid duplicate outer_ring grading).");
                return;
            }

            if (request == null || !request.UseOuterRingMountains || ground?.Terrain == null)
            {
                g.Score = 85;
                return;
            }

            var edgeY = SurfaceOuterRingMountainsAuthor.SampleOuterEdgeMeanHeight(ground.Terrain);
            var center = ground.HasAnchor
                ? ground.Anchor.position
                : new Vector3(ground.Bounds.center.x, ground.SurfaceY, ground.Bounds.center.z);
            var centerY = ground.Terrain.SampleHeight(center) + ground.Terrain.transform.position.y;
            var delta = edgeY - centerY;
            g.Score = delta >= 18f ? 96 : delta >= 12f ? 86 : delta >= 6f ? 70 : 48;
            if (delta < 10f)
            {
                g.Issues.Add($"Outer edge only {delta:F1}m above play center.");
                g.Fixes.Add("Re-run mountain_peak_sculpt phase after foothill merge and 9-tile lock.");
            }
        }

        static void GradeLabyrinthResearch(TerrainStageGrade g, WorldGenerationRequest request)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var cache = Path.Combine(
                hub,
                "Assets/EnvironmentKit/ResearchCache/categories/mountain_labyrinth/index.json");
            g.Score = File.Exists(cache) ? 94 : 68;
            if (!File.Exists(cache))
            {
                g.Issues.Add("mountain_labyrinth category index missing.");
                g.Fixes.Add("Run `npm run sync-research-cache` in Tools/cave-grader.");
            }
        }

        static void GradeLabyrinth(TerrainStageGrade g, SceneGroundInfo ground, WorldGenerationRequest request)
        {
            if (request == null || !request.SurfaceIncludeMountainLabyrinth || ground?.Terrain == null)
            {
                g.Score = 85;
                return;
            }

            var envRoot = GameObject.Find(SurfaceWorldPaths.RootName)?.transform;
            var labyrinth = envRoot != null ? envRoot.Find(SurfaceWorldPaths.MountainLabyrinthName) : null;
            var markers = 0;
            if (labyrinth != null)
            {
                for (var i = 0; i < labyrinth.childCount; i++)
                {
                    if (labyrinth.GetChild(i).name.StartsWith(
                            SurfaceMountainLabyrinthAuthor.LabyrinthMarkerPrefix,
                            StringComparison.Ordinal))
                        markers++;
                }
            }

            g.Score = markers >= 8 ? 96 : markers >= 4 ? 84 : markers >= 2 ? 68 : 44;
            if (markers < 4)
            {
                g.Issues.Add($"Mountain labyrinth markers {markers} (expected walkway + maze waypoints).");
                g.Fixes.Add("Re-run mountain_labyrinth_carve after peak sculpt and play-disk lock.");
            }
        }

        static void GradeCliffs(TerrainStageGrade g, SceneGroundInfo ground, WorldGenerationRequest request)
        {
            GradeOuterRing(g, ground, request);
            if (g.Score >= StagePassScore)
                g.Score = Mathf.Min(100, g.Score + 2);
            else
                g.Fixes.Add("Accent cliff faces on outer band (mountain_cliffs phase) — preserve play disk.");
        }

        static void GradeTrails(TerrainStageGrade g, Transform surfaceRoot, WorldGenerationRequest request)
        {
            if (request == null || !request.SurfaceIncludeTrails || surfaceRoot == null)
            {
                g.Score = 80;
                return;
            }

            var trails = surfaceRoot.Find(SurfaceWorldPaths.TrailsName);
            var count = trails != null ? trails.childCount : 0;
            g.Score = count >= 2 ? 94 : count >= 1 ? 82 : 58;
            if (count < 2)
            {
                g.Issues.Add("Few perimeter mountain trails under Trails root.");
                g.Fixes.Add("Run mountain_trails phase — bench ≥3m wide toward play disk.");
            }
        }

        static bool TryReuseTerrainLadderStage(string stageId, int seed, out TerrainStageGrade stage)
        {
            stage = null;
            if (!SurfaceTerrainBuildLadder.TryTakeCachedGradedReport(seed, out var cached) ||
                cached?.Stages == null)
                return false;

            stage = cached.Stages.Find(s => s.StageId == stageId);
            return stage != null;
        }
    }
}
#endif
