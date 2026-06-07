using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Underground look is per-player-camera only. Restores surface settings on exit.
    /// Applies light fog and narrower FOV while inside the cave volume.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class CaveUndergroundAtmosphere : MonoBehaviour
    {
        [Header("Camera")]
        public Color cameraBackground = new(0.008f, 0.009f, 0.012f, 1f);
        public bool overrideFieldOfView = true;
        [Tooltip("Slightly narrower FOV underground for a tunnel feel.")]
        public float undergroundFieldOfView = 64f;

        [Header("Fog (heavy — claustrophobic cave feel)")]
        public bool overrideFog = true;
        public Color fogColor = new(0.018f, 0.02f, 0.026f, 1f);
        public float fogDensity = 0.045f;
        public FogMode fogMode = FogMode.ExponentialSquared;

        [Header("Ambient (force dark so cave isn't lit by surface daytime)")]
        public bool overrideAmbient = true;
        public Color ambientSky = new(0.04f, 0.04f, 0.05f, 1f);
        public Color ambientEquator = new(0.025f, 0.025f, 0.03f, 1f);
        public Color ambientGround = new(0.015f, 0.013f, 0.014f, 1f);
        public float ambientIntensity = 0.35f;

        [Header("Debug")]
        public bool logTransitions;

        readonly Dictionary<Camera, CameraState> _savedCameras = new();
        FogState _savedFog;
        AmbientState _savedAmbient;
        bool _fogSaved;
        bool _ambientSaved;
        int _occupants;

        struct CameraState
        {
            public CameraClearFlags clearFlags;
            public Color backgroundColor;
            public float fieldOfView;
        }

        struct FogState
        {
            public bool enabled;
            public Color color;
            public float density;
            public FogMode mode;
        }

        struct AmbientState
        {
            public UnityEngine.Rendering.AmbientMode mode;
            public Color sky;
            public Color equator;
            public Color ground;
            public Color light;
            public float intensity;
        }

        void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!IsPlayer(other))
                return;

            _occupants++;
            if (_occupants == 1)
                EnterCave();
        }

        void OnTriggerExit(Collider other)
        {
            if (!IsPlayer(other))
                return;

            _occupants = Mathf.Max(0, _occupants - 1);
            if (_occupants == 0)
                ExitCave();
        }

        static bool IsPlayer(Collider other)
        {
            if (CavePlaytestBotMarker.IsBotCollider(other))
                return false;

            if (other.GetComponentInParent<CavePlaytestBotAvatar>() != null)
                return false;

            if (other.CompareTag("Player"))
                return true;

            var cc = other.GetComponentInParent<CharacterController>();
            if (cc == null)
                return false;

            return cc.GetComponentInParent<CavePlaytestBotMarker>() == null;
        }

        /// <summary>Apply underground camera background immediately (e.g. after cave warp teleport).</summary>
        public void ForceUndergroundLook()
        {
            _occupants = Mathf.Max(1, _occupants);
            EnterCave();
        }

        void EnterCave()
        {
            ApplyFog(true);
            ApplyAmbient(true);
            ApplyToPlayerCameras(true);
            if (logTransitions)
                Debug.Log("[CaveUndergroundAtmosphere] Entered cave (camera + fog + ambient).", this);
        }

        void ExitCave()
        {
            ApplyToPlayerCameras(false);
            ApplyAmbient(false);
            ApplyFog(false);
            if (logTransitions)
                Debug.Log("[CaveUndergroundAtmosphere] Exited cave — restored camera, fog, ambient.", this);
        }

        /// <summary>Restore surface sky/fog when play starts outside the cave (fixes oversized atmosphere triggers).</summary>
        public void ResetSurfaceLookIfUnoccupied()
        {
            if (_occupants > 0)
                return;

            _occupants = 0;
            ExitCave();
        }

        void ApplyAmbient(bool underground)
        {
            if (!overrideAmbient)
                return;

            if (underground)
            {
                if (!_ambientSaved)
                {
                    _savedAmbient = new AmbientState
                    {
                        mode = RenderSettings.ambientMode,
                        sky = RenderSettings.ambientSkyColor,
                        equator = RenderSettings.ambientEquatorColor,
                        ground = RenderSettings.ambientGroundColor,
                        light = RenderSettings.ambientLight,
                        intensity = RenderSettings.ambientIntensity
                    };
                    _ambientSaved = true;
                }

                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = ambientSky;
                RenderSettings.ambientEquatorColor = ambientEquator;
                RenderSettings.ambientGroundColor = ambientGround;
                RenderSettings.ambientLight = ambientEquator;
                RenderSettings.ambientIntensity = ambientIntensity;
            }
            else if (_ambientSaved)
            {
                RenderSettings.ambientMode = _savedAmbient.mode;
                RenderSettings.ambientSkyColor = _savedAmbient.sky;
                RenderSettings.ambientEquatorColor = _savedAmbient.equator;
                RenderSettings.ambientGroundColor = _savedAmbient.ground;
                RenderSettings.ambientLight = _savedAmbient.light;
                RenderSettings.ambientIntensity = _savedAmbient.intensity;
                _ambientSaved = false;
            }
        }

        void ApplyFog(bool underground)
        {
            if (!overrideFog)
                return;

            if (underground)
            {
                if (!_fogSaved)
                {
                    _savedFog = new FogState
                    {
                        enabled = RenderSettings.fog,
                        color = RenderSettings.fogColor,
                        density = RenderSettings.fogDensity,
                        mode = RenderSettings.fogMode
                    };
                    _fogSaved = true;
                }

                RenderSettings.fog = true;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogMode = fogMode;
            }
            else if (_fogSaved)
            {
                RenderSettings.fog = _savedFog.enabled;
                RenderSettings.fogColor = _savedFog.color;
                RenderSettings.fogDensity = _savedFog.density;
                RenderSettings.fogMode = _savedFog.mode;
                _fogSaved = false;
            }
        }

        void ApplyToPlayerCameras(bool underground)
        {
            foreach (var cam in FindPlayerCameras())
            {
                if (cam == null)
                    continue;

                if (underground)
                {
                    if (!_savedCameras.ContainsKey(cam))
                    {
                        _savedCameras[cam] = new CameraState
                        {
                            clearFlags = cam.clearFlags,
                            backgroundColor = cam.backgroundColor,
                            fieldOfView = cam.fieldOfView
                        };
                    }

                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = cameraBackground;
                    if (overrideFieldOfView)
                        cam.fieldOfView = undergroundFieldOfView;
                }
                else if (_savedCameras.TryGetValue(cam, out var state))
                {
                    cam.clearFlags = state.clearFlags;
                    cam.backgroundColor = state.backgroundColor;
                    if (overrideFieldOfView)
                        cam.fieldOfView = state.fieldOfView;
                    _savedCameras.Remove(cam);
                }
            }
        }

        static IEnumerable<Camera> FindPlayerCameras()
        {
            var rig = Object.FindAnyObjectByType<PlayerCameraRig>();
            if (rig != null && rig.playerCamera != null)
            {
                yield return rig.playerCamera;
                yield break;
            }

            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null)
            {
                foreach (var cam in tagged.GetComponentsInChildren<Camera>(true))
                {
                    if (cam != null && cam.GetComponent<CavePlaytestBotSpectator>() == null)
                        yield return cam;
                }
            }

            if (Camera.main != null && Camera.main.GetComponent<CavePlaytestBotSpectator>() == null)
                yield return Camera.main;
        }
    }
}
