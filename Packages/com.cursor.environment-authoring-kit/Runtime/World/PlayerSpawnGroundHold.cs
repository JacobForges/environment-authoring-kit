using EnvironmentAuthoringKit.Cave;
using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>
    /// Keeps the play character frozen above the spawn point until Physics confirms walkable ground.
    /// Terrain heightmap sampling alone is not enough — colliders often appear a few frames later.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSpawnGroundHold : MonoBehaviour
    {
        const int MaxHoldFrames = 240;
        const float EmergencyFloorHalfExtentM = 120f;

        static GameObject _sharedEmergencyFloor;

        CharacterController _controller;
        int _framesHeld;
        bool _released;

        public static bool IsActiveOn(Transform root)
        {
            if (root == null)
                return false;

            return root.GetComponent<PlayerSpawnGroundHold>() != null;
        }

        public static PlayerSpawnGroundHold Ensure(Transform root)
        {
            if (root == null)
                return null;

            var existing = root.GetComponent<PlayerSpawnGroundHold>();
            if (existing != null)
                return existing;

            return root.gameObject.AddComponent<PlayerSpawnGroundHold>();
        }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (_controller != null)
                _controller.enabled = false;
        }

        void OnDestroy()
        {
            if (!_released && _controller != null)
                _controller.enabled = true;
        }

        void Update()
        {
            if (_released)
                return;

            _framesHeld++;
            HoldAboveExpectedGround();

            if (HasPhysicsWalkableGround() || _framesHeld >= MaxHoldFrames)
                Release();
        }

        void HoldAboveExpectedGround()
        {
            var near = transform.position;
            if (near.sqrMagnitude < 0.01f)
            {
                var marker = CaveMainAreaRespawn.ResolveSurfaceSpawn();
                if (marker != null)
                    near = marker.position;
            }

            if (_controller != null)
                _controller.enabled = false;

            var dropPos = PlayerSpawnHeightUtility.ResolveDropSpawnPosition(near, _controller, 3f);
            transform.position = dropPos;
            Physics.SyncTransforms();
            PlayerGroundSnap.SnapTransform(transform, transform.position);

            if (!HasPhysicsWalkableGround()
                && PlayerSpawnHeightUtility.TrySampleWalkableSurfaceY(transform.position, out var surfaceY))
            {
                EnsureEmergencyFloor(new Vector3(transform.position.x, surfaceY, transform.position.z), surfaceY);
            }
        }

        void Release()
        {
            if (_released)
                return;

            _released = true;
            PlayerGroundSnap.SnapTransform(transform, transform.position);

            if (_controller != null)
            {
                Physics.SyncTransforms();
                _controller.enabled = true;
            }

            DestroyEmergencyFloorIfUnused();
            Destroy(this);
        }

        public static bool HasPhysicsWalkableGround(Vector3 near, CharacterController controller)
        {
            const float probeUp = 1.25f;
            const float probeDown = 6f;
            var origin = near + Vector3.up * probeUp;
            var hits = Physics.RaycastAll(origin, Vector3.down, probeUp + probeDown, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
                return false;

            var best = float.MinValue;
            var found = false;
            foreach (var hit in hits)
            {
                if (!IsWalkablePhysicsHit(hit))
                    continue;

                if (hit.point.y > best)
                {
                    best = hit.point.y;
                    found = true;
                }
            }

            if (!found)
                return false;

            if (controller == null || !controller.enabled)
                return true;

            var soleY = near.y + controller.center.y - controller.height * 0.5f;
            return soleY <= best + controller.skinWidth + 0.35f;
        }

        static bool HasPhysicsWalkableGround() =>
            HasPhysicsWalkableGround(
                transform.position,
                _controller != null && _controller.enabled ? _controller : GetComponent<CharacterController>());

        static bool IsWalkablePhysicsHit(RaycastHit hit)
        {
            if (hit.collider == null || hit.collider.isTrigger)
                return false;

            if (hit.normal.y < 0.35f)
                return false;

            if (CaveWalkableSurface.IsWalkableCollider(hit.collider))
                return true;

            if (hit.collider is TerrainCollider)
                return true;

            return PlayerSpawnHeightUtility.IsWalkableForSpawn(hit.collider) && hit.normal.y > 0.5f;
        }

        static void EnsureEmergencyFloor(Vector3 center, float surfaceY)
        {
            if (_sharedEmergencyFloor != null)
            {
                _sharedEmergencyFloor.transform.position = new Vector3(center.x, surfaceY - 0.55f, center.z);
                return;
            }

            _sharedEmergencyFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _sharedEmergencyFloor.name = "EmergencySpawnFloor";
            _sharedEmergencyFloor.hideFlags = HideFlags.DontSave;
            _sharedEmergencyFloor.transform.position = new Vector3(center.x, surfaceY - 0.55f, center.z);
            _sharedEmergencyFloor.transform.localScale = new Vector3(
                EmergencyFloorHalfExtentM * 2f,
                1f,
                EmergencyFloorHalfExtentM * 2f);

            var renderer = _sharedEmergencyFloor.GetComponent<Renderer>();
            if (renderer != null)
                renderer.enabled = false;
        }

        static void DestroyEmergencyFloorIfUnused()
        {
            if (_sharedEmergencyFloor == null)
                return;

            foreach (var hold in Object.FindObjectsByType<PlayerSpawnGroundHold>())
            {
                if (hold != null && !hold._released)
                    return;
            }

            Object.Destroy(_sharedEmergencyFloor);
            _sharedEmergencyFloor = null;
        }
    }
}
