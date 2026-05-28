#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Terrain Cursor is optional grading — must not block cave geometry after pre-build Cursor.
    /// </summary>
    static class CaveBuildTerrainCursorDeferred
    {
        static SurfaceTerrainLadderReport _pendingReport;
        static SceneGroundInfo _pendingGround;
        static WorldGenerationRequest _pendingRequest;

        public static bool HasPending => _pendingReport != null && _pendingGround != null;

        public static void MarkAfterSurface(
            SurfaceTerrainLadderReport report,
            SceneGroundInfo ground,
            WorldGenerationRequest request)
        {
            if (report == null || ground == null || report.BuildAcceptable)
            {
                Clear();
                return;
            }

            _pendingReport = report;
            _pendingGround = ground;
            _pendingRequest = request;
            Debug.Log(
                "[CaveBuild] Terrain below target — Cursor terrain workflow deferred until cave build finishes " +
                "(avoids blocking pre-build / queued geometry).");
        }

        public static void Clear()
        {
            _pendingReport = null;
            _pendingGround = null;
            _pendingRequest = null;
        }

        public static void TryInvokeAfterCavePipeline()
        {
            if (!HasPending)
                return;

            if (_pendingRequest?.SurfaceScope == SurfaceBuildScope.FullWorld &&
                CaveBuildSurfaceCompletionGate.IsTerrainGradingComplete)
            {
                Clear();
                Debug.Log(
                    "[CaveBuild] Skipping deferred terrain Cursor — FullWorld surface/terrain already graded in startup.");
                return;
            }

            var report = _pendingReport;
            var ground = _pendingGround;
            var request = _pendingRequest;
            Clear();

            report = SurfaceTerrainQualityGrader.Run(
                ground,
                request,
                SurfaceTerrainQualityGrader.ResolveSurfaceRoot());
            if (report.BuildAcceptable)
            {
                Debug.Log(
                    $"[CaveBuild] Terrain now acceptable ({report.LetterGrade} {report.OverallScore}) — skipping deferred Cursor.");
                return;
            }

            CaveBuildSurfacePipeline.InvokeTerrainCursorWorkflow(report, ground);
        }
    }
}
#endif
