using System.Collections;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.World;
using UnityEngine;

namespace Hub.Competition
{
    /// <summary>Drop-from-above spawn + snap when terrain colliders are ready.</summary>
    public static class GameplaySpawnGrounding
    {
        const int GroundingRetryFrames = 240;
        public static Transform ResolveSpawnAnchor()
        {
            var spawn = GameObject.Find("PlayerSpawnPoint");
            if (spawn != null)
                return spawn.transform;

            var marker = CaveMainAreaRespawn.ResolveSurfaceSpawn();
            return marker != null ? marker : null;
        }

        public static Vector3 ResolveHorizontalSpawn(Vector3 fallback)
        {
            var anchor = ResolveSpawnAnchor();
            return anchor != null ? anchor.position : fallback;
        }

        public static Transform ResolvePlayerTransform()
        {
            var go = WorldPlayerSetupRuntime.ResolvePlayerRoot();
            if (go != null)
                return go.transform;

            var demo = GameObject.Find("DemoPlayer");
            return demo != null ? demo.transform : null;
        }

        /// <summary>CharacterController for FPS movement; strips conflicting root colliders.</summary>
        public static void EnsurePlayableCollision(Transform root)
        {
            if (root == null)
                return;

            var go = root.gameObject;
            if (!WorldPlayerSetupRuntime.IsPlayableCharacter(go))
                return;

            PlayableHumanoidScale.Apply(root);

            var cc = go.GetComponent<CharacterController>();
            if (cc != null)
            {
                DisableEnvKitController(go);
                if (go.GetComponent<PlayerController>() == null)
                    go.AddComponent<PlayerController>();
                return;
            }

            foreach (var col in go.GetComponents<Collider>())
            {
                if (col is CharacterController)
                    continue;
                Object.Destroy(col);
            }

            foreach (var rb in go.GetComponents<Rigidbody>())
                Object.Destroy(rb);

            cc = go.AddComponent<CharacterController>();
            cc.height = PlayableHumanoidScale.Height;
            cc.radius = PlayableHumanoidScale.Radius;
            cc.center = new Vector3(0f, PlayableHumanoidScale.Height * 0.5f, 0f);
            cc.stepOffset = PlayableHumanoidScale.StepOffset;
            cc.skinWidth = 0.05f;

            if (go.GetComponent<PlayerController>() == null)
                go.AddComponent<PlayerController>();

            DisableEnvKitController(go);
        }

        static void DisableEnvKitController(GameObject go)
        {
            var envKit = go.GetComponent<EnvironmentAuthoringKit.Cave.PlayerController>();
            if (envKit != null)
                envKit.enabled = false;
        }

        public static void PlaceAboveGround(Transform root, float dropM = PlayerSpawnHeightUtility.DefaultDropHeightM)
        {
            if (root == null)
                return;

            EnsurePlayableCollision(root);
            EnsureSpawnHold(root);

            var cc = root.GetComponent<CharacterController>();
            var near = root.position;
            if (near.sqrMagnitude < 0.01f)
                near = ResolveHorizontalSpawn(Vector3.zero);

            var dropPos = PlayerSpawnHeightUtility.ResolveDropSpawnPosition(near, cc, dropM);
            var wasEnabled = cc != null && cc.enabled;
            if (cc != null)
                cc.enabled = false;

            root.position = dropPos;
            Physics.SyncTransforms();

            if (cc != null && !PlayerSpawnGroundHold.IsActiveOn(root))
                cc.enabled = wasEnabled;

            SnapHard(root, 6);
        }

        public static void EnsureSpawnHold(Transform root)
        {
            if (root == null || WorldUiBootstrapGate.SuppressDuringTitleMenu)
                return;

            PlayerSpawnGroundHold.Ensure(root);
        }

        public static IEnumerator PlaceAboveGroundWhenReady(Transform root, float dropM = PlayerSpawnHeightUtility.DefaultDropHeightM)
        {
            if (root == null)
                yield break;

            EnsurePlayableCollision(root);
            EnsureSpawnHold(root);

            for (var i = 0; i < GroundingRetryFrames; i++)
            {
                PlaceAboveGround(root, dropM);
                if (HasWalkableSupport(root))
                    yield break;

                yield return null;
            }

            PlaceAboveGround(root, dropM);
        }

        static bool HasWalkableSupport(Transform root)
        {
            if (root == null)
                return false;

            var cc = root.GetComponent<CharacterController>();
            if (PlayerSpawnGroundHold.HasPhysicsWalkableGround(root.position, cc))
                return true;

            return !PlayerSpawnGroundHold.IsActiveOn(root);
        }

        public static void SnapHard(Transform root, int passes = 3)
        {
            if (root == null)
                return;

            for (var i = 0; i < passes; i++)
            {
                Physics.SyncTransforms();
                PlayerGroundSnap.SnapTransform(root, root.position);
            }
        }

        public static void PreparePlayerAndAgent()
        {
            var player = ResolvePlayerTransform();
            if (player != null)
            {
                EnsurePlayableCollision(player);
                PlaceAboveGround(player);
            }

            var agent = CompetitionAgentNetworkRegistry.LocalPawn != null
                ? CompetitionAgentNetworkRegistry.LocalPawn.transform
                : CompetitionAgentPawn.Active != null
                    ? CompetitionAgentPawn.Active.transform
                    : null;

            if (agent != null)
                PlaceAboveGround(agent, PlayerSpawnHeightUtility.DefaultDropHeightM - 0.5f);
        }
    }
}
