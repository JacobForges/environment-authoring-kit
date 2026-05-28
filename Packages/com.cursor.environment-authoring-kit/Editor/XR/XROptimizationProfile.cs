using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.XR
{
    [CreateAssetMenu(fileName = "XROptimizationProfile", menuName = "Environment Kit/XR Optimization Profile")]
    public sealed class XROptimizationProfile : ScriptableObject
    {
        [Header("Viture XR Pro / Mobile XR")]
        [Range(0.5f, 1f)] public float renderScale = 0.9f;
        public bool disableHdr = true;
        public int msaa = 2;
        public float shadowDistance = 30f;
        public int shadowCascades = 1;
        public int maxScatterTextureSize = 1024;
        public int maxTerrainTextureSize = 2048;
        public float lodCullDistance = 50f;
        public int targetFrameRate = 72;
        public bool enableGpuInstancing = true;
        public bool markEnvironmentStatic = true;
    }
}
