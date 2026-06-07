using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Jump pits and lava volumes: when the player enters (or falls below the walk floor), respawn on the surface main area.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CavePitFallRecovery : MonoBehaviour
    {
        public float cooldownSeconds = 2f;
        public bool respawnToMainArea = true;
        /// <summary>Walk floor Y in cave-root local space (from layout). NaN = trigger-only.</summary>
        public float killFloorLocalY = float.NaN;

        float _lastRecovery;
        Transform _trackedPlayer;

        void OnTriggerEnter(Collider other) => TryRecover(other);

        void OnTriggerStay(Collider other) => TryRecover(other);

        void Update()
        {
            if (_trackedPlayer == null || float.IsNaN(killFloorLocalY))
                return;

            if (Time.time - _lastRecovery < cooldownSeconds)
                return;

            var cave = CaveGeometryPaths.FindCaveSystemRoot();
            if (cave == null)
                return;

            var localY = cave.InverseTransformPoint(_trackedPlayer.position).y;
            if (localY > killFloorLocalY - 2.5f)
                return;

            RecoverPlayer(_trackedPlayer, "Fell below pit kill plane");
        }

        void TryRecover(Collider other)
        {
            var controller = other.GetComponentInParent<CharacterController>();
            if (controller == null && !other.CompareTag("Player"))
                return;

            var player = controller != null ? controller.transform : other.transform.root;
            _trackedPlayer = player;
            RecoverPlayer(player, "Pit trigger");
        }

        void RecoverPlayer(Transform player, string reason)
        {
            if (player == null || Time.time - _lastRecovery < cooldownSeconds)
                return;

            _lastRecovery = Time.time;

            if (respawnToMainArea && CaveMainAreaRespawn.TryRespawnPlayer(player, reason))
                return;

            if (!respawnToMainArea && CaveWalkableMarker.TryGetCheckpoint(out var pos, out var rot))
            {
                player.position = pos;
                player.rotation = rot;
                PlayerGroundSnap.SnapTransform(player, pos);
                Debug.Log("[CavePitFallRecovery] Respawned on last platform checkpoint.", this);
                return;
            }

            var spawn = CaveEntranceTeleport.ResolveSpawnPoint();
            if (spawn == null)
                return;

            CaveEntranceTeleport.TeleportPlayer(player, spawn);
            PlayerGroundSnap.SnapTransform(player, player.position);
            Debug.Log("[CavePitFallRecovery] Recovered player to cave entrance.", this);
        }
    }
}
