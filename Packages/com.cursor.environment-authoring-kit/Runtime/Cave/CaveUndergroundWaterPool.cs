using UnityEngine;
using UnityEngine.Rendering;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Ignite water pool plane for the lava-tube cave (no SUIMONO components).</summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class CaveUndergroundWaterPool : MonoBehaviour
    {
        void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null)
                Destroy(col);

            var renderer = GetComponent<MeshRenderer>();
            if (renderer == null)
                return;

            if (renderer.sharedMaterial != null)
                CaveUndergroundWaterMaterial.Cache(renderer.sharedMaterial);
            else
                CaveUndergroundWaterMaterial.ApplyToRenderer(renderer);

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }
    }
}
