#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Ensures no cave ceiling/roof geometry protrudes above the open-sky surface except at marked entry points.
    /// </summary>
    public static class SurfaceCaveRoofAuditor
    {
        public const string ReportRel = CaveBuildAgentContextExporter.Folder + "/SurfaceCaveRoofAudit.json";
        public const float EntryExemptRadiusMeters = 10f;
        public const float AboveSurfaceToleranceMeters = 0.65f;

        public static bool AuditAndStrip(Transform caveRoot, SceneGroundInfo ground, out string summary)
        {
            summary = string.Empty;
            var issues = new List<string>();
            var stripped = 0;

            if (caveRoot == null || ground == null)
            {
                summary = "Skipped — no cave or ground.";
                return true;
            }

            var openings = new List<Vector3>();
            foreach (var marker in SurfaceWorldGenerator.FindCaveOpenings())
            {
                if (marker != null)
                    openings.Add(marker.transform.position);
            }

            var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot, ground);
            if (mouth.sqrMagnitude > 0.01f)
                openings.Add(mouth);

            foreach (var r in caveRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled)
                    continue;
                if (IsExemptName(r.gameObject.name))
                    continue;

                var center = r.bounds.center;
                if (IsNearEntry(center, openings))
                    continue;

                var surfaceY = CaveGroundPlacementUtility.SampleSurfaceWorldY(ground, center);
                if (r.bounds.max.y <= surfaceY + AboveSurfaceToleranceMeters)
                    continue;

                issues.Add(
                    $"[visual_shell] Cave roof above ground at ({center.x:F0},{center.z:F0}) " +
                    $"maxY={r.bounds.max.y:F1} surface={surfaceY:F1} — {r.gameObject.name}");
                CaveEditorUndo.RecordObject(r.gameObject, "Strip above-ground cave roof");
                r.enabled = false;
                stripped++;
            }

            foreach (var c in caveRoot.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || !c.enabled || c.isTrigger)
                    continue;
                if (IsExemptName(c.gameObject.name))
                    continue;

                var center = c.bounds.center;
                if (IsNearEntry(center, openings))
                    continue;

                var surfaceY = CaveGroundPlacementUtility.SampleHeightmapWorldY(ground, center);
                if (float.IsNaN(surfaceY))
                    surfaceY = CaveGroundPlacementUtility.SampleSurfaceWorldY(ground, center);
                if (c.bounds.max.y <= surfaceY + AboveSurfaceToleranceMeters)
                    continue;

                issues.Add(
                    $"[geometry_integrity] Cave collider above ground at ({center.x:F0},{center.z:F0}) — {c.gameObject.name}");
                CaveEditorUndo.RecordObject(c, "Disable above-ground cave collider");
                c.enabled = false;
                stripped++;
            }

            summary = stripped == 0
                ? "No above-ground cave roof."
                : $"Stripped/disabled {stripped} above-ground element(s).";
            WriteReport(issues.Count == 0, stripped, issues);
            return issues.Count == 0;
        }

        static bool IsExemptName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            return name.Contains("SurfaceWalkIn") ||
                   name.Contains("MouthPad") ||
                   name.Contains("Descent") ||
                   name.Contains("Entrance") ||
                   name.Contains("Shrine") ||
                   name.Contains("Opening");
        }

        static bool IsNearEntry(Vector3 world, List<Vector3> openings)
        {
            foreach (var o in openings)
            {
                if (Vector3.Distance(new Vector3(world.x, 0f, world.z), new Vector3(o.x, 0f, o.z)) <=
                    EntryExemptRadiusMeters)
                    return true;
            }

            return false;
        }

        static void WriteReport(bool passed, int stripped, List<string> issues)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ReportRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"passed\": {(passed ? "true" : "false")},");
            sb.AppendLine($"  \"strippedCount\": {stripped},");
            sb.AppendLine("  \"issues\": [");
            for (var i = 0; i < issues.Count; i++)
            {
                var comma = i < issues.Count - 1 ? "," : "";
                sb.AppendLine($"    \"{Escape(issues[i])}\"{comma}");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
        }

        static string Escape(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
#endif
