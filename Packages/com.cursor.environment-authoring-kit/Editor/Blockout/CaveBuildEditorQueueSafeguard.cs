#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Prevents editor queue floods (e.g. hundreds of duplicate radial trail bench jobs) during long builds.
    /// </summary>
    static class CaveBuildEditorQueueSafeguard
    {
        public const int MaxPendingPerLabel = 12;
        public const int StallQueueDepth = 384;
        public const double StallSeconds = 420.0;

        static readonly Dictionary<string, int> PendingByLabel = new();
        static string _lastProgressLabel = string.Empty;
        static double _lastProgressAt;
        static double _stallWarnedAt;

        public static void ResetForNewBuildSession()
        {
            PendingByLabel.Clear();
            _lastProgressLabel = string.Empty;
            _lastProgressAt = EditorApplication.timeSinceStartup;
            _stallWarnedAt = 0;
        }

        public static void NotifyQueueProgress(string label)
        {
            if (string.IsNullOrEmpty(label))
                return;

            if (!string.Equals(_lastProgressLabel, label, System.StringComparison.Ordinal))
            {
                _lastProgressLabel = label;
                _lastProgressAt = EditorApplication.timeSinceStartup;
            }
        }

        /// <summary>Returns false when the enqueue should be dropped to protect the editor.</summary>
        public static bool TryAcceptEnqueue(string label, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(label))
                return true;

            if (!CaveBuildEditorResponsiveness.IsLongBuildActive)
                return true;

            PendingByLabel.TryGetValue(label, out var pending);
            if (pending >= MaxPendingPerLabel)
            {
                reason =
                    $"queue safeguard — dropped duplicate '{label}' (pending={pending}, max={MaxPendingPerLabel})";
                return false;
            }

            var depth = CaveBuildActionPacing.QueuedCount;
            var now = EditorApplication.timeSinceStartup;
            if (depth >= StallQueueDepth &&
                now - _lastProgressAt >= StallSeconds &&
                now - _stallWarnedAt > 30.0)
            {
                _stallWarnedAt = now;
                CaveBuildPipelineDomains.LogCaveWarning(
                    $"[Queue] Safeguard: depth={depth} unchanged ~{(now - _lastProgressAt):F0}s " +
                    $"(last='{_lastProgressLabel}'). Consider canceling build or Full AAA Rebuild (non-additive).");
            }

            return true;
        }

        public static void OnEnqueued(string label)
        {
            if (string.IsNullOrEmpty(label))
                return;

            PendingByLabel.TryGetValue(label, out var n);
            PendingByLabel[label] = n + 1;
        }

        public static void OnDequeued(string label)
        {
            if (string.IsNullOrEmpty(label))
                return;

            if (!PendingByLabel.TryGetValue(label, out var n))
                return;

            n--;
            if (n <= 0)
                PendingByLabel.Remove(label);
            else
                PendingByLabel[label] = n;
        }
    }
}
#endif
