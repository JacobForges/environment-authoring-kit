#if UNITY_EDITOR
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    public static class WorldPlanV4RuntimeSystemsAuthor
    {
        public const string RootName = "WorldSystems";

        public static void EnsureInScene()
        {
            var root = GameObject.Find(RootName);
            if (root == null)
                root = new GameObject(RootName);

            if (root.GetComponent<WorldBiomeMusicDirector>() == null)
            {
                var music = root.AddComponent<WorldBiomeMusicDirector>();
                AssignMusicClips(music);
            }

            if (root.GetComponent<WorldBiomeAtmosphere>() == null)
                root.AddComponent<WorldBiomeAtmosphere>();

            EnsureSkyAndWeather(root);

            if (Object.FindAnyObjectByType<WorldCinematicDirector>() == null)
            {
                var dir = new GameObject("WorldCinematicDirector");
                dir.AddComponent<WorldCinematicDirector>();
            }

            EditorUtility.SetDirty(root);
        }

        static void EnsureSkyAndWeather(GameObject root)
        {
            var wind = root.GetComponent<WorldWindController>() ?? root.AddComponent<WorldWindController>();
            wind.EnsureZone();

            if (root.GetComponent<WorldWeatherDirector>() == null)
                root.AddComponent<WorldWeatherDirector>();

            var sun = EnsureDirectionalLight("WorldSun", new Color(1f, 0.96f, 0.88f), 1.1f, true);
            var moon = EnsureDirectionalLight("WorldMoon", new Color(0.72f, 0.78f, 0.95f), 0.25f, false);
            moon.enabled = false;

            sun.transform.SetParent(root.transform, false);
            moon.transform.SetParent(root.transform, false);

            var sky = root.GetComponent<WorldSkyCycleController>() ?? root.AddComponent<WorldSkyCycleController>();
            sky.BindLights(sun, moon);
        }

        static Light EnsureDirectionalLight(string name, Color color, float intensity, bool castShadows)
        {
            var light = WorldDirectionalLightUtility.Ensure(name);
            if (light == null)
                return null;

            light.color = color;
            light.intensity = intensity;
            light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
            return light;
        }

        static void AssignMusicClips(WorldBiomeMusicDirector music)
        {
            music.AssignClips(
                LoadClip("adventure_explore"),
                LoadClip("theme_loop"),
                LoadClip("battle_loop"),
                LoadClip("loops-pack/level1-step1"),
                LoadClip("loops-pack/level1-step2"),
                LoadClip("loops-pack/level1-step3-evil"));
        }

        static AudioClip LoadClip(string pathNoExt)
        {
            var root = Cc0ContentImportUtility.Cc0Root + "/Audio/";
            return AssetDatabase.LoadAssetAtPath<AudioClip>($"{root}{pathNoExt}.ogg")
                   ?? AssetDatabase.LoadAssetAtPath<AudioClip>($"{root}{pathNoExt}.mp3")
                   ?? AssetDatabase.LoadAssetAtPath<AudioClip>($"{root}{pathNoExt}.wav");
        }
    }
}
#endif
