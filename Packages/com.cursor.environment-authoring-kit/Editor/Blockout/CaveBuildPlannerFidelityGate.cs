#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Compares live scene vs planner concept (75%) + marker plan (25%) on layout rungs only.
    /// Geo/heightfield rungs use <see cref="SurfaceTerrainLadderFixer"/> — never resculpt heightmaps here.
    /// </summary>
    public static class CaveBuildPlannerFidelityGate
    {
        public const string ReportRelPath =
            "Assets/EnvironmentKit/Generated/CaveBuildPlannerFidelityReport.json";

        /// <summary>Layout/prop fidelity only — geo rungs are graded and fixed by the terrain ladder.</summary>
        static readonly string[] GatedRungs =
        {
            "trail_walkability",
            "prop_trees",
            "prop_grass",
        };

        static readonly Dictionary<string, int> RetryCountByRung = new();
        static readonly Dictionary<string, float> LastSimilarityByRung = new();

        const float StagnantSimilarityEpsilon = 0.5f;

        [Serializable]
        public sealed class FidelityReport
        {
            public string rungId;
            public float similarityPercent;
            public float divergencePercent;
            public float conceptWeight;
            public string conceptImageRel;
            public int markerCount;
            public int samplesMatched;
            public int samplesTotal;
            public int retryAttempt;
            public bool passedGate;
            public string generatedUtc;
        }

        public static bool IsPlannerGateActive(WorldGenerationRequest request) =>
            CaveBuildSessionConfig.HasFinalizedActive &&
            (request == null || CaveBuildSessionConfig.IsSessionRequest(request));

        public static bool ShouldGateRung(string rungId)
        {
            if (string.IsNullOrEmpty(rungId))
                return false;

            foreach (var id in GatedRungs)
            {
                if (string.Equals(id, rungId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public static FidelityReport ScoreScene(
            string rungId,
            SceneGroundInfo ground,
            Transform surfaceRoot,
            WorldGenerationRequest request,
            int retryAttempt = 0)
        {
            var report = new FidelityReport
            {
                rungId = rungId,
                conceptWeight = CaveBuildPlannerConceptGuide.ActiveConceptWeight,
                conceptImageRel = CaveBuildPlannerConceptGuide.LoadedConceptRel ??
                                  CaveBuildPlannerLayoutBridge.LoadedConceptImageRel,
                retryAttempt = retryAttempt,
                generatedUtc = DateTime.UtcNow.ToString("o"),
            };

            if (ground?.Terrain == null)
            {
                report.similarityPercent = 0f;
                report.divergencePercent = 100f;
                return report;
            }

            CaveBuildPlannerLayoutBridge.TryLoad(out var brief, out _);
            report.markerCount = brief?.layoutPlan?.markers?.Length ?? 0;

            var main = ground.Terrain;
            var hub = SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main);
            var tileSize = main.terrainData.size;
            var span = tileSize.x * 3.2f;
            const int grid = 13;
            var matched = 0;
            var total = 0;

            for (var iz = 0; iz < grid; iz++)
            {
                for (var ix = 0; ix < grid; ix++)
                {
                    var u = ix / (float)(grid - 1);
                    var v = iz / (float)(grid - 1);
                    var wx = hub.x + (u - 0.5f) * span * 2f;
                    var wz = hub.z + (v - 0.5f) * span * 2f;
                    var world = new Vector3(wx, hub.y, wz);

                    if (!CaveBuildPlannerConceptGuide.TrySampleExpectedZone(
                            world, main, ground, out var expected, out _))
                        continue;

                    var actual = ClassifyActualZone(world, main, ground, surfaceRoot);
                    total++;
                    if (ZonesCompatible(expected, actual))
                        matched++;
                }
            }

            report.samplesTotal = total;
            report.samplesMatched = matched;
            report.similarityPercent = total > 0 ? matched * 100f / total : 0f;
            report.divergencePercent = 100f - report.similarityPercent;
            report.passedGate = report.similarityPercent >= CaveBuildPlannerConceptGuide.PassSimilarityPercent;
            return report;
        }

        public static bool TryGateAfterRung(
            string rungId,
            SceneGroundInfo ground,
            Transform surfaceRoot,
            WorldGenerationRequest request,
            Action<bool> onComplete)
        {
            if (!IsPlannerGateActive(request) || !ShouldGateRung(rungId))
            {
                onComplete?.Invoke(true);
                return false;
            }

            if (!RetryCountByRung.TryGetValue(rungId, out var attempt))
                attempt = 0;

            var report = ScoreScene(rungId, ground, surfaceRoot, request, attempt);
            ExportReport(report);
            LogReport(report);

            if (report.passedGate)
            {
                RetryCountByRung.Remove(rungId);
                onComplete?.Invoke(true);
                return false;
            }

            if (attempt >= CaveBuildPlannerConceptGuide.MaxFidelityRetries)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[PlannerFidelity] Rung '{rungId}' — max retries ({CaveBuildPlannerConceptGuide.MaxFidelityRetries}) " +
                    $"reached at {report.similarityPercent:F1}% similarity. Continuing build.",
                    forceUnityConsole: true);
                RetryCountByRung.Remove(rungId);
                LastSimilarityByRung.Remove(rungId);
                onComplete?.Invoke(true);
                return false;
            }

            if (attempt > 0 &&
                LastSimilarityByRung.TryGetValue(rungId, out var priorSimilarity) &&
                report.similarityPercent <= priorSimilarity + StagnantSimilarityEpsilon)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[PlannerFidelity] Rung '{rungId}' — similarity stagnant ({report.similarityPercent:F1}% vs {priorSimilarity:F1}%) " +
                    "— skipping layout remedy; terrain ladder will fix geo rungs.",
                    forceUnityConsole: true);
                ExportFidelityRetryPrompt(rungId, request, attempt);
                RetryCountByRung.Remove(rungId);
                onComplete?.Invoke(true);
                return false;
            }

            LastSimilarityByRung[rungId] = report.similarityPercent;

            attempt++;
            RetryCountByRung[rungId] = attempt;
            CaveBuildPlannerConceptGuide.BoostWeightForRetry(attempt);
            ExportFidelityRetryPrompt(rungId, request, attempt);

            CaveBuildEditorLog.LogSurface(
                $"[PlannerFidelity] Rung '{rungId}' — retry {attempt}/{CaveBuildPlannerConceptGuide.MaxFidelityRetries} " +
                $"— re-applying planner layout remedy ({CaveBuildPlannerConceptGuide.ActiveConceptWeight:P0} concept weight)…",
                forceUnityConsole: true);

            QueueRemedy(ground, surfaceRoot, request, rungId, () =>
            {
                var recheck = ScoreScene(rungId, ground, surfaceRoot, request, attempt);
                ExportReport(recheck);
                LogReport(recheck);
                onComplete?.Invoke(recheck.passedGate || attempt >= CaveBuildPlannerConceptGuide.MaxFidelityRetries);
            });
            return true;
        }

        public static void ResetRetryState()
        {
            RetryCountByRung.Clear();
            LastSimilarityByRung.Clear();
        }

        static void ExportFidelityRetryPrompt(string rungId, WorldGenerationRequest request, int attempt)
        {
            if (!SurfaceTerrainBuildLadder.TryTakeCachedGradedReport(request?.Seed ?? 0, out var report) ||
                report == null)
                return;

            TerrainBuildRungPromptExporter.WriteTailoredFixPrompt(
                rungId,
                report,
                request?.Seed ?? 0,
                attempt,
                "terrain",
                "planner_fidelity");
        }

        static void QueueRemedy(
            SceneGroundInfo ground,
            Transform surfaceRoot,
            WorldGenerationRequest request,
            string rungId,
            Action onComplete)
        {
            CaveBuildActionPacing.ScheduleHeavyChain(
                () => QueueLayoutRemedy(ground, surfaceRoot, request, rungId, onComplete),
                CaveBuildPipelineDomains.SurfaceQueueLabel($"planner fidelity layout remedy ({rungId})"));
        }

        static void QueueLayoutRemedy(
            SceneGroundInfo ground,
            Transform surfaceRoot,
            WorldGenerationRequest request,
            string rungId,
            Action onComplete)
        {
            if (surfaceRoot == null || ground?.Terrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildPlannerLayoutAuthor.ResetBuildSession();
            CaveBuildPlannerMeshLandscapeAuthor.ResetBuildSession();
            CaveBuildPlannerMarkerPropScatter.ResetBuildSession();
            CaveBuildPlannerContentAuthor.ResetBuildSession();
            CaveBuildPlannerTrailAuthor.ResetBuildSession();

            CaveBuildPlannerLayoutAuthor.TryApply(ground, surfaceRoot, request, out var layoutMsg);
            CaveBuildEditorLog.LogSurface("[PlannerFidelity] " + layoutMsg, forceUnityConsole: true);

            if (string.Equals(rungId, "trail_walkability", StringComparison.Ordinal))
            {
                CaveBuildPlannerTrailAuthor.TryApply(ground, surfaceRoot, request, out var trailMsg);
                CaveBuildEditorLog.LogSurface("[PlannerFidelity] " + trailMsg, forceUnityConsole: true);
                SurfaceTerrainLadderFixer.QueueTryFix(
                    rungId,
                    ground,
                    request,
                    surfaceRoot,
                    (_, _) => CaveBuildActionPacing.ScheduleLight(onComplete, "planner fidelity remedy complete"));
                return;
            }

            if (rungId.StartsWith("prop_", StringComparison.Ordinal))
            {
                CaveBuildPlannerMarkerPropScatter.TryPlaceLockedVegetation(
                    ground,
                    surfaceRoot,
                    request,
                    out var scatterMsg);
                CaveBuildEditorLog.LogSurface("[PlannerFidelity] " + scatterMsg, forceUnityConsole: true);
                CaveBuildPlannerContentAuthor.TryApply(ground, surfaceRoot, request, out var contentMsg);
                CaveBuildEditorLog.LogSurface("[PlannerFidelity] " + contentMsg, forceUnityConsole: true);
            }

            CaveBuildActionPacing.ScheduleLight(onComplete, "planner fidelity remedy complete");
        }

        static CaveBuildPlannerConceptGuide.PlannerZone ClassifyActualZone(
            Vector3 world,
            Terrain main,
            SceneGroundInfo ground,
            Transform surfaceRoot)
        {
            var hubY = SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main).y;
            CaveBuildPlannerLayoutBridge.TryLoad(out var brief, out _);
            var specs = CaveBuildPlannerLayoutBridge.ResolveSpecs(brief?.layoutPlan);

            if (!TrySampleTerrainY(world, main, out var y))
                return CaveBuildPlannerConceptGuide.PlannerZone.Void;

            var platformY = hubY + specs.platformHeightM;
            var plateauY = hubY + specs.plateauHeightM;

            if (y < hubY - 4f)
                return CaveBuildPlannerConceptGuide.PlannerZone.Void;
            if (Mathf.Abs(y - plateauY) < 2.5f)
                return CaveBuildPlannerConceptGuide.PlannerZone.Island;
            if (Mathf.Abs(y - platformY) < 1.8f)
                return CaveBuildPlannerConceptGuide.PlannerZone.PlayPlatform;
            if (y > platformY + 0.35f && y < platformY + 1.2f)
                return CaveBuildPlannerConceptGuide.PlannerZone.HopPad;

            if (surfaceRoot != null)
            {
                var layout = surfaceRoot.Find(CaveBuildPlannerLayoutAuthor.LayoutRootName);
                if (layout != null)
                {
                    foreach (Transform child in layout)
                    {
                        if (child == null)
                            continue;
                        if (Vector3.Distance(child.position, world) < 2.5f)
                        {
                            if (child.name.IndexOf("Switch", StringComparison.OrdinalIgnoreCase) >= 0)
                                return CaveBuildPlannerConceptGuide.PlannerZone.Prop;
                            if (child.name.IndexOf("Npc", StringComparison.OrdinalIgnoreCase) >= 0)
                                return CaveBuildPlannerConceptGuide.PlannerZone.Npc;
                            if (child.name.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0)
                                return CaveBuildPlannerConceptGuide.PlannerZone.Enemy;
                            if (child.name.IndexOf("Spawn", StringComparison.OrdinalIgnoreCase) >= 0)
                                return CaveBuildPlannerConceptGuide.PlannerZone.Spawn;
                            return CaveBuildPlannerConceptGuide.PlannerZone.Prop;
                        }
                    }
                }
            }

            return CaveBuildPlannerConceptGuide.PlannerZone.PlayPlatform;
        }

        static bool TrySampleTerrainY(Vector3 world, Terrain main, out float y)
        {
            y = world.y;
            foreach (var t in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(main))
            {
                if (t?.terrainData == null)
                    continue;
                var pos = t.transform.position;
                var size = t.terrainData.size;
                if (world.x < pos.x || world.z < pos.z || world.x > pos.x + size.x || world.z > pos.z + size.z)
                    continue;
                y = t.SampleHeight(world) + pos.y;
                return true;
            }

            return false;
        }

        static bool ZonesCompatible(
            CaveBuildPlannerConceptGuide.PlannerZone expected,
            CaveBuildPlannerConceptGuide.PlannerZone actual)
        {
            if (expected == actual)
                return true;

            if (expected == CaveBuildPlannerConceptGuide.PlannerZone.Unknown ||
                actual == CaveBuildPlannerConceptGuide.PlannerZone.Unknown)
                return true;

            if (expected == CaveBuildPlannerConceptGuide.PlannerZone.PlayPlateau &&
                actual == CaveBuildPlannerConceptGuide.PlannerZone.PlayPlatform)
                return true;

            if (expected == CaveBuildPlannerConceptGuide.PlannerZone.PlayPlatform &&
                actual == CaveBuildPlannerConceptGuide.PlannerZone.PlayPlateau)
                return true;

            if (expected == CaveBuildPlannerConceptGuide.PlannerZone.Island &&
                actual == CaveBuildPlannerConceptGuide.PlannerZone.PlayPlateau)
                return true;

            if (expected == CaveBuildPlannerConceptGuide.PlannerZone.Trail &&
                (actual == CaveBuildPlannerConceptGuide.PlannerZone.HopPad ||
                 actual == CaveBuildPlannerConceptGuide.PlannerZone.PlayPlatform))
                return true;

            if (expected == CaveBuildPlannerConceptGuide.PlannerZone.HopPad &&
                actual == CaveBuildPlannerConceptGuide.PlannerZone.PlayPlatform)
                return true;

            if (expected == CaveBuildPlannerConceptGuide.PlannerZone.Prop &&
                (actual == CaveBuildPlannerConceptGuide.PlannerZone.Prop ||
                 actual == CaveBuildPlannerConceptGuide.PlannerZone.Npc ||
                 actual == CaveBuildPlannerConceptGuide.PlannerZone.Enemy))
                return true;

            return false;
        }

        static void LogReport(FidelityReport report)
        {
            var status = report.passedGate ? "PASS" : "RETRY";
            CaveBuildEditorLog.LogSurface(
                $"[PlannerFidelity] ═══ Rung '{report.rungId}' gate — {status} ═══",
                forceUnityConsole: true);
            CaveBuildEditorLog.LogSurface(
                $"[PlannerFidelity] Concept: {report.conceptImageRel ?? "(missing)"} — weight {report.conceptWeight:P0}",
                forceUnityConsole: true);
            CaveBuildEditorLog.LogSurface(
                $"[PlannerFidelity] Marker plan: {report.markerCount} markers — weight {CaveBuildPlannerConceptGuide.MarkerPlanWeight:P0}",
                forceUnityConsole: true);
            CaveBuildEditorLog.LogSurface(
                $"[PlannerFidelity] Similarity {report.similarityPercent:F1}% — divergence {report.divergencePercent:F1}% " +
                $"(gate ≥ {CaveBuildPlannerConceptGuide.PassSimilarityPercent:F0}%) " +
                $"[{report.samplesMatched}/{report.samplesTotal} samples]",
                forceUnityConsole: true);

            if (!report.passedGate)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[PlannerFidelity] Scene diverges > {100f - CaveBuildPlannerConceptGuide.PassSimilarityPercent:F0}% " +
                    $"from planner concept + layout — layout remedy queued (attempt {report.retryAttempt}).",
                    forceUnityConsole: true);
            }
        }

        public static void ExportReport(FidelityReport report)
        {
            try
            {
                var hub = CaveBuildCursorSettings.ResolveHubRoot();
                var path = Path.Combine(hub, ReportRelPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
                var sb = new StringBuilder(512);
                sb.AppendLine("{");
                sb.AppendLine($"  \"generatedUtc\": \"{report.generatedUtc}\",");
                sb.AppendLine($"  \"rungId\": \"{report.rungId}\",");
                sb.AppendLine($"  \"similarityPercent\": {report.similarityPercent:F2},");
                sb.AppendLine($"  \"divergencePercent\": {report.divergencePercent:F2},");
                sb.AppendLine($"  \"conceptWeight\": {report.conceptWeight:F3},");
                sb.AppendLine($"  \"conceptImageRel\": \"{report.conceptImageRel ?? string.Empty}\",");
                sb.AppendLine($"  \"markerCount\": {report.markerCount},");
                sb.AppendLine($"  \"samplesMatched\": {report.samplesMatched},");
                sb.AppendLine($"  \"samplesTotal\": {report.samplesTotal},");
                sb.AppendLine($"  \"retryAttempt\": {report.retryAttempt},");
                sb.AppendLine($"  \"passedGate\": {(report.passedGate ? "true" : "false")},");
                sb.AppendLine($"  \"passThresholdPercent\": {CaveBuildPlannerConceptGuide.PassSimilarityPercent:F0}");
                sb.AppendLine("}");
                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlannerFidelity] Failed to write report: " + ex.Message);
            }
        }
    }
}
#endif
