using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Pulses emission on lava pits/pools so they read hot and alive.</summary>
    [DisallowMultipleComponent]
    public sealed class CaveLavaGlow : MonoBehaviour
    {
        public Color baseEmission = new(1.4f, 0.35f, 0.05f);
        public float pulseSpeed = 1.2f;
        public float pulseAmount = 0.45f;
        public float bobAmplitude = 0.06f;
        public float bobSpeed = 0.7f;

        Renderer _renderer;
        Material _instance;
        Vector3 _baseLocalPos;
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            _baseLocalPos = transform.localPosition;
            _renderer = GetComponent<Renderer>();
            if (_renderer != null)
                _instance = _renderer.material;
        }

        void Update()
        {
            if (_instance == null)
                return;

            var pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
            if (_instance.HasProperty(EmissionColor))
                _instance.SetColor(EmissionColor, baseEmission * pulse);

            transform.localPosition = _baseLocalPos + Vector3.up * Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
        }
    }
}
