using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Builds drifting fog/mist particle systems along the cave path so the atmosphere reads
    /// as actual volumetric fog instead of a flat exponential color shift.
    /// </summary>
    static class CaveFogMistBuilder
    {
        public const string MistRootName = "FogMist";

        public static int Build(Transform caveRoot, CaveSplinePath spline)
        {
            if (caveRoot == null || spline == null || spline.KnotCount < 2)
                return 0;

            var mistRoot = EnvironmentSceneUtility.GetOrCreateChild(caveRoot, MistRootName);
            for (var i = mistRoot.childCount - 1; i >= 0; i--)
                CaveEditorUndo.DestroyImmediate(mistRoot.GetChild(i).gameObject);

            var spacing = Mathf.Clamp(spline.TotalLength / 14f, 6f, 16f);
            var count = Mathf.Max(4, Mathf.CeilToInt(spline.TotalLength / spacing));
            var placed = 0;
            var sharedMat = GetOrCreateFogMistMaterial();

            for (var i = 0; i < count; i++)
            {
                var d = (i + 0.5f) / count * spline.TotalLength;
                var sample = spline.SampleAtDistance(d);
                var floor = sample.Position - sample.Up * (sample.RadiusY * 0.5f);

                var go = new GameObject($"FogMist_{i:D2}");
                CaveEditorUndo.RegisterCreated(go, "Fog Mist");
                go.transform.SetParent(mistRoot, false);
                go.transform.localPosition = floor;
                go.transform.localRotation = Quaternion.LookRotation(sample.Tangent, sample.Up);

                ConfigureMistSystem(go, sharedMat, sample.RadiusX, sample.RadiusY);
                placed++;
            }

            return placed;
        }

        static void ConfigureMistSystem(GameObject go, Material mat, float radiusX, float radiusY)
        {
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 6f;
            main.loop = true;
            main.startLifetime = 8f;
            main.startSpeed = 0.18f;
            main.startSize = Mathf.Max(5f, radiusX * 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.55f, 0.6f, 0.7f, 0.045f);
            main.maxParticles = 18;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = 0f;
            main.prewarm = true;
            main.scalingMode = ParticleSystemScalingMode.Local;

            var emission = ps.emission;
            emission.rateOverTime = 1.4f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(radiusX * 1.6f, radiusY * 0.6f, 8f);

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.55f, 0.6f, 0.7f), 0f),
                    new GradientColorKey(new Color(0.45f, 0.5f, 0.6f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.07f, 0.35f),
                    new GradientAlphaKey(0.05f, 0.7f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = gradient;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.6f),
                    new Keyframe(0.5f, 1.2f),
                    new Keyframe(1f, 1.6f)));

            var velocityOverLifetime = ps.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            velocityOverLifetime.space = ParticleSystemSimulationSpace.Local;
            velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(-0.02f, 0.04f);
            velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = 5f;
        }

        const string MistMaterialPath = "Assets/EnvironmentKit/Presets/CaveFogMist_URP.mat";

        static Material GetOrCreateFogMistMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MistMaterialPath);
            if (existing != null)
                return existing;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null)
                return null;

            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit/Presets"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                    AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
                AssetDatabase.CreateFolder("Assets/EnvironmentKit", "Presets");
            }

            var mat = new Material(shader) { name = "CaveFogMist_URP" };
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = (int)RenderQueue.Transparent;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", new Color(0.55f, 0.6f, 0.7f, 0.05f));
            if (mat.HasProperty("_TintColor"))
                mat.SetColor("_TintColor", new Color(0.55f, 0.6f, 0.7f, 0.05f));

            AssetDatabase.CreateAsset(mat, MistMaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
