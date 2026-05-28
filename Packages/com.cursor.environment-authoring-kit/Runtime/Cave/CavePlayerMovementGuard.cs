using System.Reflection;
using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Keeps the player on the cave spawn pad with CharacterController enabled and movement unlocked.
    /// Attached to LavaTubeCaveSystem during cave build.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CavePlayerMovementGuard : MonoBehaviour
    {
        public bool snapNearbyPlayerOnPlay;
        public float snapRadius = 90f;

        float _nextSafetyCheck;

        void Start()
        {
            var spawn = CaveEntranceTeleport.ResolveSpawnPoint();
            if (spawn == null)
                return;

            var spawnPoint = spawn.GetComponent<CaveEntranceSpawnPoint>();
            if (spawnPoint == null)
                spawnPoint = spawn.gameObject.AddComponent<CaveEntranceSpawnPoint>();

            if (spawnPoint.snapPlayerOnStart)
                TeleportAndUnlock(spawn);
            else if (snapNearbyPlayerOnPlay && ShouldSnapPlayer(spawn))
                TeleportAndUnlock(spawn);

            _nextSafetyCheck = Time.time + 1.5f;
        }

        void LateUpdate()
        {
            if (Time.time < _nextSafetyCheck)
                return;

            _nextSafetyCheck = Time.time + 2f;
            var spawn = CaveEntranceTeleport.ResolveSpawnPoint();
            if (spawn == null)
                return;

            var player = ResolvePlayerRoot();
            if (player == null)
                return;

            UnlockMovement(player);

            var cc = player.GetComponent<CharacterController>();
            if (cc == null || !cc.enabled)
                return;

            if (!cc.isGrounded && HorizontalDistance(player.position, spawn.position) < snapRadius)
                PlayerGroundSnap.SnapTransform(player, player.position);
        }

        bool ShouldSnapPlayer(Transform spawn)
        {
            if (!snapNearbyPlayerOnPlay)
                return false;

            var player = ResolvePlayerRoot();
            if (player == null)
                return false;

            return HorizontalDistance(player.position, spawn.position) < snapRadius &&
                   player.position.y < spawn.position.y + 25f;
        }

        void TeleportAndUnlock(Transform spawn)
        {
            var player = ResolvePlayerRoot();
            if (player == null)
                return;

            CaveEntranceTeleport.TeleportPlayer(player, spawn);
            UnlockMovement(player);
        }

        public static void UnlockMovement(Transform playerRoot)
        {
            if (playerRoot == null || IsWarpPlaying())
                return;

            var cc = playerRoot.GetComponent<CharacterController>();
            if (cc != null && !cc.enabled)
                cc.enabled = true;

            SetPlayerControllerFlags(playerRoot, introActive: false, dialogActive: false);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        static void SetPlayerControllerFlags(Transform playerRoot, bool introActive, bool dialogActive)
        {
            foreach (var mb in playerRoot.GetComponents<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != "PlayerController")
                    continue;

                var t = mb.GetType();
                var intro = t.GetField("introActive", BindingFlags.Instance | BindingFlags.Public);
                var dialog = t.GetField("dialogActive", BindingFlags.Instance | BindingFlags.Public);
                intro?.SetValue(mb, introActive);
                dialog?.SetValue(mb, dialogActive);
                return;
            }
        }

        static bool IsWarpPlaying()
        {
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != "CaveWarpTransition")
                    continue;

                var prop = mb.GetType().GetProperty("IsPlaying", BindingFlags.Instance | BindingFlags.Public);
                return prop != null && prop.GetValue(mb) is true;
            }

            return false;
        }

        static Transform ResolvePlayerRoot()
        {
            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null)
                return tagged.transform;

            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>())
            {
                if (mb != null && mb.GetType().Name == "PlayerController")
                    return mb.transform;
            }

            return null;
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
