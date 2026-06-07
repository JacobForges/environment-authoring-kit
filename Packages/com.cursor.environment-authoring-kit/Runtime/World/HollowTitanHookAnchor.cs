using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Valid grappling-hook latch point on stair treads and boulder platforms.</summary>
    [DisallowMultipleComponent]
    public sealed class HollowTitanHookAnchor : MonoBehaviour
    {
        [SerializeField] float latchRadius = 1.2f;

        public float LatchRadius => latchRadius;
        public Vector3 LatchPoint => transform.position + Vector3.up * 0.15f;

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.55f);
            Gizmos.DrawWireSphere(LatchPoint, latchRadius);
        }
    }
}
