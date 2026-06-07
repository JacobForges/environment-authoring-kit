#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Editor surface checks run with route probe / validation bot.</summary>
    public static class SurfacePlaytestValidator
    {
        public const string ReportPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildSurfaceProbe.json";

        public sealed class SurfaceProbeReport
        {
            public bool Passed;
            public bool HasSurfaceRoot;
            public bool HasTerrain;
            public int EntranceOnionSlabCount;
            public int VegetationCount;
            public readonly List<string> Issues = new();
        }

        public static SurfaceProbeReport Run(Transform caveRoot = null)
        {
            var report = new SurfaceProbeReport();
            var env = Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            var surface = env != null ? env.transform.Find(SurfaceWorldPaths.RootName) : null;
            report.HasSurfaceRoot = surface != null;

            var ground = SceneGroundResolver.Resolve();
            report.HasTerrain = ground?.Terrain != null;

            if (!report.HasSurfaceRoot)
                report.Issues.Add("[terrain_integration] Missing GeneratedSurfaceWorld.");
            if (!report.HasTerrain)
                report.Issues.Add("[terrain_integration] No Terrain on scene Ground.");

            if (surface != null)
            {
                var veg = surface.Find(SurfaceWorldPaths.VegetationName);
                report.VegetationCount = veg != null ? veg.childCount : 0;

                var water = surface.Find(SurfaceWorldPaths.WaterName);
                if (water != null)
                {
                    foreach (Transform child in water)
                    {
                        if (child == null)
                            continue;
                        if (ground?.Terrain != null)
                        {
                            var surfaceY = CaveGroundPlacementUtility.SampleHeightmapWorldY(ground, child.position);
                            if (!float.IsNaN(surfaceY) && child.position.y > surfaceY + 1.5f)
                                report.Issues.Add($"[water] {child.name} floating above terrain.");
                        }
                    }
                }
            }

            if (caveRoot != null)
            {
                report.EntranceOnionSlabCount = CountEntranceOnionSlabs(caveRoot, ground);
                if (report.EntranceOnionSlabCount >= 3)
                {
                    report.Issues.Add(
                        $"[visual_shell] {report.EntranceOnionSlabCount} stacked flat slab(s) at cave mouth — run surface walk-in rebuild.");
                }

                if (!CaveSurfaceEntranceBuilder.HasDescentWalk(caveRoot))
                    report.Issues.Add("[ground_placement] No professional entrance descent mesh at mouth.");
            }

            report.Passed = report.Issues.Count == 0;
            return report;
        }

        public static void Export(SurfaceProbeReport report)
        {
            if (report == null)
                return;

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"passed\": {(report.Passed ? "true" : "false")},");
            sb.AppendLine($"  \"hasSurfaceRoot\": {(report.HasSurfaceRoot ? "true" : "false")},");
            sb.AppendLine($"  \"hasTerrain\": {(report.HasTerrain ? "true" : "false")},");
            sb.AppendLine($"  \"entranceOnionSlabCount\": {report.EntranceOnionSlabCount},");
            sb.AppendLine($"  \"vegetationCount\": {report.VegetationCount},");
            sb.AppendLine("  \"issues\": [");
            for (var i = 0; i < report.Issues.Count; i++)
            {
                var comma = i < report.Issues.Count - 1 ? "," : "";
                sb.AppendLine($"    {JsonQuote(report.Issues[i])}{comma}");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
        }

        static int CountEntranceOnionSlabs(Transform caveRoot, SceneGroundInfo ground)
        {
            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
                return 0;

            var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot);
            var count = 0;
            foreach (var r in geometry.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled)
                    continue;
                if (Vector3.Distance(r.bounds.center, mouth) > 14f)
                    continue;
                var ext = r.bounds.extents;
                if (ext.y < 1.1f && ext.x > 1.2f && ext.z > 1.2f)
                {
                    if (r.gameObject.name.Contains("RouteTerrain") ||
                        r.gameObject.name.Contains("MouthPad") ||
                        r.gameObject.name.Contains(DescentMeshName()))
                        continue;
                    count++;
                }
            }

            return count;
        }

        static string DescentMeshName() => CaveEntranceVolumeBuilder.DescentMeshName;

        static string JsonQuote(string s) =>
            "\"" + (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
#endif
