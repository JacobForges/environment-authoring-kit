using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Stores underground pool/waterfall positions for SUIMONO placement (stage 7).</summary>
    public sealed class CaveWaterBranchAnchor : MonoBehaviour
    {
        public Vector3 poolLocalPosition;
        public Vector3 waterfallLocalPosition;

        public void SetBranchPositions(Vector3 pool, Vector3 waterfall)
        {
            poolLocalPosition = pool;
            waterfallLocalPosition = waterfall;
        }
    }
}
