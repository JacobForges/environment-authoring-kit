using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Place on the pickaxe prefab. Mines MinableRock on primary action / trigger collision.
    /// Works with desktop raycast and XR direct tool colliders.
    /// </summary>
    public sealed class PickaxeTool : MonoBehaviour
    {
        [Header("Mining")]
        public int damagePerSwing = 1;
        public float swingCooldown = 0.35f;
        public float raycastRange = 2.5f;
        public LayerMask mineableLayers = ~0;
        public Transform strikeOrigin;

        [Header("Input")]
        public KeyCode swingKey = KeyCode.Mouse0;
        public bool mineOnTrigger = true;

        float _lastSwingTime;

        void Update()
        {
            if (!Input.GetKeyDown(swingKey))
                return;
            TrySwing();
        }

        public void TrySwing()
        {
            if (Time.time - _lastSwingTime < swingCooldown)
                return;

            _lastSwingTime = Time.time;
            var origin = strikeOrigin != null ? strikeOrigin.position : transform.position;
            var direction = strikeOrigin != null ? strikeOrigin.forward : transform.forward;

            if (!Physics.Raycast(origin, direction, out var hit, raycastRange, mineableLayers,
                    QueryTriggerInteraction.Ignore))
                return;

            var rock = hit.collider.GetComponentInParent<MinableRock>();
            if (rock != null)
                rock.ApplyPickaxeHit(damagePerSwing);
        }

        void OnTriggerEnter(Collider other)
        {
            if (!mineOnTrigger)
                return;

            var rock = other.GetComponentInParent<MinableRock>();
            if (rock != null && Time.time - _lastSwingTime >= swingCooldown)
            {
                _lastSwingTime = Time.time;
                rock.ApplyPickaxeHit(damagePerSwing);
            }
        }
    }
}
