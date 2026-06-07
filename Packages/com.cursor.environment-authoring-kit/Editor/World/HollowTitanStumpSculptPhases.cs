#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// 32-phase paced meat builder for Hollow Titan exterior stump sculpt only
    /// (bowl, rim, crown, titan-level detail, nearby foothill curves).
    /// </summary>
    public static class HollowTitanStumpSculptPhases
    {
        public const int PhaseCount = 32;

        public static readonly string[] PhaseLabels =
        {
            "Hollow Titan stump — scale verify",
            "Hollow Titan stump — inner bowl NE",
            "Hollow Titan stump — inner bowl SE",
            "Hollow Titan stump — inner bowl SW",
            "Hollow Titan stump — inner bowl NW",
            "Hollow Titan stump — rim ring NE",
            "Hollow Titan stump — rim ring SE",
            "Hollow Titan stump — rim ring SW",
            "Hollow Titan stump — rim ring NW",
            "Hollow Titan stump — crown cluster A",
            "Hollow Titan stump — crown cluster B",
            "Hollow Titan stump — crown cluster C",
            "Hollow Titan stump — crown cluster D",
            "Hollow Titan stump — entrance gap",
            "Hollow Titan stump — outer blend east",
            "Hollow Titan stump — outer blend west",
            "Hollow Titan stump — bark detail NE",
            "Hollow Titan stump — bark detail SE",
            "Hollow Titan stump — bark detail SW",
            "Hollow Titan stump — bark detail NW",
            "Hollow Titan stump — titan ring NE",
            "Hollow Titan stump — titan ring SE",
            "Hollow Titan stump — titan ring SW",
            "Hollow Titan stump — titan ring NW",
            "Hollow Titan stump — foothill curve north",
            "Hollow Titan stump — foothill curve east",
            "Hollow Titan stump — foothill curve south",
            "Hollow Titan stump — foothill curve west",
            "Hollow Titan stump — peak approach soften",
            "Hollow Titan stump — crown roundness",
            "Hollow Titan stump — resnap + silhouette",
            "Hollow Titan stump — ladder grade",
        };

        sealed class SculptSession
        {
            public Terrain MainTerrain;
            public WorldGenerationRequest Request;
            public int PhaseIndex;
            public Action OnComplete;
            public Transform Root;
            public HollowTitanStumpSculptLadderReport LadderReport = new();
            public bool ForceSync;
        }

        static SculptSession _active;

        public static bool IsRunning => _active != null;

        public static void Begin(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            Action onComplete = null,
            bool forceSyncFullBuild = false)
        {
            if (mainTerrain == null || request == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (IsRunning)
            {
                CaveBuildEditorLog.LogSurfaceWarning(
                    "[HollowTitan] 32-phase stump sculpt already running — ignored duplicate.",
                    forceUnityConsole: true);
                return;
            }

            var rootGo = GameObject.Find(HollowTitanLandmarkAuthor.RootName);
            if (rootGo == null)
            {
                CaveBuildEditorLog.LogSurfaceWarning(
                    "[HollowTitan] Stump sculpt skipped — no landmark root (run meat build first).",
                    forceUnityConsole: true);
                onComplete?.Invoke();
                return;
            }

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            var queueBetweenPhases = !forceSyncFullBuild &&
                                     settings.hollowTitanPhasedBuild &&
                                     CaveBuildLiveSceneFeedback.SessionActive;

            _active = new SculptSession
            {
                MainTerrain = mainTerrain,
                Request = request,
                PhaseIndex = 0,
                OnComplete = onComplete,
                Root = rootGo.transform,
                ForceSync = forceSyncFullBuild,
            };

            CaveBuildEditorLog.LogSurface(
                queueBetweenPhases
                    ? "[HollowTitan] Starting 32-phase stump sculpt (live session, paced, preview)."
                    : "[HollowTitan] Starting 32-phase stump sculpt (sync).",
                forceUnityConsole: true);

            if (!queueBetweenPhases || forceSyncFullBuild)
            {
                RunAllSync(_active);
                Finish(_active);
                return;
            }

            ScheduleNext(_active);
        }

        [MenuItem("Window/Environment Kit/World/Build Hollow Titan Stump (32 phases)")]
        public static void BuildFromMenu()
        {
            var terrain = UnityEngine.Object.FindAnyObjectByType<Terrain>();
            var request = WorldGenerationRequest.LoadOrDefault();
            request.EnsureFullWorldSurfaceContract();
            Begin(terrain, request, forceSyncFullBuild: false);
        }

        static void ScheduleNext(SculptSession session) =>
            CaveBuildActionPacing.ScheduleLight(
                () => RunQueuedStep(session),
                CaveBuildPipelineDomains.QueueLabel($"Hollow Titan stump {session.PhaseIndex + 1}/{PhaseCount}"));

        static void RunQueuedStep(SculptSession session)
        {
            if (session == null)
                return;

            var index = session.PhaseIndex;
            RunPhase(session, index);
            NotifyPhase(index, session.Root);
            GradePhase(session, index);

            if (index >= PhaseCount - 1)
            {
                Finish(session);
                return;
            }

            session.PhaseIndex = index + 1;
            ScheduleNext(session);
        }

        static void RunAllSync(SculptSession session)
        {
            for (var i = 0; i < PhaseCount; i++)
            {
                RunPhase(session, i);
                NotifyPhase(i, session.Root);
                GradePhase(session, i);
                CaveBuildActionPacing.TouchQueueActivity();
            }
        }

        static void RunPhase(SculptSession session, int phaseIndex)
        {
            if (session?.MainTerrain == null)
                return;

            if (phaseIndex == PhaseCount - 1)
            {
                HollowTitanStumpSculptLadder.FinalizeReport(session.LadderReport);
                return;
            }

            var touched = HollowTitanExteriorTerraform.SculptStumpPhasePass(
                session.MainTerrain,
                phaseIndex,
                force: true);

            CaveBuildEditorLog.LogSurface(
                $"[HollowTitan] Stump phase {phaseIndex + 1}/{PhaseCount} — {PhaseLabels[phaseIndex]} ({touched} cells).",
                forceUnityConsole: false);
        }

        static void NotifyPhase(int phaseIndex, Transform focus)
        {
            if (phaseIndex < 0 || phaseIndex >= PhaseLabels.Length)
                return;

            var label = PhaseLabels[phaseIndex];
            CaveBuildEditorLog.LogSurface($"[HollowTitan] {label}", forceUnityConsole: false);
            CaveBuildLiveSceneFeedback.NotifySurfacePhase(label);
            if (focus != null)
                CaveBuildLiveSceneFeedback.NotifyStep(label, focus, frameScene: true);
        }

        static void GradePhase(SculptSession session, int phaseIndex)
        {
            if (session?.Root == null || phaseIndex <= 0 || phaseIndex >= PhaseCount - 1)
                return;

            session.LadderReport.Seed = session.Request?.Seed ?? 0;
            HollowTitanStumpSculptLadder.GradeAndLogPhase(
                phaseIndex,
                session.Root,
                session.MainTerrain,
                session.LadderReport);
        }

        static void Finish(SculptSession session)
        {
            var cb = session?.OnComplete;
            if (session?.LadderReport != null)
            {
                session.LadderReport.Seed = session.Request?.Seed ?? 0;
                HollowTitanStumpSculptLadder.FinalizeReport(session.LadderReport);
                CaveBuildEditorLog.LogSurface(
                    $"[HollowTitan] Stump ladder — {session.LadderReport.OverallScore}/100 " +
                    $"(acceptable={session.LadderReport.BuildAcceptable}). " +
                    $"→ {HollowTitanStumpSculptLadder.ReportPath}",
                    forceUnityConsole: true);
            }

            _active = null;
            CaveBuildEditorLog.LogSurface(
                "[HollowTitan] 32-phase stump sculpt complete.",
                forceUnityConsole: true);
            cb?.Invoke();
        }
    }
}
#endif
