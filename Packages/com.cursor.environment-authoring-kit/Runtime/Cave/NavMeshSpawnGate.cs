using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Defer or skip NavMeshAgent spawns until baked NavMesh data exists.</summary>
    public static class NavMeshSpawnGate
    {
        static bool _warnedOnce;

        public static bool HasNavMeshData()
        {
            var triangulation = NavMesh.CalculateTriangulation();
            return triangulation.vertices != null && triangulation.vertices.Length > 0;
        }

        public static bool CanPlaceAgent(Vector3 position, float maxDistance = 12f)
        {
            if (!HasNavMeshData())
                return false;

            return NavMesh.SamplePosition(position, out _, maxDistance, NavMesh.AllAreas);
        }

        /// <summary>Snap a position onto the nearest NavMesh point when one exists nearby.</summary>
        public static bool TrySnapToNavMesh(ref Vector3 position, float maxDistance = 12f)
        {
            if (!HasNavMeshData())
                return false;

            if (!NavMesh.SamplePosition(position, out var hit, maxDistance, NavMesh.AllAreas))
                return false;

            position = hit.position;
            return true;
        }

        public static void ApplyAgentOrDisable(NavMeshAgent agent, Vector3 position, float height, float radius)
        {
            if (agent == null)
                return;

            if (!CanPlaceAgent(position))
            {
                agent.enabled = false;
                return;
            }

            agent.enabled = true;
            agent.height = height;
            agent.radius = radius;
            agent.speed = 3.6f;
            agent.stoppingDistance = 1.2f;
            agent.autoBraking = true;
        }

        public static void WarnOnce(string tag, string message)
        {
            if (_warnedOnce)
                return;

            _warnedOnce = true;
            Debug.LogWarning($"[{tag}] {message}");
        }

        public static IEnumerator WaitUntilReadyOrTimeout(float timeoutSeconds, float pollInterval = 0.25f)
        {
            var elapsed = 0f;
            while (elapsed < timeoutSeconds)
            {
                if (HasNavMeshData())
                    yield break;

                elapsed += pollInterval;
                yield return new WaitForSeconds(pollInterval);
            }
        }
    }
}
