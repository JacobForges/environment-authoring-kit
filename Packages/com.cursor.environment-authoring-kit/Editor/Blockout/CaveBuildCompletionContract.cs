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
    /// <summary>Verifies FullWorld pipeline produced required scene + artifact roots.</summary>
    public static class CaveBuildCompletionContract
    {
        public const string ContractJsonRel = CaveBuildAgentContextExporter.Folder + "/CaveBuildCompletionContract.json";

        [Serializable]
        public class ContractResult
        {
            public string generatedUtc;
            public int seed;
            public string surfaceScope;
            public bool passed;
            public string[] satisfied = Array.Empty<string>();
            public string[] missing = Array.Empty<string>();
            public string[] warnings = Array.Empty<string>();
        }

        public static ContractResult Evaluate(
            WorldGenerationRequest request,
            SceneGroundInfo ground,
            LavaTubeCaveBuildReport buildReport)
        {
            var satisfied = new List<string>();
            var missing = new List<string>();
            var warnings = new List<string>();
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var seed = request?.Seed ?? 0;
            var scope = request?.SurfaceScope ?? SurfaceBuildScope.CaveOnly;

            var caveRoot = FindCaveRoot(ground);
            if (caveRoot != null)
            {
                satisfied.Add("cave_system_root");
                var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot) ??
                                 caveRoot.Find(CaveAdventureCaveGenerator.GeometryRootName);
                if (geometry != null && geometry.childCount > 0)
                    satisfied.Add("cave_geometry_children");
                else
                    missing.Add("cave_geometry_children");
            }
            else
            {
                missing.Add("cave_system_root");
            }

            if (scope == SurfaceBuildScope.FullWorld || scope == SurfaceBuildScope.SurfaceOnly)
            {
                var surface = FindSurfaceRoot();
                if (surface != null)
                    satisfied.Add("surface_world_root");
                else
                    missing.Add("surface_world_root");

                if (CaveBuildSurfaceCompletionGate.WasSurfacePipelineQueued)
                    satisfied.Add("florida_lidar_handoff");
                else if (scope == SurfaceBuildScope.FullWorld)
                    warnings.Add("florida_lidar_handoff_not_recorded");

                if (request != null && CaveBuildSurfaceCompletionGate.IsCompleteForSeed(request))
                    satisfied.Add("surface_completion_gate");
                else if (scope == SurfaceBuildScope.FullWorld)
                    warnings.Add("surface_completion_gate_pending");
            }

            var readout = Path.Combine(hub, CaveBuildCompletionSummary.ReadoutRelativePath);
            if (File.Exists(readout))
                satisfied.Add("completion_readout_md");
            else
                warnings.Add("completion_readout_md");

            var quality = Path.Combine(hub, "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json");
            if (File.Exists(quality))
                satisfied.Add("quality_report_json");
            else
                warnings.Add("quality_report_json");

            var ladder = Path.Combine(hub, CaveBuildPhaseContractRegistry.CompletionRel);
            if (File.Exists(ladder))
                satisfied.Add("ladder_completion_json");
            else
                warnings.Add("ladder_completion_json");

            if (buildReport != null && buildReport.PieceCount > 0)
                satisfied.Add("build_piece_count");
            else
                warnings.Add("build_piece_count_zero");

            var passed = missing.Count == 0;
            var result = new ContractResult
            {
                generatedUtc = DateTime.UtcNow.ToString("o"),
                seed = seed,
                surfaceScope = scope.ToString(),
                passed = passed,
                satisfied = satisfied.ToArray(),
                missing = missing.ToArray(),
                warnings = warnings.ToArray(),
            };

            var path = Path.Combine(hub, ContractJsonRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            File.WriteAllText(path, JsonUtility.ToJson(result, true), Encoding.UTF8);
            CaveBuildPipelineLog.Info(
                $"Completion contract {(passed ? "PASSED" : "FAILED")} — missing={missing.Count} warn={warnings.Count}",
                "Contract");
            return result;
        }

        static Transform FindCaveRoot(SceneGroundInfo ground)
        {
            if (ground.HasAnchor)
            {
                var t = ground.Anchor.Find(CaveGeometryPaths.CaveSystemRootName);
                if (t != null)
                    return t;
                t = ground.Anchor.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
                if (t != null)
                    return t;
            }

            return GameObject.Find(CaveGeometryPaths.CaveSystemRootName)?.transform;
        }

        static Transform FindSurfaceRoot()
        {
            var env = UnityEngine.Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            if (env == null)
                return null;
            return env.transform.Find(SurfaceWorldPaths.RootName);
        }
    }
}
#endif
