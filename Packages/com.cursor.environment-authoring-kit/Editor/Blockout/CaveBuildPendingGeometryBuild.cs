using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.XR;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Deferred cave geometry after Cursor pre-build workflow completes (mirrors post-build → auto-rebuild).</summary>
    public static class CaveBuildPendingGeometryBuild
    {
        public enum ContinuationKind
        {
            BuilderMenu,
            PipelineOnRoot,
        }

        public struct PendingState
        {
            public ContinuationKind Kind;
            public SceneGroundInfo Ground;
            public WorldGenerationRequest Request;
            public CaveLayoutRoll LayoutRoll;
            public bool HasLayoutRoll;
            public bool HideLegacyBlockout;
            public bool SkipDialogs;
            public Transform RootAnchor;
            public XROptimizationProfile XrProfile;
        }

        static PendingState? _pending;
        static bool _running;
        static int _surfaceRetryCount;

        public static bool HasPending => _pending.HasValue;

        public static void QueueFromBuilder(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            bool hideLegacyBlockout,
            bool skipDialogs,
            CaveLayoutRoll layoutRoll = null)
        {
            _pending = new PendingState
            {
                Kind = ContinuationKind.BuilderMenu,
                Ground = ground,
                Request = request,
                LayoutRoll = layoutRoll,
                HasLayoutRoll = layoutRoll != null,
                HideLegacyBlockout = hideLegacyBlockout,
                SkipDialogs = skipDialogs,
            };
        }

        public static void QueueFromWorldGenerator(
            Transform rootAnchor,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            XROptimizationProfile xrProfile,
            CaveLayoutRoll layoutRoll = null)
        {
            _pending = new PendingState
            {
                Kind = ContinuationKind.PipelineOnRoot,
                Ground = ground,
                Request = request,
                LayoutRoll = layoutRoll,
                HasLayoutRoll = layoutRoll != null,
                RootAnchor = rootAnchor,
                XrProfile = xrProfile,
                SkipDialogs = true,
            };
        }

        public static void Clear()
        {
            _pending = null;
            _surfaceRetryCount = 0;
        }

        public static bool TryRunPending(out string message)
        {
            message = null;
            if (_running)
            {
                message = "Pending cave build already running.";
                return false;
            }

            if (!_pending.HasValue)
            {
                message = "No pending cave build.";
                return false;
            }

            var p = _pending.Value;

            CaveBuildSurfaceCompletionGate.EnsureHandoffAfterSuccessfulSurfaceWorld(
                p.Request,
                p.Ground,
                CaveBuildSurfaceCompletionGate.IsSurfaceWorldGeneratorFinished ||
                CaveBuildSurfaceCompletionGate.IsCompleteForSeed(p.Request));

            if (p.Request != null &&
                CaveBuildSurfaceCompletionGate.MustFinishSurfaceBeforeCave(p.Request) &&
                !CaveBuildSurfaceCompletionGate.CanStartCaveGeometryNow(p.Request, p.Ground))
            {
                if (_surfaceRetryCount >= 2)
                {
                    UnityEngine.Debug.LogWarning(
                        "[CaveBuild] Surface gate still pending after retries — starting cave geometry anyway (usable terrain in scene).");
                    _surfaceRetryCount = 0;
                }
                else
                {
                    _surfaceRetryCount++;
                    CaveBuildSurfaceCompletionGate.LogHandoffBlocker(
                        p.Request, p.Ground, $"pending cave retry {_surfaceRetryCount}");
                    message =
                        "[Surface] Finishing above-ground terrain before cave geometry (pre-build handoff waiting).";
                    CaveBuildSurfacePipeline.QueueBeforeCaveBuild(
                        p.Ground,
                        p.Request,
                        report =>
                        {
                            CaveBuildSurfaceCompletionGate.EnsureHandoffAfterSuccessfulSurfaceWorld(
                                p.Request, p.Ground, report is { Success: true });

                            if (!CaveBuildSurfaceCompletionGate.CanStartCaveGeometryNow(p.Request, p.Ground) &&
                                (report == null || !report.Success))
                            {
                                Clear();
                                UnityEngine.Debug.LogWarning(
                                    "[CaveBuild] Cave continuation cancelled — surface did not complete.");
                                return;
                            }

                            _surfaceRetryCount = 0;
                            TryRunPending(out _);
                        });
                    return true;
                }
            }

            _surfaceRetryCount = 0;
            _running = true;
            _pending = null;

            switch (p.Kind)
            {
                case ContinuationKind.BuilderMenu:
                    CaveBuildActionPacing.ScheduleLight(
                        () =>
                        {
                            try
                            {
                                var roll = p.HasLayoutRoll ? p.LayoutRoll : null;
                                LavaTubeCaveBuilder.ContinueCaveGeometryAfterPreBuild(
                                    p.Ground,
                                    p.Request,
                                    p.HideLegacyBlockout,
                                    p.SkipDialogs,
                                    roll);
                            }
                            catch (System.Exception ex)
                            {
                                UnityEngine.Debug.LogException(ex);
                            }
                            finally
                            {
                                _running = false;
                            }
                        },
                        CaveBuildPipelineDomains.QueueLabel(
                            "hand off to cave continuation"));
                    message = p.Request?.SurfaceScope == SurfaceBuildScope.FullWorld
                        ? "[Cave] Queued FullWorld cave pipeline after pre-build (terrain should already be graded)."
                        : "[Cave] Queued cave geometry after pre-build (scope=" + p.Request?.SurfaceScope + ").";
                    CaveBuildEditorLog.LogCave(message, forceUnityConsole: true);
                    return true;

                case ContinuationKind.PipelineOnRoot:
                    if (p.RootAnchor == null || p.Ground == null || !p.Ground.HasAnchor)
                    {
                        message = "Pending world-gen cave build missing ground anchor.";
                        _running = false;
                        return false;
                    }

                    try
                    {
                        LavaTubeCaveBuildPipeline.Run(
                            p.RootAnchor,
                            p.Ground,
                            p.Request,
                            p.XrProfile,
                            showProgress: true);
                        message = "Continued Generate World cave pipeline after pre-build Cursor workflow.";
                        return true;
                    }
                    finally
                    {
                        _running = false;
                    }

                default:
                    message = "Unknown pending build kind.";
                    _running = false;
                    return false;
            }
        }
    }
}
