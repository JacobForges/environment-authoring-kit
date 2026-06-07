using UnityEngine;
using UnityEngine.Rendering;

namespace EnvironmentAuthoringKit.Editor.Atmosphere
{
    [CreateAssetMenu(fileName = "AtmospherePreset", menuName = "Environment Kit/Atmosphere Preset")]
    public sealed class AtmospherePreset : ScriptableObject
    {
        public Color sunColor = new(1f, 0.95f, 0.85f);
        [Range(0f, 3f)] public float sunIntensity = 1.1f;
        public Vector3 sunRotation = new(50f, -30f, 0f);
        public bool sunShadows = true;

        public AmbientMode ambientMode = AmbientMode.Trilight;
        public Color skyAmbient = new(0.45f, 0.55f, 0.7f);
        public Color equatorAmbient = new(0.4f, 0.42f, 0.38f);
        public Color groundAmbient = new(0.2f, 0.22f, 0.18f);

        public bool fogEnabled = true;
        public Color fogColor = new(0.65f, 0.72f, 0.78f);
        [Range(0f, 0.1f)] public float fogDensity = 0.012f;
        public FogMode fogMode = FogMode.ExponentialSquared;

        public Material skyboxMaterial;
        public Color skyTint = new(0.5f, 0.65f, 0.9f);

        [Range(0f, 1f)] public float bloomIntensity;
        [Range(-1f, 1f)] public float colorAdjustmentsPostExposure;
    }
}
