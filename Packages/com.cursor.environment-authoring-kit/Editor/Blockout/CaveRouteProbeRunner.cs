#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Editor "bot" that walks the maze route with physics probes and writes JSON for Cursor targeted fixes
    /// (no full cave rebuild required).
    /// </summary>
    public static class CaveRouteProbeRunner
    {
        public const string ReportPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildRouteProbe.json";

        public struct ProbeIssue
        {
            public string Code;
            public string Message;
            public string SuggestedStageId;
            public int PathIndex;
            public Vector3 WorldPosition;
        }

        public sealed class ProbeReport
        {
            public bool Passed;
            public int PathSteps;
            public int InvisibleCollidersNearRoute;
            public int InvisibleSolidTotal;
            public readonly List<ProbeIssue> Issues = new();
        }

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Run Cave Route Probe (Bot)")]
        public static void RunFromMenu()
        {
            var cave = FindCaveRoot();
            if (cave == null)
            {
                EditorUtility.DisplayDialog("Cave Route Probe", "No cave in scene. Build Complete Cave first.", "OK");
                return;
            }

            var report = Run(cave);
            Export(report, cave);
            var msg = report.Passed
                ? $"Route probe passed ({report.PathSteps} steps)."
                : $"Route probe found {report.Issues.Count} issue(s). See {ReportPath}";
            EditorUtility.DisplayDialog("Cave Route Probe", msg, "OK");
        }

        public static ProbeReport Run(Transform caveRoot)
        {
            var report = new ProbeReport();
            if (caveRoot == null)
                return report;

            var surfaceRoute = SurfaceRouteProbeRunner.Run(caveRoot);
            SurfaceRouteProbeRunner.Export(surfaceRoute);
            foreach (var line in surfaceRoute.Issues)
            {
                var stage = "terrain_integration";
                var msg = line;
                if (line.StartsWith("[") && line.IndexOf(']') > 1)
                {
                    stage = line.Substring(1, line.IndexOf(']') - 1);
                    msg = line.Substring(line.IndexOf(']') + 1).Trim();
                }

                report.Issues.Add(Issue("surface_route", msg, stage, -1, caveRoot.position));
            }

            if (!surfaceRoute.ReachedCaveMouth)
            {
                report.Issues.Add(Issue(
                    "mouth_unreachable",
                    "Surface bot did not reach walkable cave mouth — fix trails/terrain before underground route.",
                    "ground_placement",
                    -1,
                    caveRoot.position));
            }

            var meta = EnsureMetadata(caveRoot);
            if (meta == null)
            {
                report.Issues.Add(Issue("no_metadata", "Missing CaveBuildMetadata on cave root.", "path", -1, caveRoot.position));
                return report;
            }

            var layout = CaveMazeLayoutGenerator.Generate(meta.seed, meta.tunnelSegments, meta.chamberCount);
            if (layout?.SolutionPath == null || layout.SolutionPath.Count == 0)
            {
                report.Issues.Add(Issue("no_path", "Maze layout has no solution path.", "path", -1, caveRoot.position));
                return report;
            }

            report.PathSteps = layout.SolutionPath.Count;
            report.InvisibleSolidTotal = CavePlayabilityValidator.CountInvisibleSolidColliders(caveRoot);

            if (report.InvisibleSolidTotal > 0)
            {
                report.Issues.Add(Issue(
                    "invisible_solid",
                    $"{report.InvisibleSolidTotal} invisible solid collider(s) in cave.",
                    "geometry_integrity",
                    -1,
                    caveRoot.position));
            }

            if (!CavePlayabilityValidator.CheckSpawnReachability(caveRoot))
            {
                report.Issues.Add(Issue(
                    "spawn_unreachable",
                    "Entrance spawn not reachable on walk surface.",
                    "spawn_reachability",
                    0,
                    caveRoot.position));
            }

            var minClear = CaveMazeLayout.MinWalkClearanceMeters;
            for (var i = 0; i < layout.SolutionPath.Count; i++)
            {
                var cell = layout.SolutionPath[i];
                var localFloor = layout.GetFloorSurfaceLocal(cell.x, cell.y);
                var worldFloor = caveRoot.TransformPoint(localFloor);
                var eye = worldFloor + Vector3.up * 1.1f;

                if (layout.IsJumpGap(cell.x, cell.y))
                {
                    ProbeJumpGap(caveRoot, layout, cell, worldFloor, report, i);
                    continue;
                }

                if (!RaycastWalkFloor(eye, caveRoot, out var hitFloor))
                {
                    report.Issues.Add(Issue(
                        "floor_missing",
                        $"No walkable floor under route cell ({cell.x},{cell.y}).",
                        "player_floor",
                        i,
                        worldFloor));
                }

                var clearance = layout.GetCeilingClearanceAt(cell.x, cell.y);
                if (!RaycastCeiling(hitFloor.point, clearance * 0.85f, out var hitCeiling))
                {
                    report.Issues.Add(Issue(
                        "ceiling_open",
                        $"Open ceiling or low headroom at cell ({cell.x},{cell.y}) (need ~{clearance:F1}m).",
                        "visual_shell",
                        i,
                        hitFloor.point));
                }
                else
                {
                    var headroom = hitCeiling.point.y - hitFloor.point.y;
                    if (headroom < minClear)
                    {
                        report.Issues.Add(Issue(
                            "ceiling_low",
                            $"Headroom {headroom:F2}m < {minClear}m at ({cell.x},{cell.y}).",
                            "visual_shell",
                            i,
                            hitFloor.point));
                    }
                }

                report.InvisibleCollidersNearRoute += CountInvisibleNear(eye, caveRoot, 1.4f);
            }

            if (report.InvisibleCollidersNearRoute > 0)
            {
                report.Issues.Add(Issue(
                    "invisible_near_route",
                    $"{report.InvisibleCollidersNearRoute} invisible collider(s) within 1.4m of route.",
                    "geometry_integrity",
                    -1,
                    caveRoot.position));
            }

            report.Passed = report.Issues.Count == 0;
            if (!report.Passed)
                Debug.LogWarning(
                    $"[CaveBuild] Route probe (surface-first): {report.Issues.Count} issue(s) — see {ReportPath}");
            return report;
        }

        static void ProbeJumpGap(
            Transform caveRoot,
            CaveMazeLayout layout,
            Vector2Int cell,
            Vector3 worldFloor,
            ProbeReport report,
            int pathIndex)
        {
            var pit = FindPitForCell(caveRoot, cell);
            if (pit == null)
            {
                report.Issues.Add(Issue(
                    "pit_missing",
                    $"Jump gap ({cell.x},{cell.y}) has no Pit_Lava recovery volume.",
                    "path",
                    pathIndex,
                    worldFloor));
            }
            else if (pit.GetComponent<CavePitFallRecovery>() == null)
            {
                report.Issues.Add(Issue(
                    "pit_no_recovery",
                    $"Jump gap ({cell.x},{cell.y}) pit missing CavePitFallRecovery.",
                    "player_floor",
                    pathIndex,
                    worldFloor));
            }

            if (HasBlockingColliderAcrossGap(caveRoot, worldFloor, layout.CellSize * 0.5f))
            {
                report.Issues.Add(Issue(
                    "pit_blocked",
                    $"Invisible/solid blocker spans jump gap ({cell.x},{cell.y}).",
                    "geometry_integrity",
                    pathIndex,
                    worldFloor));
            }
        }

        static Transform FindPitForCell(Transform caveRoot, Vector2Int cell)
        {
            var name = $"JumpGap_{cell.x}_{cell.y}";
            var features = caveRoot.Find("AdventureFeatures");
            if (features == null)
                return null;
            var gap = features.Find(name);
            return gap != null ? gap.Find("Pit_Lava") : null;
        }

        static bool HasBlockingColliderAcrossGap(Transform caveRoot, Vector3 worldFloor, float halfWidth)
        {
            var center = worldFloor + Vector3.up * 0.35f;
            var halfExtents = new Vector3(halfWidth, 0.35f, halfWidth * 0.9f);
            var hits = Physics.OverlapBox(center, halfExtents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            foreach (var col in hits)
            {
                if (col == null || col.isTrigger)
                    continue;
                if (col.GetComponentInParent<CaveWalkableMarker>() != null)
                    continue;
                if (col.GetComponentInParent<MinableRock>() != null)
                    continue;
                if (!CaveRendererVisibility.HasVisibleRenderer(col, true))
                    return true;
            }

            return false;
        }

        static bool RaycastWalkFloor(Vector3 from, Transform caveRoot, out RaycastHit hit)
        {
            if (Physics.Raycast(from, Vector3.down, out hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (CaveWalkableSurface.IsWalkableCollider(hit.collider))
                    return true;
            }

            hit = default;
            return false;
        }

        static bool RaycastCeiling(Vector3 floorPoint, float maxDistance, out RaycastHit hit) =>
            Physics.Raycast(floorPoint + Vector3.up * 0.2f, Vector3.up, out hit, maxDistance, ~0, QueryTriggerInteraction.Ignore);

        static int CountInvisibleNear(Vector3 worldPoint, Transform caveRoot, float radius)
        {
            var count = 0;
            foreach (var col in Physics.OverlapSphere(worldPoint, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (col == null || col.isTrigger)
                    continue;
                if (!col.transform.IsChildOf(caveRoot))
                    continue;
                if (CaveColliderUtility.IsProtectedPlayCollider(col, caveRoot))
                    continue;
                if (!CaveRendererVisibility.HasVisibleRenderer(col, true))
                    count++;
            }

            return count;
        }

        static CaveBuildMetadata EnsureMetadata(Transform caveRoot)
        {
            if (caveRoot == null)
                return null;

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta != null)
                return meta;

            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
                return null;

            meta = caveRoot.gameObject.AddComponent<CaveBuildMetadata>();
            var inferredSegments = Mathf.Max(8, authoring.Knots.Count);
            var inferredChambers = Mathf.Clamp(Mathf.RoundToInt(inferredSegments * 0.28f), 3, 8);
            var inferredSeed = CaveBuildWorkflowCoordinator.TryReadLastSeed(out var seed)
                ? seed
                : caveRoot.GetInstanceID();
            meta.Set(inferredSeed, inferredSegments, inferredChambers, CaveGeometryPaths.IsAdventureCave(caveRoot));
            EditorUtility.SetDirty(caveRoot.gameObject);
            return meta;
        }

        public static void Export(ProbeReport report, Transform caveRoot)
        {
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit/Generated"))
                AssetDatabase.CreateFolder("Assets/EnvironmentKit", "Generated");

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"passed\": {(report.Passed ? "true" : "false")},");
            sb.AppendLine($"  \"pathSteps\": {report.PathSteps},");
            sb.AppendLine($"  \"invisibleSolidTotal\": {report.InvisibleSolidTotal},");
            sb.AppendLine($"  \"invisibleNearRoute\": {report.InvisibleCollidersNearRoute},");
            sb.AppendLine($"  \"caveRoot\": \"{Escape(caveRoot != null ? caveRoot.name : "")}\",");
            sb.AppendLine($"  \"qualityReport\": \"Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json\",");
            sb.AppendLine("  \"instructions\": \"Apply targeted kit fixes for suggestedStageId only — do not rebuild entire cave unless path/ground_placement failed.\",");
            sb.AppendLine("  \"issues\": [");
            for (var i = 0; i < report.Issues.Count; i++)
            {
                var issue = report.Issues[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"code\": \"{Escape(issue.Code)}\",");
                sb.AppendLine($"      \"message\": \"{Escape(issue.Message)}\",");
                sb.AppendLine($"      \"suggestedStageId\": \"{Escape(issue.SuggestedStageId)}\",");
                sb.AppendLine($"      \"pathIndex\": {issue.PathIndex},");
                sb.AppendLine($"      \"worldPosition\": [{issue.WorldPosition.x:F3}, {issue.WorldPosition.y:F3}, {issue.WorldPosition.z:F3}]");
                sb.AppendLine(i < report.Issues.Count - 1 ? "    }," : "    }");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            File.WriteAllText(Path.Combine(projectRoot, ReportPath), sb.ToString());
            Debug.Log($"[CaveProbe] Wrote {ReportPath} — {(report.Passed ? "PASS" : $"{report.Issues.Count} issue(s)")}.");
        }

        public static void ExportAndNotifyCursor(Transform caveRoot, bool invokeAgent)
        {
            var report = Run(caveRoot);
            Export(report, caveRoot);
            if (report.Passed)
                return;

            var messages = new List<string>();
            foreach (var issue in report.Issues)
                messages.Add($"[{issue.SuggestedStageId}] {issue.Message}");

            CaveLiveCodegenRequest.Write(caveRoot, messages, "route_probe");
            if (!invokeAgent)
                return;

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if ((settings.autoInvokeAfterEveryBuild || settings.autoInvokeOnDud) &&
                CaveBuildCursorAgentBridge.HasApiKey)
                CaveBuildCursorAgentBridge.TryInvokeGradeAndFixBackground(out _, includeLiveFix: true);
        }

        static ProbeIssue Issue(string code, string message, string stage, int pathIndex, Vector3 world) =>
            new()
            {
                Code = code,
                Message = message,
                SuggestedStageId = stage,
                PathIndex = pathIndex,
                WorldPosition = world
            };

        static string Escape(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        internal static Transform FindCaveRoot() => CaveGeometryPaths.FindCaveSystemRoot();
    }
}
#endif
