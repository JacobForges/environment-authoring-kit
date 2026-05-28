#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Per-build shrine blockout at surface cave openings (Tomb Raider / Zelda / Diablo variants).
    /// </summary>
    public static class SurfaceEntryShrineBuilder
    {
        public const string ShrinesRootName = "SurfaceCaveShrines";
        public const string ReportRel = CaveBuildAgentContextExporter.Folder + "/SurfaceEntryShrineManifest.json";

        public static int BuildAtAllOpenings(
            Transform caveRoot,
            SceneGroundInfo ground,
            int seed,
            WorldGenerationRequest request,
            out string message)
        {
            message = string.Empty;
            var markers = SurfaceWorldGenerator.FindCaveOpenings();
            if (markers == null || markers.Count == 0)
            {
                message = "No surface cave opening markers.";
                return 0;
            }

            request ??= new WorldGenerationRequest { Seed = seed };
            var style = string.IsNullOrEmpty(request.BuildVisualStyle)
                ? CaveBuildStylePalette.Classic
                : request.BuildVisualStyle;
            var rng = new System.Random(seed + 4417);

            var env = Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            var surface = env != null ? env.transform.Find(SurfaceWorldPaths.RootName) : null;
            if (surface == null)
            {
                message = "Missing GeneratedSurfaceWorld.";
                return 0;
            }

            var root = surface.Find(ShrinesRootName);
            if (root == null)
            {
                var go = new GameObject(ShrinesRootName);
                CaveEditorUndo.RegisterCreated(go, "Surface shrines root");
                go.transform.SetParent(surface, false);
                root = go.transform;
            }

            var built = 0;
            var archetype = CaveBuildStylePalette.ShrineArchetypeLabel(style, rng);
            foreach (var marker in markers)
            {
                if (marker == null)
                    continue;
                if (BuildOne(root, marker, ground, style, archetype, rng))
                    built++;
            }

            message = $"Built/updated {built} shrine(s) ({style}/{archetype}).";
            WriteManifest(built, markers.Count, style, archetype);
            return built;
        }

        static bool BuildOne(
            Transform shrinesRoot,
            SurfaceCaveOpeningMarker marker,
            SceneGroundInfo ground,
            string styleId,
            string archetype,
            System.Random rng)
        {
            var name = $"Shrine_Sector{marker.sectorIndex}";
            var existing = shrinesRoot.Find(name);
            if (existing != null)
                CaveEditorUndo.DestroyImmediate(existing.gameObject);

            var shrine = new GameObject(name);
            CaveEditorUndo.RegisterCreated(shrine, "Surface cave shrine");
            shrine.transform.SetParent(shrinesRoot, false);

            var pos = marker.transform.position;
            pos.y = CaveGroundPlacementUtility.SampleSurfaceWorldY(ground, pos);
            shrine.transform.position = pos;
            var forward = marker.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            shrine.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

            CaveBuildStylePalette.GetShrineColors(styleId, rng, out var rock, out var accent);

            switch (archetype)
            {
                case "zelda_ring":
                    BuildZeldaRing(shrine.transform, rock, accent, rng);
                    break;
                case "diablo_blood_gate":
                    BuildDiabloGate(shrine.transform, rock, accent, rng, blood: true);
                    break;
                case "diablo_skull_steps":
                    BuildDiabloGate(shrine.transform, rock, accent, rng, blood: false);
                    break;
                case "flooded_basin_gate":
                    BuildFloodedBasin(shrine.transform, rock, accent);
                    break;
                case "tomb_steps":
                    BuildTombArch(shrine.transform, rock, accent, rng, wide: false);
                    break;
                default:
                    BuildTombArch(shrine.transform, rock, accent, rng, wide: archetype == "tomb_arch");
                    break;
            }

            return true;
        }

        static void BuildTombArch(Transform root, Color rock, Color accent, System.Random rng, bool wide)
        {
            var pillarW = wide ? 0.65f : 0.5f;
            PlaceBlock(root, new Vector3(-1.5f, 0.9f, 0f), new Vector3(pillarW, 1.8f, 0.5f), rock);
            PlaceBlock(root, new Vector3(1.5f, 0.9f, 0f), new Vector3(pillarW, 1.8f, 0.5f), rock);
            PlaceBlock(root, new Vector3(0f, 2.35f, 0f), new Vector3(wide ? 4.2f : 3.4f, 0.32f, 0.55f), accent);
            var steps = 2 + rng.Next(0, 3);
            for (var s = 0; s < steps; s++)
            {
                PlaceBlock(
                    root,
                    new Vector3(0f, 0.08f + s * 0.11f, 1.1f + s * 0.42f),
                    new Vector3(1.7f - s * 0.15f, 0.1f, 0.5f),
                    rock);
            }
        }

        static void BuildZeldaRing(Transform root, Color rock, Color accent, System.Random rng)
        {
            for (var i = 0; i < 6; i++)
            {
                var ang = i / 6f * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(ang) * 2.2f, 0.55f, Mathf.Sin(ang) * 1.4f);
                PlaceBlock(root, p, new Vector3(0.45f, 1.1f, 0.45f), rock);
            }

            PlaceBlock(root, Vector3.up * 1.8f, new Vector3(0.35f, 0.35f, 0.35f), accent);
            PlaceBlock(root, new Vector3(0f, 0.1f, 1.4f), new Vector3(1.4f, 0.12f, 0.9f), rock);
        }

        static void BuildDiabloGate(Transform root, Color rock, Color accent, System.Random rng, bool blood)
        {
            PlaceBlock(root, new Vector3(-1.3f, 1.1f, 0f), new Vector3(0.55f, 2.2f, 0.55f), rock);
            PlaceBlock(root, new Vector3(1.3f, 1.1f, 0f), new Vector3(0.55f, 2.2f, 0.55f), rock);
            PlaceBlock(root, new Vector3(0f, 2.6f, 0f), new Vector3(3.2f, 0.28f, 0.35f), blood ? accent : rock);
            PlaceBlock(root, new Vector3(0f, 0.12f, 0.8f), new Vector3(2.4f, 0.15f, 1.1f), rock);
            if (rng.NextDouble() < 0.6)
                PlaceBlock(root, new Vector3(0f, 0.5f, 1.2f), new Vector3(0.8f, 0.8f, 0.25f), accent);
        }

        static void BuildFloodedBasin(Transform root, Color rock, Color accent)
        {
            PlaceBlock(root, new Vector3(0f, 0.05f, 0.6f), new Vector3(3.5f, 0.08f, 2.2f), accent);
            PlaceBlock(root, new Vector3(-1.4f, 0.7f, -0.2f), new Vector3(0.5f, 1.4f, 0.5f), rock);
            PlaceBlock(root, new Vector3(1.4f, 0.7f, -0.2f), new Vector3(0.5f, 1.4f, 0.5f), rock);
        }

        static void PlaceBlock(Transform parent, Vector3 localPos, Vector3 scale, Color color)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "ShrineBlock";
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPos;
            cube.transform.localScale = scale;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = color;
            cube.GetComponent<Renderer>().sharedMaterial = mat;
        }

        static void WriteManifest(int built, int markerCount, string style, string archetype)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ReportRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"built\": {built},");
            sb.AppendLine($"  \"markerCount\": {markerCount},");
            sb.AppendLine($"  \"visualStyle\": \"{style}\",");
            sb.AppendLine($"  \"archetype\": \"{archetype}\"");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
        }
    }
}
#endif
