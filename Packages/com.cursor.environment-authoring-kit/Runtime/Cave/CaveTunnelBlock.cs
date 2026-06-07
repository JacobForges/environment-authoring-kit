using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Marks a morphed cave shell cube for distance-based culling.</summary>
    [DisallowMultipleComponent]
    public sealed class CaveTunnelBlock : MonoBehaviour
    {
        public Renderer blockRenderer;
        public Collider blockCollider;

        void Awake() => CacheReferences();

        public void CacheReferences()
        {
            blockRenderer ??= GetComponent<Renderer>();
            blockCollider ??= GetComponent<Collider>();
        }
    }
}
