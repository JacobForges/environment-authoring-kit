#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Raycast-down prop snap after each vegetation category — rejects floaters.</summary>
    public static class CaveBuildPropSnapRepair
    {
        public const string ReportRel = CaveBuildAgentContextExporter.Folder + "/PropSnapRepair.json";
        public const float SnapFailMeters = CaveBuildWorldLayoutAudit.PropFloaterFailMeters;
        public const float SnapWarnMeters = CaveBuildWorldLayoutAudit.PropFloaterWarnMeters;

        [Serializable]
        public class RepairReport
        {
            public string generatedUtc;
            public string category;
            public int sampled;
            public int snapped;
            public int rejected;
            public string[] rejectedNames = Array.Empty<string>();
        }

        public static int SnapVegetationCategory(
            Terrain mainTerrain,
            Transform vegetationRoot,
            SurfacePropCategory category,
            out RepairReport report)
        {
            report = new RepairReport
            {
                generatedUtc = DateTime.UtcNow.ToString("o"),
                category = category.ToString(),
            };

            if (mainTerrain == null || vegetationRoot == null)
                return 0;

            var rejected = new List<string>();
            var snapped = 0;
            foreach (Transform child in vegetationRoot)
            {
                if (child == null || !MatchesCategory(child, category))
                    continue;

                report.sampled++;
                if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(
                        mainTerrain,
                        child.position.x,
                        child.position.z,
                        out var terrain) ||
                    terrain == null)
                    continue;

                var expectedY = terrain.SampleHeight(child.position) + terrain.transform.position.y;
                var delta = child.position.y - expectedY;
                if (Mathf.Abs(delta) <= SnapWarnMeters)
                    continue;

                if (Mathf.Abs(delta) > SnapFailMeters)
                {
                    report.rejected++;
                    if (rejected.Count < 12)
                        rejected.Add(child.name);
                    CaveEditorUndo.DestroyImmediate(child.gameObject);
                    continue;
                }

                var pos = child.position;
                pos.y = expectedY;
                child.position = pos;
                snapped++;
            }

            report.snapped = snapped;
            report.rejectedNames = rejected.ToArray();
            Write(report);
            return snapped + report.rejected;
        }

        static bool MatchesCategory(Transform child, SurfacePropCategory category)
        {
            var name = child.name.ToLowerInvariant();
            var tag = $"surface_{category.ToString().ToLowerInvariant()}_";
            if (name.Contains(tag))
                return true;

            return category switch
            {
                SurfacePropCategory.Trees => name.Contains("tree") || name.Contains("palm") || name.Contains("oak"),
                SurfacePropCategory.Grass => name.Contains("grass") || name.Contains("fern") || name.Contains("reed") ||
                                             name.StartsWith("g-g") || name.StartsWith("g-c") || name.StartsWith("g-t"),
                SurfacePropCategory.Bushes => name.Contains("bush") || name.Contains("shrub") || name.Contains("hedge") ||
                                              name.StartsWith("b-"),
                SurfacePropCategory.GroundCover =>
                    name.Contains("moss") || name.Contains("cover") || name.Contains("clover") || name.Contains("leaf"),
                SurfacePropCategory.Rocks => name.Contains("rock") || name.Contains("stone") || name.Contains("boulder") ||
                                             name.StartsWith("k-"),
                _ => true,
            };
        }

        public static void QueueAfterPropCategory(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            string categoryName,
            Action onComplete)
        {
            if (ground?.Terrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    var surfaceRoot = GameObject.Find("GeneratedSurfaceWorld")?.transform;
                    var veg = surfaceRoot != null ? surfaceRoot.Find(SurfaceWorldPaths.VegetationName) : null;
                    if (veg == null || !Enum.TryParse(categoryName, out SurfacePropCategory category))
                    {
                        onComplete?.Invoke();
                        return;
                    }

                    var touched = SnapVegetationCategory(ground.Terrain, veg, category, out var repair);
                    if (touched > 0)
                    {
                        CaveBuildEditorLog.LogSurface(
                            $"[Surface] Prop snap ({categoryName}) — snapped={repair.snapped} rejected={repair.rejected} → {ReportRel}",
                            forceUnityConsole: false);
                    }

                    var instanceCount = veg.childCount;
                    if (request != null)
                    {
                        CaveBuildTerrainFingerprint.RecordPropCategory(
                            request.Seed,
                            categoryName,
                            ground.Terrain,
                            instanceCount);
                    }

                    CaveBuildLayoutAuditMilestones.QueuePropsCategoryAudit(
                        ground,
                        request,
                        categoryName,
                        onComplete);
                },
                CaveBuildPipelineDomains.QueueLabel($"prop snap — {categoryName}"));
        }

        static void Write(RepairReport report)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ReportRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"generatedUtc\": \"{report.generatedUtc}\",");
            sb.AppendLine($"  \"category\": \"{Escape(report.category)}\",");
            sb.AppendLine($"  \"sampled\": {report.sampled},");
            sb.AppendLine($"  \"snapped\": {report.snapped},");
            sb.AppendLine($"  \"rejected\": {report.rejected},");
            sb.AppendLine("  \"rejectedNames\": [");
            for (var i = 0; i < report.rejectedNames.Length; i++)
            {
                var comma = i < report.rejectedNames.Length - 1 ? "," : "";
                sb.AppendLine($"    \"{Escape(report.rejectedNames[i])}\"{comma}");
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
