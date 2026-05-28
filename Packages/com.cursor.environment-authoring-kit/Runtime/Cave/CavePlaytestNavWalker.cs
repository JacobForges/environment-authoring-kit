using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Human-like NavMesh walking for playtest bot — reports snags, slopes, off-mesh teleports.</summary>
    public static class CavePlaytestNavWalker
    {
        public const float SampleRadius = 4f;
        public const float MaxSlopeDegrees = 42f;

        public static bool TrySampleNavPosition(Vector3 near, out Vector3 onNav)
        {
            onNav = near;
            if (!NavMesh.SamplePosition(near, out var hit, SampleRadius, NavMesh.AllAreas))
            {
                return false;
            }

            onNav = hit.position;
            return true;
        }

        public static List<Vector3> BuildNavPath(Vector3 from, Vector3 to, List<string> issues, string stage)
        {
            var corners = new List<Vector3>();
            if (!TrySampleNavPosition(from, out var start))
            {
                issues?.Add($"[{stage}] Off NavMesh at start {from.x:F0},{from.z:F0} — terrain gap or missing bake.");
                start = from;
            }

            if (!TrySampleNavPosition(to, out var end))
            {
                issues?.Add($"[{stage}] Off NavMesh at goal {to.x:F0},{to.z:F0}.");
                end = to;
            }

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete)
            {
                issues?.Add($"[{stage}] NavMesh path incomplete ({path.status}) — layout blocked or disconnected.");
                corners.Add(start);
                corners.Add(end);
                return corners;
            }

            for (var i = 0; i < path.corners.Length; i++)
                corners.Add(path.corners[i]);
            return corners;
        }

        public static List<Vector3> BuildPatrolAlongPoints(
            IReadOnlyList<Vector3> goals,
            List<string> issues,
            string stage)
        {
            var route = new List<Vector3>();
            if (goals == null || goals.Count == 0)
                return route;

            Vector3? last = null;
            foreach (var goal in goals)
            {
                if (!last.HasValue)
                {
                    if (TrySampleNavPosition(goal, out var first))
                        route.Add(first);
                    else
                        issues?.Add($"[{stage}] Patrol start off NavMesh at {goal.x:F0},{goal.z:F0}.");
                    last = goal;
                    continue;
                }

                var segment = BuildNavPath(last.Value, goal, issues, stage);
                for (var i = 0; i < segment.Count; i++)
                {
                    if (route.Count == 0 || Vector3.Distance(route[route.Count - 1], segment[i]) > 0.35f)
                        route.Add(segment[i]);
                }

                last = goal;
            }

            return route;
        }

        public static IEnumerator WalkCorners(
            Transform walker,
            CavePlaytestBotAvatar avatar,
            IReadOnlyList<Vector3> corners,
            float moveSpeed,
            float stuckThreshold,
            List<string> issues,
            string stage)
        {
            if (walker == null || corners == null || corners.Count == 0)
                yield break;

            CavePlaytestBotScale.GetDimensions(walker, out var height, out _);
            var eye = height * 0.58f;

            for (var c = 0; c < corners.Count; c++)
            {
                var target = corners[c] + Vector3.up * eye;
                var start = walker.position;
                var elapsed = 0f;
                var maxSeconds = 12f;

                while (elapsed < maxSeconds && Vector3.Distance(walker.position, target) > 0.45f)
                {
                    if (avatar != null && avatar.BotController != null)
                        avatar.BotController.MoveTowardGoal(target, allowRun: true);
                    else if (avatar != null)
                        avatar.MoveToward(target, moveSpeed);
                    else
                        walker.position = Vector3.MoveTowards(walker.position, target, moveSpeed * Time.deltaTime);

                    ProbeStep(walker.position, issues, stage, c);
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                if (Vector3.Distance(start, walker.position) < stuckThreshold)
                {
                    issues?.Add(
                        $"[{stage}] Human tester stuck at corner {c}/{corners.Count - 1} " +
                        $"({walker.position.x:F1},{walker.position.z:F1}) — snag or bad layout.");
                }

                yield return null;
            }
        }

        static void ProbeStep(Vector3 world, List<string> issues, string stage, int cornerIndex)
        {
            if (!NavMesh.SamplePosition(world, out var hit, 1.2f, NavMesh.AllAreas))
            {
                issues?.Add($"[{stage}] Left walkable mesh at corner {cornerIndex} — improper terrain or missing NavMesh.");
                return;
            }

            var slope = Vector3.Angle(hit.normal, Vector3.up);
            if (slope > MaxSlopeDegrees && cornerIndex % 3 == 0)
            {
                issues?.Add(
                    $"[{stage}] Steep slope {slope:F0}° at ({world.x:F0},{world.z:F0}) — polish terrain or fix trail bench.");
            }

            var minHead = CaveThirdPersonClearance.ResolveMinWalkClearance();
            if (Physics.Raycast(world + Vector3.up * 0.2f, Vector3.up, out var ceiling, minHead + 6f))
            {
                if (ceiling.collider != null &&
                    ceiling.collider.GetComponentInParent<CavePlaytestBotMarker>() == null &&
                    ceiling.distance < minHead * 0.55f)
                {
                    issues?.Add(
                        $"[{stage}] Low ceiling {ceiling.distance:F1}m (need ~{minHead:F1}m TPS) at ({world.x:F0},{world.z:F0}).");
                }
            }
        }
    }
}
