using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    static class CaveSplineFxBuilder
    {
        const string LightParticlePrefab =
            "Assets/PolitePenguin/LPMagicalForest/Prefabs/Environmental/Lightparticle_01.prefab";

        public static int Build(Transform cavesRoot, CaveSplinePath spline, System.Random rng)
        {
            if (cavesRoot == null || spline == null)
                return 0;

            var fxRoot = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, "FX");
            ClearChildren(fxRoot);

            var count = 0;
            count += PlaceMotesAlongSpline(fxRoot, spline, rng);
            count += PlaceCrystalGleamLights(fxRoot, spline, rng);
            EnsureEntranceGlow(cavesRoot);
            return count;
        }

        static int PlaceMotesAlongSpline(Transform fxRoot, CaveSplinePath spline, System.Random rng)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LightParticlePrefab);
            var spacing = 18f;
            var n = Mathf.Max(2, Mathf.FloorToInt(spline.TotalLength / spacing));
            var placed = 0;

            for (var i = 0; i < n; i++)
            {
                var dist = (i + 0.35f) / n * spline.TotalLength;
                var sample = spline.SampleAtDistance(dist);
                var pos = sample.Position + sample.Up * (sample.RadiusY * 0.35f);

                if (prefab != null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, fxRoot);
                    if (instance != null)
                    {
                        CaveEditorUndo.RegisterCreated(instance, "Cave Motes");
                        instance.name = $"CaveMotes_{i:D2}";
                        instance.transform.localPosition = pos;
                        instance.transform.localRotation = Quaternion.identity;
                        placed++;
                        continue;
                    }
                }

                placed += CreateFallbackDust(fxRoot, pos, i);
            }

            return placed;
        }

        static int CreateFallbackDust(Transform parent, Vector3 localPos, int index)
        {
            var go = new GameObject($"CaveDust_{index:D2}");
            CaveEditorUndo.RegisterCreated(go, "Cave Dust");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 4f;
            main.startSpeed = 0.08f;
            main.startSize = 0.12f;
            main.maxParticles = 24;
            main.startColor = new Color(0.7f, 0.85f, 1f, 0.35f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 6f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1.2f;

            return 1;
        }

        static int PlaceCrystalGleamLights(Transform fxRoot, CaveSplinePath spline, System.Random rng)
        {
            var count = Mathf.Max(4, Mathf.FloorToInt(spline.TotalLength / 22f));
            var placed = 0;
            for (var i = 0; i < count; i++)
            {
                var dist = (float)rng.NextDouble() * spline.TotalLength;
                var sample = spline.SampleAtDistance(dist);
                var side = sample.Right * ((float)rng.NextDouble() * 2f - 1f) * sample.RadiusX * 0.55f;

                var lightGo = new GameObject($"CrystalGleam_{i:D2}");
                CaveEditorUndo.RegisterCreated(lightGo, "Crystal Gleam");
                lightGo.transform.SetParent(fxRoot, false);
                lightGo.transform.localPosition = sample.Position + side + sample.Up * 0.4f;

                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 9f;
                light.intensity = 0.35f + (float)rng.NextDouble() * 0.25f;
                light.color = new Color(0.45f, 0.75f, 1f);
                light.shadows = LightShadows.None;
                placed++;
            }

            return placed;
        }

        static void EnsureEntranceGlow(Transform cavesRoot)
        {
            var entrance = cavesRoot.Find("Entrance");
            if (entrance == null || entrance.Find("EntranceGlow") != null)
                return;

            var glow = new GameObject("EntranceGlow");
            CaveEditorUndo.RegisterCreated(glow, "Entrance Glow");
            glow.transform.SetParent(entrance, false);
            glow.transform.localPosition = new Vector3(3f, 2.2f, 2f);

            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 16f;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.72f, 0.42f);
            light.shadows = LightShadows.None;
        }

        static void ClearChildren(Transform root)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
                CaveEditorUndo.DestroyImmediate(root.GetChild(i).gameObject);
        }
    }
}
