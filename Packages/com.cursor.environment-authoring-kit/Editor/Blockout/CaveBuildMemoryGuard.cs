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
        const float PressurePauseRatio = 0.84f;
        const float PressureUnloadRatio = 0.72f;
        const int UnloadEveryStepsExtended = 6;

        static int _stepsSinceUnload;
        static bool _pressureDialogShown;
        static double _lastPressureAt;

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
                $"unload every {UnloadEveryStepsExtended} steps. Checkpoint saved each terraform tile.",
                forceUnityConsole: true);
        }

        public static void OnFullWorldQueueStepCompleted(int tileCount)
        {
            if (tileCount <= ExtendedGridTileThreshold)
                return;

            _stepsSinceUnload++;
            if (_stepsSinceUnload < UnloadEveryStepsExtended)
                return;

            _stepsSinceUnload = 0;
            ReleaseWorkingSet("extended grid step");
        }

        /// <summary>Returns true when queue must not run (memory pause or user pause).</summary>
        public static bool ShouldHoldQueueForMemory()
        {
            if (MemoryPauseActive)
                return true;

            if (!CaveBuildSurfaceCompletionGate.IsFullWorldGridPipelineActive)
                return false;

            var ratio = PressureRatio01();
            if (ratio < PressurePauseRatio)
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
            Resources.UnloadUnusedAssets();
            EditorUtility.UnloadUnusedAssetsImmediate();
            GC.Collect();
            CaveBuildEditorLog.LogSurface(
                $"[MemoryGuard] Working-set release ({reason}) — WS {WorkingSetGb():F1} GB, pressure {PressureRatio01() * 100f:F0}%",
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
