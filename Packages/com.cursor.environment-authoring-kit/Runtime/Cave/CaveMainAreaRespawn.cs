using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Teleports the player to the surface main play area (not underground cave spawn).</summary>
    public static class CaveMainAreaRespawn
    {
        public static bool TryRespawnPlayer(Transform playerRoot, string logReason = null)
        {
            if (playerRoot == null)
                return false;

            var marker = ResolveSurfaceSpawn();
            if (marker == null)
                return false;

            var target = marker.position;
            playerRoot.position = target;
            playerRoot.rotation = marker.rotation;
            PlayerGroundSnap.SnapTransform(playerRoot, target);
            CavePlayerMovementGuard.UnlockMovement(playerRoot);

            if (!string.IsNullOrEmpty(logReason))
                Debug.Log($"[CaveMainAreaRespawn] {logReason} → {marker.name} @ {target}", marker);

            return true;
        }

        public static Transform ResolveSurfaceSpawn()
        {
            var tagged = GameObject.FindWithTag("PlayerSpawn");
            if (tagged != null && !IsUnderCaveSystem(tagged.transform))
                return tagged.transform;

            var byName = GameObject.Find("PlayerSpawnPoint");
            if (byName != null && !IsUnderCaveSystem(byName.transform))
                return byName.transform;

            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || IsUnderCaveSystem(root.transform))
                    continue;
                if (root.name.Contains("PlayerSpawn") || root.CompareTag("PlayerSpawn"))
                    return root.transform;
            }

            return null;
        }

        static bool IsUnderCaveSystem(Transform t)
        {
            if (t == null)
                return false;

            var cave = CaveGeometryPaths.FindCaveSystemRoot();
            return cave != null && t.IsChildOf(cave);
        }
    }
}
