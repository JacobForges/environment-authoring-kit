using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Warm torches, lava glow, and underground atmosphere for compact adventure caves.</summary>
    public static class CaveAdventureCaveLighting
    {
        public static int Apply(Transform caveRoot, CaveMazeLayout layout)
        {
            if (caveRoot == null)
                return 0;

            var n = 0;
            n += BoostTorches(caveRoot);
            n += BoostLavaFeatures(caveRoot);
            n += TuneAtmosphere(caveRoot, layout);
            n += TuneAmbientFill(caveRoot);
            return n;
        }

        static int BoostTorches(Transform caveRoot)
        {
            var count = 0;
            foreach (var light in caveRoot.GetComponentsInChildren<Light>(true))
            {
                if (light == null || !light.gameObject.name.Contains("Torch"))
                    continue;

                light.type = LightType.Point;
                light.color = new Color(1f, 0.58f, 0.22f);
                light.intensity = Mathf.Max(light.intensity, 7.5f);
                light.range = Mathf.Max(light.range, 22f);
                light.shadows = LightShadows.Soft;
                count++;
            }

            return count;
        }

        static int BoostLavaFeatures(Transform caveRoot)
        {
            var count = 0;
            foreach (var glow in caveRoot.GetComponentsInChildren<CaveLavaGlow>(true))
            {
                if (glow == null)
                    continue;

                glow.baseEmission = new Color(2.4f, 0.55f, 0.06f);
                glow.pulseSpeed = 1.35f;
                glow.pulseAmount = 0.55f;
                EditorUtility.SetDirty(glow);
                count++;

                foreach (var light in glow.GetComponentsInChildren<Light>(true))
                {
                    if (light == null)
                        continue;
                    light.type = LightType.Point;
                    light.color = new Color(1f, 0.42f, 0.1f);
                    light.intensity = Mathf.Max(light.intensity, 9f);
                    light.range = Mathf.Max(light.range, 14f);
                }
            }

            foreach (var t in caveRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || !t.name.Contains("LavaSurface"))
                    continue;

                if (t.GetComponent<CaveLavaGlow>() == null)
                    t.gameObject.AddComponent<CaveLavaGlow>();
            }

            return count;
        }

        static int TuneAtmosphere(Transform caveRoot, CaveMazeLayout layout)
        {
            var zone = caveRoot.Find("CaveAtmosphereZone");
            if (zone == null)
                return 0;

            var atmosphere = zone.GetComponent<CaveUndergroundAtmosphere>();
            if (atmosphere == null)
                atmosphere = zone.gameObject.AddComponent<CaveUndergroundAtmosphere>();

            atmosphere.cameraBackground = new Color(0.02f, 0.015f, 0.012f, 1f);
            atmosphere.fogColor = new Color(0.08f, 0.04f, 0.025f, 1f);
            atmosphere.fogDensity = 0.032f;
            atmosphere.ambientSky = new Color(0.06f, 0.04f, 0.035f, 1f);
            atmosphere.ambientEquator = new Color(0.12f, 0.06f, 0.03f, 1f);
            atmosphere.ambientGround = new Color(0.18f, 0.08f, 0.03f, 1f);
            atmosphere.ambientIntensity = 0.55f;

            if (layout != null)
            {
                layout.ComputeLocalBounds(out var min, out var max);
                var center = (min + max) * 0.5f;
                var size = max - min + new Vector3(8f, 10f, 8f);
                zone.localPosition = center;
                if (zone.GetComponent<BoxCollider>() is BoxCollider box)
                {
                    box.size = size;
                    box.isTrigger = true;
                }
            }

            EditorUtility.SetDirty(atmosphere);
            return 1;
        }

        static int TuneAmbientFill(Transform caveRoot)
        {
            var fill = caveRoot.Find("Lighting/CaveAmbientFill");
            if (fill == null)
                return 0;

            var light = fill.GetComponent<Light>();
            if (light == null)
                return 0;

            light.color = new Color(0.95f, 0.45f, 0.18f);
            light.intensity = 0.28f;
            light.range = 28f;
            return 1;
        }
    }
}
