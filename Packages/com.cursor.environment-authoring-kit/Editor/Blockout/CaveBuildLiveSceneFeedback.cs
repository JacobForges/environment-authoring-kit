#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Scene-view banner, selection ping, and camera beat dispatch for live builds.
    /// Camera moves are owned by <see cref="CaveBuildSceneCameraDirector"/>.
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
        static double _nextSettingsRefreshAt;
        static int _demoRecordingDepth;

        static CaveBuildLiveSceneFeedback()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        public static bool Enabled
        {
            get
            {
                RefreshSettingsCacheIfNeeded();
                return _enabledCached || _sessionActive || _demoRecordingDepth > 0;
            }
        }

        public static bool SessionActive => (_sessionActive || _demoRecordingDepth > 0) && Enabled;

        public static void PushDemoRecordingSession()
        {
            _demoRecordingDepth++;
            CaveBuildSceneCameraDirector.PushDemoRecordingSession();
        }

        public static void PopDemoRecordingSession()
        {
            _demoRecordingDepth = Mathf.Max(0, _demoRecordingDepth - 1);
            CaveBuildSceneCameraDirector.PopDemoRecordingSession();
        }

        public static void FlushWorldView(Terrain terrain = null, bool forceRepaint = false) =>
            CaveBuildLiveSceneFlushUtility.FlushWorldView(terrain, forceRepaint);

        public static void BeginBuildSession()
        {
            _sessionActive = true;
            _placementSerial = 0;
            CaveBuildSceneCameraDirector.BeginSession();
            NotifyStep("Cave build started — watch Scene view for live placement", null, frameScene: true);
        }

        public static void EndBuildSession()
        {
            if (_sessionActive)
                NotifyStep("Cave build session finished", null, frameScene: false);
            _sessionActive = false;
            _banner = string.Empty;
            CaveBuildSceneCameraDirector.EndSession();
            RepaintViews();
        }

        public static void NotifyStep(string label, Transform focus = null, bool frameScene = true)
        {
            if (!Enabled)
                return;

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
                    CaveBuildSceneCameraDirector.RequestPipelineStep(focus);
                else if (label != null &&
                         (label.IndexOf("phase", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                          label.IndexOf("terraform", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                          label.IndexOf("surface", System.StringComparison.OrdinalIgnoreCase) >= 0))
                    CaveBuildSceneCameraDirector.RequestPhaseChange(label);
                else
                    CaveBuildSceneCameraDirector.RequestBuildArea();
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
            FlushWorldView();
            CaveBuildSceneCameraDirector.RequestPropPlacement(instance.transform);
            if (showPing)
                EditorGUIUtility.PingObject(instance);
        }

        public static void NotifySurfacePhase(string phaseLabel, Terrain terrain = null)
        {
            if (!SessionActive)
                return;

            if (terrain != null)
            {
                NotifyTerrainTile(terrain, phaseLabel, ping: false);
            }
            else
            {
                _banner = phaseLabel ?? string.Empty;
                _subBanner = "Surface phase";
                _bannerUntil = EditorApplication.timeSinceStartup + BannerSeconds;
                CaveBuildSceneCameraDirector.RequestPhaseChange(phaseLabel);
            }

            FlushWorldView(terrain);
        }

        public static void NotifyTerrainTile(Terrain terrain, string label, bool ping = true, bool forceCamera = false)
        {
            if (!SessionActive || terrain == null)
                return;

            _placementSerial++;
            _banner = label ?? terrain.name;
            _subBanner = BuildTerrainSubBanner(terrain, label);
            _bannerUntil = EditorApplication.timeSinceStartup + 4.0;

            SurfaceTerrainTileExpansion.SetLiveTerrainFocus(terrain);
            var sculptBeat = forceCamera ||
                             !string.IsNullOrEmpty(label) &&
                             (label.IndexOf("sculpt", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                              label.IndexOf("terraform", System.StringComparison.OrdinalIgnoreCase) >= 0);
            CaveBuildSceneCameraDirector.RequestTerrainTile(terrain, force: sculptBeat);
            FlushWorldView(terrain, forceRepaint: sculptBeat);
            if (ping && (_placementSerial <= 16 || _placementSerial % PingEveryNthPlacement == 0))
                EditorGUIUtility.PingObject(terrain);
        }

        static string BuildTerrainSubBanner(Terrain terrain, string label)
        {
            var name = terrain.name ?? string.Empty;
            var sculpting = !string.IsNullOrEmpty(label) &&
                            label.IndexOf("sculpt", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (sculpting && name.StartsWith(
                    SurfaceTerrainTileExpansion.MountainPeakTileNamePrefix,
                    System.StringComparison.Ordinal))
            {
                return name + "\nPeak rock/grass dressing — per-tile pass after sculpt; bulk mountain_cliffs catches remainder";
            }

            if (sculpting && name.StartsWith(
                    SurfaceTerrainTileExpansion.MountainFoothillTileNamePrefix,
                    System.StringComparison.Ordinal))
            {
                return name + "\nFoothill props after mountain_cliffs phase";
            }

            return name;
        }

        static void RepaintViews()
        {
            var now = EditorApplication.timeSinceStartup;
            var minInterval = _sessionActive || CaveBuildDemoAutoRecorder.KeepSceneViewLiveForRecording
                ? MinRepaintIntervalSeconds
                : MinRepaintIntervalIdleSeconds;
            if (now - _lastRepaintAt < minInterval)
                return;
            _lastRepaintAt = now;

            SceneView.RepaintAll();
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
