using System;
using System.Reflection;
using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Human-like playtest locomotion: walk, run, jump, attack, defend — for world route validation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class CavePlaytestBotController : MonoBehaviour
    {
        [Header("Movement")]
        public float walkSpeed = 4.2f;
        public float runSpeed = 7f;
        public float runDistanceThreshold = 8f;
        public float jumpForce = 7.5f;
        public float gravity = -18f;

        [Header("Combat probe")]
        public float attackRange = 2.8f;
        public int attackDamage = 12;
        public float attackInterval = 0.55f;
        public float defendDuration = 0.9f;
        public LayerMask targetMask = ~0;

        [Header("Automation")]
        public bool autoCombatWhileMoving = true;
        public bool autoDefendWhenLowHp = true;
        public float lowHpFraction = 0.35f;

        CharacterController _controller;
        Animator _animator;
        Vector3 _velocity;
        float _nextAttack;
        float _defendUntil;
        bool _isDefending;
        Type _combatStatsType;
        Component _botStats;

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int AttackHash = Animator.StringToHash("Attack");
        static readonly int JumpHash = Animator.StringToHash("Jump");
        static readonly int DefendHash = Animator.StringToHash("Defend");
        static readonly int IsWalkingHash = Animator.StringToHash("isWalking");

        public bool IsDefending => _isDefending;
        public float CurrentMoveSpeed { get; private set; }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _animator = GetComponentInChildren<Animator>();
            _combatStatsType = Type.GetType("CombatStats, Assembly-CSharp");
            if (_combatStatsType != null)
                _botStats = GetComponent(_combatStatsType);
        }

        public void MoveTowardGoal(Vector3 worldGoal, bool allowRun = true)
        {
            if (_controller == null)
                return;

            var flatGoal = worldGoal;
            flatGoal.y = transform.position.y;
            var to = flatGoal - transform.position;
            to.y = 0f;
            var dist = to.magnitude;
            if (dist < 0.05f)
            {
                SyncAnimator(0f, false);
                return;
            }

            var run = allowRun && dist > runDistanceThreshold;
            CurrentMoveSpeed = run ? runSpeed : walkSpeed;
            var move = to.normalized * (CurrentMoveSpeed * Time.deltaTime);

            if (to.sqrMagnitude > 0.01f)
            {
                var look = Quaternion.LookRotation(to.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, 12f * Time.deltaTime);
            }

            TryJumpIfBlocked(to.normalized);
            ApplyGravity();
            _controller.Move((move + _velocity * Time.deltaTime));

            SyncAnimator(CurrentMoveSpeed, run);

            if (autoCombatWhileMoving)
                TryCombatProbe();
        }

        void TryJumpIfBlocked(Vector3 forward)
        {
            if (!_controller.isGrounded)
                return;

            var origin = transform.position + Vector3.up * (_controller.height * 0.25f);
            if (!Physics.Raycast(origin, forward, out var hit, 1.1f, targetMask, QueryTriggerInteraction.Ignore))
                return;

            if (hit.normal.y > 0.6f)
                return;

            _velocity.y = jumpForce;
            if (_animator != null)
                _animator.SetTrigger(JumpHash);
        }

        void ApplyGravity()
        {
            if (_controller.isGrounded && _velocity.y < 0f)
                _velocity.y = -2f;
            _velocity.y += gravity * Time.deltaTime;
        }

        void TryCombatProbe()
        {
            if (Time.time < _defendUntil)
            {
                _isDefending = true;
                return;
            }

            _isDefending = false;

            if (autoDefendWhenLowHp && ShouldDefend())
            {
                BeginDefend();
                return;
            }

            var hits = Physics.OverlapSphere(
                transform.position + transform.forward * 0.6f + Vector3.up,
                attackRange,
                targetMask,
                QueryTriggerInteraction.Ignore);

            CombatStatsProxy target = default;
            var bestDist = float.MaxValue;
            foreach (var col in hits)
            {
                if (col.GetComponentInParent<CavePlaytestBotMarker>() != null)
                    continue;

                var stats = col.GetComponentInParent(_combatStatsType);
                if (stats == null || stats == _botStats)
                    continue;

                var aliveProp = _combatStatsType.GetProperty("IsAlive");
                if (aliveProp != null && !(bool)aliveProp.GetValue(stats))
                    continue;

                var d = Vector3.Distance(transform.position, col.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    target = new CombatStatsProxy { Stats = stats, Collider = col };
                }
            }

            if (target.Stats == null || Time.time < _nextAttack)
                return;

            _nextAttack = Time.time + attackInterval;
            if (_animator != null)
                _animator.SetTrigger(AttackHash);

            ApplyDamageToTarget(target.Stats, attackDamage, "Playtest Strike");
        }

        void ApplyDamageToTarget(Component targetStats, int damage, string label)
        {
            if (targetStats == null || _combatStatsType == null)
                return;

            var damageInfoType = Type.GetType("DamageInfo, Assembly-CSharp");
            if (damageInfoType == null)
                return;

            var info = Activator.CreateInstance(damageInfoType);
            damageInfoType.GetField("Amount")?.SetValue(info, damage);
            damageInfoType.GetField("SkillName")?.SetValue(info, label);
            damageInfoType.GetField("Source")?.SetValue(info, transform);
            if (_botStats != null)
                damageInfoType.GetField("SourceStats")?.SetValue(info, _botStats);

            _combatStatsType.GetMethod("ApplyDamage", new[] { damageInfoType })
                ?.Invoke(targetStats, new[] { info });
        }

        bool ShouldDefend()
        {
            if (_botStats == null || _combatStatsType == null)
                return false;

            var currentHp = (int)_combatStatsType.GetField("currentHp").GetValue(_botStats);
            var maxHp = (int)_combatStatsType.GetField("maxHp").GetValue(_botStats);
            return maxHp > 0 && currentHp / (float)maxHp <= lowHpFraction;
        }

        public void BeginDefend()
        {
            _defendUntil = Time.time + defendDuration;
            _isDefending = true;
            if (_animator != null)
                _animator.SetTrigger(DefendHash);
        }

        void SyncAnimator(float speed, bool running)
        {
            if (_animator == null)
                return;

            foreach (var p in _animator.parameters)
            {
                if (p.nameHash == SpeedHash)
                    _animator.SetFloat(SpeedHash, speed);
                else if (p.nameHash == IsWalkingHash)
                    _animator.SetBool(IsWalkingHash, speed > 0.2f && !running);
            }
        }

        struct CombatStatsProxy
        {
            public Component Stats;
            public Collider Collider;
        }
    }
}
