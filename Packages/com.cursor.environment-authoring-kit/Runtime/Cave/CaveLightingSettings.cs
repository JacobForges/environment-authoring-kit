using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Caps cave point lights so they do not wash out the sunny surface scene.</summary>
    public static class CaveLightingSettings
    {
        public const float MaxPointLightRange = 22f;
        public const float MaxChamberLightRange = 28f;
        public const float MaxAmbientFillRange = 32f;

        public static void ApplyCaveLight(Light light, bool isChamber = false)
        {
            if (light == null)
                return;

            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            var maxRange = isChamber ? MaxChamberLightRange : MaxPointLightRange;
            light.range = Mathf.Min(light.range, maxRange);
        }
    }
}
