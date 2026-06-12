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
        /// <summary>Locked Hub denominator — set once per session, never mid-run auto-expansion.</summary>
        static int _plannedTotal;
        static bool _plannedTotalLocked;
        static int _scheduledLifetime;
        static bool _plannedTotalFrozen;
        static double _sessionStart;
        static double _lastAdvanceAt;
        static double _emaStepsPerSecond;
        static double _emaWeightedSecondsPerStep = 0.12;
        static bool _sessionActive;

        static BuildSegment _segment = BuildSegment.General;
        static string _segmentLabel = string.Empty;
        static int _segmentBudgetSteps;
        static float _segmentProgress01;
        static float _surfaceWeightedProgress01;
        static float _terrainSculptProgress01;

        public static int Current => _current;

        public static int EstimatedTotal => _estimatedTotal;

        /// <summary>Fixed step budget shown in Hub (current / planned).</summary>
        public static int PlannedTotal => _plannedTotal > 0 ? _plannedTotal : _estimatedTotal;

        /// <summary>Hub denominator — locked at session configure unless current exceeds budget.</summary>
        public static int EffectivePlannedTotal
        {
            get
            {
                if (!_sessionActive)
                    return PlannedTotal;

                return Mathf.Max(PlannedTotal, _current);
            }
        }

        /// <summary>0–1 for Hub progress bars — blends paced steps with surface/sculpt segment progress.</summary>
        public static float Progress01
        {
            get
            {
                if (!_sessionActive || EffectivePlannedTotal <= 0)
                    return 0f;

                return CompositeProgress01();
            }
        }

        public static bool HasSession => _sessionActive;

        public static BuildSegment Segment => _segment;

        public static string SegmentLabel => _segmentLabel;

        public static void BeginSession(int estimatedTotal = 0)
        {
            if (_sessionActive)
                return;

            _current = 0;
            _plannedTotalLocked = false;
            _plannedTotalFrozen = false;
            _plannedTotal = 0;
            _scheduledLifetime = 0;
            _estimatedTotal = estimatedTotal > 0 ? estimatedTotal : EstimateInitialTotal();
            SetPlannedTotal(_estimatedTotal);
            _sessionStart = EditorApplication.timeSinceStartup;
            _lastAdvanceAt = _sessionStart;
            _emaStepsPerSecond = 0;
            _emaWeightedSecondsPerStep = 0.12;
            _segment = BuildSegment.General;
            _segmentLabel = string.Empty;
            _segmentBudgetSteps = 0;
            _segmentProgress01 = 0f;
            _surfaceWeightedProgress01 = 0f;
            _terrainSculptProgress01 = 0f;
            _sessionActive = true;
            CaveBuildHardwareMonitor.EnsureSampling();
        }

        public static void EndSession()
        {
            _sessionActive = false;
            CaveBuildHardwareMonitor.StopSampling();
            CaveBuildPipelinePhaseTracker.OnSessionEnded();
        }

        /// <summary>Restore Hub step display after playtest / checkpoint resume.</summary>
        public static void RestoreSession(int pacedStep, int plannedTotal = 0)
        {
            if (!_sessionActive)
                BeginSession(plannedTotal);

            _current = Mathf.Max(0, pacedStep);
            _scheduledLifetime = Mathf.Max(_scheduledLifetime, _current);
            if (plannedTotal > 0)
                SetPlannedTotal(plannedTotal, replace: true);
            else
                ReconcilePlannedTotal();

            _lastAdvanceAt = EditorApplication.timeSinceStartup;
        }

        public static void ConfigureForBuild(SurfaceBuildScope scope, bool extendedOpenWorldGrid)
        {
            if (!_sessionActive)
                BeginSession();

            var tileCount = extendedOpenWorldGrid
                ? SurfaceOpenWorldGridExpansion.ExtendedBuildTileSlotCount
                : SurfaceTerrainTileExpansion.FullWorldTerrainTileCount;
            var total = CaveBuildPlannedStepBudget.Compute(
                scope,
                tileCount,
                extendedOpenWorldGrid && tileCount > 81);
            _estimatedTotal = total;
            SetPlannedTotal(total);
        }

        /// <summary>Lock Hub step total from active concept preset (tile plan + style flags).</summary>
        public static void ConfigureForRequest(WorldGenerationRequest request)
        {
            if (request == null)
                return;

            if (!_sessionActive)
                BeginSession();

            var total = CaveBuildPlannedStepBudget.ComputeForRequest(request);
            _estimatedTotal = total;
            SetPlannedTotal(total, replace: true);
        }

        /// <summary>Lock Hub denominator from the actual tile plan (all paced queue steps).</summary>
        public static void ConfigureForExtendedTilePlan(int tileCount)
        {
            if (!_sessionActive)
                BeginSession();

            var total = CaveBuildPlannedStepBudget.Compute(
                SurfaceBuildScope.FullWorld,
                tileCount,
                extendedGrid: tileCount > 81);
            _estimatedTotal = total;
            SetPlannedTotal(total, replace: true);
        }

        /// <summary>Tracks queue depth for diagnostics — does not inflate the locked Hub denominator.</summary>
        public static void RegisterScheduledStep(int count = 1)
        {
            if (count <= 0)
                return;

            if (!_sessionActive)
                BeginSession();

            _scheduledLifetime += count;
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

        /// <summary>0–5% band while purging/consolidating before the first ground-lay batch.</summary>
        public static void PulseFlatGridPrepareProgress(int step, int total, string label)
        {
            if (total <= 0)
                return;

            var p = step / (float)total * 0.05f;
            var budget = _segmentBudgetSteps > 0 ? _segmentBudgetSteps : 900;
            SetSegment(BuildSegment.FlatGrid, budget, p, label);
        }

        public static void SetFlatGridTerraformProgress(int step, int total)
        {
            if (total <= 0)
                return;

            var p = 0.5f + step / (float)total * 0.5f;
            SetSegment(BuildSegment.FlatGrid, total * 2 + 320, p, $"Terraform {step}/{total}");
        }

        /// <summary>50–58% band after ground lay: grid prep, snap batches, weld lead-in (before terraform).</summary>
        public static void PulseFlatGridPostPlaceProgress(string label, float subFrac01)
        {
            var p = 0.5f + Mathf.Clamp01(subFrac01) * 0.08f;
            var budget = _segmentBudgetSteps > 0 ? _segmentBudgetSteps : 900;
            SetSegment(BuildSegment.FlatGrid, budget, p, label);
        }

        /// <summary>58–70% band while welding grid edges before terraform.</summary>
        public static void PulseFlatGridWeldProgress(int step, int total, string label)
        {
            if (total <= 0)
                return;

            var p = 0.58f + step / (float)total * 0.12f;
            var budget = _segmentBudgetSteps > 0 ? _segmentBudgetSteps : 900;
            SetSegment(BuildSegment.FlatGrid, budget, p, label);
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

        /// <summary>Fractional sculpt progress (pass/row) for ETC when micro steps outrun macro budget.</summary>
        public static void NotifyTerrainSculptMicroProgress(
            int passIndex,
            int passCount,
            int row,
            int resolution)
        {
            if (passCount <= 0 || resolution <= 0)
                return;

            var passFrac = (passIndex + row / (float)resolution) / passCount;
            _terrainSculptProgress01 = Mathf.Clamp01(passFrac);
            _segmentProgress01 = Mathf.Max(_segmentProgress01, _terrainSculptProgress01 * 0.92f);
        }

        public static void Advance(string label = null)
        {
            if (!_sessionActive)
                BeginSession();

            _current++;
            CaveBuildLateBuildPerformance.NotifyStepAdvanced(_current);
            CaveBuildPacedStepPersistence.OnPacedStepCompleted(_current, label);
            var now = EditorApplication.timeSinceStartup;
            var dt = (float)Math.Max(0.001, now - _lastAdvanceAt);
            _lastAdvanceAt = now;
            var weight = StepTimeWeight(label);
            var weightedDt = dt / Mathf.Max(0.15f, weight);
            var instant = 1f / weightedDt;
            _emaStepsPerSecond = _emaStepsPerSecond <= 0 ? instant : _emaStepsPerSecond * 0.88f + instant * 0.12f;
            _emaWeightedSecondsPerStep = _emaWeightedSecondsPerStep <= 0
                ? weightedDt
                : _emaWeightedSecondsPerStep * 0.88 + weightedDt * 0.12;

            if (!string.IsNullOrEmpty(label))
                CaveBuildRunStatusPublisher.RecordActivity("step", label);
        }

        public static void SyncLiveTotals()
        {
            if (!_sessionActive || _plannedTotalFrozen)
                return;

            ReconcilePlannedTotal();
        }

        static float StepTimeWeight(string label)
        {
            if (string.IsNullOrEmpty(label))
                return 1f;

            if (label.IndexOf("terrain sculpt micro", StringComparison.OrdinalIgnoreCase) >= 0)
                return 0.28f;
            if (label.IndexOf("planner brief", StringComparison.OrdinalIgnoreCase) >= 0)
                return 0.35f;
            if (label.IndexOf("prop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("vegetation", StringComparison.OrdinalIgnoreCase) >= 0)
                return 0.55f;
            if (label.IndexOf("CC0", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("import", StringComparison.OrdinalIgnoreCase) >= 0)
                return 1.35f;
            if (label.IndexOf("validate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("navmesh", StringComparison.OrdinalIgnoreCase) >= 0)
                return 1.2f;

            return 1f;
        }

        static float CompositeProgress01()
        {
            if (!_sessionActive || EffectivePlannedTotal <= 0)
                return 0f;

            var stepProgress = _current / (float)EffectivePlannedTotal;
            return Mathf.Clamp01(Mathf.Max(stepProgress, _surfaceWeightedProgress01, _segmentProgress01, _terrainSculptProgress01));
        }

        public static string FormatHubStepCurrent() =>
            _sessionActive ? $"{_current:N0}" : "0";

        public static string FormatHubStepPlannedTotal() =>
            $"{EffectivePlannedTotal:N0}";

        public static string FormatHubStepLine()
        {
            if (!_sessionActive)
                return "0";

            return $"{FormatHubStepCurrent()} / {FormatHubStepPlannedTotal()}";
        }

        static void ReconcilePlannedTotal()
        {
            if (!_sessionActive || _plannedTotalFrozen)
                return;

            if (_current > _plannedTotal)
            {
                _plannedTotal = _current;
                _estimatedTotal = _current;
            }
        }

        static void SetPlannedTotal(int total, bool replace = false)
        {
            if (total <= 0)
                return;

            if (replace || !_plannedTotalLocked || total > _plannedTotal)
            {
                _plannedTotal = total;
                _plannedTotalLocked = true;
                _estimatedTotal = total;
                if (replace)
                    _plannedTotalFrozen = true;
            }
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
            if (!_sessionActive || _current < 3)
                return "…";

            var elapsed = EditorApplication.timeSinceStartup - _sessionStart;
            if (_emaStepsPerSecond <= 0.04 && elapsed < 12)
                return "…";

            var composite = CompositeProgress01();
            double sec;

            if (composite > 0.06f && elapsed > 2.5)
            {
                sec = elapsed / composite * (1.0 - composite);
            }
            else
            {
                var effectiveTotal = EffectivePlannedTotal;
                var remainingSteps = Mathf.Max(0, effectiveTotal - _current);
                if (remainingSteps <= 0 && CaveBuildActionPacing.HasQueuedWork)
                    remainingSteps = Mathf.Max(CaveBuildActionPacing.QueuedCount, 48);

                sec = remainingSteps * Math.Max(_emaWeightedSecondsPerStep, 0.06);
            }

            if (_terrainSculptProgress01 > 0.02f && _terrainSculptProgress01 < 0.98f)
            {
                var sculptSec = elapsed / Math.Max(_terrainSculptProgress01, 0.04) * (1.0 - _terrainSculptProgress01);
                sec = sec * 0.35 + sculptSec * 0.65;
            }
            else if (_surfaceWeightedProgress01 > 0.04f && _surfaceWeightedProgress01 < 0.98f)
            {
                var surfaceSec = elapsed / _surfaceWeightedProgress01 * (1.0 - _surfaceWeightedProgress01);
                sec = sec * 0.4 + surfaceSec * 0.6;
            }

            if (_current > 20 && EffectivePlannedTotal > 0 && elapsed > 1)
            {
                var paceSec = (EffectivePlannedTotal - _current) * (_emaWeightedSecondsPerStep > 0
                    ? _emaWeightedSecondsPerStep
                    : elapsed / Math.Max(_current, 1));
                sec = sec * 0.5 + paceSec * 0.5;
            }

            if (CaveBuildActionPacing.HasQueuedWork)
            {
                var queueSec = CaveBuildActionPacing.QueuedCount * Math.Max(_emaWeightedSecondsPerStep, 0.05);
                sec = Math.Max(sec, queueSec * 0.55);
            }

            if (sec < 0.5)
                sec = 0.5;

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

        static int EstimateInitialTotal()
        {
            var idx = CaveBuildConceptSession.ResolveLockedConceptIndex();
            var request = new WorldGenerationRequest
            {
                SurfaceScope = SurfaceBuildScope.FullWorld,
                ConceptLayoutIndex = idx,
            };
            FullWorldConceptLayoutCatalog.ApplyByIndex(request, idx);
            return CaveBuildPlannedStepBudget.ComputeForRequest(request);
        }

        internal static int ConfigureForExtendedTilePlanEstimate(int tileCount) =>
            CaveBuildPlannedStepBudget.Compute(
                SurfaceBuildScope.FullWorld,
                tileCount,
                extendedGrid: tileCount > 81);
    }
}
#endif
