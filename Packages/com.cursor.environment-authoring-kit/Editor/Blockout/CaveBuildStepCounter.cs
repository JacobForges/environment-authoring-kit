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

        /// <summary>Fixed step budget shown in Hub (current / planned).</summary>
        public static int PlannedTotal => _plannedTotal > 0 ? _plannedTotal : _estimatedTotal;

        /// <summary>Live denominator — never below current or scheduled queue depth.</summary>
        public static int EffectivePlannedTotal
        {
            get
            {
                if (!_sessionActive)
                    return PlannedTotal;

                var baseline = PlannedTotal;
                var live = Mathf.Max(baseline, _scheduledLifetime, _current);
                if (CaveBuildActionPacing.HasQueuedWork)
                    live = Mathf.Max(live, _current + CaveBuildActionPacing.QueuedCount);

                return live;
            }
        }

        /// <summary>0–1 for Hub progress bars when current can exceed the initial estimate.</summary>
        public static float Progress01
        {
            get
            {
                if (!_sessionActive || EffectivePlannedTotal <= 0)
                    return 0f;

                return Mathf.Clamp01(_current / (float)EffectivePlannedTotal);
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
            _plannedTotal = 0;
            _scheduledLifetime = 0;
            _estimatedTotal = estimatedTotal > 0 ? estimatedTotal : EstimateInitialTotal();
            SetPlannedTotal(_estimatedTotal);
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

        /// <summary>One editor-queue step was scheduled (mirrors Advance at execution time).</summary>
        public static void RegisterScheduledStep(int count = 1)
        {
            if (count <= 0)
                return;

            if (!_sessionActive)
                BeginSession();

            _scheduledLifetime += count;
            ReconcilePlannedTotal();
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

        public static void Advance(string label = null)
        {
            if (!_sessionActive)
                BeginSession();

            _current++;
            ReconcilePlannedTotal();
            CaveBuildLateBuildPerformance.NotifyStepAdvanced(_current);
            CaveBuildPacedStepPersistence.OnPacedStepCompleted(_current, label);
            var now = EditorApplication.timeSinceStartup;
            var dt = (float)Math.Max(0.001, now - _lastAdvanceAt);
            _lastAdvanceAt = now;
            var instant = 1f / dt;
            _emaStepsPerSecond = _emaStepsPerSecond <= 0 ? instant : _emaStepsPerSecond * 0.9f + instant * 0.1f;

            if (!string.IsNullOrEmpty(label))
                CaveBuildRunStatusPublisher.RecordActivity("step", label);
        }

        public static void SyncLiveTotals() => ReconcilePlannedTotal();

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
            if (!_sessionActive)
                return;

            var live = EffectivePlannedTotal;
            if (live > _plannedTotal)
            {
                _plannedTotal = live;
                _estimatedTotal = live;
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

            var effectiveTotal = EffectivePlannedTotal;
            var remainingSteps = effectiveTotal - _current;

            if (remainingSteps <= 0)
            {
                if (CaveBuildActionPacing.HasQueuedWork)
                    remainingSteps = Mathf.Max(CaveBuildActionPacing.QueuedCount, 24);
                else if (_segmentProgress01 > 0.02f && _segmentProgress01 < 0.985f)
                    remainingSteps = Mathf.Max(16, (int)((1f - _segmentProgress01) / _segmentProgress01 * _current));
                else if (_sessionActive && _current > 40)
                    remainingSteps = Mathf.Max(32, (int)(elapsed / Math.Max(_current, 1) * 48));
                else
                    return "…";
            }

            var globalSec = remainingSteps / Math.Max(_emaStepsPerSecond, 0.08);

            var segmentSec = globalSec;
            if (_segmentBudgetSteps > 0 && _segmentProgress01 < 0.995f)
            {
                var segRemaining = (1f - _segmentProgress01) * _segmentBudgetSteps;
                segmentSec = segRemaining / Math.Max(_emaStepsPerSecond, 0.08);
            }

            var trustSegment = _segmentProgress01 > 0.03f && _segmentProgress01 < 0.99f ? 0.62f : 0.2f;
            var sec = segmentSec * trustSegment + globalSec * (1f - trustSegment);

            if (_current > 20 && effectiveTotal > 0 && elapsed > 1)
            {
                var pace = _current / elapsed;
                var needed = remainingSteps / Math.Max(pace, 0.01);
                sec = sec * 0.45 + needed * 0.55;
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
