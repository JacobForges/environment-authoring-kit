using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// AAA-style third-person orbit camera: collision pull-in, smooth follow, player or auto-track modes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class ThirdPersonFollowCamera : MonoBehaviour
    {
        public enum FollowMode
        {
            PlayerControlled,
            AutoTrackTarget,
        }

        public struct Preset
        {
            public float Distance;
            public float MinDistance;
            public float ShoulderHeight;
            public float PitchDegrees;
            public float MinPitch;
            public float MaxPitch;
            public float FieldOfView;
            public float PositionSmoothTime;
            public float RotationSmoothTime;
        }

        public static readonly Preset PlayerPreset = new()
        {
            Distance = 4.35f,
            MinDistance = 1.15f,
            ShoulderHeight = 1.38f,
            PitchDegrees = 16f,
            MinPitch = -22f,
            MaxPitch = 52f,
            FieldOfView = 58f,
            PositionSmoothTime = 0.09f,
            RotationSmoothTime = 0.06f,
        };

        public static readonly Preset SpectatorPreset = new()
        {
            Distance = 7.25f,
            MinDistance = 2.2f,
            ShoulderHeight = 1.42f,
            PitchDegrees = 14f,
            MinPitch = -18f,
            MaxPitch = 42f,
            FieldOfView = 52f,
            PositionSmoothTime = 0.12f,
            RotationSmoothTime = 0.08f,
        };

        [SerializeField] Transform target;
        [SerializeField] Transform aimPivot;
        [SerializeField] FollowMode mode = FollowMode.PlayerControlled;

        [SerializeField] float distance = 4.35f;
        [SerializeField] float minDistance = 1.15f;
        [SerializeField] float shoulderHeight = 1.38f;
        [SerializeField] float lookHeightOffset = 0.12f;
        [SerializeField] float yawDegrees;
        [SerializeField] float pitchDegrees = 16f;
        [SerializeField] float minPitch = -22f;
        [SerializeField] float maxPitch = 52f;

        [SerializeField] float positionSmoothTime = 0.09f;
        [SerializeField] float rotationSmoothTime = 0.06f;
        [SerializeField] float autoYawSmooth = 7f;
        [SerializeField] float velocityLeadSeconds = 0.35f;
        [SerializeField] float autoPitchFromSpeed = 3.5f;

        [SerializeField] float collisionRadius = 0.28f;
        [SerializeField] LayerMask obstructionMask = ~0;
        [SerializeField] float cellSizeScaleReference = 3f;

        Camera _camera;
        float _currentDistance;
        Vector3 _positionVelocity;
        CharacterController _targetController;
        float _baseDistance;
        float _baseMinDistance;
        float _baseShoulderHeight;
        float _basePitch;
        float _lastCellScale = 1f;

        public Transform Target => target;
        public Transform AimPivot => aimPivot;
        public FollowMode Mode => mode;
        public float YawDegrees => yawDegrees;
        public float PitchDegrees => pitchDegrees;

        public Vector3 MovementForward
        {
            get
            {
                var fwd = Quaternion.Euler(0f, yawDegrees, 0f) * Vector3.forward;
                fwd.y = 0f;
                return fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
            }
        }

        public Vector3 MovementRight
        {
            get
            {
                var right = Quaternion.Euler(0f, yawDegrees, 0f) * Vector3.right;
                right.y = 0f;
                return right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
            }
        }

        public static ThirdPersonFollowCamera Ensure(
            Camera cam,
            Transform followTarget,
            Transform aim,
            FollowMode followMode,
            Preset preset)
        {
            if (cam == null || followTarget == null)
                return null;

            if (CanDetachCameraFromHierarchy(cam.transform))
                cam.transform.SetParent(null, true);

            var follow = cam.GetComponent<ThirdPersonFollowCamera>();
            if (follow == null)
                follow = cam.gameObject.AddComponent<ThirdPersonFollowCamera>();

            follow._camera = cam;
            follow.target = followTarget;
            follow.aimPivot = aim;
            follow.mode = followMode;
            follow.ApplyPreset(preset);
            follow._targetController = followTarget.GetComponent<CharacterController>();
            follow.SyncYawFromTarget();
            follow._currentDistance = follow.distance;
            follow.SnapToIdeal();

            cam.nearClipPlane = 0.08f;
            cam.farClipPlane = 200f;
            return follow;
        }

        static bool CanDetachCameraFromHierarchy(Transform camTransform)
        {
            if (camTransform == null)
                return false;

#if UNITY_EDITOR
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(camTransform))
                return false;
#endif
            return true;
        }

        public void ApplyPreset(Preset preset)
        {
            _baseDistance = preset.Distance;
            _baseMinDistance = preset.MinDistance;
            _baseShoulderHeight = preset.ShoulderHeight;
            _basePitch = preset.PitchDegrees;
            distance = preset.Distance;
            minDistance = preset.MinDistance;
            shoulderHeight = preset.ShoulderHeight;
            pitchDegrees = preset.PitchDegrees;
            minPitch = preset.MinPitch;
            maxPitch = preset.MaxPitch;
            positionSmoothTime = preset.PositionSmoothTime;
            rotationSmoothTime = preset.RotationSmoothTime;
            _lastCellScale = 1f;
            if (_camera != null)
                _camera.fieldOfView = preset.FieldOfView;
        }

        public void ScaleForCellSize(float cellMeters)
        {
            if (cellMeters <= 0.5f || cellSizeScaleReference <= 0.5f)
                return;

            var scale = cellMeters / cellSizeScaleReference;
            if (Mathf.Abs(scale - _lastCellScale) < 0.02f)
                return;

            _lastCellScale = scale;
            distance = _baseDistance * scale;
            minDistance = _baseMinDistance * Mathf.Max(0.85f, scale * 0.9f);
            shoulderHeight = _baseShoulderHeight * Mathf.Max(0.9f, scale * 0.95f);
        }

        public void SyncYawFromTarget()
        {
            if (target == null)
                return;

            yawDegrees = target.eulerAngles.y;
        }

        public void ApplyLookDelta(float yawDelta, float pitchDelta)
        {
            yawDegrees += yawDelta;
            pitchDegrees = Mathf.Clamp(pitchDegrees + pitchDelta, minPitch, maxPitch);
        }

        public void SetFieldOfView(float fov)
        {
            if (_camera != null)
                _camera.fieldOfView = fov;
        }

        public void SnapToIdeal()
        {
            if (target == null)
                return;

            ComputeIdealPose(out var pos, out var rot);
            transform.SetPositionAndRotation(pos, rot);
            _currentDistance = distance;
            _positionVelocity = Vector3.zero;
            SyncAimPivot(rot);
        }

        void Awake()
        {
            _camera = GetComponent<Camera>();
            _currentDistance = distance;
        }

        void LateUpdate()
        {
            if (target == null || _camera == null || !_camera.enabled)
                return;

            if (mode == FollowMode.AutoTrackTarget)
                UpdateAutoTrack();

            var cell = CavePlaytestBotScale.ResolveCellSizeMeters(target);
            ScaleForCellSize(cell);

            ComputeIdealPose(out var idealPos, out var idealRot);

            var smoothPos = positionSmoothTime > 0.001f
                ? Vector3.SmoothDamp(transform.position, idealPos, ref _positionVelocity, positionSmoothTime)
                : idealPos;

            var smoothRot = rotationSmoothTime > 0.001f
                ? Quaternion.Slerp(transform.rotation, idealRot, 1f - Mathf.Exp(-Time.deltaTime / rotationSmoothTime))
                : idealRot;

            transform.SetPositionAndRotation(smoothPos, smoothRot);
            SyncAimPivot(smoothRot);
        }

        void UpdateAutoTrack()
        {
            var desiredYaw = target.eulerAngles.y;
            if (_targetController != null)
            {
                var vel = _targetController.velocity;
                var planar = new Vector3(vel.x, 0f, vel.z);
                if (planar.sqrMagnitude > 0.35f)
                {
                    var lead = planar * velocityLeadSeconds;
                    var leadDir = lead.sqrMagnitude > 0.01f ? lead.normalized : planar.normalized;
                    var velYaw = Mathf.Atan2(leadDir.x, leadDir.z) * Mathf.Rad2Deg;
                    desiredYaw = Mathf.LerpAngle(desiredYaw, velYaw, 0.65f);
                }

                var speed = planar.magnitude;
                var pitchBoost = Mathf.Clamp(speed * 0.04f * autoPitchFromSpeed, -4f, 6f);
                var targetPitch = _basePitch + pitchBoost;
                pitchDegrees = Mathf.Lerp(pitchDegrees, targetPitch, Time.deltaTime * autoYawSmooth * 0.35f);
            }

            yawDegrees = Mathf.LerpAngle(yawDegrees, desiredYaw, Time.deltaTime * autoYawSmooth);
        }

        void ComputeIdealPose(out Vector3 position, out Quaternion rotation)
        {
            var pivot = GetPivotWorld();
            var lookPoint = pivot + Vector3.up * lookHeightOffset;
            var orbit = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
            var back = orbit * Vector3.back;
            var desiredDist = distance;
            _currentDistance = Mathf.Max(minDistance, desiredDist);

            var idealCam = pivot + back * _currentDistance;
            var resolved = ResolveCollision(pivot, idealCam);
            _currentDistance = Vector3.Distance(pivot, resolved);

            position = resolved;
            var toLook = lookPoint - position;
            rotation = toLook.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(toLook.normalized, Vector3.up)
                : orbit;
        }

        Vector3 GetPivotWorld() =>
            target.position + Vector3.up * shoulderHeight;

        Vector3 ResolveCollision(Vector3 pivot, Vector3 idealCameraPos)
        {
            var toCam = idealCameraPos - pivot;
            var dist = toCam.magnitude;
            if (dist < 0.01f)
                return idealCameraPos;

            var dir = toCam / dist;
            var mask = obstructionMask;
            if (mask == 0)
                mask = Physics.DefaultRaycastLayers;

            if (Physics.SphereCast(
                    pivot,
                    collisionRadius,
                    dir,
                    out var hit,
                    dist,
                    mask,
                    QueryTriggerInteraction.Ignore) &&
                hit.collider != null &&
                !hit.collider.transform.IsChildOf(target) &&
                hit.collider.transform != target)
            {
                var safeDist = Mathf.Max(minDistance, hit.distance - collisionRadius * 0.65f);
                return pivot + dir * safeDist;
            }

            return idealCameraPos;
        }

        void SyncAimPivot(Quaternion cameraRotation)
        {
            if (aimPivot == null)
                return;

            aimPivot.position = GetPivotWorld() + Vector3.up * lookHeightOffset;
            var euler = cameraRotation.eulerAngles;
            aimPivot.rotation = Quaternion.Euler(euler.x, euler.y, 0f);
        }
    }
}
