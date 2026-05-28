using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Scans the current grade + scene and decides what the research phase should focus on
    /// (rungs, stages, queries) before Cursor or enrichment export runs.
    /// </summary>
    public static class CaveBuildResearchNeedsAnalyzer
    {
        public const string NeedsPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildResearchNeeds.json";
        const int MaxQueries = 6;
        const int MaxPhaseLogEntries = 48;

        [Serializable]
        public sealed class ResearchNeedsSnapshot
        {
            public string generatedUtc;
            public string scene;
            public int overallScore;
            public string letterGrade;
            public bool meetsShipTarget;
            public bool meetsBetaTarget;
            public bool researchRequired;
            public string primaryRung;
            public string[] failingRungs;
            public FailingStageEntry[] failingStages;
            public string[] suggestedQueries;
            public string[] focusTopics;
            public string persistenceNote;
        }

        [Serializable]
        public sealed class FailingStageEntry
        {
            public string stageId;
            public string stageName;
            public int score;
            public string mappedRung;
            public string topIssue;
        }

        /// <summary>Run local analysis and write <see cref="NeedsPath"/> (overwrites prior file).</summary>
        public static ResearchNeedsSnapshot AnalyzeAndWrite(
            CaveBuildQualityReport quality,
            Transform caveRoot,
            SceneGroundInfo ground)
        {
            var snapshot = BuildSnapshot(quality, caveRoot, ground);
            WriteNeedsFile(snapshot);
            Debug.Log(
                "[CaveBuild] Research needs — " +
                $"primary={snapshot.primaryRung}, rungs=[{string.Join(", ", snapshot.failingRungs ?? Array.Empty<string>())}], " +
                $"queries={snapshot.suggestedQueries?.Length ?? 0}, required={snapshot.researchRequired}. " +
                $"Saved {NeedsPath} (project file until you delete it; overwritten each build).");
            return snapshot;
        }

        public static ResearchNeedsSnapshot BuildSnapshot(
            CaveBuildQualityReport quality,
            Transform caveRoot,
            SceneGroundInfo ground)
        {
            var failingStages = CollectFailingStages(quality);
            var failingRungs = quality != null
                ? CaveBuildPromptLadder.GetFailingRungs(quality, caveRoot, ground).ToArray()
                : Array.Empty<string>();

            var primaryRung = quality != null
                ? CaveBuildPromptLadder.PickActiveRung(quality, caveRoot, ground)
                : CaveBuildPromptLadder.RungResearch;

            var queries = BuildQueries(failingStages, failingRungs, primaryRung, caveRoot, ground);
            var topics = BuildFocusTopics(failingStages, failingRungs);

            var ship = quality != null && CaveBuildQualityRubric.MeetsShipTarget(quality);
            var researchRequired = quality != null && !ship &&
                                   (failingStages.Count > 0 || failingRungs.Length > 0);

            return new ResearchNeedsSnapshot
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                scene = quality?.SceneName ?? string.Empty,
                overallScore = quality?.OverallScore ?? 0,
                letterGrade = quality?.LetterGrade ?? "?",
                meetsShipTarget = ship,
                meetsBetaTarget = quality != null && CaveBuildQualityRubric.MeetsBetaTarget(quality),
                researchRequired = researchRequired,
                primaryRung = primaryRung ?? CaveBuildPromptLadder.RungResearch,
                failingRungs = failingRungs,
                failingStages = failingStages.ToArray(),
                suggestedQueries = queries.ToArray(),
                focusTopics = topics.ToArray(),
                persistenceNote =
                    "Stored under Assets/EnvironmentKit/Generated/. Persists in the Hub project until deleted. " +
                    "Overwritten on each Build Complete Cave research phase (not a cloud account lifetime store). " +
                    "CaveBuildResearchPhaseLog.json appends entries (capped).",
            };
        }

        static List<FailingStageEntry> CollectFailingStages(CaveBuildQualityReport quality)
        {
            var list = new List<FailingStageEntry>();
            if (quality?.Stages == null)
                return list;

            foreach (var stage in quality.Stages)
            {
                if (stage == null || stage.Passed)
                    continue;

                var issue = stage.Issues != null && stage.Issues.Count > 0
                    ? stage.Issues[0]
                    : string.Empty;

                list.Add(new FailingStageEntry
                {
                    stageId = stage.StageId,
                    stageName = stage.StageName,
                    score = stage.Score,
                    mappedRung = MapStageToRung(stage.StageId),
                    topIssue = issue,
                });
            }

            list.Sort((a, b) => a.score.CompareTo(b.score));
            return list;
        }

        static string MapStageToRung(string stageId)
        {
            if (string.IsNullOrEmpty(stageId))
                return CaveBuildPromptLadder.RungOther;

            if (stageId is "ground" or "cave_mouth_seal" or "terrain_integration" or "terrain_carve" or "portal")
                return CaveBuildPromptLadder.RungGroundPlacement;
            if (stageId is "player_floor" or "walkways" or "spawn_reachability")
                return CaveBuildPromptLadder.RungFloorCollision;
            if (stageId is "navmesh")
                return CaveBuildPromptLadder.RungNavmesh;
            if (stageId is "materials" or "material_binding")
                return CaveBuildPromptLadder.RungMaterials;
            if (stageId is "performance" or "triangle_budget")
                return CaveBuildPromptLadder.RungPerformance;
            if (stageId is "visual_shell" or "enclosure_policy" or "geometry_integrity" or "block_tunnel" or
                "organic_mesh" or "enclosure" or "interior_ribs" or "layout_integrity")
                return CaveBuildPromptLadder.RungVisualShell;

            return CaveBuildPromptLadder.RungOther;
        }

        static List<string> BuildQueries(
            List<FailingStageEntry> failingStages,
            string[] failingRungs,
            string primaryRung,
            Transform caveRoot,
            SceneGroundInfo ground)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queries = new List<string>();

            void Add(string q)
            {
                if (string.IsNullOrWhiteSpace(q) || seen.Contains(q) || queries.Count >= MaxQueries)
                    return;
                seen.Add(q);
                queries.Add(q);
            }

            Add(QueryForRung(primaryRung, caveRoot, ground));

            foreach (var rung in failingRungs)
                Add(QueryForRung(rung, caveRoot, ground));

            foreach (var stage in failingStages)
            {
                Add(QueryForStage(stage.stageId, caveRoot, ground));
                if (queries.Count >= MaxQueries)
                    break;
            }

            if (queries.Count == 0)
            {
                Add("Unity 6 procedural cave environment editor pipeline best practices 2025");
                Add("game level blockout mesh LOD navmesh underground spawn 2025");
            }

            return queries;
        }

        static string QueryForRung(string rung, Transform caveRoot, SceneGroundInfo ground)
        {
            switch (rung)
            {
                case CaveBuildPromptLadder.RungGroundPlacement:
                    if (caveRoot != null && ground != null && ground.HasAnchor)
                    {
                        var mouthErr = CaveGroundPlacementUtility.MeasureEntranceMouthSurfaceError(caveRoot, ground);
                        var depthErr = CaveGroundPlacementUtility.MeasureRootDepthError(caveRoot, ground);
                        return
                            $"Unity terrain cave entrance alignment mouth error {mouthErr:F1}m root depth {depthErr:F1}m 2025";
                    }

                    return "Unity Terrain SampleHeight cave entrance world alignment underground 2025";
                case CaveBuildPromptLadder.RungFloorCollision:
                    return "Unity navmesh underground spawn player floor collider walkable mesh 2025";
                case CaveBuildPromptLadder.RungNavmesh:
                    return "Unity 6 NavMesh bake large procedural cave editor batch 2025";
                case CaveBuildPromptLadder.RungMaterials:
                    return "Unity URP cave rock floor material batch authoring 2025";
                case CaveBuildPromptLadder.RungPerformance:
                    return "Unity editor procedural mesh triangle budget LOD disable renderer keep collider 2025";
                case CaveBuildPromptLadder.RungVisualShell:
                    return "procedural cave shell mesh single route enclosure game environment 2025";
                default:
                    return "AAA game procedural cave blockout pipeline Unity editor 2025";
            }
        }

        static string QueryForStage(string stageId, Transform caveRoot, SceneGroundInfo ground) =>
            QueryForRung(MapStageToRung(stageId), caveRoot, ground);

        static List<string> BuildFocusTopics(
            List<FailingStageEntry> failingStages,
            string[] failingRungs)
        {
            var topics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rung in failingRungs)
                topics.Add(rung);
            foreach (var s in failingStages)
            {
                if (!string.IsNullOrEmpty(s.stageId))
                    topics.Add(s.stageId);
            }

            if (topics.Count == 0)
                topics.Add("general_cave_pipeline");

            var list = new List<string>(topics);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        static void WriteNeedsFile(ResearchNeedsSnapshot snapshot)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, NeedsPath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonUtility.ToJson(snapshot, true);
            File.WriteAllText(path, json);
        }

        /// <summary>Cap growth of append-only phase log.</summary>
        public static void TrimPhaseLogIfNeeded()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, CaveBuildResearchPhase.PhaseLogPath);
            if (!File.Exists(path))
                return;

            try
            {
                var text = File.ReadAllText(path).Trim();
                if (!text.StartsWith("[") || !text.EndsWith("]"))
                    return;

                var inner = text.Substring(1, text.Length - 2).Trim();
                if (string.IsNullOrEmpty(inner))
                    return;

                var parts = inner.Split(new[] { "},\n  {" }, StringSplitOptions.None);
                if (parts.Length <= MaxPhaseLogEntries)
                    return;

                var keep = parts[^MaxPhaseLogEntries..];
                for (var i = 0; i < keep.Length; i++)
                {
                    if (i == 0 && !keep[i].TrimStart().StartsWith("{"))
                        keep[i] = "{" + keep[i];
                    if (i == keep.Length - 1 && !keep[i].TrimEnd().EndsWith("}"))
                        keep[i] = keep[i] + "}";
                }

                File.WriteAllText(path, "[\n  " + string.Join("},\n  {", keep) + "\n]");
            }
            catch
            {
                // ignored
            }
        }
    }
}
