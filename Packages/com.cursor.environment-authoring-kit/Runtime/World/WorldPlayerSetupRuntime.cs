using EnvironmentAuthoringKit.Cave;
using UnityEngine;
using UnityEngine.AI;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Runtime player discovery, spawn, and component wiring (Play mode + builds).</summary>
    public static class WorldPlayerSetupRuntime
    {
        public const string PlayerTag = "Player";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootstrapAfterSceneLoad()
        {
            if (!Application.isPlaying)
                return;

            SchedulePostStartRewire();
        }

        static void SchedulePostStartRewire()
        {
            var go = new GameObject("WorldPlayerMovementBootstrap");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<WorldPlayerMovementBootstrap>();
        }

        public static bool WireScenePlayer()
        {
            if (WorldUiBootstrapGate.SuppressDuringTitleMenu)
                return false;

            SanitizeMisTaggedPlayers();

            var player = ResolvePlayerRoot();
            if (player == null)
                player = CreatePlayerAtSurfaceSpawn();

            if (player == null)
            {
                Debug.LogWarning(
                    "[World] No player found — add PlayerSpawnPoint or tag a CharacterController root as Player.");
                return false;
            }

            EnsurePlayerTag(player);
            EnsurePlayerController(player);

            if (player.GetComponent<PlayerPersistence>() == null)
                player.AddComponent<PlayerPersistence>();

            if (player.GetComponent<WorldEconomyHud>() == null)
                player.AddComponent<WorldEconomyHud>();

            PlayerCameraRig.Ensure(player.transform);
            return true;
        }

        public static GameObject ResolvePlayerRoot()
        {
            GameObject tagged;
            try
            {
                tagged = GameObject.FindGameObjectWithTag(PlayerTag);
            }
            catch (UnityException)
            {
                tagged = null;
            }

            if (tagged != null && IsPlayableCharacter(tagged))
                return tagged;

            foreach (var controller in Object.FindObjectsByType<PlayerController>())
            {
                if (controller != null && IsPlayableCharacter(controller.gameObject))
                    return controller.gameObject;
            }

            var spawn = GameObject.Find("PlayerSpawnPoint");
            if (spawn != null && spawn.transform.childCount > 0)
            {
                var child = spawn.transform.GetChild(0).gameObject;
                if (IsPlayableCharacter(child))
                    return child;
            }

            foreach (var cc in Object.FindObjectsByType<CharacterController>())
            {
                if (cc != null && IsPlayableCharacter(cc.gameObject))
                    return cc.gameObject;
            }

            return null;
        }

        static void EnsurePlayerController(GameObject player)
        {
            if (player.GetComponent<PlayerController>() != null)
                return;

            player.AddComponent<PlayerController>();
            Debug.Log("[World] Added PlayerController to " + player.name, player);
        }

        static GameObject CreatePlayerAtSurfaceSpawn()
        {
            var marker = CaveMainAreaRespawn.ResolveSurfaceSpawn();
            var pos = marker != null ? marker.position : new Vector3(0f, 1.2f, 0f);
            var rot = marker != null ? marker.rotation : Quaternion.identity;

            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Player";
            go.transform.SetPositionAndRotation(pos, rot);

            var primitiveCollider = go.GetComponent<Collider>();
            if (primitiveCollider != null)
                Object.Destroy(primitiveCollider);

            HumanoidDimensions.Resolve(out var height, out var radius, out var stepOffset);
            go.transform.localScale = HumanoidDimensions.CapsuleLocalScale(height, radius);

            var cc = go.AddComponent<CharacterController>();
            cc.height = height;
            cc.radius = radius;
            cc.center = new Vector3(0f, height * 0.5f, 0f);
            cc.stepOffset = stepOffset;
            cc.skinWidth = 0.05f;

            go.AddComponent<PlayerController>();
            EnsurePlayerTag(go);
            var grounded = PlayerSpawnHeightUtility.ResolveSpawnMarkerPosition(pos, cc);
            go.transform.position = grounded;
            PlayerGroundSnap.SnapTransform(go.transform, grounded);

            Debug.Log("[World] Created play character at surface spawn.", go);
            return go;
        }

        static void SanitizeMisTaggedPlayers()
        {
            GameObject[] tagged;
            try
            {
                tagged = GameObject.FindGameObjectsWithTag(PlayerTag);
            }
            catch (UnityException)
            {
                return;
            }

            foreach (var go in tagged)
            {
                if (go != null && !IsPlayableCharacter(go))
                    go.tag = "Untagged";
            }
        }

        static void EnsurePlayerTag(GameObject player)
        {
            if (player.CompareTag(PlayerTag))
                return;

            try
            {
                player.tag = PlayerTag;
            }
            catch (UnityException)
            {
                Debug.LogWarning(
                    "[World] Tag 'Player' is missing — add it under Project Settings → Tags and Layers.");
            }
        }

        public static bool IsPlayableCharacter(GameObject go)
        {
            if (go == null)
                return false;

            if (IsCompetitionAgent(go))
                return false;

            if (go.GetComponent<CavePlaytestBotMarker>() != null)
                return false;
            if (go.GetComponent<HollowTitanPatrolAgent>() != null)
                return false;
            if (go.GetComponent<HollowTitanFloorGuardBehavior>() != null)
                return false;
            if (go.GetComponent<CaveSpawnedEnemyAdapter>() != null)
                return false;
            if (go.GetComponent<NavMeshAgent>() != null && go.GetComponent<PlayerController>() == null)
                return false;
            if (go.name.StartsWith("HollowTitan_") && go.GetComponent<PlayerController>() == null)
                return false;

            return true;
        }

        static bool IsCompetitionAgent(GameObject go)
        {
            if (go.name == "CompetitionAgentPawn")
                return true;

            foreach (var mb in go.GetComponents<MonoBehaviour>())
            {
                if (mb == null)
                    continue;

                var typeName = mb.GetType().Name;
                if (typeName is "CompetitionAgentPawn" or "CompetitionAgentMarker")
                    return true;
            }

            return false;
        }
    }

    /// <summary>Re-wires the play character after scene Start() so runtime spawns cannot steal Player tag.</summary>
    sealed class WorldPlayerMovementBootstrap : MonoBehaviour
    {
        void Start()
        {
            if (!WorldUiBootstrapGate.SuppressDuringTitleMenu)
                WorldPlayerSetupRuntime.WireScenePlayer();

            var player = WorldPlayerSetupRuntime.ResolvePlayerRoot();
            if (player != null)
            {
                PlayerGroundSnap.SnapTransform(player.transform, player.transform.position);
                CavePlayerMovementGuard.UnlockMovement(player.transform);
            }

            Destroy(gameObject);
        }
    }
}
