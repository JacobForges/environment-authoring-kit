using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Global directional wind zone — strength modulated by weather.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldWindController : MonoBehaviour
    {
        [SerializeField] float baseStrength = 0.35f;
        [SerializeField] float highWindStrength = 1.35f;
        [SerializeField] float pulseSpeed = 0.18f;

        WindZone _zone;
        float _weatherMultiplier = 1f;
        float _pulseOffset;

        public WindZone Zone => _zone;

        void Awake()
        {
            EnsureZone();
            _pulseOffset = Random.Range(0f, 100f);
        }

        void Update()
        {
            if (_zone == null)
                return;

            var pulse = 1f + Mathf.Sin((Time.time + _pulseOffset) * pulseSpeed) * 0.12f;
            _zone.windMain = baseStrength * _weatherMultiplier * pulse;
        }

        public void EnsureZone()
        {
            if (_zone != null)
                return;

            var existing = FindAnyObjectByType<WindZone>();
            if (existing != null)
            {
                _zone = existing;
                return;
            }

            var go = new GameObject("WorldWindZone");
            go.transform.SetParent(transform, false);
            _zone = go.AddComponent<WindZone>();
            _zone.mode = WindZoneMode.Directional;
            _zone.windMain = baseStrength;
            _zone.windTurbulence = 0.28f;
            _zone.windPulseMagnitude = 0.18f;
            _zone.windPulseFrequency = 0.22f;
        }

        public void SetWeatherMultiplier(float multiplier)
        {
            _weatherMultiplier = Mathf.Max(0.05f, multiplier);
            if (_zone != null && _weatherMultiplier >= highWindStrength * 0.75f)
                _zone.windMain = highWindStrength;
        }

        public void ApplyHighWind(bool enabled)
        {
            SetWeatherMultiplier(enabled ? highWindStrength / Mathf.Max(0.01f, baseStrength) : 1f);
        }
    }
}
