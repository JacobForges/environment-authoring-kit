#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Lightweight creative passes — seed-driven variety without new macro pipeline steps.</summary>
    public static class CaveBuildEnhancementCreativePasses
    {
        public static void ApplyRequestRolls(WorldGenerationRequest request, System.Random rng)
        {
            if (request == null || rng == null)
                return;

            if (CaveResearchLayoutHints.PreferTombRaiderCadence(request, rng))
                CaveResearchLayoutHints.ApplyTombRaiderGuidance(request, rng);

            if (request.MazeGenFlavor < 0)
                request.MazeGenFlavor = rng.Next(0, 6);

            if (request.SurfaceTileLayoutVariant < 0)
                request.SurfaceTileLayoutVariant = rng.Next(0, 48);

            if (string.IsNullOrEmpty(request.BuildVisualStyle))
                request.BuildVisualStyle = CaveBuildStylePalette.PickVisualStyle(rng);

            if (request.PreferredCaveOpeningSector < 0)
                request.PreferredCaveOpeningSector = rng.Next(0, 8);

            if (request.CavePathYawVariance <= 0f)
                request.CavePathYawVariance = 18f + (float)rng.NextDouble() * 14f;

            if (request.CaveChamberSizeMultiplier <= 0f)
                request.CaveChamberSizeMultiplier = 2.1f + (float)rng.NextDouble() * 0.55f;

            if (string.IsNullOrEmpty(request.PropEmphasis))
                request.PropEmphasis = "karst_forest_canopy";

            request.FogDensityMultiplier = request.BuildVisualStyle switch
            {
                CaveBuildStylePalette.FloodedGrotto => 1.35f,
                CaveBuildStylePalette.DiabloCatacomb => 0.85f,
                _ => 1f + (float)rng.NextDouble() * 0.15f,
            };

            request.ColorMood = Mathf.Clamp01(0.35f + (float)rng.NextDouble() * 0.35f);
        }

        public static void ApplyAfterShell(Transform caveRoot, WorldGenerationRequest request)
        {
            if (caveRoot == null || request == null)
                return;

            var rng = new System.Random(request.Seed + 4403);
            EnsureEntranceLighting(caveRoot, request, rng);
            EnsureBiolumAccents(caveRoot, rng);
            EnsureFinishBeacon(caveRoot, rng);
            ApplyTorchWarmth(caveRoot, request);
        }

        static void EnsureEntranceLighting(Transform caveRoot, WorldGenerationRequest request, System.Random rng)
        {
            var entrance = caveRoot.Find("Entrance") ?? caveRoot.Find("EntranceMarker");
            if (entrance == null)
                return;

            var key = entrance.Find("EnhancementKeyLight");
            if (key == null)
            {
                var go = new GameObject("EnhancementKeyLight");
                CaveEditorUndo.RegisterCreated(go, "Enhancement key light");
                go.transform.SetParent(entrance, false);
                go.transform.localPosition = new Vector3(0f, 1.2f, -1.5f);
                key = go.transform;
                var light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.range = 14f;
                light.spotAngle = 42f;
                light.shadows = LightShadows.Soft;
            }

            var l = key.GetComponent<Light>();
            if (l == null)
                return;

            l.intensity = request.BuildVisualStyle == CaveBuildStylePalette.FloodedGrotto ? 1.1f : 1.45f;
            l.color = request.BuildVisualStyle switch
            {
                CaveBuildStylePalette.TombExplorer => new Color(1f, 0.92f, 0.78f),
                CaveBuildStylePalette.DiabloCatacomb => new Color(1f, 0.55f, 0.35f),
                _ => new Color(0.95f, 0.88f, 0.75f),
            };
        }

        static void EnsureBiolumAccents(Transform caveRoot, System.Random rng)
        {
            var root = EnvironmentSceneUtility.GetOrCreateChild(caveRoot, "EnhancementBiolum");
            if (root.childCount > 0)
                return;

            var geometry = caveRoot.Find(CaveAdventureCaveGenerator.GeometryRootName);
            if (geometry == null)
                return;

            var count = 3 + rng.Next(0, 4);
            for (var i = 0; i < count; i++)
            {
                var go = new GameObject($"Biolum_{i}");
                CaveEditorUndo.RegisterCreated(go, "Biolum accent");
                go.transform.SetParent(root, false);
                var t = geometry.position + new Vector3(
                    (float)(rng.NextDouble() * 24 - 12),
                    -2f - (float)rng.NextDouble() * 6f,
                    (float)(rng.NextDouble() * 40 + 8));
                go.transform.position = t;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 5f + (float)rng.NextDouble() * 3f;
                light.intensity = 0.35f + (float)rng.NextDouble() * 0.25f;
                light.color = new Color(0.45f, 0.85f, 1f);
                light.shadows = LightShadows.None;
            }
        }

        static void EnsureFinishBeacon(Transform caveRoot, System.Random rng)
        {
            var goal = caveRoot.Find("FinishGoal") ?? caveRoot.Find("Goal");
            if (goal == null)
                return;

            var beacon = goal.Find("EnhancementBeacon");
            if (beacon != null)
                return;

            var go = new GameObject("EnhancementBeacon");
            CaveEditorUndo.RegisterCreated(go, "Finish beacon");
            go.transform.SetParent(goal, false);
            go.transform.localPosition = Vector3.up * 1.5f;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 10f;
            light.intensity = 1.2f;
            light.color = new Color(0.55f, 1f, 0.65f);
        }

        static void ApplyTorchWarmth(Transform caveRoot, WorldGenerationRequest request)
        {
            var details = caveRoot.Find("Details");
            if (details == null)
                return;

            foreach (var light in details.GetComponentsInChildren<Light>(true))
            {
                if (!light.name.Contains("Torch", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                light.color = request.BuildVisualStyle == CaveBuildStylePalette.DiabloCatacomb
                    ? new Color(1f, 0.5f, 0.25f)
                    : new Color(1f, 0.75f, 0.45f);
            }
        }

        public static void ApplyPostColorMood(Transform caveRoot, WorldGenerationRequest request)
        {
            if (caveRoot == null || request == null)
                return;

            RenderSettings.fog = request.FogDensityMultiplier > 1.05f;
            if (RenderSettings.fog)
            {
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = 0.004f * request.FogDensityMultiplier;
                RenderSettings.fogColor = new Color(0.35f, 0.42f, 0.48f);
            }
        }
    }
}
#endif
