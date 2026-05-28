#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using EnvironmentAuthoringKit;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    static class CaveBuildWorkflowGuardrails
    {
        const string ResearchCacheIndexRel = "Assets/EnvironmentKit/ResearchCache/index.json";
        const string ResearchCatalogSeedRel =
            "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/research-catalog.seed.json";

        [Serializable]
        class PlacementPlanFile
        {
            public int placedCount;
            public int targetCount;
            public PlacementPlanEntry[] placements;
        }

        [Serializable]
        class PlacementPlanEntry
        {
            public float worldX;
            public float worldZ;
        }

        public static bool TryArtifactPreflight(out string message)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var missing = new List<string>();
            if (!File.Exists(Path.Combine(hub, ResearchCacheIndexRel)))
                missing.Add(ResearchCacheIndexRel);
            if (!File.Exists(Path.Combine(hub, ResearchCatalogSeedRel)))
                missing.Add(ResearchCatalogSeedRel);

            if (missing.Count > 0)
            {
                message =
                    "artifact_preflight failed — missing: " + string.Join(", ", missing) +
                    ". Run Tools/cave-grader sync-research-cache + sync-research-catalog.";
                return false;
            }

            message = "artifact_preflight passed (research cache + catalog present).";
            return true;
        }

        public static void ClassifyTerrainDelta(SceneGroundInfo ground, out bool neighborOnly, out string message)
        {
            neighborOnly = false;
            if (ground?.Terrain == null)
            {
                message = "terrain_delta_classifier: no terrain; default full pass.";
                return;
            }

            var tiles = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(ground.Terrain).Count;
            neighborOnly =
                SurfaceFloridaDemBuildState.AuthoritativeStampCompletedThisBuild &&
                tiles > 1;
            message = neighborOnly
                ? $"terrain_delta_classifier: neighbor-only seam delta on {tiles - 1} tile(s)."
                : $"terrain_delta_classifier: full terrain delta across {tiles} tile(s).";
        }

        public static bool AuditSurfacePropCoverage(out string message)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var generated = Path.Combine(hub, CaveBuildAgentContextExporter.Folder);
            if (!Directory.Exists(generated))
            {
                message = "surface_prop_coverage_audit: no Generated folder.";
                return false;
            }

            var files = Directory.GetFiles(generated, "SurfacePropPlacementPlan*.json");
            if (files.Length == 0)
            {
                message = "surface_prop_coverage_audit: no placement plan files found.";
                return false;
            }

            var covered = 0;
            var target = 0;
            var spreadOk = 0;
            foreach (var file in files)
            {
                try
                {
                    var text = File.ReadAllText(file);
                    var plan = JsonUtility.FromJson<PlacementPlanFile>(text);
                    if (plan == null)
                        continue;

                    covered += Mathf.Max(0, plan.placedCount);
                    target += Mathf.Max(0, plan.targetCount);

                    if (HasReasonableSpread(plan.placements))
                        spreadOk++;
                }
                catch
                {
                    // Keep audit best-effort.
                }
            }

            var ratio = target > 0 ? covered / (float)target : 0f;
            var ok = ratio >= 0.88f && spreadOk > 0;
            message =
                $"surface_prop_coverage_audit: placed={covered}, target={target}, coverage={ratio:P0}, spreadFiles={spreadOk}/{files.Length}.";
            return ok;
        }

        public static bool TryFinalSurfaceNavMeshCommit(
            SceneGroundInfo ground,
            Transform surfaceRoot,
            EnvironmentRoot envRoot,
            out string message)
        {
            message = "single_navmesh_commit skipped — missing terrain or environment root.";
            if (ground?.Terrain == null || envRoot == null)
                return false;

            var ok = SurfaceNavMeshBaker.BakePhase(envRoot.transform, ground.Terrain, surfaceRoot, out var bakeMsg);
            message = ok
                ? "single_navmesh_commit: " + bakeMsg
                : "single_navmesh_commit failed: " + bakeMsg;
            return ok;
        }

        public static bool EnsureCaveBurialEnvelope(Transform caveRoot, SceneGroundInfo ground, out string message)
        {
            message = "cave_burial_envelope_check skipped — no cave root or terrain.";
            if (caveRoot == null || ground?.Terrain == null)
                return false;

            var openings = new List<Vector3>();
            var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot, ground);
            if (mouth.sqrMagnitude > 0.01f)
                openings.Add(mouth);

            var protrusion = CaveGroundPlacementUtility.MeasureMaxCaveProtrusionAboveHeightmap(
                caveRoot, ground, openings, out _);
            if (protrusion <= 0.85f)
            {
                message = $"cave_burial_envelope_check passed (protrusion {protrusion:F2}m).";
                return true;
            }

            CaveGroundPlacementUtility.EnsureFullyBuriedUnderSurface(caveRoot, ground, out var buryMsg);
            CaveGroundPlacementUtility.ReseatCaveUnderTerrainAfterSurface(caveRoot, ground, out var reseatMsg);
            protrusion = CaveGroundPlacementUtility.MeasureMaxCaveProtrusionAboveHeightmap(
                caveRoot, ground, openings, out _);
            var fixedNow = protrusion <= 0.85f;
            message = fixedNow
                ? $"cave_burial_envelope_check corrected: {buryMsg} {reseatMsg}"
                : $"cave_burial_envelope_check still high (protrusion {protrusion:F2}m). {buryMsg}";
            return fixedNow;
        }

        public static bool PreValidationGuardrailCheck(
            WorldGenerationRequest request,
            SceneGroundInfo ground,
            Transform caveRoot,
            out string message)
        {
            if (request == null)
            {
                message = "post_meat_guardrail_check passed (no request).";
                return true;
            }

            if (!CaveBuildPhaseContractRegistry.HasPlayableCaveLayoutInScene())
            {
                message = "post_meat_guardrail_check failed — playable cave layout missing.";
                return false;
            }

            if (request.SurfaceScope == SurfaceBuildScope.FullWorld &&
                !CaveBuildSurfaceCompletionGate.IsReadyForCaveMeatLoop(request, ground))
            {
                message =
                    "post_meat_guardrail_check failed — FullWorld surface gate incomplete before validation.";
                return false;
            }

            if (caveRoot == null)
            {
                message = "post_meat_guardrail_check failed — cave root missing.";
                return false;
            }

            message = "post_meat_guardrail_check passed.";
            return true;
        }

        static bool HasReasonableSpread(PlacementPlanEntry[] placements)
        {
            if (placements == null || placements.Length < 8)
                return false;

            var minX = float.PositiveInfinity;
            var maxX = float.NegativeInfinity;
            var minZ = float.PositiveInfinity;
            var maxZ = float.NegativeInfinity;
            for (var i = 0; i < placements.Length; i++)
            {
                var p = placements[i];
                minX = Mathf.Min(minX, p.worldX);
                maxX = Mathf.Max(maxX, p.worldX);
                minZ = Mathf.Min(minZ, p.worldZ);
                maxZ = Mathf.Max(maxZ, p.worldZ);
            }

            return (maxX - minX) > 40f && (maxZ - minZ) > 40f;
        }

        static float EstimateMaxRendererY(Transform root)
        {
            var maxY = root.position.y;
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled)
                    continue;
                maxY = Mathf.Max(maxY, r.bounds.max.y);
            }

            return maxY;
        }
    }
}
#endif
