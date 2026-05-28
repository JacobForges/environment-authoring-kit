#if UNITY_EDITOR
using EnvironmentAuthoringKit;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Scene-view banner, camera framing, and selection ping so builds feel live (not "frozen").
    /// </summary>
    [InitializeOnLoad]
    static class CaveBuildLiveSceneFeedback
    {
        const double BannerSeconds = 12.0;
        const int PingEveryNthPlacement = 5;
        const double MinRepaintIntervalSeconds = 0.12;
        const double MinRepaintIntervalIdleSeconds = 0.6;
        const double SettingsRefreshSeconds = 2.0;

        static string _banner = string.Empty;
        static string _subBanner = string.Empty;
        static double _bannerUntil;
        static bool _sessionActive;
        static int _placementSerial;
        static double _lastRepaintAt;
        static bool _enabledCached = true;
        static bool _allowCameraHijackCached = true;
        static double _nextSettingsRefreshAt;

        static CaveBuildLiveSceneFeedback()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        public static bool Enabled
        {
            get
            {
                RefreshSettingsCacheIfNeeded();
                return _enabledCached;
            }
        }

        public static bool SessionActive => _sessionActive && Enabled;

        public static void BeginBuildSession()
        {
            _sessionActive = true;
            _placementSerial = 0;
            NotifyStep("Cave build started — watch Scene view for live placement", null, frameScene: false);
        }

        public static void EndBuildSession()
        {
            if (_sessionActive)
                NotifyStep("Cave build session finished", null, frameScene: false);
            _sessionActive = false;
            _banner = string.Empty;
            RepaintViews();
        }

        public static void NotifyStep(string label, Transform focus = null, bool frameScene = true)
        {
            if (!Enabled)
                return;

            if (EnvironmentKitHardwareBudget.Active.DisableLiveSceneFraming || !AllowCameraHijack())
                frameScene = false;

            _banner = label ?? string.Empty;
            _subBanner = CaveBuildPipelineScope.CaveOnlyContinuation
                ? "[Cave] queued pipeline — underground only (surface frozen)"
                : LavaTubeCaveBuildPipeline.IsPhasedBuildActive
                    ? "[Cave] queued pipeline — cave geometry step by step"
                    : "Building…";
            _bannerUntil = EditorApplication.timeSinceStartup + BannerSeconds;

            if (frameScene)
            {
                if (focus != null)
                    FrameTransform(focus);
                else
                    TryFrameBuildArea();
            }

            FlushWorldView();
            if (!label.StartsWith("[", System.StringComparison.Ordinal))
                label = CaveBuildPipelineDomains.CaveLive + " " + label;
            CaveBuildEditorLog.LogLiveStep(label);
        }

        public static void NotifyPlaced(GameObject instance, string kind)
        {
            if (!SessionActive || instance == null)
                return;

            _placementSerial++;
            var showPing = _placementSerial <= 12 || _placementSerial % PingEveryNthPlacement == 0;

            _banner = $"Placed {kind}";
            _subBanner = instance.name;
            _bannerUntil = EditorApplication.timeSinceStartup + 3.0;
            RepaintViews();
        }

        public static void NotifySurfacePhase(string phaseLabel)
        {
            if (!SessionActive)
                return;
            NotifyStep(phaseLabel, null, frameScene: false);
        }

        static void TryFrameBuildArea()
        {
            var env = Object.FindAnyObjectByType<EnvironmentRoot>();
            if (env == null)
                return;

            var cave = env.transform.Find(CaveGeometryPaths.CaveSystemRootName);
            if (cave == null)
                cave = env.transform.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
            var surface = env.transform.Find(SurfaceWorldPaths.RootName);

            if (cave != null)
                FrameTransform(cave);
            else if (surface != null)
                FrameTransform(surface);
            else
                FrameTransform(env.transform);
        }

        static void FrameTransform(Transform t)
        {
            if (t == null)
                return;

            var sv = SceneView.lastActiveSceneView;
            if (sv == null)
                return;

            var bounds = new Bounds(t.position, Vector3.one * 8f);
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null)
                    continue;
                bounds.Encapsulate(r.bounds);
            }

            sv.Frame(bounds, false);
            sv.Repaint();
        }

        static void RepaintViews()
        {
            var now = EditorApplication.timeSinceStartup;
            var minInterval = _sessionActive ? MinRepaintIntervalSeconds : MinRepaintIntervalIdleSeconds;
            if (now - _lastRepaintAt < minInterval)
                return;
            _lastRepaintAt = now;

            SceneView.RepaintAll();
        }

        static bool AllowCameraHijack()
        {
            RefreshSettingsCacheIfNeeded();
            return _allowCameraHijackCached;
        }

        static void RefreshSettingsCacheIfNeeded()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now < _nextSettingsRefreshAt)
                return;
            _nextSettingsRefreshAt = now + SettingsRefreshSeconds;

            var s = CaveBuildCursorSettings.LoadOrCreate();
            s.LoadFromPrefs();
            _enabledCached = s.showLiveScenePlacement;
            _allowCameraHijackCached = !s.stabilizationMode;
        }

        static void OnSceneGui(SceneView view)
        {
            if (!Enabled || string.IsNullOrEmpty(_banner))
                return;
            if (EditorApplication.timeSinceStartup > _bannerUntil)
                return;

            Handles.BeginGUI();
            var width = Mathf.Min(520f, view.position.width - 24f);
            var rect = new Rect(12f, 12f, width, 54f);
            GUI.Box(rect, GUIContent.none);
            var style = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, wordWrap = true };
            GUI.Label(
                new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 22f),
                "● LIVE BUILD",
                style);
            GUI.Label(
                new Rect(rect.x + 10f, rect.y + 26f, rect.width - 20f, 36f),
                _banner + (string.IsNullOrEmpty(_subBanner) ? string.Empty : "\n" + _subBanner),
                EditorStyles.wordWrappedLabel);
            Handles.EndGUI();
        }
    }
}
#endif
