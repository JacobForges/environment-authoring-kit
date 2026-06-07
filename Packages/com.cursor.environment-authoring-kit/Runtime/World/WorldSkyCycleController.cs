using UnityEngine;
using UnityEngine.Rendering;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>
    /// Rotates sun/moon directional lights through a full day/night arc at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldSkyCycleController : MonoBehaviour
    {
        [SerializeField] float dayLengthMinutes = 24f;
        [SerializeField] float timeOfDay01 = 0.35f;
        [SerializeField] float sunIntensityDay = 1.15f;
        [SerializeField] float sunIntensityNight = 0.04f;
        [SerializeField] float moonIntensity = 0.28f;
        [SerializeField] float sunriseHour = 6f;
        [SerializeField] float sunsetHour = 18f;

        Light _sun;
        Light _moon;
        float _daySeconds;

        public float TimeOfDay01
        {
            get => timeOfDay01;
            set => timeOfDay01 = Mathf.Repeat(value, 1f);
        }

        public float DayLengthMinutes
        {
            get => dayLengthMinutes;
            set => dayLengthMinutes = Mathf.Max(0.5f, value);
        }

        void Awake()
        {
            _daySeconds = dayLengthMinutes * 60f;
            EnsureLights();
            ApplyLighting();
        }

        void Update()
        {
            if (_daySeconds <= 0f)
                _daySeconds = dayLengthMinutes * 60f;

            timeOfDay01 = Mathf.Repeat(timeOfDay01 + Time.deltaTime / _daySeconds, 1f);
            ApplyLighting();
        }

        public void BindLights(Light sun, Light moon)
        {
            _sun = sun;
            _moon = moon;
            if (_sun != null)
                RenderSettings.sun = _sun;
            ApplyLighting();
        }

        void EnsureLights()
        {
            if (_sun == null)
            {
                var sunGo = GameObject.Find("WorldSun");
                if (sunGo == null)
                {
                    sunGo = new GameObject("WorldSun");
                    sunGo.transform.SetParent(transform, false);
                }

                _sun = sunGo.GetComponent<Light>() ?? sunGo.AddComponent<Light>();
                _sun.type = LightType.Directional;
                _sun.shadows = LightShadows.Soft;
            }

            if (_moon == null)
            {
                var moonGo = GameObject.Find("WorldMoon");
                if (moonGo == null)
                {
                    moonGo = new GameObject("WorldMoon");
                    moonGo.transform.SetParent(transform, false);
                }

                _moon = moonGo.GetComponent<Light>() ?? moonGo.AddComponent<Light>();
                _moon.type = LightType.Directional;
                _moon.color = new Color(0.72f, 0.78f, 0.95f);
                _moon.shadows = LightShadows.None;
            }

            RenderSettings.sun = _sun;
        }

        void ApplyLighting()
        {
            EnsureLights();

            var hours = timeOfDay01 * 24f;
            var sunPitch = SunPitchDegrees(hours);
            var sunYaw = timeOfDay01 * 360f - 90f;

            _sun.transform.rotation = Quaternion.Euler(sunPitch, sunYaw, 0f);

            var sunAboveHorizon = sunPitch > -2f;
            var dayBlend = Mathf.Clamp01(Mathf.InverseLerp(-8f, 12f, sunPitch));
            _sun.intensity = Mathf.Lerp(sunIntensityNight, sunIntensityDay, dayBlend);
            _sun.color = Color.Lerp(
                new Color(0.55f, 0.62f, 0.85f),
                new Color(1f, 0.96f, 0.88f),
                dayBlend);

            var moonActive = !sunAboveHorizon || hours >= sunsetHour || hours < sunriseHour;
            _moon.enabled = moonActive;
            if (moonActive)
            {
                var moonPitch = sunPitch - 180f;
                _moon.transform.rotation = Quaternion.Euler(moonPitch, sunYaw + 40f, 0f);
                _moon.intensity = moonIntensity * (1f - dayBlend * 0.85f);
            }

            var sky = SampleSkyColor(hours, dayBlend);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.42f, 0.44f, 0.4f), sky, 0.45f);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.18f, 0.16f, 0.14f), sky * 0.35f, dayBlend);
        }

        static float SunPitchDegrees(float hours)
        {
            var noonOffset = (hours - 12f) / 12f;
            return -Mathf.Cos(noonOffset * Mathf.PI) * 68f;
        }

        static Color SampleSkyColor(float hours, float dayBlend)
        {
            if (hours >= 5f && hours < 7.5f)
                return Color.Lerp(new Color(0.28f, 0.22f, 0.38f), new Color(0.95f, 0.62f, 0.42f), (hours - 5f) / 2.5f);
            if (hours >= 17f && hours < 20f)
                return Color.Lerp(new Color(0.82f, 0.72f, 0.92f), new Color(0.92f, 0.48f, 0.32f), (hours - 17f) / 3f);
            if (hours >= 20f || hours < 5f)
                return new Color(0.12f, 0.14f, 0.24f);
            return Color.Lerp(new Color(0.35f, 0.42f, 0.55f), new Color(0.62f, 0.78f, 0.98f), dayBlend);
        }
    }
}
