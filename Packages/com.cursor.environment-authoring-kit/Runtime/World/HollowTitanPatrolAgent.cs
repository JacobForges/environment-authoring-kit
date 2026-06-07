using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Landmark-scoped patrol loop between floor waypoints (NavMesh when available).</summary>
    public sealed class HollowTitanPatrolAgent : MonoBehaviour
    {
        [SerializeField] float moveSpeed = 2.2f;
        [SerializeField] float waypointPauseSeconds = 0.75f;
        [SerializeField] float arriveDistance = 1.1f;

        readonly List<Vector3> _waypoints = new();
        NavMeshAgent _agent;
        int _index;
        float _pauseUntil;

        public void Configure(IReadOnlyList<Vector3> waypoints)
        {
            _waypoints.Clear();
            if (waypoints != null)
            {
                foreach (var wp in waypoints)
                    _waypoints.Add(wp);
            }

            _index = 0;
            _agent = GetComponent<NavMeshAgent>();
            if (_agent != null)
            {
                _agent.stoppingDistance = arriveDistance * 0.85f;
                _agent.speed = moveSpeed;
            }
        }

        void Update()
        {
            if (_waypoints.Count == 0 || Time.time < _pauseUntil)
                return;

            var target = _waypoints[_index];
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.SetDestination(target);
                if (!_agent.pathPending && _agent.remainingDistance <= arriveDistance)
                    Advance();
                return;
            }

            var pos = transform.position;
            var next = Vector3.MoveTowards(pos, target, moveSpeed * Time.deltaTime);
            transform.position = next;
            var flat = next - target;
            flat.y = 0f;
            if (flat.sqrMagnitude <= arriveDistance * arriveDistance)
                Advance();
        }

        void Advance()
        {
            _pauseUntil = Time.time + waypointPauseSeconds;
            _index = (_index + 1) % _waypoints.Count;
        }
    }
}
