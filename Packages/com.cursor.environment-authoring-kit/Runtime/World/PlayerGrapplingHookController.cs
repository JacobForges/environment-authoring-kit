using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>
    /// Landmark grappling hook — aim at <see cref="HollowTitanHookAnchor"/>, pull player to ledge.
    /// Equipped when G-GRAPPLING_HOOK is in inventory.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerGrapplingHookController : MonoBehaviour
    {
        public const string ItemDefinitionId = "G-GRAPPLING_HOOK";

        [Header("Aim")]
        [SerializeField] float maxRangeMeters = 32f;
        [SerializeField] float aimConeDegrees = 18f;
        [SerializeField] KeyCode activateKey = KeyCode.E;

        [Header("Pull")]
        [SerializeField] float pullSpeedMeters = 16f;
        [SerializeField] float arrivalDistance = 0.55f;
        [SerializeField] float cooldownSeconds = 0.65f;

        CharacterController _controller;
        Transform _cameraTransform;
        bool _equipped;
        bool _pulling;
        Vector3 _pullTarget;
        float _cooldownUntil;

        public bool IsEquipped => _equipped;
        public bool IsPulling => _pulling;

        public static bool HasHookInInventory(PlayerCardData card)
        {
            if (card?.inventory == null)
                return false;

            foreach (var stack in card.inventory)
            {
                if (stack != null && stack.definitionId == ItemDefinitionId && stack.quantity > 0)
                    return true;
            }

            return false;
        }

        public void SetEquipped(bool equipped)
        {
            _equipped = equipped;
            if (!equipped)
                _pulling = false;
        }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            ResolveCamera();
        }

        void Update()
        {
            if (!_equipped || _controller == null)
                return;

            if (Input.GetKeyDown(activateKey) && Time.time >= _cooldownUntil)
                TryFireHook();

            if (_pulling)
                UpdatePull();
        }

        void ResolveCamera()
        {
            var cam = Camera.main;
            if (cam != null)
                _cameraTransform = cam.transform;
        }

        void TryFireHook()
        {
            ResolveCamera();
            if (_cameraTransform == null)
                return;

            var origin = _cameraTransform.position;
            var forward = _cameraTransform.forward;
            HollowTitanHookAnchor best = null;
            var bestScore = float.MaxValue;

            foreach (var anchor in Object.FindObjectsByType<HollowTitanHookAnchor>())
            {
                if (anchor == null)
                    continue;

                var toAnchor = anchor.LatchPoint - origin;
                var dist = toAnchor.magnitude;
                if (dist > maxRangeMeters || dist < 1.5f)
                    continue;

                var angle = Vector3.Angle(forward, toAnchor);
                if (angle > aimConeDegrees)
                    continue;

                if (Physics.Raycast(origin, toAnchor.normalized, out var hit, dist, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider != null &&
                        hit.collider.GetComponentInParent<HollowTitanHookAnchor>() == null)
                        continue;
                }

                var score = angle + dist * 0.04f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = anchor;
                }
            }

            if (best == null)
                return;

            _pullTarget = best.LatchPoint + Vector3.up * 0.35f;
            _pulling = true;
            _cooldownUntil = Time.time + cooldownSeconds;
            WorldEconomyHud.NotifyCollected("Grappling hook latched — pulling to ledge.");
        }

        void UpdatePull()
        {
            var pos = transform.position;
            var next = Vector3.MoveTowards(pos, _pullTarget, pullSpeedMeters * Time.deltaTime);
            var delta = next - pos;

            if (_controller.enabled)
                _controller.Move(delta);
            else
                transform.position = next;

            if ((next - _pullTarget).sqrMagnitude <= arrivalDistance * arrivalDistance)
            {
                _pulling = false;
                _cooldownUntil = Time.time + cooldownSeconds * 0.5f;
            }
        }
    }
}
