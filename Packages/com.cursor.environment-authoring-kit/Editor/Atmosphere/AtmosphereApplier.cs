using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_RENDER_PIPELINE_UNIVERSAL
using UnityEngine.Rendering.Universal;
#endif

namespace EnvironmentAuthoringKit.Editor.Atmosphere
{
    static class AtmosphereApplier
    {
        public static void Apply(AtmospherePreset preset)
        {
            if (preset == null)
                return;

            var sun = FindOrCreateSun();
            Undo.RecordObject(sun, "Apply Atmosphere Sun");
            sun.color = preset.sunColor;
            sun.intensity = preset.sunIntensity;
            sun.transform.rotation = Quaternion.Euler(preset.sunRotation);
            sun.shadows = preset.sunShadows ? LightShadows.Soft : LightShadows.None;

            if (preset.skyboxMaterial != null)
                RenderSettings.skybox = preset.skyboxMaterial;
            else
                RenderSettings.skybox = CreateProceduralSkybox(preset.skyTint);

            RenderSettings.ambientMode = preset.ambientMode;
            RenderSettings.ambientSkyColor = preset.skyAmbient;
            RenderSettings.ambientEquatorColor = preset.equatorAmbient;
            RenderSettings.ambientGroundColor = preset.groundAmbient;

            RenderSettings.fog = preset.fogEnabled;
            RenderSettings.fogColor = preset.fogColor;
            RenderSettings.fogMode = preset.fogMode;
            RenderSettings.fogDensity = preset.fogDensity;

            EnsureGlobalVolume(preset);
            EnvironmentSceneUtility.MarkSceneDirty();
        }

        static Light FindOrCreateSun()
        {
            var lights = Object.FindObjectsByType<Light>();
            foreach (var light in lights)
            {
                if (light.type == LightType.Directional)
                    return light;
            }

            var go = new GameObject("Sun");
            Undo.RegisterCreatedObjectUndo(go, "Create Sun");
            var sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            return sun;
        }

        static Material CreateProceduralSkybox(Color tint)
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null)
                return null;

            var mat = new Material(shader) { name = "GeneratedSkybox" };
            mat.SetColor("_SkyTint", tint);
            mat.SetFloat("_AtmosphereThickness", 1f);
            mat.SetFloat("_Exposure", 1.2f);
            return mat;
        }

        static void EnsureGlobalVolume(AtmospherePreset preset)
        {
#if UNITY_RENDER_PIPELINE_UNIVERSAL
            var volumeGo = GameObject.Find("Global Volume");
            if (volumeGo == null)
            {
                volumeGo = new GameObject("Global Volume");
                Undo.RegisterCreatedObjectUndo(volumeGo, "Create Global Volume");
                volumeGo.AddComponent<Volume>();
            }

            var volume = volumeGo.GetComponent<Volume>();
            if (volume == null)
                volume = volumeGo.AddComponent<Volume>();

            volume.isGlobal = true;
            if (volume.sharedProfile == null)
                volume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();

            Undo.RecordObject(volume.sharedProfile, "Apply Atmosphere Volume");

            if (!volume.sharedProfile.TryGet(out Bloom bloom))
                bloom = volume.sharedProfile.Add<Bloom>(true);
            bloom.active = preset.bloomIntensity > 0.01f;
            bloom.intensity.value = preset.bloomIntensity;

            if (!volume.sharedProfile.TryGet(out ColorAdjustments color))
                color = volume.sharedProfile.Add<ColorAdjustments>(true);
            color.active = true;
            color.postExposure.value = preset.colorAdjustmentsPostExposure;
#else
            _ = preset;
#endif
        }
    }
}
