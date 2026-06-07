using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Human-like NavMesh playtest: surface trails → cave mouth → underground knots; reports every snag.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CavePlaytestRouteBot : MonoBehaviour
    {
        public float moveSpeed = 3.8f;
        public float stuckDistanceThreshold = 0.2f;
        public bool useVisibleBotAvatar = true;
        public bool enableSpectatorCamera = true;
        [Tooltip("Optional animated character prefab — defaults to player-sized capsule.")]
        public GameObject playtestBotPrefab;

        readonly List<string> _playIssues = new();
        bool _running;

        public IReadOnlyList<string> PlayIssues => _playIssues;

        public void BeginRouteWalk()
        {
            if (_running)
                return;
            StartCoroutine(WalkSurfaceThenCaveRoute());
        }

        IEnumerator WalkSurfaceThenCaveRoute()
        {
            _running = true;
            _playIssues.Clear();

            yield return WalkHumanNavRoute(
                SurfacePlaytestRouteCollector.CollectWorldWaypoints(transform),
                "surface_human");

            var authoring = GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
            {
                _playIssues.Add("[path] Missing CaveSplinePathAuthoring knots.");
                FinishWalk();
                yield break;
            }

            var knotGoals = new List<Vector3>();
            foreach (var knot in authoring.Knots)
                knotGoals.Add(transform.TransformPoint(knot.Position));

            yield return WalkHumanNavRoute(knotGoals, "cave_human");
            yield return TestAllPits(ResolveWalker(out var avatar), avatar);
            FinishWalk();
        }

        IEnumerator WalkHumanNavRoute(IReadOnlyList<Vector3> goals, string stage)
        {
            if (goals == null || goals.Count < 2)
            {
                _playIssues.Add($"[terrain_integration] {stage} route too short — build trails and openings first.");
                yield break;
            }

            var avatar = useVisibleBotAvatar
                ? CavePlaytestBotAvatar.Ensure(transform, visible: true, playtestBotPrefab)
                : null;
            var walker = avatar != null ? avatar.transform : ResolvePlayer();
            if (walker == null)
            {
                _playIssues.Add($"[terrain_integration] No walker for {stage}.");
                yield break;
            }

            if (avatar != null && enableSpectatorCamera)
                CavePlaytestBotSpectator.Ensure(walker, enable: true);

            if (avatar == null)
                CavePlayerMovementGuard.UnlockMovement(walker);

            var corners = CavePlaytestNavWalker.BuildPatrolAlongPoints(goals, _playIssues, stage);
            if (corners.Count == 0)
            {
                _playIssues.Add($"[{stage}] NavMesh patrol empty — bake surface/cave NavMesh.");
                yield break;
            }

            yield return CavePlaytestNavWalker.WalkCorners(
                walker,
                avatar,
                corners,
                moveSpeed,
                stuckDistanceThreshold,
                _playIssues,
                stage);

            if (stage == "surface_human")
                Debug.Log("[CavePlaytestBot] Surface NavMesh walk complete — entering cave route.");
        }

        IEnumerator TestAllPits(Transform walker, CavePlaytestBotAvatar avatar)
        {
            if (walker == null)
                yield break;

            var features = transform.Find("AdventureFeatures");
            if (features == null)
                yield break;

            foreach (Transform gap in features)
            {
                if (gap == null || !gap.name.StartsWith("JumpGap_"))
                    continue;

                var pit = gap.Find("Pit_Lava");
                if (pit == null)
                {
                    _playIssues.Add($"[path] {gap.name} missing Pit_Lava.");
                    continue;
                }

                if (avatar != null)
                    avatar.TeleportTo(pit.position);
                else
                    walker.position = pit.position;
                yield return new WaitForSeconds(0.7f);

                if (!IsOnSurface(walker))
                {
                    _playIssues.Add(
                        $"[player_floor] {gap.name} did not respawn bot to surface main area.");
                }

                yield return new WaitForSeconds(0.35f);
            }
        }

        Transform ResolveWalker(out CavePlaytestBotAvatar avatar)
        {
            avatar = useVisibleBotAvatar
                ? CavePlaytestBotAvatar.Ensure(transform, visible: true, playtestBotPrefab)
                : null;
            return avatar != null ? avatar.transform : ResolvePlayer();
        }

        void FinishWalk()
        {
            CavePlaytestIssueReport.Export(_playIssues, "play_mode_human_walk");
            Debug.Log(
                _playIssues.Count == 0
                    ? "[CavePlaytestBot] Human NavMesh route PASS."
                    : $"[CavePlaytestBot] Human walk found {_playIssues.Count} issue(s).",
                this);
            _running = false;
        }

        static bool IsOnSurface(Transform player)
        {
            if (CaveMainAreaRespawn.ResolveSurfaceSpawn() == null)
                return false;

            var cave = CaveGeometryPaths.FindCaveSystemRoot();
            return cave == null || !player.IsChildOf(cave);
        }

        static Transform ResolvePlayer()
        {
            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null)
                return tagged.transform;

            var controller = Object.FindAnyObjectByType<CharacterController>();
            if (controller != null && controller.GetComponentInParent<CavePlaytestBotMarker>() == null)
                return controller.transform;
            return null;
        }
    }
}
