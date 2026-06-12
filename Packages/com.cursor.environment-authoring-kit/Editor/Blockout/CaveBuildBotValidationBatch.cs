#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Read-only pipeline audit for cursor-bot — probes and JSON export only (no meat loop, no scene save).
    /// Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RunBotPipelineAudit
    /// </summary>
    public static class CaveBuildBotValidationBatch
    {
        public const string ReportRel = CaveBuildAgentContextExporter.Folder + "/CaveBuildBotValidationReport.json";

        public static void Run()
        {
            TryOpenWorldScene();
            CaveBuildCompileGate.RequestRecompileAndExportDiagnostics();

            var checks = new System.Collections.Generic.List<CheckRow>();
            void Add(string id, bool pass, string detail, bool blocking = false)
            {
                checks.Add(new CheckRow(id, pass, detail, blocking));
            }

            if (CaveBuildCompileGate.HasBlockingErrors())
            {
                Add("compile.clean", false, "Verified compile errors remain.", blocking: true);
                WriteReport(checks, 2);
                EditorApplication.Exit(2);
                return;
            }

            Add("compile.clean", true, "No blocking compile errors.");

            var caveRoot = FindCaveRoot();
            if (caveRoot == null)
            {
                Add("scene.cave_root", false, "Cave root not found in open scene.", blocking: true);
                WriteReport(checks, 3);
                EditorApplication.Exit(3);
                return;
            }

            Add("scene.cave_root", true, caveRoot.name);

            var ground = SceneGroundResolver.Resolve();
            var request = ResolveRequest(caveRoot);

            var surfaceRoute = SurfaceRouteProbeRunner.Run(caveRoot, lightweightBuildProbe: true);
            SurfaceRouteProbeRunner.Export(surfaceRoute);
            Add(
                "probe.surface_route",
                surfaceRoute.Passed,
                surfaceRoute.Passed
                    ? $"PASS — {surfaceRoute.WaypointCount} waypoints, mouth={surfaceRoute.ReachedCaveMouth}"
                    : $"FAIL — {surfaceRoute.Issues.Count} issue(s)");

            var caveRoute = CaveRouteProbeRunner.Run(caveRoot);
            CaveRouteProbeRunner.Export(caveRoute, caveRoot);
            Add(
                "probe.cave_route",
                caveRoute.Passed,
                caveRoute.Passed
                    ? "PASS — underground route traversable"
                    : $"FAIL — {caveRoute.Issues.Count} issue(s)");

            var surfaceProbe = SurfacePlaytestValidator.Run(caveRoot);
            SurfacePlaytestValidator.Export(surfaceProbe);
            Add(
                "probe.surface_walkin",
                surfaceProbe.Passed,
                surfaceProbe.Passed
                    ? "PASS — surface walk-in probe"
                    : $"FAIL — {surfaceProbe.Issues.Count} issue(s)");

            if (ground != null)
            {
                var layout = CaveBuildWorldLayoutAudit.Run(ground, request);
                var layoutOk = !layout.blocksSurfaceContinue && !layout.blocksCaveQueue;
                Add(
                    "audit.world_layout",
                    layoutOk,
                    layoutOk
                        ? $"PASS — seam {layout.maxSeamGapMeters:F2}m"
                        : $"FAIL — {layout.suggestedFix}");
            }
            else
            {
                Add("audit.world_layout", false, "No scene ground anchor — skipped layout audit.");
            }

            if (!CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot))
            {
                var shell = CaveBuildVisualShellAuditor.Audit(caveRoot);
                shell.CollectIssues(compactRoute: true, layoutPrototype: false);
                var shellScore = shell.ComputeScore(compactRoute: true, layoutPrototype: false);
                var shellOk = shellScore >= CaveBuildQualityRubric.StagePassScore && shell.Issues.Count == 0;
                Add(
                    "audit.visual_shell",
                    shellOk,
                    shellOk
                        ? $"PASS — score {shellScore}"
                        : $"FAIL — score {shellScore}, {shell.Issues.Count} issue(s)");
            }

            var invisible = CavePlayabilityValidator.CountInvisibleSolidColliders(caveRoot);
            Add(
                "audit.geometry_integrity",
                invisible == 0,
                invisible == 0 ? "PASS — no invisible solids" : $"FAIL — {invisible} invisible solid collider(s)");

            var combat = CaveCombatProbeRunner.Run(caveRoot);
            CaveCombatProbeRunner.Export(combat, caveRoot);
            Add(
                "probe.combat",
                combat.Passed,
                combat.Passed
                    ? "PASS — spawners + combat sim"
                    : $"FAIL — {combat.Issues.Count} issue(s)");

            var triCount = CountCaveTriangles(caveRoot);
            var perfOk = triCount <= 800_000;
            Add(
                "gate.performance_tri",
                perfOk,
                perfOk ? $"PASS — {triCount:N0} tris" : $"WARN — {triCount:N0} tris (>800k)");

            var hubChecklist = InvokeHubVoid("HubDemoSmokeExporter", "Export");
            Add("gameplay.smoke_checklist", hubChecklist, "HubDemoSmoke checklist exported.");

            var competitionOk = TryCompetitionSmoke(out var competitionDetail);
            Add(
                "competition.smoke",
                competitionOk,
                competitionOk ? "PASS — ONNX + streaming assets" : competitionDetail);

            var blockingFail = checks.Exists(c => !c.Pass && c.Blocking);
            var anyFail = checks.Exists(c => !c.Pass);
            var exit = blockingFail ? 3 : anyFail ? 1 : 0;
            WriteReport(checks, exit);
            Debug.Log($"[CaveBuild] Pipeline audit complete — exit={exit} checks={checks.Count}");
            EditorApplication.Exit(exit);
        }

        readonly struct CheckRow
        {
            public readonly string Id;
            public readonly bool Pass;
            public readonly string Detail;
            public readonly bool Blocking;

            public CheckRow(string id, bool pass, string detail, bool blocking = false)
            {
                Id = id;
                Pass = pass;
                Detail = detail;
                Blocking = blocking;
            }
        }

        static void WriteReport(System.Collections.Generic.List<CheckRow> checks, int exitCode)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ReportRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"capturedUtc\": \"{DateTime.UtcNow:o}\",");
            sb.AppendLine($"  \"exitCode\": {exitCode},");
            sb.AppendLine($"  \"readOnly\": true,");
            sb.AppendLine("  \"checks\": [");
            for (var i = 0; i < checks.Count; i++)
            {
                var c = checks[i];
                var comma = i < checks.Count - 1 ? "," : "";
                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{Escape(c.Id)}\",");
                sb.AppendLine($"      \"pass\": {(c.Pass ? "true" : "false")},");
                sb.AppendLine($"      \"blocking\": {(c.Blocking ? "true" : "false")},");
                sb.AppendLine($"      \"detail\": \"{Escape(c.Detail)}\"");
                sb.AppendLine($"    }}{comma}");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
        }

        static bool InvokeHubVoid(string typeName, string methodName)
        {
            var type = Type.GetType($"{typeName}, Hub.Editor");
            var method = type?.GetMethod(
                methodName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null)
                return false;

            method.Invoke(null, null);
            return true;
        }

        static bool TryCompetitionSmoke(out string detail)
        {
            detail = "Hub.Editor CompetitionSmokeTest missing";
            var type = Type.GetType("CompetitionSmokeTest, Hub.Editor");
            var method = type?.GetMethod(
                "RunAll",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null)
                return false;

            var args = new object[] { null };
            var ok = method.Invoke(null, args) is bool pass && pass;
            detail = args[0] as string ?? (ok ? "PASS" : "FAIL");
            return ok;
        }

        static int CountCaveTriangles(Transform caveRoot)
        {
            var total = 0;
            foreach (var mf in caveRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh != null)
                    total += mf.sharedMesh.triangles.Length / 3;
            }

            return total;
        }

        static WorldGenerationRequest ResolveRequest(Transform caveRoot)
        {
            var request = CaveBuildRecipeLibrary.BuildRequestFromActiveRecipe(out _);
            request ??= new WorldGenerationRequest
            {
                UseSplineMesh = true,
                UseBlockTunnel = true,
                UseTrue3DCaveSystem = true,
                UseLayoutPrototype = CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot),
            };

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta != null)
            {
                request.Seed = meta.seed;
                request.CaveTunnelSegments = meta.tunnelSegments;
                request.CaveChamberCount = meta.chamberCount;
                request.MazeGenFlavor = meta.mazeGenFlavor;
            }

            return request;
        }

        static Transform FindCaveRoot()
        {
            var grid = GameObject.Find("Grid");
            if (grid != null)
            {
                var t = grid.transform.Find(CaveGeometryPaths.CaveSystemRootName);
                if (t != null)
                    return t;
                t = grid.transform.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
                if (t != null)
                    return t;
            }

            var legacy = GameObject.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
            return legacy != null ? legacy.transform : null;
        }

        static void TryOpenWorldScene()
        {
            foreach (var path in new[]
                     {
                         "Assets/TheStart.unity",
                         "Assets/MainScene.unity",
                         "Assets/NeonCity.unity",
                     })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                    continue;
                if (SceneManager.GetActiveScene().path != path)
                    EnvironmentKitSceneSafeguards.TryOpenSceneByPath(path);
                return;
            }
        }

        static string Escape(string v) =>
            string.IsNullOrEmpty(v) ? string.Empty : v.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
#endif
