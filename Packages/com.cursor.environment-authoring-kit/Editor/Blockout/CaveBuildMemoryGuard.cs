#if UNITY_EDITOR
using System;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Prevents macOS OOM during ~289-tile FullWorld builds: RAM sampling, pacing, checkpoint+pause.
    /// </summary>
    public static class CaveBuildMemoryGuard
    {
        const string PrefMemoryPause = "CaveBuild_MemoryPauseActive";
        const float ExtendedGridTileThreshold = 81f;
        const float PressurePauseRatio16Gb = 0.62f;
        const float PressureUnloadRatio16Gb = 0.55f;
        const float PressurePauseRatio = 0.84f;
        const float PressureUnloadRatio = 0.72f;
        const int UnloadEveryStepsExtended = 6;
        const int UnloadEveryStepsExtended16Gb = 3;
        const int UnloadEveryStepsLateBand16Gb = 10;

        static int UnloadEveryExtendedSteps =>
            SystemRamGb() is > 0f and <= 17f
                ? CaveBuildLateBuildPerformance.IsInLateBuildBand
                    ? UnloadEveryStepsLateBand16Gb
                    : UnloadEveryStepsExtended16Gb
                : UnloadEveryStepsExtended;

        static float PauseThresholdRatio =>
            SystemRamGb() is > 0f and <= 17f ? PressurePauseRatio16Gb : PressurePauseRatio;

        static float UnloadThresholdRatio =>
            SystemRamGb() is > 0f and <= 17f ? PressureUnloadRatio16Gb : PressureUnloadRatio;

        /// <summary>
        /// Mandatory release at CC0 → Hollow Titan boundary (~289 tiles + CC0 meshes = 10GB+ on 16GB Macs).
        /// </summary>
        public static void PreparePostCc0Handoff(Terrain mainTerrain)
        {
            if (mainTerrain != null)
                CaveBuildTerrainHeightmapMemory.FlushAllSurfaceTerrains(mainTerrain);

            ReleaseWorkingSetAtBoundary("post-CC0 handoff — before Hollow Titan / terraform");
        }

        public static void ReleaseWorkingSetAtBoundary(string reason)
        {
            CaveBuildTerrainHeightmapMemory.ReleaseWorkingSet(force: true);
            Resources.UnloadUnusedAssets();
            EditorUtility.UnloadUnusedAssetsImmediate();
            GC.Collect();
            CaveBuildEditorLog.LogSurface(
                $"[MemoryGuard] Boundary release ({reason}) — WS {WorkingSetGb():F1} GB / {SystemRamGb():F0} GB RAM, " +
                $"pressure {PressureRatio01() * 100f:F0}%",
                forceUnityConsole: true);
        }

        static int _stepsSinceUnload;
        static bool _pressureDialogShown;
        static bool _suppressPeriodicRelease;
        static double _lastPressureAt;

        /// <summary>Skip periodic UnloadUnusedAssets during CC0 import finalize (grid ~70%).</summary>
        public static void SetCc0ImportPhaseActive(bool active) =>
            _suppressPeriodicRelease = active;

        public static bool IsCc0ImportPhaseActive => _suppressPeriodicRelease;

        public static bool MemoryPauseActive =>
            EditorPrefs.GetBool(PrefMemoryPause, false);

        public static float SystemRamGb()
        {
            try
            {
                if (UnityEngine.SystemInfo.systemMemorySize > 0)
                    return UnityEngine.SystemInfo.systemMemorySize / 1024f;
            }
            catch
            {
                /* fall through */
            }

            return 0f;
        }

        public static float WorkingSetGb()
        {
            try
            {
                return Process.GetCurrentProcess().WorkingSet64 / (1024f * 1024f * 1024f);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>Working set / physical RAM when known; else vs editor budget.</summary>
        public static float PressureRatio01()
        {
            var ws = WorkingSetGb();
            var sys = SystemRamGb();
            if (sys >= 4f)
                return Mathf.Clamp01(ws / sys);

            var budget = EnvironmentKitHardwareBudget.ResolveEditorRamBudgetGb();
            return budget >= 4f ? Mathf.Clamp01(ws / budget) : 0f;
        }

        public static void ApplyExtendedGridMemoryProfile(int tileCount)
        {
            if (tileCount <= ExtendedGridTileThreshold)
                return;

            var sys = SystemRamGb();
            if (sys >= 4f && sys <= 17f)
            {
                var cap = Mathf.Max(10f, sys * 0.75f);
                EditorPrefs.SetFloat(EnvironmentKitHardwareBudget.EditorRamBudgetGbKey, cap);
            }

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            settings.editorQueueBatchSize = 1;
            settings.mirrorPacedBuildLogsToConsole = false;
            settings.SaveToPrefs();
            CaveBuildEditorResponsiveness.ApplyForActiveBuild(settings);

            CaveBuildEditorLog.LogSurface(
                $"[MemoryGuard] Extended grid ({tileCount} tiles) — batch=1, RAM cap {EnvironmentKitHardwareBudget.ResolveEditorRamBudgetGb():F0} GB, " +
                $"unload every {UnloadEveryExtendedSteps} steps. Checkpoint JSON throttled; scene save every 48 terraform tiles.",
                forceUnityConsole: true);
        }

        public static void OnFullWorldQueueStepCompleted(int tileCount)
        {
            if (_suppressPeriodicRelease || tileCount <= ExtendedGridTileThreshold)
                return;

            _stepsSinceUnload++;
            if (_stepsSinceUnload < UnloadEveryExtendedSteps)
                return;

            _stepsSinceUnload = 0;
            if (PressureRatio01() >= UnloadThresholdRatio)
                ReleaseWorkingSet("extended grid step — elevated pressure");
            else
                ReleaseWorkingSetLight("extended grid step");
        }

        /// <summary>Returns true when queue must not run (memory pause or user pause).</summary>
        public static bool ShouldHoldQueueForMemory()
        {
            if (MemoryPauseActive)
                return true;

            if (!CaveBuildSurfaceCompletionGate.IsFullWorldGridPipelineActive &&
                !CaveBuildEditorResponsiveness.IsLongBuildActive)
                return false;

            var ratio = PressureRatio01();
            if (ratio < PauseThresholdRatio)
                return false;

            var now = EditorApplication.timeSinceStartup;
            if (now - _lastPressureAt < 2.0)
                return true;

            _lastPressureAt = now;
            TriggerMemoryPressurePause(ratio);
            return true;
        }

        public static void ClearMemoryPause()
        {
            EditorPrefs.SetBool(PrefMemoryPause, false);
            _pressureDialogShown = false;
        }

        public static void ReleaseWorkingSet(string reason)
        {
            CaveBuildTerrainHeightmapMemory.ReleaseWorkingSet(force: false);
            if (!CaveBuildActionPacing.HasQueuedWork)
            {
                Resources.UnloadUnusedAssets();
                EditorUtility.UnloadUnusedAssetsImmediate();
            }

            GC.Collect();
            CaveBuildEditorLog.LogSurface(
                $"[MemoryGuard] Working-set release ({reason}) — WS {WorkingSetGb():F1} GB, pressure {PressureRatio01() * 100f:F0}%",
                forceUnityConsole: false);
        }

        static void ReleaseWorkingSetLight(string reason)
        {
            CaveBuildTerrainHeightmapMemory.ReleaseWorkingSet(force: false);
            GC.Collect(0, GCCollectionMode.Optimized);
            CaveBuildEditorLog.LogSurface(
                $"[MemoryGuard] Working-set trim ({reason}) — WS {WorkingSetGb():F1} GB, pressure {PressureRatio01() * 100f:F0}%",
                forceUnityConsole: false);
        }

        static void TriggerMemoryPressurePause(float ratio)
        {
            CaveBuildFullWorldGridCheckpoint.SaveActiveSession(
                $"memory pressure {ratio * 100f:F0}% — paused before OOM");
            EditorPrefs.SetBool(PrefMemoryPause, true);
            CaveBuildPauseController.Pause();

            ReleaseWorkingSet("memory pressure pause");

            if (_pressureDialogShown)
                return;

            _pressureDialogShown = true;
            EditorUtility.DisplayDialog(
                "Cave build — memory guard",
                "Unity was close to running out of RAM during the FullWorld grid build.\n\n" +
                "The build is PAUSED and a checkpoint was saved.\n\n" +
                "Quit Cursor/other heavy apps, then use:\n" +
                "Cave Build → Diagnostics → Resume FullWorld Grid From Checkpoint\n\n" +
                "Or reduce scope (play disk only) before rebuilding.",
                "OK");
        }
    }
}
#endif
