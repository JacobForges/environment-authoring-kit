#if UNITY_EDITOR
using EnvironmentAuthoringKit;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Scene-view camera crew for live builds: frames placed terrain work area + slow cinematic orbit.
    /// </summary>
    [InitializeOnLoad]
    static class CaveBuildSceneCameraDirector
    {
        public enum CameraBeat
        {
            SessionOpen,
            PhaseChange,
            TerrainGridWork,
            TerrainTilePlaced,
            PropPlaced,
            PipelineStep,
            BuildArea,
            DemoRecording,
        }

        enum ShotRole
        {
            EstablishingWide,
            MasterCoverage,
            MediumCoverage,
            DemoHero,
        }

        struct ShotProfile
        {
            public float PaddingFraction;
            public float DistanceScale;
            public float Pitch;
            public float FocusPivotBlend;
            public int ReframeEveryNthEvent;
            public double HoldSeconds;
            /// <summary>Seconds to hold still on the active subject before the next arc.</summary>
            public double OrbitDwellSec;
            /// <summary>Seconds for one slow eased orbit arc (documentary pan).</summary>
            public double OrbitArcSec;
            /// <summary>Yaw degrees traveled per arc segment.</summary>
            public float OrbitArcDegrees;
        }

        struct CameraRigState
        {
            public Vector3 Pivot;
            public float Distance;
            public float Yaw;
            public float Pitch;
            public float LookSize;
            public double LastReframeAt;
            public int EventSerial;
            public CameraBeat LastBeat;
        }

        struct CameraRigTargets
        {
            public Vector3 Pivot;
            public float Distance;
            public float Pitch;
            public float LookSize;
        }

        const float DefaultPitch = 48f;
        const float BasePaddingFraction = 0.28f;
        const float ZoomSliderMin = 1f;
        const float ZoomSliderStandardMax = 6f;
        const float ZoomSliderLegacyExtendedMax = 35f;
        const float ZoomSliderExtendedMax = 100f;
        const float ZoomCoverageAtStandardMax = 0.92f;
        const float ZoomCoverageAtExtendedMax = 8.5f;
        const float ZoomCoverageAtAbsoluteMax = 24.3f;
        const double UserOverrideHoldSeconds = 20.0;
        const float PivotSmoothTimeMin = 0.28f;
        const float PivotSmoothTimeMax = 0.58f;
        const float DistanceSmoothTime = 0.36f;
        const float PitchSmoothTime = 0.44f;
        const float YawSmoothTime = 0.5f;
        const float LookSizeSmoothTime = 0.38f;
        const float PivotMaxSpeed = 1400f;
        const float YawMaxSpeed = 95f;

        static CameraRigState _rig;
        static CameraRigTargets _targets;
        static Vector3 _pivotVelocity;
        static float _distanceVelocity;
        static float _pitchVelocity;
        static float _yawVelocity;
        static float _lookSizeVelocity;
        static double _lastSmoothUpdateAt;
        static bool _forceInstantSnapOnce;
        static double _orbitDwellUntil;
        static double _orbitArcStart;
        static double _orbitArcEnd;
        static float _orbitYawFrom;
        static float _orbitYawTo;
        static float _orbitRestYaw = 36f;
        static int _orbitArcSign = 1;
        static bool _orbitArcActive;
        static bool _sessionActive;
        static int _demoRecordingDepth;
        static bool _allowHijack = true;
        static bool _cinematicEnabled = true;
        static float _zoomOutMultiplier = 3f;
        static float _appliedZoomOutMultiplier = 3f;
        static double _nextSettingsRefreshAt;
        static double _userCameraOverrideUntil;
        static double _lastEventPulseAt;
        static CameraBeat _pendingBeat = CameraBeat.BuildArea;

        static CaveBuildSceneCameraDirector()
        {
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.update += OnEditorUpdate;
        }

        public static bool SessionActive => _sessionActive || _demoRecordingDepth > 0;

        public static void PushDemoRecordingSession() => _demoRecordingDepth++;

        public static void PopDemoRecordingSession() =>
            _demoRecordingDepth = Mathf.Max(0, _demoRecordingDepth - 1);

        public static void BeginSession()
        {
            _sessionActive = true;
            _rig = default;
            _targets = default;
            _rig.Pitch = DefaultPitch;
            _rig.Yaw = 38f;
            _rig.LookSize = 48f;
            _pivotVelocity = Vector3.zero;
            _distanceVelocity = 0f;
            _pitchVelocity = 0f;
            _yawVelocity = 0f;
            _lookSizeVelocity = 0f;
            _lastSmoothUpdateAt = EditorApplication.timeSinceStartup;
            _forceInstantSnapOnce = true;
            _orbitRestYaw = 38f;
            _orbitArcActive = false;
            _orbitDwellUntil = 0;
            _userCameraOverrideUntil = 0;
            _lastEventPulseAt = 0;
            _pendingBeat = CameraBeat.SessionOpen;
            PulseLiveView(force: true);
        }

        public static void EndSession()
        {
            _sessionActive = false;
            _rig = default;
            _targets = default;
            _pivotVelocity = Vector3.zero;
            _distanceVelocity = 0f;
            _pitchVelocity = 0f;
            _yawVelocity = 0f;
            _lookSizeVelocity = 0f;
            _userCameraOverrideUntil = 0;
            SurfaceTerrainTileExpansion.ClearLiveTerrainFocus();
        }

        public static void RequestPhaseChange(string label, Transform focus = null) =>
            TryRequest(CameraBeat.PhaseChange, force: true);

        public static void RequestTerrainTile(Terrain terrain, bool force = false)
        {
            if (terrain == null)
                return;

            SurfaceTerrainTileExpansion.SetLiveTerrainFocus(terrain);
            _rig.EventSerial++;
            if (_demoRecordingDepth > 0)
                force = true;

            var profile = ShotProfileFor(
                _demoRecordingDepth > 0 ? ShotRole.DemoHero : ShotRoleFor(CameraBeat.TerrainTilePlaced));
            if (!force && profile.ReframeEveryNthEvent > 0 && _rig.EventSerial % profile.ReframeEveryNthEvent != 0)
                return;

            TryRequest(CameraBeat.TerrainTilePlaced, force: force);
        }

        public static void RequestPropPlacement(Transform instance) =>
            TryRequest(CameraBeat.PropPlaced, force: false);

        public static void RequestPipelineStep(Transform focus) =>
            TryRequest(CameraBeat.PipelineStep, force: true);

        public static void RequestBuildArea() =>
            TryRequest(CameraBeat.BuildArea, force: true);

        /// <summary>Hub zoom slider — apply immediately; do not wait for EditorPrefs poll.</summary>
        public static void NotifyLiveZoomChanged(float sliderValue)
        {
            var clamped = Mathf.Clamp(sliderValue, ZoomSliderMin, ZoomSliderExtendedMax);
            if (Mathf.Approximately(clamped, _zoomOutMultiplier))
                return;

            _zoomOutMultiplier = clamped;
            EditorPrefs.SetFloat("CaveBuild_LiveSceneCameraZoomOut", clamped);
            _forceInstantSnapOnce = true;
            if (CanOperate())
                PulseLiveView(force: true);
        }

        /// <summary>Force one accurate cinematic frame before Scene-view PNG capture (demo timelapse).</summary>
        public static void ApplyDemoCaptureFrame()
        {
            if (!CanOperate())
                return;

            if (!TryResolveActiveBuildBounds(
                    ShotProfileFor(ShotRole.DemoHero),
                    out var bounds,
                    out var pivot,
                    allowWidePlacedBounds: !ShouldFrameLiveWorkTile()))
                return;

            var sv = SceneView.lastActiveSceneView ?? ResolveAnySceneView();
            if (sv == null)
                return;

            ApplyCinematicWideShot(
                sv,
                bounds,
                pivot,
                ShotProfileFor(ShotRole.DemoHero),
                snapFraming: false,
                resetOrbit: false);
            sv.Repaint();
        }

        static int _lastSmoothFrame = -1;
        static float _cachedDeltaTime = 0.016f;

        static float SampleDeltaTime()
        {
            var frame = Time.frameCount;
            if (frame == _lastSmoothFrame)
                return _cachedDeltaTime;

            _lastSmoothFrame = frame;
            var now = EditorApplication.timeSinceStartup;
            var dt = (float)(now - _lastSmoothUpdateAt);
            _lastSmoothUpdateAt = now;
            _cachedDeltaTime = Mathf.Clamp(dt, 0.008f, 0.05f);
            return _cachedDeltaTime;
        }

        static float ResolvePivotSmoothTime()
        {
            var tileSpan = ResolveTypicalTileSpan();
            var delta = Vector3.Distance(_rig.Pivot, _targets.Pivot);
            var t = Mathf.Clamp01(delta / Mathf.Max(tileSpan * 0.85f, 128f));
            return Mathf.Lerp(PivotSmoothTimeMin, PivotSmoothTimeMax, t);
        }

        static void AdvanceRigSmoothing(float dt, ShotProfile profile, double now, bool snapInstant)
        {
            if (snapInstant || _rig.Distance <= 0.01f)
            {
                _rig.Pivot = _targets.Pivot;
                _rig.Distance = _targets.Distance;
                _rig.Pitch = _targets.Pitch;
                _rig.LookSize = _targets.LookSize;
                _rig.Yaw = SampleProfessionalOrbitYaw(now, profile);
                _pivotVelocity = Vector3.zero;
                _distanceVelocity = 0f;
                _pitchVelocity = 0f;
                _yawVelocity = 0f;
                _lookSizeVelocity = 0f;
                _forceInstantSnapOnce = false;
                return;
            }

            var pivotSmooth = ResolvePivotSmoothTime();
            _rig.Pivot = Vector3.SmoothDamp(
                _rig.Pivot,
                _targets.Pivot,
                ref _pivotVelocity,
                pivotSmooth,
                PivotMaxSpeed,
                dt);
            _rig.Distance = Mathf.SmoothDamp(
                _rig.Distance,
                _targets.Distance,
                ref _distanceVelocity,
                DistanceSmoothTime,
                Mathf.Infinity,
                dt);
            _rig.Pitch = Mathf.SmoothDamp(
                _rig.Pitch,
                _targets.Pitch,
                ref _pitchVelocity,
                PitchSmoothTime,
                Mathf.Infinity,
                dt);
            _rig.LookSize = Mathf.SmoothDamp(
                _rig.LookSize,
                _targets.LookSize,
                ref _lookSizeVelocity,
                LookSizeSmoothTime,
                Mathf.Infinity,
                dt);

            var orbitYaw = SampleProfessionalOrbitYaw(now, profile);
            _rig.Yaw = Mathf.SmoothDampAngle(
                _rig.Yaw,
                orbitYaw,
                ref _yawVelocity,
                YawSmoothTime,
                YawMaxSpeed,
                dt);
        }

        static void TryRequest(CameraBeat beat, bool force)
        {
            _pendingBeat = beat;
            PulseLiveView(force: force);
        }

        static void PulseLiveView(bool force)
        {
            if (!CanOperate())
                return;

            if (!force && UserCameraOverrideActive())
                return;

            var role = ShotRoleFor(_pendingBeat);
            var profile = ShotProfileFor(role);
            var now = EditorApplication.timeSinceStartup;
            if (!force && now - _lastEventPulseAt < profile.HoldSeconds)
                return;

            var wideBeat = _pendingBeat is CameraBeat.SessionOpen or CameraBeat.PhaseChange or CameraBeat.BuildArea
                           && !ShouldFrameLiveWorkTile();
            if (!TryResolveActiveBuildBounds(profile, out var bounds, out var pivot, allowWidePlacedBounds: wideBeat))
                return;

            _lastEventPulseAt = now;
            _rig.LastReframeAt = now;
            _rig.LastBeat = _pendingBeat;
            var resetOrbit = _forceInstantSnapOnce && !ShouldFrameLiveWorkTile();
            if (_forceInstantSnapOnce)
                BeginOrbitHold(now, profile, snapYaw: _rig.Yaw);
            ApplyWideShot(bounds, pivot, profile, snapFraming: _forceInstantSnapOnce, resetOrbit: resetOrbit);
        }

        /// <summary>Demo timelapse + active sculpt tile — orbit the tile being worked, not the whole grid.</summary>
        static bool ShouldFrameLiveWorkTile()
        {
            if (SurfaceTerrainTileExpansion.LiveFocusTerrain == null)
                return false;
            if (_demoRecordingDepth > 0)
                return true;
            if (!_sessionActive)
                return false;

            return _pendingBeat is CameraBeat.TerrainTilePlaced or CameraBeat.TerrainGridWork
                   || ShotRoleFor(_pendingBeat) == ShotRole.MediumCoverage;
        }

        static bool TryResolveActiveBuildBounds(
            ShotProfile profile,
            out Bounds bounds,
            out Vector3 pivot,
            bool allowWidePlacedBounds = false)
        {
            pivot = Vector3.zero;
            var tileSpan = ResolveTypicalTileSpan();

            if (ShouldFrameLiveWorkTile() &&
                SurfaceTerrainTileExpansion.TryResolveLiveTerrainFocusBounds(out bounds))
            {
                pivot = bounds.center;
                TightenTileBounds(ref bounds, profile.PaddingFraction, tileSpan);
                return true;
            }

            var preferPlacedWork = allowWidePlacedBounds || (_cinematicEnabled && _sessionActive && _demoRecordingDepth == 0);
            var useTileTight = !_sessionActive &&
                (_pendingBeat == CameraBeat.TerrainTilePlaced ||
                 ShotRoleFor(_pendingBeat) == ShotRole.MediumCoverage);

            if (preferPlacedWork && !useTileTight &&
                SurfaceTerrainTileExpansion.TryResolvePlacedTerrainWorkBounds(
                    out bounds,
                    paddingFraction: profile.PaddingFraction))
            {
                pivot = ResolvePlayDiskPivot();
                var maxWideSpan = tileSpan * WideBuildBoundsSpanMultiplier();
                CapBoundsHorizontalSpan(ref bounds, maxWideSpan);
                return true;
            }

            if (useTileTight &&
                SurfaceTerrainTileExpansion.TryResolveLiveTerrainFocusBounds(out bounds))
            {
                pivot = bounds.center;
                TightenTileBounds(ref bounds, profile.PaddingFraction, tileSpan);
                return true;
            }

            if (!allowWidePlacedBounds)
            {
                var main = SurfaceTerrainTileExpansion.FindMainTerrainInScene();
                if (main?.terrainData != null)
                {
                    bounds = new Bounds(
                        main.transform.position + main.terrainData.bounds.center,
                        main.terrainData.size);
                    pivot = ResolvePlayDiskPivot();
                    TightenTileBounds(ref bounds, profile.PaddingFraction, tileSpan);
                    return true;
                }

                bounds = new Bounds(Vector3.zero, Vector3.one * tileSpan);
                pivot = bounds.center;
                return false;
            }

            if (SurfaceTerrainTileExpansion.TryResolvePlacedTerrainWorkBounds(
                    out bounds,
                    paddingFraction: profile.PaddingFraction))
            {
                pivot = ResolvePlayDiskPivot();
                var maxWideSpan = tileSpan * WideBuildBoundsSpanMultiplier();
                CapBoundsHorizontalSpan(ref bounds, maxWideSpan);
                return true;
            }

            var env = Object.FindAnyObjectByType<EnvironmentRoot>();
            if (env == null)
            {
                bounds = new Bounds(Vector3.zero, Vector3.one * tileSpan);
                pivot = bounds.center;
                return false;
            }

            var envMain = SurfaceTerrainTileExpansion.FindMainTerrainInScene();
            if (envMain != null)
            {
                bounds = new Bounds(
                    envMain.transform.position + envMain.terrainData.bounds.center,
                    envMain.terrainData.size);
                pivot = bounds.center;
                return true;
            }

            bounds = new Bounds(env.transform.position, Vector3.one * tileSpan);
            pivot = bounds.center;
            return true;
        }

        static void TightenTileBounds(ref Bounds bounds, float paddingFraction, float tileSpan)
        {
            var pad = Mathf.Max(bounds.size.x, bounds.size.z) * paddingFraction;
            bounds.Expand(Vector3.one * Mathf.Max(pad, tileSpan * 0.04f));
            CapBoundsHorizontalSpan(ref bounds, tileSpan * ZoomTileSpanCapMultiplier());
        }

        static float ZoomTileSpanCapMultiplier()
        {
            var slider = Mathf.Clamp(_zoomOutMultiplier, ZoomSliderMin, ZoomSliderExtendedMax);
            if (slider <= ZoomSliderStandardMax)
                return Mathf.Lerp(1.08f, 1.45f, (slider - ZoomSliderMin) / (ZoomSliderStandardMax - ZoomSliderMin));

            var legacyT = LegacyExtendedSliderT(slider);
            var span = Mathf.Lerp(1.45f, 14f, legacyT);
            if (slider <= ZoomSliderLegacyExtendedMax)
                return span;

            var ultraT = UltraExtendedSliderT(slider);
            return Mathf.Lerp(span, 40f, ultraT);
        }

        static float WideBuildBoundsSpanMultiplier()
        {
            var slider = Mathf.Clamp(_zoomOutMultiplier, ZoomSliderMin, ZoomSliderExtendedMax);
            if (slider < 14f)
                return 1.25f;

            var legacyT = (Mathf.Min(slider, ZoomSliderLegacyExtendedMax) - 14f) /
                          (ZoomSliderLegacyExtendedMax - 14f);
            var span = Mathf.Lerp(1.25f, 12f, legacyT);
            if (slider <= ZoomSliderLegacyExtendedMax)
                return span;

            var ultraT = UltraExtendedSliderT(slider);
            return Mathf.Lerp(span, 36f, ultraT);
        }

        static float LegacyExtendedSliderT(float slider)
        {
            slider = Mathf.Clamp(slider, ZoomSliderStandardMax, ZoomSliderLegacyExtendedMax);
            return (slider - ZoomSliderStandardMax) / (ZoomSliderLegacyExtendedMax - ZoomSliderStandardMax);
        }

        static float UltraExtendedSliderT(float slider)
        {
            if (slider <= ZoomSliderLegacyExtendedMax)
                return 0f;
            return (slider - ZoomSliderLegacyExtendedMax) /
                   (ZoomSliderExtendedMax - ZoomSliderLegacyExtendedMax);
        }

        static bool PreferWideBuildBounds() => _zoomOutMultiplier >= 14f;

        static float ResolveTypicalTileSpan()
        {
            var main = SurfaceTerrainTileExpansion.FindMainTerrainInScene();
            if (main?.terrainData == null)
                return 512f;

            return Mathf.Max(main.terrainData.size.x, main.terrainData.size.z, 256f);
        }

        static void CapBoundsHorizontalSpan(ref Bounds bounds, float maxSpan)
        {
            var horizontal = Mathf.Max(bounds.size.x, bounds.size.z);
            if (horizontal <= maxSpan)
                return;

            var center = bounds.center;
            bounds = new Bounds(center, new Vector3(maxSpan, bounds.size.y, maxSpan));
        }

        static void ApplyWideShot(Bounds bounds, Vector3 pivot, ShotProfile profile, bool snapFraming, bool resetOrbit = true)
        {
            var sv = SceneView.lastActiveSceneView ?? ResolveAnySceneView();
            if (sv == null)
                return;

            if (_cinematicEnabled || _demoRecordingDepth > 0)
                ApplyCinematicWideShot(sv, bounds, pivot, profile, snapFraming, resetOrbit);
            else
                ApplyDocumentaryFrame(sv, bounds);
        }

        static void ApplyDocumentaryFrame(SceneView sv, Bounds bounds)
        {
            var padded = bounds;
            padded.Expand(bounds.size * BasePaddingFraction * ZoomCoverageScale());
            sv.Frame(padded, false);
            sv.Repaint();
        }

        static void ApplyCinematicWideShot(
            SceneView sv,
            Bounds bounds,
            Vector3 pivot,
            ShotProfile profile,
            bool snapFraming,
            bool resetOrbit = true)
        {
            var horizontalSpan = Mathf.Max(bounds.size.x, bounds.size.z);
            var verticalSpan = Mathf.Max(bounds.size.y, 96f);
            var coverageSpan = Mathf.Max(horizontalSpan, verticalSpan * 0.55f);
            var now = EditorApplication.timeSinceStartup;
            var dolly = SampleRecordingDollyScale(now);
            var pitchWobble = SampleRecordingPitchWobble(now, profile);
            var targetDistance = ComputeFramingDistance(coverageSpan, profile.DistanceScale) * dolly;
            var lookSize = ComputeLookAtSize(coverageSpan, profile.DistanceScale) * dolly;

            _targets.Pivot = pivot;
            _targets.Pitch = profile.Pitch + pitchWobble;
            _targets.Distance = targetDistance;
            _targets.LookSize = lookSize;
            _appliedZoomOutMultiplier = _zoomOutMultiplier;

            if (resetOrbit && snapFraming)
                BeginOrbitHold(now, profile, snapYaw: _rig.Yaw);

            var dt = SampleDeltaTime();
            AdvanceRigSmoothing(dt, profile, now, snapFraming || _forceInstantSnapOnce);

            var rot = Quaternion.Euler(_rig.Pitch, _rig.Yaw, 0f);
            var eye = _rig.Pivot + rot * (Vector3.back * _rig.Distance) + Vector3.up * (_rig.Distance * 0.1f);
            var viewDir = Quaternion.LookRotation(_rig.Pivot - eye, Vector3.up);
            sv.LookAt(_rig.Pivot, viewDir, _rig.LookSize);
            sv.Repaint();
        }

        static float ComputeFramingDistance(float coverageSpan, float distanceScale)
        {
            // Distance follows lookSize — SceneView.LookAt positions from size, not our eye offset.
            return ComputeLookAtSize(coverageSpan, distanceScale) * 1.08f;
        }

        static float ComputeLookAtSize(float coverageSpan, float distanceScale)
        {
            var tileSpan = ResolveTypicalTileSpan();
            var zoom = ZoomCoverageScale();
            var span = Mathf.Max(coverageSpan, 64f);
            var lookSize = span * distanceScale * 0.22f * zoom;
            var maxSize = tileSpan * 0.78f * Mathf.Lerp(0.85f, 1.15f, Mathf.Min(zoom, ZoomCoverageAtStandardMax));
            if (zoom > ZoomCoverageAtStandardMax)
            {
                var extra = zoom <= ZoomCoverageAtExtendedMax
                    ? Mathf.InverseLerp(ZoomCoverageAtStandardMax, ZoomCoverageAtExtendedMax, zoom)
                    : 1f + Mathf.InverseLerp(ZoomCoverageAtExtendedMax, ZoomCoverageAtAbsoluteMax, zoom);
                maxSize = Mathf.Lerp(tileSpan * 0.9f, tileSpan * 16f, Mathf.Clamp01(extra));
            }

            var minSize = (_sessionActive || _cinematicEnabled) ? 36f : 24f;
            if (_sessionActive || _cinematicEnabled)
                maxSize = Mathf.Max(maxSize, tileSpan * 1.02f);

            return Mathf.Clamp(lookSize, minSize, maxSize);
        }

        static Vector3 ResolvePlayDiskPivot()
        {
            var main = SurfaceTerrainTileExpansion.FindMainTerrainInScene();
            var ground = main != null
                ? SceneGroundResolver.ResolveForFullWorld(main.transform)
                : null;
            if (ground == null)
                return main != null && main.terrainData != null
                    ? main.transform.position + main.terrainData.bounds.center
                    : Vector3.zero;

            var centerXZ = SurfaceTerrainTileExpansion.ResolvePlayDiskCenterXZ(ground, main);
            return new Vector3(centerXZ.x, ground.SurfaceY, centerXZ.z);
        }

        static float ZoomCoverageScale()
        {
            var slider = Mathf.Clamp(_zoomOutMultiplier, ZoomSliderMin, ZoomSliderExtendedMax);
            if (slider <= ZoomSliderStandardMax)
            {
                var t = (slider - ZoomSliderMin) / (ZoomSliderStandardMax - ZoomSliderMin);
                return Mathf.Lerp(0.32f, 0.92f, t);
            }

            var legacyT = LegacyExtendedSliderT(slider);
            var coverage = Mathf.Lerp(ZoomCoverageAtStandardMax, ZoomCoverageAtExtendedMax, legacyT);
            if (slider <= ZoomSliderLegacyExtendedMax)
                return coverage;

            var ultraT = UltraExtendedSliderT(slider);
            return Mathf.Lerp(coverage, ZoomCoverageAtAbsoluteMax, ultraT);
        }

        static void BeginOrbitHold(double now, ShotProfile profile, float snapYaw)
        {
            _orbitRestYaw = snapYaw;
            _orbitArcActive = false;
            _orbitDwellUntil = now + profile.OrbitDwellSec;
        }

        static void BeginOrbitArc(double now, ShotProfile profile)
        {
            _orbitArcActive = true;
            _orbitArcStart = now;
            _orbitArcEnd = now + profile.OrbitArcSec;
            _orbitYawFrom = _orbitRestYaw;
            var arcDegrees = profile.OrbitArcDegrees;
            if (_demoRecordingDepth > 0)
                arcDegrees *= 1f + 0.15f * ((_rig.EventSerial % 3) - 1);
            _orbitYawTo = _orbitRestYaw + arcDegrees * _orbitArcSign;
            _orbitArcSign = -_orbitArcSign;
        }

        static float SampleRecordingDollyScale(double now)
        {
            if (_demoRecordingDepth <= 0)
                return 1f;

            // Hub zoom slider is authoritative — never auto pull-in closer than user framing.
            if (_zoomOutMultiplier > ZoomSliderStandardMax)
                return 1f;

            var cycle = 16.0 + (_rig.EventSerial % 5) * 2.5;
            var t = (float)((now % cycle) / cycle);
            var wobble = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f);
            return 1f + 0.06f * wobble;
        }

        static float SampleRecordingPitchWobble(double now, ShotProfile profile)
        {
            if (_demoRecordingDepth <= 0)
                return 0f;

            return Mathf.Sin((float)now * 0.17f) * 3.5f;
        }

        static float SampleProfessionalOrbitYaw(double now, ShotProfile profile)
        {
            if (_orbitArcActive)
            {
                var span = Mathf.Max(0.001f, (float)(_orbitArcEnd - _orbitArcStart));
                var t = Mathf.Clamp01((float)((now - _orbitArcStart) / span));
                t = t * t * (3f - 2f * t);
                if (t >= 0.999f)
                {
                    _orbitArcActive = false;
                    _orbitRestYaw = _orbitYawTo;
                    _orbitDwellUntil = now + profile.OrbitDwellSec;
                }

                return Mathf.LerpAngle(_orbitYawFrom, _orbitYawTo, t);
            }

            if (now >= _orbitDwellUntil)
                BeginOrbitArc(now, profile);

            return _orbitRestYaw;
        }

        static ShotRole ShotRoleFor(CameraBeat beat)
        {
            if (_demoRecordingDepth > 0)
                return ShotRole.DemoHero;

            switch (beat)
            {
                case CameraBeat.SessionOpen:
                case CameraBeat.PhaseChange:
                case CameraBeat.BuildArea:
                    return ShotRole.EstablishingWide;
                case CameraBeat.TerrainGridWork:
                case CameraBeat.TerrainTilePlaced:
                case CameraBeat.PipelineStep:
                    return ShotRole.MasterCoverage;
                case CameraBeat.PropPlaced:
                    return ShotRole.MediumCoverage;
                case CameraBeat.DemoRecording:
                    return ShotRole.DemoHero;
                default:
                    return ShotRole.MasterCoverage;
            }
        }

        static ShotProfile ShotProfileFor(ShotRole role)
        {
            switch (role)
            {
                case ShotRole.EstablishingWide:
                    return new ShotProfile
                    {
                        PaddingFraction = 0.22f,
                        DistanceScale = 1.45f,
                        Pitch = 50f,
                        FocusPivotBlend = 0.12f,
                        ReframeEveryNthEvent = 1,
                        HoldSeconds = 2.5,
                        OrbitDwellSec = 5.0,
                        OrbitArcSec = 32.0,
                        OrbitArcDegrees = 26f,
                    };
                case ShotRole.MasterCoverage:
                    return new ShotProfile
                    {
                        PaddingFraction = 0.18f,
                        DistanceScale = 1.05f,
                        Pitch = 48f,
                        FocusPivotBlend = 0.28f,
                        ReframeEveryNthEvent = 6,
                        HoldSeconds = 1.2,
                        OrbitDwellSec = 4.5,
                        OrbitArcSec = 26.0,
                        OrbitArcDegrees = 22f,
                    };
                case ShotRole.MediumCoverage:
                    return new ShotProfile
                    {
                        PaddingFraction = 0.16f,
                        DistanceScale = 0.95f,
                        Pitch = 46f,
                        FocusPivotBlend = 0.42f,
                        ReframeEveryNthEvent = 1,
                        HoldSeconds = 1.0,
                        OrbitDwellSec = 3.5,
                        OrbitArcSec = 20.0,
                        OrbitArcDegrees = 18f,
                    };
                case ShotRole.DemoHero:
                default:
                    return new ShotProfile
                    {
                        PaddingFraction = 0.12f,
                        DistanceScale = 0.78f,
                        Pitch = 44f,
                        FocusPivotBlend = 0.95f,
                        ReframeEveryNthEvent = 1,
                        HoldSeconds = 0.25,
                        OrbitDwellSec = 1.4,
                        OrbitArcSec = 11.0,
                        OrbitArcDegrees = 42f,
                    };
            }
        }

        static bool CanOperate()
        {
            RefreshSettingsIfNeeded();
            if (!_sessionActive && _demoRecordingDepth <= 0)
                return false;
            if (!_allowHijack)
                return false;
            if (EnvironmentKitHardwareBudget.Active.DisableLiveSceneFraming)
                return false;
            return true;
        }

        static bool UserCameraOverrideActive() =>
            EditorApplication.timeSinceStartup < _userCameraOverrideUntil;

        static void MarkUserCameraOverride()
        {
            _userCameraOverrideUntil = EditorApplication.timeSinceStartup + UserOverrideHoldSeconds;
        }

        static void RefreshSettingsIfNeeded()
        {
            // Zoom slider must apply immediately — not on a 0.75s throttle.
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            var prefZoom = EditorPrefs.GetFloat(
                "CaveBuild_LiveSceneCameraZoomOut",
                settings.liveSceneCameraZoomOut);
            var newZoom = Mathf.Clamp(prefZoom, ZoomSliderMin, ZoomSliderExtendedMax);
            if (!Mathf.Approximately(newZoom, _zoomOutMultiplier))
            {
                _zoomOutMultiplier = newZoom;
                _forceInstantSnapOnce = true;
            }
            else
            {
                _zoomOutMultiplier = newZoom;
            }

            var now = EditorApplication.timeSinceStartup;
            if (now < _nextSettingsRefreshAt)
                return;
            _nextSettingsRefreshAt = now + 0.75;

            var s = CaveBuildCursorSettings.LoadOrCreate();
            s.LoadFromPrefs();
            _cinematicEnabled = s.cinematicSceneCamera || _demoRecordingDepth > 0;
            _allowHijack = s.cinematicSceneCamera ||
                           s.showLiveScenePlacement ||
                           !s.stabilizationMode ||
                           s.forceLivePreviewWhenRecording ||
                           _demoRecordingDepth > 0;
        }

        static void OnEditorUpdate()
        {
            if (!CanOperate() || UserCameraOverrideActive())
                return;

            var role = _demoRecordingDepth > 0 ? ShotRole.DemoHero : ShotRoleFor(_rig.LastBeat);
            var profile = ShotProfileFor(role);
            var allowWide = (_sessionActive || PreferWideBuildBounds()) && !ShouldFrameLiveWorkTile();
            if (!TryResolveActiveBuildBounds(profile, out var bounds, out var pivot, allowWidePlacedBounds: allowWide))
                return;

            ApplyWideShot(bounds, pivot, profile, snapFraming: false);
        }

        static SceneView ResolveAnySceneView()
        {
            foreach (SceneView sv in SceneView.sceneViews)
            {
                if (sv != null)
                    return sv;
            }

            return null;
        }

        static void OnSceneGui(SceneView view)
        {
            if (!SessionActive)
                return;

            var e = Event.current;
            if (e == null)
                return;

            if (e.type == EventType.ScrollWheel ||
                (e.type == EventType.MouseDrag && (e.button == 1 || e.button == 2)))
                MarkUserCameraOverride();
        }
    }
}
#endif
