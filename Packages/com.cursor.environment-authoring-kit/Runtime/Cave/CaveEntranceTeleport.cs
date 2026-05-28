using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    public static class CaveEntranceTeleport
    {
        public const string SpawnPointObjectName = "CaveEntrance_SpawnPoint";
        public const string EntranceMarkerObjectName = "CaveEntrance_Marker";
        public const string CaveSystemObjectName = CaveGeometryPaths.CaveSystemRootName;

        public static Transform ResolveSpawnPoint(Transform explicitSpawn = null)
        {
            if (explicitSpawn != null)
                return explicitSpawn;

            var caveRoot = GameObject.Find(CaveSystemObjectName);
            if (caveRoot != null)
            {
                var spawn = caveRoot.transform.Find($"Entrance/{SpawnPointObjectName}");
                if (spawn != null)
                    return spawn;

                foreach (var t in caveRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == SpawnPointObjectName && t.GetComponent<CaveEntranceSpawnPoint>() != null)
                        return t;
                }

            }

            var registry = CaveSystemRegistry.Instance;
            if (registry?.entrance != null)
            {
                var underEntrance = registry.entrance.Find(SpawnPointObjectName);
                if (underEntrance != null)
                    return underEntrance;
            }

            var byName = GameObject.Find(SpawnPointObjectName);
            if (byName != null)
            {
                var walk = byName.transform.parent;
                while (walk != null)
                {
                    if (walk.name == CaveSystemObjectName)
                        return byName.transform;
                    walk = walk.parent;
                }
            }

            return null;
        }

        public static bool TeleportPlayer(Transform playerRoot, Transform spawnPoint)
        {
            if (playerRoot == null || spawnPoint == null)
                return false;

            var spawnMarker = spawnPoint.GetComponent<CaveEntranceSpawnPoint>();
            var position = spawnMarker != null ? spawnMarker.SpawnPosition : spawnPoint.position;
            var rotation = spawnMarker != null ? spawnMarker.SpawnRotation : spawnPoint.rotation;

            var controller = playerRoot.GetComponent<CharacterController>();
            var body = playerRoot.GetComponent<Rigidbody>();

            // Disable both before moving so nothing tries to integrate motion mid-teleport.
            if (controller != null)
                controller.enabled = false;
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.Sleep();
            }

            playerRoot.SetPositionAndRotation(position, rotation);
            playerRoot.rotation = rotation;
            Physics.SyncTransforms();

            // Snap onto walk floor / spawn pad only (never the outer shell floor at the box bottom).
            if (controller != null)
                controller.enabled = false;

            if (!PlayerGroundSnap.SnapTransform(playerRoot, playerRoot.position))
                TryPlaceOnSpawnPadTop(playerRoot, spawnPoint);

            Physics.SyncTransforms();

            if (controller != null)
            {
                controller.enabled = true;
                // Do NOT call SimpleMove here — it advances the controller by Time.deltaTime
                // using whatever velocity was last applied, which causes the "glide backward".
            }

            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }

            CavePlayerMovementGuard.UnlockMovement(playerRoot);
            ForceCaveAtmosphereOnTeleport();
            return true;
        }

        static void TryPlaceOnSpawnPadTop(Transform playerRoot, Transform spawnPoint)
        {
            if (playerRoot == null || spawnPoint == null)
                return;

            var pad = spawnPoint.Find(CaveSpawnPadUtility.PadName);
            if (pad == null)
                return;

            var top = pad.position.y + pad.lossyScale.y * 0.5f;
            var controller = playerRoot.GetComponent<CharacterController>();
            var y = controller != null
                ? top + controller.height * 0.5f + controller.skinWidth + 0.06f
                : top + 0.2f;
            playerRoot.position = new Vector3(spawnPoint.position.x, y, spawnPoint.position.z);
        }

        /// <summary>
        /// Applies cave camera background + fog immediately after teleport so the player
        /// never sees the surface skybox through the cave entrance.
        /// </summary>
        static void ForceCaveAtmosphereOnTeleport()
        {
            var caveRoot = GameObject.Find(CaveSystemObjectName);
            if (caveRoot == null)
                return;

            var atmosphere = caveRoot.GetComponentInChildren<CaveUndergroundAtmosphere>(true);
            if (atmosphere != null)
                atmosphere.ForceUndergroundLook();
        }
    }
}
