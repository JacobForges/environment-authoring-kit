#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Easter egg only — CAVE_ENABLE_AI_DIRECTOR=1. Not part of the Hub build pipeline.
    /// </summary>
    static class CaveBuildRecapDashboardGate
    {
        const string PrefOpenBeforeCompose = "EnvironmentKit_RecapDashboard_OpenBeforeCompose";
        const string PrefSkipReview = "EnvironmentKit_RecapDashboard_SkipReview";
        const string PrefAutoProceedMinutes = "EnvironmentKit_RecapDashboard_AutoProceedMinutes";
        const string PrefPendingGateFolder = "EnvironmentKit_RecapDashboard_PendingGateFolder";

        /// <summary>Opt-in only — export CAVE_ENABLE_AI_DIRECTOR=1 before launching Unity.</summary>
        public static bool AutoLaunchDirectorFromBuild =>
            string.Equals(
                Environment.GetEnvironmentVariable("CAVE_ENABLE_AI_DIRECTOR"),
                "1",
                StringComparison.Ordinal);

        public static bool OpenRecapDashboardBeforeCompose
        {
            get => EditorPrefs.GetBool(PrefOpenBeforeCompose, false);
            set => EditorPrefs.SetBool(PrefOpenBeforeCompose, value);
        }

        public static bool SkipRecapReview
        {
            get => EditorPrefs.GetBool(PrefSkipReview, true);
            set => EditorPrefs.SetBool(PrefSkipReview, value);
        }

        /// <summary>0 = disabled; legacy Hub pref (director is manual-only).</summary>
        public static int AutoProceedTimeoutMinutes
        {
            get => EditorPrefs.GetInt(PrefAutoProceedMinutes, 0);
            set => EditorPrefs.SetInt(PrefAutoProceedMinutes, Mathf.Max(0, value));
        }

        public static bool IsWaitingForProceed => false;

        public static string WaitingRunFolder => string.Empty;

        /// <summary>Run compose immediately — director is not auto-launched from builds.</summary>
        public static void BeginOrRunCompose(string runFolder, Action composeAction)
        {
            if (composeAction == null || string.IsNullOrEmpty(runFolder))
                return;

            if (CaveBuildRecapComposeLatch.IsComposeCompleted(runFolder))
            {
                Debug.Log("[DemoRecorder] Compose already completed — skipping: " + runFolder);
                return;
            }

            if (!AutoLaunchDirectorFromBuild)
            {
                composeAction();
                return;
            }

            if (SkipRecapReview || !OpenRecapDashboardBeforeCompose)
            {
                composeAction();
                return;
            }

            Debug.LogWarning(
                "[DemoRecorder] CAVE_ENABLE_AI_DIRECTOR=1 is set but the build-pipeline director gate was removed. " +
                "Run start-ai-director.sh manually, then compose will proceed.");
            composeAction();
        }

        /// <summary>Clears stale gate state from prior sessions.</summary>
        public static void TryResumePendingGate(Action<string> composeAction)
        {
            var folder = EditorPrefs.GetString(PrefPendingGateFolder, string.Empty);
            if (!string.IsNullOrEmpty(folder))
                EditorPrefs.DeleteKey(PrefPendingGateFolder);
        }
    }
}
#endif
