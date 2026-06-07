#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Waits for startup + phased pipeline + pacing to finish before batchmode exit.</summary>
    public static class CaveBuildPipelineWaiter
    {
        const double DefaultTimeoutSeconds = 14_400; // 4 hours
        const int IdleFramesRequired = 45;

        static bool _active;
        static double _startedUtc;
        static double _timeoutSeconds;
        static int _idleFrames;
        static int _exitSuccess;
        static int _exitFailure;
        static Action _onComplete;

        public static bool IsWaiting => _active;

        public static void Begin(
            int exitCodeOnSuccess = 0,
            int exitCodeOnFailure = 1,
            double timeoutSeconds = DefaultTimeoutSeconds,
            Action onComplete = null)
        {
            if (_active)
                return;

            _active = true;
            _startedUtc = EditorApplication.timeSinceStartup;
            _timeoutSeconds = timeoutSeconds;
            _idleFrames = 0;
            _exitSuccess = exitCodeOnSuccess;
            _exitFailure = exitCodeOnFailure;
            _onComplete = onComplete;
            EditorApplication.update += Tick;
            Debug.Log(
                "[CaveBuild] Pipeline waiter armed — batchmode will exit when startup + queued pipeline are idle.");
        }

        static void Tick()
        {
            if (!_active)
                return;

            var elapsed = EditorApplication.timeSinceStartup - _startedUtc;
            if (elapsed > _timeoutSeconds)
            {
                Fail($"Pipeline waiter timeout after {elapsed:F0}s.");
                return;
            }

            if (IsPipelineBusy())
            {
                _idleFrames = 0;
                return;
            }

            _idleFrames++;
            if (_idleFrames < IdleFramesRequired)
                return;

            Complete();
        }

        static bool IsPipelineBusy() =>
            CaveBuildStartupCoordinator.IsActive ||
            LavaTubeCaveBuilder.IsBuildInProgress ||
            LavaTubeCaveBuildPipeline.IsPhasedBuildActive ||
            CaveBuildActionPacing.IsBusy ||
            CaveBuildBatchRunner.IsActive;

        static void Complete()
        {
            EditorApplication.update -= Tick;
            _active = false;
            try
            {
                _onComplete?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError("[CaveBuild] Pipeline waiter onComplete: " + ex.Message);
            }

            if (Application.isBatchMode)
            {
                Debug.Log("[CaveBuild] Pipeline waiter — idle, exiting editor.");
                EditorApplication.Exit(_exitSuccess);
            }
        }

        static void Fail(string message)
        {
            Debug.LogError("[CaveBuild] " + message);
            EditorApplication.update -= Tick;
            _active = false;
            if (Application.isBatchMode)
                EditorApplication.Exit(_exitFailure);
        }
    }
}
#endif
