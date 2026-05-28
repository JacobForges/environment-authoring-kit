#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Queues optional path imports after metadata writes. Does not import Generated JSON/markdown.
    /// Pipeline steps call <see cref="EnvironmentKitScopedAssetRefresh.ImportForTaskNow"/> directly.
    /// </summary>
    static class CaveBuildDeferredAssetRefresh
    {
        const double DebounceSeconds = 1.75;

        static bool _pendingPaths;
        static bool _updateHooked;
        static double _refreshAfter;
        static readonly HashSet<string> PendingPaths = new();

        /// <summary>Metadata / JSON / MD write — no import (replaces old full-folder refresh).</summary>
        public static void RequestRefresh() => RequestRefresh(CaveBuildAssetImportTask.None);

        public static void RequestRefresh(CaveBuildAssetImportTask task)
        {
            if (task != CaveBuildAssetImportTask.None)
                EnvironmentKitScopedAssetRefresh.ImportForTaskNow(task);
        }

        public static void RequestImportPaths(params string[] assetPaths)
        {
            if (assetPaths == null)
                return;

            foreach (var path in assetPaths)
            {
                if (!EnvironmentKitScopedAssetRefresh.IsUnityAssetPath(path))
                    continue;
                PendingPaths.Add(path);
            }

            if (PendingPaths.Count == 0)
                return;

            _pendingPaths = true;
            ScheduleDebouncedRefresh();
        }

        public static void Flush()
        {
            if (!_pendingPaths && PendingPaths.Count == 0)
                return;
            ScheduleDebouncedRefresh();
        }

        static void ScheduleDebouncedRefresh()
        {
            _refreshAfter = EditorApplication.timeSinceStartup + DebounceSeconds;
            if (_updateHooked)
                return;
            _updateHooked = true;
            EditorApplication.update += DebouncedRefreshUpdate;
        }

        static void DebouncedRefreshUpdate()
        {
            if (EditorApplication.timeSinceStartup < _refreshAfter)
                return;

            if (CaveBuildActionPacing.IsBusy)
            {
                _refreshAfter = EditorApplication.timeSinceStartup + DebounceSeconds;
                return;
            }

            EditorApplication.update -= DebouncedRefreshUpdate;
            _updateHooked = false;

            if (!_pendingPaths || PendingPaths.Count == 0)
                return;

            _pendingPaths = false;
            var paths = PendingPaths.ToArray();
            PendingPaths.Clear();
            EnvironmentKitScopedAssetRefresh.ImportAssetPathsNow(paths);
        }
    }
}
#endif
