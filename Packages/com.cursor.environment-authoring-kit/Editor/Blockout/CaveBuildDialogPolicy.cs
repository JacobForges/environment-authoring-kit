#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>During automated cave builds, log instead of stacking modal dialogs.</summary>
    public static class CaveBuildDialogPolicy
    {
        static int _unifiedSessionDepth;

        public static bool SuppressModals =>
            _unifiedSessionDepth > 0 ||
            LavaTubeCaveBuilder.IsBuildInProgress ||
            LavaTubeCaveBuildPipeline.IsPhasedBuildActive ||
            CaveBuildBatchRunner.IsActive;

        public static void BeginUnifiedSession() => _unifiedSessionDepth++;

        public static void EndUnifiedSession()
        {
            _unifiedSessionDepth = Mathf.Max(0, _unifiedSessionDepth - 1);
        }

        public static void Notify(string title, string message)
        {
            if (ShouldLogInsteadOfModal())
            {
                Debug.Log($"[CaveBuild] {title}\n{message}");
                return;
            }

            EditorUtility.DisplayDialog(title, message, "OK");
        }

        static bool ShouldLogInsteadOfModal()
        {
            if (SuppressModals || CaveBuildAutonomousOrchestrator.IsRunning)
                return true;

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            return settings.suppressMidBuildDialogs;
        }

        public static void TryShowReloopDialog(int iteration, int maxIterations)
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (!settings.showReloopDialog)
            {
                Debug.Log($"[CaveBuild] Autonomous reloop {iteration}/{maxIterations} (dialog suppressed).");
                return;
            }

            if (ShouldLogInsteadOfModal())
            {
                Debug.Log($"[CaveBuild] Autonomous reloop {iteration}/{maxIterations}");
                return;
            }

            EditorUtility.DisplayDialog(
                "Autonomous Fix Loop",
                $"Continuing iteration {iteration} of {maxIterations}.\n\n" +
                "Cursor will run another fix pass. Watch Pipeline Console for status.",
                "OK");
        }

        public static void TryShowEndDialog(string sceneName, string message)
        {
            if (ShouldLogInsteadOfModal())
            {
                Debug.Log($"[CaveBuild] {sceneName} finished\n{message}");
                return;
            }

            EditorUtility.DisplayDialog(
                "Build Complete — Finished",
                message,
                "OK");
        }

        public static void EndUnifiedSessionWhenAgentIdle()
        {
            if (!CaveBuildCursorAgentBridge.IsAgentRunning)
            {
                EndUnifiedSession();
                return;
            }

            EditorApplication.update -= EndUnifiedSessionWhenAgentIdleUpdate;
            EditorApplication.update += EndUnifiedSessionWhenAgentIdleUpdate;
        }

        static void EndUnifiedSessionWhenAgentIdleUpdate()
        {
            if (CaveBuildCursorAgentBridge.IsAgentRunning)
                return;

            EditorApplication.update -= EndUnifiedSessionWhenAgentIdleUpdate;
            EndUnifiedSession();
        }
    }
}
#endif
