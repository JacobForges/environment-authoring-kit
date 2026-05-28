using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Marks the cave entrance teleport destination (MainScene: CaveEntrance_SpawnPoint).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CaveEntranceSpawnPoint : MonoBehaviour
    {
        public Vector3 positionOffset = Vector3.zero;
        public bool applyRotation = true;

        [Tooltip("Snap player to floor on Start (helps test in-editor without portal).")]
        public bool snapPlayerOnStart;

        [Tooltip("When true, MainScene portal teleport targets maze route start (set by cave build).")]
        public bool teleportFromMainAreaUsesMazeStart = true;

        public Vector3 SpawnPosition => transform.position + positionOffset;
        public Quaternion SpawnRotation => applyRotation ? transform.rotation : Quaternion.identity;

        void Start()
        {
            if (!snapPlayerOnStart)
                return;

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
                return;

            CaveEntranceTeleport.TeleportPlayer(player.transform, transform);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.45f, 0.9f);
            Gizmos.DrawWireSphere(SpawnPosition, 0.65f);
            Gizmos.DrawLine(transform.position, SpawnPosition);
            Gizmos.DrawRay(SpawnPosition, transform.forward * 1.5f);
        }
    }
}
