using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    static class CavePlayerMovementFixMenu
    {
        [MenuItem("Window/Environment Kit/Respawn Player At Cave Spawn (Play Mode)", true)]
        public static bool RespawnPlayModeValidate() => Application.isPlaying;

        [MenuItem("Window/Environment Kit/Respawn Player At Cave Spawn (Play Mode)")]
        public static void RespawnAtCaveSpawnPlayMode()
        {
            var spawn = CaveEntranceTeleport.ResolveSpawnPoint();
            var player = GameObject.FindGameObjectWithTag("Player");
            if (spawn == null || player == null)
            {
                Debug.LogWarning("[CaveBuild] Respawn failed — missing spawn or Player tag.");
                return;
            }

            CaveEntranceTeleport.TeleportPlayer(player.transform, spawn);
            Debug.Log($"[CaveBuild] Respawned player at {spawn.name} @ {spawn.position}");
        }

        [MenuItem("Window/Environment Kit/Fix Cave Player Movement (Active Scene)")]
        public static void FixFromMenu()
        {
            var caveRoot = GameObject.Find("Grid")?.transform.Find("LavaTubeCaveSystem");
            if (caveRoot == null)
                caveRoot = GameObject.Find("LavaTubeCaveSystem")?.transform;

            if (caveRoot == null)
            {
                EditorUtility.DisplayDialog(
                    "Fix Cave Player Movement",
                    "LavaTubeCaveSystem not found. Run Build Complete Cave Level first.",
                    "OK");
                return;
            }

            var spawn = CaveEntranceTeleport.ResolveSpawnPoint();
            if (spawn == null)
            {
                EditorUtility.DisplayDialog(
                    "Fix Cave Player Movement",
                    "CaveEntrance_SpawnPoint not found under the cave entrance.",
                    "OK");
                return;
            }

            CaveSpawnPadUtility.EnsureUnderSpawn(spawn, new Vector3(9f, 0.85f, 9f));

            var marker = spawn.GetComponent<CaveEntranceSpawnPoint>();
            if (marker == null)
                marker = spawn.gameObject.AddComponent<CaveEntranceSpawnPoint>();
            marker.snapPlayerOnStart = false;

            if (caveRoot.GetComponent<CavePlayerMovementGuard>() == null)
                caveRoot.gameObject.AddComponent<CavePlayerMovementGuard>();

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                PlayerCameraRig.Ensure(player.transform);

            CavePlayabilityFix.RunSilent(caveRoot);

            // compile_gate | arXiv:2503.05146 — single Player lookup; play-mode respawn reuses outer scope.
            if (Application.isPlaying && player != null)
            {
                CaveEntranceTeleport.TeleportPlayer(player.transform, spawn);
                CavePlayerMovementGuard.UnlockMovement(player.transform);
            }

            EditorUtility.SetDirty(caveRoot.gameObject);
            EditorUtility.DisplayDialog(
                "Fix Cave Player Movement",
                "Spawn pad, walkable markers, and movement guard updated.\n\n" +
                "Press Play while standing near the cave (or use the portal) to test movement.",
                "OK");
        }
    }
}
