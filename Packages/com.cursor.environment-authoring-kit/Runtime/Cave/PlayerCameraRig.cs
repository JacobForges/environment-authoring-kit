using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Third-person player camera rig — orbit + collision; playtest bot uses a separate spectator camera.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        public const string PivotName = "CameraPivot";

        [Header("Camera")]
        public Camera playerCamera;
        public Transform cameraPivot;
        [SerializeField] float surfaceFieldOfView = 58f;
        [SerializeField] float caveFieldOfView = 54f;
        [SerializeField] float shoulderHeightMeters = 1.38f;

        [Header("Character")]
        [SerializeField] float controllerHeight = 1.85f;
        [SerializeField] float controllerRadius = 0.35f;

        public bool PlaytestSpectateActive { get; private set; }

        ThirdPersonFollowCamera _thirdPerson;
        CharacterController _controller;
        float _savedFov = 58f;
        bool _savedCamEnabled = true;

        public ThirdPersonFollowCamera ThirdPerson => _thirdPerson;

        public static PlayerCameraRig Ensure(Transform playerRoot)
        {
            if (playerRoot == null)
                return null;

            var rig = playerRoot.GetComponent<PlayerCameraRig>();
            if (rig == null)
                rig = playerRoot.gameObject.AddComponent<PlayerCameraRig>();
#if UNITY_EDITOR
            rig.TryAutoWireForEditor();
#else
            rig.AutoWire();
#endif
            return rig;
        }

        void Awake() => AutoWire();

        /// <summary>Editor-safe wiring — skips prefab-instance cameras that cannot be reparented (e.g. Main View).</summary>
        public bool TryAutoWireForEditor()
        {
#if UNITY_EDITOR
            if (playerCamera != null &&
                UnityEditor.PrefabUtility.IsPartOfPrefabInstance(playerCamera.transform))
                return false;
#endif
            AutoWire();
            return true;
        }

        public void AutoWire()
        {
            _controller = GetComponent<CharacterController>();
            if (_controller != null)
            {
                HumanoidDimensions.Resolve(out var h, out var r, out var step);
                controllerHeight = h;
                controllerRadius = r;
                _controller.height = h;
                _controller.radius = r;
                _controller.center = new Vector3(0f, h * 0.5f, 0f);
                _controller.stepOffset = step;
                shoulderHeightMeters = Mathf.Clamp(h * 0.78f, 1.1f, 1.55f);
            }

            EnsureCameraPivot();
            ResolvePlayerCamera();
            EnsureThirdPerson();

            BindPlayerControllerPivot();
        }

        void EnsureCameraPivot()
        {
            if (cameraPivot == null)
            {
                cameraPivot = transform.Find(PivotName);
                if (cameraPivot == null)
                {
                    foreach (Transform child in transform)
                    {
                        if (child.GetComponent<Camera>() != null)
                        {
                            cameraPivot = child;
                            break;
                        }
                    }
                }
            }

            if (cameraPivot == null)
            {
                var pivotGo = new GameObject(PivotName);
                pivotGo.transform.SetParent(transform, false);
                pivotGo.transform.localPosition = new Vector3(0f, shoulderHeightMeters, 0f);
                pivotGo.transform.localRotation = Quaternion.identity;
                cameraPivot = pivotGo.transform;
            }
            else if (CanReparentInEditor(cameraPivot))
            {
                cameraPivot.SetParent(transform, true);
                cameraPivot.localPosition = new Vector3(0f, shoulderHeightMeters, 0f);
            }
            else
            {
                shoulderHeightMeters = Mathf.Max(shoulderHeightMeters, cameraPivot.localPosition.y);
            }
        }

        void ResolvePlayerCamera()
        {
            if (playerCamera == null && cameraPivot != null)
                playerCamera = cameraPivot.GetComponent<Camera>();

            if (playerCamera == null)
                playerCamera = GetComponentInChildren<Camera>(true);

            if (playerCamera == null)
            {
                var camGo = new GameObject("PlayerCamera");
                playerCamera = camGo.AddComponent<Camera>();
                playerCamera.tag = "MainCamera";
            }

            if (playerCamera.transform.parent == cameraPivot && CanReparentInEditor(playerCamera.transform))
                playerCamera.transform.SetParent(null, true);

            var listener = playerCamera.GetComponent<AudioListener>();
            if (listener == null)
                playerCamera.gameObject.AddComponent<AudioListener>();
        }

        void EnsureThirdPerson()
        {
            if (playerCamera == null)
                return;

            _thirdPerson = ThirdPersonFollowCamera.Ensure(
                playerCamera,
                transform,
                cameraPivot,
                ThirdPersonFollowCamera.FollowMode.PlayerControlled,
                ThirdPersonFollowCamera.PlayerPreset);

            if (_thirdPerson != null)
            {
                _thirdPerson.SetFieldOfView(surfaceFieldOfView);
                playerCamera.depth = 0f;
            }
        }

        static bool CanReparentInEditor(Transform t)
        {
            if (t == null)
                return false;

#if UNITY_EDITOR
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(t))
                return false;
#endif
            return true;
        }

        void BindPlayerControllerPivot()
        {
            foreach (var mb in GetComponents<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != "PlayerController")
                    continue;

                var field = mb.GetType().GetField(
                    "cameraPivot",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                if (field != null && cameraPivot != null)
                    field.SetValue(mb, cameraPivot);
                return;
            }
        }

        public void ApplyLookInput(float yawDelta, float pitchDelta)
        {
            if (_thirdPerson == null || PlaytestSpectateActive)
                return;

            _thirdPerson.ApplyLookDelta(yawDelta, pitchDelta);
        }

        public Vector3 GetMovementForward() =>
            _thirdPerson != null ? _thirdPerson.MovementForward : transform.forward;

        public Vector3 GetMovementRight() =>
            _thirdPerson != null ? _thirdPerson.MovementRight : transform.right;

        public void SetUndergroundFov(bool underground)
        {
            if (playerCamera == null || PlaytestSpectateActive)
                return;

            var fov = underground ? caveFieldOfView : surfaceFieldOfView;
            if (_thirdPerson != null)
                _thirdPerson.SetFieldOfView(fov);
            else
                playerCamera.fieldOfView = fov;
        }

        public void SetPlaytestSpectate(bool active, Camera spectatorCamera)
        {
            PlaytestSpectateActive = active;
            if (playerCamera == null)
                return;

            if (active)
            {
                _savedCamEnabled = playerCamera.enabled;
                _savedFov = playerCamera.fieldOfView;
                playerCamera.enabled = false;
                var listener = playerCamera.GetComponent<AudioListener>();
                if (listener != null)
                    listener.enabled = false;
                if (spectatorCamera != null)
                    spectatorCamera.enabled = true;
            }
            else
            {
                playerCamera.enabled = _savedCamEnabled;
                if (_thirdPerson != null)
                    _thirdPerson.SetFieldOfView(_savedFov);
                else
                    playerCamera.fieldOfView = _savedFov;
                var listener = playerCamera.GetComponent<AudioListener>();
                if (listener != null)
                    listener.enabled = true;
                if (spectatorCamera != null)
                {
                    spectatorCamera.enabled = false;
                    var specListener = spectatorCamera.GetComponent<AudioListener>();
                    if (specListener != null)
                        specListener.enabled = false;
                }

                _thirdPerson?.SnapToIdeal();
            }
        }
    }
}
