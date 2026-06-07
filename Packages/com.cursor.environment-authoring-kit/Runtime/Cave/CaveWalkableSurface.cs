using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Marks walkable floor colliders so snap/teleport never lands on outer shell or pit bottoms.</summary>
    public sealed class CaveWalkableMarker : MonoBehaviour
    {
        public static Transform LastSafePlatform { get; private set; }

        void OnTriggerEnter(Collider other) => TryMark(other);
        void OnCollisionEnter(Collision collision) => TryMark(collision.collider);

        void TryMark(Collider other)
        {
            if (other == null)
                return;
            if (other.CompareTag("Player") || other.GetComponentInParent<CharacterController>() != null)
                LastSafePlatform = transform;
        }

        public static bool TryGetCheckpoint(out Vector3 worldPos, out Quaternion worldRot)
        {
            worldPos = default;
            worldRot = Quaternion.identity;
            if (LastSafePlatform == null)
                return false;

            worldPos = LastSafePlatform.position + Vector3.up * 1.1f;
            worldRot = LastSafePlatform.rotation;
            return true;
        }
    }

    public static class CaveWalkableSurface
    {
        public static bool IsWalkableCollider(Collider collider)
        {
            if (collider == null || collider.isTrigger)
                return false;

            var go = collider.gameObject;
            if (go.GetComponentInParent<CaveWalkableMarker>() != null)
                return true;

            var n = go.name;
            if (n.StartsWith(CaveWalkFloorPrefix) || n == "SpawnGroundPad" || n.StartsWith("Ledge_"))
                return true;

            if (n.Contains("SpawnGroundPad") || n.Contains("Entrance_Floor"))
                return true;

            if (n.StartsWith("Outer_") || n.StartsWith("RockFill_") || n.Contains("Pit_") ||
                n.Contains("Ceiling") || n.StartsWith("Wall") || n.StartsWith("CaveBlock_"))
                return false;

            if (n.Contains("RouteTerrainFloor") || n.Contains("LayoutWalkFloor"))
                return true;

            if (n.Contains("Floor") || n.Contains("Entrance_Floor") || n.Contains("Cavern_Floor"))
                return true;

            return false;
        }

        /// <summary>Matches walkway object prefix (duplicated here so runtime does not depend on editor).</summary>
        public const string CaveWalkFloorPrefix = "WalkFloor_";
    }
}
