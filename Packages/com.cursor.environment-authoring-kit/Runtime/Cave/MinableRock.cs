using UnityEngine;
using UnityEngine.Events;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Marks destructible cave geometry (rockfall / blocking rocks) for pickaxe targeting.
    /// Attach to prefab instances placed by LavaTubeCaveGenerator.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinableRock : MonoBehaviour
    {
        [Tooltip("Hits required before this rock is removed.")]
        public int hitPoints = 3;

        [Tooltip("Optional drop spawned when mined out.")]
        public GameObject breakVfxPrefab;

        public UnityEvent onMined;
        public UnityEvent<int> onHit;

        int _remainingHits;

        public bool IsDepleted => _remainingHits <= 0;

        void Awake()
        {
            _remainingHits = Mathf.Max(1, hitPoints);
            if (!gameObject.CompareTag(CaveTags.Minable))
                gameObject.tag = CaveTags.Minable;

            var registry = Object.FindAnyObjectByType<CaveMiningRegistry>();
            registry?.Register(this);
        }

        void OnDestroy()
        {
            var registry = Object.FindAnyObjectByType<CaveMiningRegistry>();
            registry?.Unregister(this);
        }

        /// <summary>Called by PickaxeTool or other mining interactables.</summary>
        public void ApplyPickaxeHit(int damage = 1)
        {
            if (IsDepleted)
                return;

            _remainingHits -= Mathf.Max(1, damage);
            onHit?.Invoke(_remainingHits);

            if (_remainingHits > 0)
                return;

            onMined?.Invoke();
            if (breakVfxPrefab != null)
                Instantiate(breakVfxPrefab, transform.position, transform.rotation);

            var registry = Object.FindAnyObjectByType<CaveMiningRegistry>();
            registry?.NotifyMined(this, transform.position);

            Destroy(gameObject);
        }
    }
}
