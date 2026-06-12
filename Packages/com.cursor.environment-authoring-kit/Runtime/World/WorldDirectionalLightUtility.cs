using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Find or create named directional lights — tolerates orphan WorldSun/WorldMoon objects.</summary>
    public static class WorldDirectionalLightUtility
    {
        public static Light Ensure(string objectName, Transform parent = null)
        {
            if (string.IsNullOrEmpty(objectName))
                return null;

            var go = GameObject.Find(objectName);
            var light = ResolveLight(go);
            if (light != null)
                return light;

            if (go != null)
                DestroyObject(go);

            go = new GameObject(objectName);
            if (parent != null)
                go.transform.SetParent(parent, false);

            light = go.AddComponent<Light>();
            if (light == null)
                return null;

            light.type = LightType.Directional;
            return light;
        }

        static Light ResolveLight(GameObject go)
        {
            if (go == null)
                return null;

            if (go.TryGetComponent(out Light light) && light != null)
            {
                light.type = LightType.Directional;
                return light;
            }

            PurgeLightComponents(go);
            light = go.AddComponent<Light>();
            if (light == null)
                return null;

            light.type = LightType.Directional;
            return light;
        }

        static void PurgeLightComponents(GameObject go)
        {
            var lights = go.GetComponents<Light>();
            for (var i = lights.Length - 1; i >= 0; i--)
            {
                if (lights[i] == null)
                    continue;

                if (Application.isPlaying)
                    Object.Destroy(lights[i]);
                else
                    Object.DestroyImmediate(lights[i]);
            }
        }

        static void DestroyObject(GameObject go)
        {
            if (go == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(go);
            else
                Object.DestroyImmediate(go);
        }
    }
}
