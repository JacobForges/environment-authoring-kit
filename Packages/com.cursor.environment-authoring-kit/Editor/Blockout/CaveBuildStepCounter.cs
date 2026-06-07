#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Unified paced-step counter (Hub) plus segment-weighted ETC for long FullWorld / AAA runs.
    /// </summary>
    public static class CaveBuildStepCounter
    {
        public enum BuildSegment
        {
            General,
            FlatGrid,
            TerrainAi,
            CaveMacro,
            Polish,
        }

        static int _current;
        static int _estimatedTotal = 12_000;
        static double _sessionStart;
        static double _lastAdvanceAt;
        static double _emaStepsPerSecond;
        static bool _sessionActive;

        static BuildSegment _segment = BuildSegment.General;
        static string _segmentLabel = string.Empty;
        static int _segmentBudgetSteps;
        static float _segmentProgress01;
        static float _surfaceWeightedProgress01;

        public static int Current => _current;

        public static int EstimatedTotal => _estimatedTotal;

        public static bool HasSession => _sessionActive;

        public static BuildSegment Segment => _segment;

        public static string SegmentLabel => _segmentLabel;

        public static void BeginSession(int estimatedTotal = 0)
        {
            if (_sessionActive)
                return;

            _current = 0;
            _estimatedTotal = estimatedTotal > 0 ? estimatedTotal : EstimateInitialTotal();
            _sessionStart = EditorApplication.timeSinceStartup;
            _lastAdvanceAt = _sessionStart;
            _emaStepsPerSecond = 0;
            _segment = BuildSegment.General;
            _segmentLabel = string.Empty;
            _segmentBudgetSteps = 0;
            _segmentProgress01 = 0f;
            _surfaceWeightedProgress01 = 0f;
            _sessionActive = true;
            CaveBuildHardwareMonitor.EnsureSampling();
        }

        public static void EndSession()
        {
            _sessionActive = false;
            CaveBuildHardwareMonitor.StopSampling();
            CaveBuildPipelinePhaseTracker.OnSessionEnded();
        }

        public static void ConfigureForBuild(SurfaceBuildScope scope, bool aaaExtendedGrid)
        {
            if (!_sessionActive)
                BeginSession();

            _estimatedTotal = EstimateTotalForScope(scope, aaaExtendedGrid);
        }

        public static void SetSegment(BuildSegment segment, int budgetSteps, float progress01, string label)
        {
            _segment = segment;
            _segmentBudgetSteps = Mathf.Max(0, budgetSteps);
            _segmentProgress01 = Mathf.Clamp01(progress01);
            _segmentLabel = label ?? string.Empty;
        }

        public static void SetSegmentProgress(float progress01) =>
            _segmentProgress01 = Mathf.Clamp01(progress01);

        public static void SetFlatGridPlaceProgress(int step, int total)
        {
            if (total <= 0)
                return;

            var p = step / (float)total * 0.5f;
            SetSegment(BuildSegment.FlatGrid, total * 2 + 320, p, $"Flat place {step}/{total}");
        }

        public static void SetFlatGridTerraformProgress(int step, int total)
        {
            if (total <= 0)
                return;

            var p = 0.5f + step / (float)total * 0.5f;
            SetSegment(BuildSegment.FlatGrid, total * 2 + 320, p, $"Terraform {step}/{total}");
        }

        /// <summary>Fractional progress while sculpting the current terraform tile (pass/row sub-steps).</summary>
        public static void PulseFlatGridTerraformSculptProgress(
            int terraformStep,
            int terraformTotal,
            int pass,
            int passTotal,
            int row,
            int rowTotal)
        {
            if (terraformTotal <= 0 || passTotal <= 0 || rowTotal <= 0)
                return;

            var tileFrac = ((pass - 1) + row / (float)rowTotal) / passTotal;
            var p = 0.5f + (Mathf.Max(0, terraformStep - 1) + tileFrac) / terraformTotal * 0.5f;
            SetSegment(
                BuildSegment.FlatGrid,
                terraformTotal * 2 + 320,
                p,
                $"Terraform {terraformStep}/{terraformTotal} · sculpt {pass}/{passTotal}");
        }

        /// <summary>Fractional progress for queued terraform micro-steps (align, sculpt rows, denoise, dress).</summary>
        public static void PulseFlatGridTerraformMicroProgress(
            int terraformStep,
            int terraformTotal,
            string microLabel,
            int microStep,
            int microTotal)
        {
            if (terraformTotal <= 0 || string.IsNullOrEmpty(microLabel))
                return;

            var microFrac = microTotal > 0 ? microStep / (float)microTotal : 0.12f;
            var p = 0.5f + (Mathf.Max(0, terraformStep - 1) + microFrac) / terraformTotal * 0.5f;
            var label = microTotal > 0
                ? $"Terraform {terraformStep}/{terraformTotal} · micro {microLabel} {microStep}/{microTotal}"
                : $"Terraform {terraformStep}/{terraformTotal} · micro {microLabel}";
            SetSegment(BuildSegment.FlatGrid, terraformTotal * 2 + 320, p, label);
        }

        public static void SyncSurfaceWeightedProgress(float progress01) =>
            _surfaceWeightedProgress01 = Mathf.Clamp01(progress01);

        public static void Advance(string label = null)
        {
            if (!_sessionActive)
                BeginSession();

            _current++;
            var now = EditorApplication.timeSinceStartup;
            var dt = (float)Math.Max(0.001, now - _lastAdvanceAt);
            _lastAdvanceAt = now;
            var instant = 1f / dt;
            _emaStepsPerSecond = _emaStepsPerSecond <= 0 ? instant : _emaStepsPerSecond * 0.9f + instant * 0.1f;

            if (_current > _estimatedTotal * 0.9f)
                _estimatedTotal = Mathf.RoundToInt(_estimatedTotal * 1.12f + 400f);

            if (!string.IsNullOrEmpty(label))
                CaveBuildRunStatusPublisher.RecordActivity("step", label);
        }

        public static string FormatHubStepLine()
        {
            if (!_sessionActive || _current <= 0)
                return "0";

            if (_estimatedTotal > _current + 50)
                return $"{_current:N0} / ~{_estimatedTotal:N0}";

            return $"{_current:N0}";
        }

        public static string FormatHubPhaseLine()
        {
            if (!_sessionActive)
                return string.Empty;

            var phase = CaveBuildPipelinePhaseTracker.CurrentPhaseLabel;
            if (string.IsNullOrEmpty(phase))
                return string.Empty;

            if (_segment == BuildSegment.FlatGrid && _segmentBudgetSteps > 0)
                return $"{phase} · grid {_segmentProgress01:P0}";

            if (_surfaceWeightedProgress01 > 0.01f && _segment != BuildSegment.CaveMacro)
                return $"{phase} · surface {_surfaceWeightedProgress01:P0}";

            if (CaveBuildPipelinePhaseTracker.PipelineMacroStep >= 0)
            {
                return $"{phase} · macro {CaveBuildPipelinePhaseTracker.PipelineMacroStep + 1}/" +
                       $"{CaveBuildPipelinePhaseTracker.PipelineMacroTotal}";
            }

            return phase;
        }

        public static string FormatElapsed()
        {
            if (!_sessionActive || _sessionStart <= 0)
                return "00:00.000";

            var elapsed = EditorApplication.timeSinceStartup - _sessionStart;
            return FormatDuration(elapsed);
        }

        public static string FormatEtc()
        {
            if (!_sessionActive || _current < 6 || _emaStepsPerSecond <= 0.04)
                return "…";

            var elapsed = EditorApplication.timeSinceStartup - _sessionStart;
            var remainingSteps = Math.Max(0, _estimatedTotal - _current);
            var globalSec = remainingSteps / _emaStepsPerSecond;

            var segmentSec = globalSec;
            if (_segmentBudgetSteps > 0 && _segmentProgress01 < 0.995f)
            {
                var segRemaining = (1f - _segmentProgress01) * _segmentBudgetSteps;
                segmentSec = segRemaining / Math.Max(_emaStepsPerSecond, 0.08);
            }

            var trustSegment = _segmentProgress01 > 0.03f && _segmentProgress01 < 0.99f ? 0.62f : 0.2f;
            var sec = segmentSec * trustSegment + globalSec * (1f - trustSegment);

            if (_current > 80 && _estimatedTotal > 0 && elapsed > 1)
            {
                var pace = _current / elapsed;
                var needed = remainingSteps / Math.Max(pace, 0.01);
                sec = sec * 0.45 + needed * 0.55;
            }

            if (sec > 86400)
                return ">24h";

            return "~" + FormatDuration(sec);
        }

        static string FormatDuration(double totalSeconds)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";

            return $"{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
        }

        static int EstimateInitialTotal() => EstimateTotalForScope(SurfaceBuildScope.FullWorld, true);

        static int EstimateTotalForScope(SurfaceBuildScope scope, bool aaaExtendedGrid)
        {
            if (aaaExtendedGrid)
                return 7_500;

            return scope switch
            {
                SurfaceBuildScope.FullWorld => 4_000,
                SurfaceBuildScope.SurfaceOnly => 14_000,
                SurfaceBuildScope.CaveOnly => 9_000,
                _ => 12_000,
            };
        }
    }
}
#endif
