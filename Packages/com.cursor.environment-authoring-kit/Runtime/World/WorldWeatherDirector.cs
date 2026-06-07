using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    public enum WorldWeatherState
    {
        Clear,
        Fog,
        Rain,
        HighWind,
    }

    /// <summary>Random weather state machine with fog/rain/wind transitions.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldWeatherDirector : MonoBehaviour
    {
        [SerializeField] float minStateSeconds = 120f;
        [SerializeField] float maxStateSeconds = 300f;
        [SerializeField] float transitionSeconds = 8f;
        [SerializeField] float rainEmissionRate = 900f;

        WorldWeatherState _state = WorldWeatherState.Clear;
        WorldWeatherState _targetState = WorldWeatherState.Clear;
        float _stateTimer;
        float _transitionT;
        float _baseFogDensity = 0.012f;
        Color _baseFogColor = new(0.55f, 0.68f, 0.72f);
        ParticleSystem _rain;
        WorldWindController _wind;

        public WorldWeatherState CurrentState => _state;

        void Awake()
        {
            _wind = GetComponent<WorldWindController>() ?? FindAnyObjectByType<WorldWindController>();
            _baseFogDensity = RenderSettings.fogDensity;
            _baseFogColor = RenderSettings.fogColor;
            RollNextState(immediate: true);
        }

        void Update()
        {
            _stateTimer -= Time.deltaTime;
            if (_stateTimer <= 0f)
                RollNextState(immediate: false);

            if (_transitionT < 1f)
            {
                _transitionT = Mathf.Min(1f, _transitionT + Time.deltaTime / Mathf.Max(0.1f, transitionSeconds));
                if (_transitionT >= 1f)
                    _state = _targetState;
            }

            ApplyStateVisuals(Mathf.SmoothStep(0f, 1f, _transitionT));
        }

        void RollNextState(bool immediate)
        {
            var options = new[]
            {
                WorldWeatherState.Clear,
                WorldWeatherState.Fog,
                WorldWeatherState.Rain,
                WorldWeatherState.HighWind,
            };
            WorldWeatherState next;
            do
            {
                next = options[Random.Range(0, options.Length)];
            }
            while (next == _targetState && options.Length > 1);

            _targetState = next;
            _stateTimer = Random.Range(minStateSeconds, maxStateSeconds);
            _transitionT = immediate ? 1f : 0f;
            if (immediate)
                _state = _targetState;
        }

        void ApplyStateVisuals(float blend)
        {
            var active = blend >= 0.5f ? _targetState : _state;
            var fogMul = active switch
            {
                WorldWeatherState.Fog => 2.8f,
                WorldWeatherState.Rain => 1.6f,
                _ => 1f,
            };
            var fogColor = active switch
            {
                WorldWeatherState.Fog => new Color(0.68f, 0.72f, 0.76f),
                WorldWeatherState.Rain => new Color(0.48f, 0.52f, 0.58f),
                _ => _baseFogColor,
            };

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = Mathf.Lerp(_baseFogDensity, _baseFogDensity * fogMul, blend);
            RenderSettings.fogColor = Color.Lerp(_baseFogColor, fogColor, blend);

            EnsureRain();
            if (_rain != null)
            {
                var raining = active == WorldWeatherState.Rain;
                var emission = _rain.emission;
                emission.rateOverTime = raining ? rainEmissionRate : 0f;
                if (raining && !_rain.isPlaying)
                    _rain.Play();
                if (!raining && _rain.isPlaying && emission.rateOverTime.constant <= 0f)
                    _rain.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            _wind ??= GetComponent<WorldWindController>();
            _wind?.ApplyHighWind(active == WorldWeatherState.HighWind);
        }

        void EnsureRain()
        {
            if (_rain != null)
                return;

            var existing = transform.Find("WeatherRain");
            if (existing != null)
            {
                _rain = existing.GetComponent<ParticleSystem>();
                return;
            }

            var go = new GameObject("WeatherRain");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 40f, 0f);
            _rain = go.AddComponent<ParticleSystem>();
            var main = _rain.main;
            main.startSpeed = 18f;
            main.startSize = 0.06f;
            main.startColor = new Color(0.75f, 0.82f, 0.92f, 0.55f);
            main.maxParticles = 6000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var shape = _rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(120f, 1f, 120f);

            var emission = _rain.emission;
            emission.rateOverTime = 0f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 0.35f;
        }

        public void CaptureBiomeFogBaseline(float density, Color color)
        {
            _baseFogDensity = density;
            _baseFogColor = color;
        }
    }
}
