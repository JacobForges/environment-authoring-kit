#if UNITY_EDITOR
using System.IO;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.World;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Lightweight quality + time-savers — fail fast or skip redundant work without weakening final output.
    /// </summary>
    public static class CaveBuildQualityFastGates
    {
        const string QualityReportRel = CaveBuildAgentContextExporter.Folder + "/CaveBuildQualityReport.json";
        public static bool Enabled
        {
            get
            {
                var s = CaveBuildCursorSettings.LoadOrCreate();
                s.LoadFromPrefs();
                return s.enableQualityFastGates;
            }
        }

        /// <summary>Skip autonomous Cursor ship loop when prior grade for this seed already meets target.</summary>
        public static bool TrySkipAutonomousShipLoop(int seed, out string reason)
        {
            reason = null;
            if (!Enabled)
                return false;

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (!settings.skipAutonomousWhenGradeAtOrAbove)
                return false;

            var threshold = Mathf.Clamp(settings.fastGateMinOverallScore, 70, 98);
            if (!TryReadLastOverallScore(seed, out var score, out var letter))
                return false;

            if (score < threshold)
                return false;

            reason =
                $"Prior overall grade {score:F0} ({letter}) for seed {seed} ≥ {threshold} — skipping autonomous until-ship loop.";
            CaveBuildPipelineLog.Info(reason, "FastGate");
            return true;
        }

        /// <summary>After flat grid place — block terraform if too few tiles spawned (bad partial build).</summary>
        public static bool ValidateFlatGridTileCount(int placedTiles, int plannedTiles, out string message)
        {
            message = null;
            if (!Enabled || plannedTiles <= 0)
                return true;

            var minFraction = CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid ? 0.92f : 0.85f;
            var minRequired = Mathf.RoundToInt(plannedTiles * minFraction);
            if (placedTiles >= minRequired)
                return true;

            message =
                $"Flat grid gate FAILED — {placedTiles}/{plannedTiles} tiles placed (need ≥{minRequired}). " +
                "Fix missing slots before terraform (check Console for place skips).";
            CaveBuildEditorLog.LogSurfaceWarning("[FastGate] " + message);
            return false;
        }

        /// <summary>Surface landmark must exist on FullWorld before sculpt/terraform continues.</summary>
        public static bool RequireHollowTitanOnSurface(WorldGenerationRequest request, out string message)
        {
            message = null;
            if (request == null || request.SurfaceScope != SurfaceBuildScope.FullWorld)
                return true;

            if (!Enabled || !CaveBuildCursorSettings.LoadOrCreate().requireHollowTitanOnSurface)
                return true;

            var root = GameObject.Find(HollowTitanLandmarkAuthor.RootName);
            if (root != null && HollowTitanLandmarkAuthor.IsMeatBuildComplete(root.transform))
            {
                LogHollowTitanGradeIfBelowTarget(root.transform);
                return true;
            }

            message = "Hollow Titan landmark missing on surface — re-placing before terraform.";
            return false;
        }

        public static void EnsureHollowTitanOnSurface(Terrain mainTerrain, WorldGenerationRequest request)
        {
            if (mainTerrain == null || request == null ||
                request.SurfaceScope != SurfaceBuildScope.FullWorld)
                return;

            if (HollowTitanLandmarkAuthor.TryFindPreservableRoot(request, out _, out _))
                return;

            if (HollowTitanLandmarkMeatPhases.IsRunning)
                return;

            HollowTitanLandmarkAuthor.PlaceAaaBeforeTerraform(mainTerrain, request);
        }

        static void LogHollowTitanGradeIfBelowTarget(Transform root)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, HollowTitanBuildLadder.ReportPath);
            if (!File.Exists(path))
                return;

            try
            {
                var json = File.ReadAllText(path);
                var scoreKey = "\"overallScore\":";
                var idx = json.IndexOf(scoreKey, System.StringComparison.Ordinal);
                if (idx < 0)
                    return;

                var start = idx + scoreKey.Length;
                var end = json.IndexOfAny(new[] { ',', '\n', '}' }, start);
                if (end <= start ||
                    !float.TryParse(
                        json.Substring(start, end - start).Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var score))
                    return;

                if (score >= HollowTitanBuildLadder.TargetOverallScore)
                    return;

                CaveBuildEditorLog.LogSurfaceWarning(
                    $"[FastGate] Hollow Titan ladder {score:F0}/100 below target " +
                    $"{HollowTitanBuildLadder.TargetOverallScore} — see {HollowTitanBuildLadder.ReportPath}.",
                    forceUnityConsole: false);
            }
            catch
            {
                // Non-blocking advisory only.
            }
        }

        static bool TryReadLastOverallScore(int seed, out float score, out string letter)
        {
            score = 0f;
            letter = "?";
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, QualityReportRel);
            if (!File.Exists(path))
                return false;

            try
            {
                var json = File.ReadAllText(path);
                if (json.IndexOf($"\"seed\": {seed}", System.StringComparison.Ordinal) < 0 &&
                    json.IndexOf($"\"seed\":{seed}", System.StringComparison.Ordinal) < 0)
                    return false;

                var scoreKey = "\"overallScore\":";
                var idx = json.IndexOf(scoreKey, System.StringComparison.Ordinal);
                if (idx < 0)
                    return false;

                var start = idx + scoreKey.Length;
                var end = json.IndexOfAny(new[] { ',', '\n', '}' }, start);
                if (end <= start ||
                    !float.TryParse(
                        json.Substring(start, end - start).Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out score))
                    return false;

                var letterKey = "\"letterGrade\":";
                var lidx = json.IndexOf(letterKey, System.StringComparison.Ordinal);
                if (lidx >= 0)
                {
                    var q0 = json.IndexOf('"', lidx + letterKey.Length);
                    var q1 = json.IndexOf('"', q0 + 1);
                    if (q1 > q0)
                        letter = json.Substring(q0 + 1, q1 - q0 - 1);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif
