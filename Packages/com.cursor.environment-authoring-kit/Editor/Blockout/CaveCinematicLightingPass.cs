#if UNITY_EDITOR
using System.IO;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// AAA-style lighting pass: surface sun + cave key/fill/rim, probes, exported manifest for AI phase prompts.
    /// </summary>
    public static class CaveCinematicLightingPass
    {
        public const string ManifestRel =
            CaveBuildAgentContextExporter.Folder + "/CaveCinematicLightingManifest.json";

        public static int Apply(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out string summary)
        {
            summary = string.Empty;
            if (caveRoot == null)
                return 0;

            var count = 0;
            count += ApplySurfaceSun(ground);
            count += ApplyCaveRouteLights(caveRoot);
            count += ApplyReflectionProbes(caveRoot);
            count += CaveAdventureCaveLighting.Apply(caveRoot, ResolveLayout(caveRoot));
            LavaTubeCavePostProcess.ApplyLightingOnly(caveRoot);
            TunePlayerCamerasForCinematic();
            ExportManifest(caveRoot, count);
            summary =
                $"Cinematic lighting: surface sun + {count} cave lights/probes + adventure pass + player FOV tune.";
            CaveBuildPipelineLog.Info(summary, "Cinematic-Lighting");
            return count;
        }

        static CaveMazeLayout ResolveLayout(Transform caveRoot)
        {
            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
                return null;
            return CaveMazeLayoutGenerator.Generate(meta.seed, meta.tunnelSegments, meta.chamberCount);
        }

        static int ApplySurfaceSun(SceneGroundInfo ground)
        {
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.72f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.48f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.26f, 0.22f);
            RenderSettings.ambientIntensity = 1.05f;

            var sun = RenderSettings.sun;
            if (sun == null)
            {
                var existing = Object.FindFirstObjectByType<Light>();
                if (existing != null && existing.type == LightType.Directional)
                    sun = existing;
            }

            if (sun == null)
            {
                var go = new GameObject("Cinematic_SurfaceSun");
                CaveEditorUndo.RegisterCreated(go, "Cinematic Sun");
                sun = go.AddComponent<Light>();
            }

            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            RenderSettings.sun = sun;

            if (ground != null && ground.HasAnchor)
                sun.transform.position = ground.Bounds.center + Vector3.up * 40f;

            return 1;
        }

        static int ApplyCaveRouteLights(Transform caveRoot)
        {
            var lighting = EnvironmentSceneUtility.GetOrCreateChild(caveRoot, "Lighting");
            var cinematic = lighting.Find("Cinematic");
            if (cinematic == null)
            {
                var go = new GameObject("Cinematic");
                CaveEditorUndo.RegisterCreated(go, "Cinematic Lights");
                go.transform.SetParent(lighting, false);
                cinematic = go.transform;
            }

            var count = 0;
            count += EnsureLight(cinematic, "Key_Route", LightType.Point,
                new Color(1f, 0.72f, 0.45f), 5.5f, 16f, new Vector3(0f, 2.2f, 4f));
            count += EnsureLight(cinematic, "Fill_Ambient", LightType.Point,
                new Color(0.55f, 0.68f, 0.95f), 2.2f, 28f, new Vector3(-3f, 3f, 0f));
            count += EnsureLight(cinematic, "Rim_Mouth", LightType.Spot,
                new Color(1f, 0.55f, 0.25f), 4f, 22f, new Vector3(0f, 3.5f, -6f));

            var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot);
            var rim = cinematic.Find("Rim_Mouth");
            if (rim != null)
            {
                rim.position = mouth + Vector3.up * 2f;
                rim.LookAt(mouth + Vector3.forward * 2f);
            }

            foreach (var light in cinematic.GetComponentsInChildren<Light>(true))
                CaveLightingSettings.ApplyCaveLight(light);

            return count;
        }

        static int EnsureLight(
            Transform parent,
            string name,
            LightType type,
            Color color,
            float intensity,
            float range,
            Vector3 localPos)
        {
            var t = parent.Find(name);
            if (t != null && !HasWorkingLight(t.gameObject))
            {
                CaveEditorUndo.DestroyImmediate(t.gameObject);
                t = null;
            }

            if (t == null)
            {
                var go = new GameObject(name);
                CaveEditorUndo.RegisterCreated(go, name);
                go.transform.SetParent(parent, false);
                t = go.transform;
            }

            t.localPosition = localPos;
            var light = GetOrAddWorkingLight(t.gameObject);
            if (light == null)
                return 0;

            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = type == LightType.Directional ? LightShadows.Soft : LightShadows.None;
            if (type == LightType.Spot)
            {
                light.spotAngle = 55f;
                light.innerSpotAngle = 38f;
            }

            return 1;
        }

        static bool HasWorkingLight(GameObject go)
        {
            if (go == null)
                return false;

            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            var lights = go.GetComponents<Light>();
            for (var i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null)
                    return true;
            }

            return false;
        }

        static Light GetOrAddWorkingLight(GameObject go)
        {
            if (go == null)
                return null;

            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            var lights = go.GetComponents<Light>();
            for (var i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null)
                    return lights[i];
            }

            return go.AddComponent<Light>();
        }

        // compile_gate | unity6-mesh-data-procedural — reflection probe helper returns local count (no outer-scope leak).
        static int ApplyReflectionProbes(Transform caveRoot)
        {
            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot) ?? caveRoot;
            var probeName = "Cinematic_ReflectionProbe";
            if (geometry.Find(probeName) != null)
                return 0;

            var probeGo = new GameObject(probeName);
            CaveEditorUndo.RegisterCreated(probeGo, "Cinematic Probe");
            probeGo.transform.SetParent(geometry, false);
            probeGo.transform.localPosition = Vector3.up * 2f;
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.size = new Vector3(24f, 12f, 24f);
            probe.resolution = 128;
            probe.hdr = true;
            probe.shadowDistance = 16f;
            probe.mode = ReflectionProbeMode.Baked;
            return 1;
        }

        static void TunePlayerCamerasForCinematic()
        {
            foreach (var rig in Object.FindObjectsByType<PlayerCameraRig>(FindObjectsSortMode.None))
            {
                if (rig == null)
                    continue;
                if (!rig.TryAutoWireForEditor())
                    continue;
                EditorUtility.SetDirty(rig);
            }
        }

        static void ExportManifest(Transform caveRoot, int lightCount)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ManifestRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            var json =
                "{\n" +
                "  \"phase\": \"cinematic_lighting\",\n" +
                "  \"surfaceFog\": false,\n" +
                "  \"surfaceSun\": true,\n" +
                "  \"caveKeyFillRim\": true,\n" +
                $"  \"lightTweaks\": {lightCount},\n" +
                "  \"playerCameraRig\": \"PlayerCameraRig — surface FOV 68, cave FOV 60\",\n" +
                "  \"playtestSpectatorFov\": 52,\n" +
                "  \"aiNote\": \"Tune torch contrast + mouth rim only; do not enable global fog on surface.\"\n" +
                "}\n";
            File.WriteAllText(path, json);
        }
    }
}
#endif
