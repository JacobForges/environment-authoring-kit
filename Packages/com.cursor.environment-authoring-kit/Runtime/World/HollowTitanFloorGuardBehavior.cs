using UnityEngine;
using UnityEngine.AI;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>
    /// Floor-platform guard — telegraphed knockback shove toward stair rushers; limited DPS, recovery windows.
    /// Does not camp the spiral column permanently.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HollowTitanFloorGuardBehavior : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField] float detectRangeMeters = 14f;
        [SerializeField] float shoveRangeMeters = 3.4f;

        [Header("Shove")]
        [SerializeField] float shoveWindUpSeconds = 0.9f;
        [SerializeField] float shoveForce = 9.5f;
        [SerializeField] float shoveCooldownSeconds = 2.8f;
        [SerializeField] float shoveRecoverySeconds = 1.1f;

        [Header("Patrol")]
        [SerializeField] float patrolRadiusMeters = 5.5f;
        [SerializeField] float patrolSpeed = 2.4f;

        [Header("Damage")]
        [SerializeField] float maxDamagePerSecond = 4f;

        enum GuardState
        {
            Patrol,
            WindUp,
            Shove,
            Recover,
        }

        GuardState _state = GuardState.Patrol;
        Transform _player;
        NavMeshAgent _agent;
        Vector3 _home;
        Vector3 _patrolTarget;
        float _stateUntil;
        float _nextPatrolPick;
        float _damageAccumulator;

        public void Configure(Vector3 homePosition, float patrolRadius)
        {
            _home = homePosition;
            patrolRadiusMeters = Mathf.Max(2f, patrolRadius);
            transform.position = homePosition;
            _patrolTarget = homePosition;
        }

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _home = transform.position;
            if (!CanUseNavMeshAgent())
                return;

            _agent.stoppingDistance = 1.4f;
            _agent.speed = patrolSpeed;
        }

        void Update()
        {
            ResolvePlayer();
            switch (_state)
            {
                case GuardState.Patrol:
                    UpdatePatrol();
                    TryBeginWindUp();
                    break;
                case GuardState.WindUp:
                    FacePlayer();
                    if (Time.time >= _stateUntil)
                        ExecuteShove();
                    break;
                case GuardState.Shove:
                    if (Time.time >= _stateUntil)
                        EnterRecover();
                    break;
                case GuardState.Recover:
                    UpdatePatrol();
                    if (Time.time >= _stateUntil)
                        _state = GuardState.Patrol;
                    break;
            }

            ApplyLimitedDamage();
        }

        void ResolvePlayer()
        {
            if (_player != null)
                return;

            var root = WorldPlayerSetupRuntime.ResolvePlayerRoot();
            if (root != null)
                _player = root.transform;
        }

        void UpdatePatrol()
        {
            if (!CanUseNavMeshAgent())
                return;

            if (Time.time >= _nextPatrolPick ||
                (_agent.pathPending == false && _agent.remainingDistance <= _agent.stoppingDistance + 0.2f))
            {
                var angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                var offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * patrolRadiusMeters * 0.65f;
                _patrolTarget = _home + offset;
                _agent.SetDestination(_patrolTarget);
                _nextPatrolPick = Time.time + Random.Range(2.5f, 4.5f);
            }
        }

        void TryBeginWindUp()
        {
            if (_player == null || Time.time < _stateUntil)
                return;

            var flat = _player.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > detectRangeMeters * detectRangeMeters)
                return;

            _state = GuardState.WindUp;
            _stateUntil = Time.time + shoveWindUpSeconds;
            if (CanUseNavMeshAgent())
                _agent.isStopped = true;
        }

        void FacePlayer()
        {
            if (_player == null)
                return;

            var flat = _player.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f)
                return;

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(flat.normalized, Vector3.up),
                Time.deltaTime * 6f);
        }

        void ExecuteShove()
        {
            _state = GuardState.Shove;
            _stateUntil = Time.time + 0.25f;

            if (_player == null)
                return;

            var flat = _player.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > shoveRangeMeters * shoveRangeMeters)
                return;

            var dir = flat.sqrMagnitude > 0.01f ? flat.normalized : transform.forward;
            var controller = _player.GetComponent<CharacterController>();
            if (controller != null && controller.enabled)
            {
                controller.Move(dir * shoveForce * 0.22f + Vector3.up * 1.2f);
            }
            else
            {
                _player.position += dir * shoveForce * 0.18f + Vector3.up * 0.8f;
            }
        }

        void EnterRecover()
        {
            _state = GuardState.Recover;
            _stateUntil = Time.time + shoveRecoverySeconds + shoveCooldownSeconds;
            if (CanUseNavMeshAgent())
                _agent.isStopped = false;
        }

        bool CanUseNavMeshAgent() =>
            _agent != null &&
            _agent.enabled &&
            _agent.isActiveAndEnabled &&
            _agent.isOnNavMesh;

        void ApplyLimitedDamage()
        {
            if (_player == null || maxDamagePerSecond <= 0f)
                return;

            var flat = _player.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > shoveRangeMeters * shoveRangeMeters)
                return;

            _damageAccumulator += maxDamagePerSecond * Time.deltaTime;
            if (_damageAccumulator < 1f)
                return;

            var damage = Mathf.FloorToInt(_damageAccumulator);
            _damageAccumulator -= damage;
            ApplyDamageToPlayer(_player.gameObject, damage);
        }

        static void ApplyDamageToPlayer(GameObject player, int damage)
        {
            if (player == null || damage <= 0)
                return;

            var statsType = System.Type.GetType("CombatStats, Assembly-CSharp");
            if (statsType != null)
            {
                var stats = player.GetComponent(statsType);
                if (stats != null)
                {
                    statsType.GetMethod("TakeDamage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                        ?.Invoke(stats, new object[] { damage });
                    return;
                }
            }

            var persistence = player.GetComponentInParent<PlayerPersistence>();
            if (persistence?.Card != null)
                persistence.Card.health = Mathf.Max(0f, persistence.Card.health - damage);
        }
    }
}
