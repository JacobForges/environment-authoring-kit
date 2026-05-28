using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// visual_shell meat-loop: rebuild RouteTerrain floor/ceiling (Unity 6 MeshData strips), clear route headroom intruders,
    /// and re-seat mouth depth (USGS DS 926 void scale — structure-only, no water table).
    /// </summary>
    static class CaveVisualShellRouteRepair
    {
        static float HeadroomProbePaddingMeters => CaveThirdPersonClearance.HeadroomProbePaddingMeters;

        public static bool TryRepair(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out string actionTaken)
        {
            actionTaken = string.Empty;
            if (caveRoot == null || request == null)
                return false;

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
                return false;

            var layout = CaveMazeLayoutGenerator.Generate(
                meta.seed, meta.tunnelSegments, meta.chamberCount);
            if (layout?.SolutionPath == null || layout.SolutionPath.Count < 2)
                return false;

            var probeBefore = CaveRouteProbeRunner.Run(caveRoot);
            var geometry = CaveAdventureCaveGenerator.EnsureGeometryRoot(caveRoot);
            var floorMat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();
            var rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            if (geometry == null || floorMat == null || rockMat == null)
                return false;

            var changed = false;
            var parts = new System.Collections.Generic.List<string>();

            var needsShell = !probeBefore.Passed || NeedsRouteShellRebuild(caveRoot);
            var needsBlocks = CaveCompactRouteUtility.NeedsOnionBlockRingRebuild(caveRoot) &&
                              !CaveCompactRouteUtility.NeedsBlockBudgetTrim(caveRoot);

            if (needsShell)
            {
                CaveEnclosureShellBuilder.InvalidatePersistedFloorAsset();
                var shellPieces = CaveEnclosureShellBuilder.Build(geometry, layout, floorMat, rockMat, meta.seed);
                if (shellPieces > 0)
                {
                    changed = true;
                    parts.Add($"RouteTerrain floor+ceiling ({shellPieces} mesh(es))");
                }
            }

            if (needsBlocks)
            {
                var blocks = CaveCompactRouteUtility.RebuildCompactBlockRingsOnly(caveRoot, layout, request.Seed);
                if (blocks > 0)
                {
                    changed = true;
                    parts.Add($"{blocks} compact block(s) (WallThickness=1 cardinal)");
                }
            }
            else if (CaveCompactRouteUtility.NeedsBlockBudgetTrim(caveRoot))
            {
                var trimmed = CaveCompactRouteUtility.PrepareCompactRouteBlockBudgetForGrading(caveRoot);
                if (trimmed > 0)
                {
                    changed = true;
                    parts.Add($"trimmed {trimmed} block(s) to grader band (≤{CaveCompactRouteUtility.GradingBlockBudgetMax})");
                }
            }

            var cleared = ClearRouteHeadroomIntruders(caveRoot, layout);
            if (cleared > 0)
            {
                changed = true;
                parts.Add($"cleared {cleared} headroom intruder(s)");
            }

            if (CaveFloorSafetyUtility.EnsureRouteTerrainPlayCollider(caveRoot) > 0)
            {
                changed = true;
                parts.Add("walk MeshCollider");
            }

            EnsureRouteTerrainRenderer(geometry);
            CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
            Physics.SyncTransforms();

            if (ground != null && ground.HasAnchor)
            {
                var placementErr = CaveGroundPlacementUtility.MeasureRootPlacementError(caveRoot, ground);
                var mouthErr = Mathf.Abs(CaveGroundPlacementUtility.MeasureEntranceMouthSurfaceError(caveRoot, ground));
                var depthErr = Mathf.Abs(CaveGroundPlacementUtility.MeasureRootDepthError(caveRoot, ground));
                var xzLocked = CaveBuildMetadata.ShouldPreserveRootXZ(caveRoot);
                var needsGroundFix = mouthErr > CaveGroundPlacementUtility.MaxEntranceMouthSurfaceErrorMeters ||
                                     depthErr > CaveGroundPlacementUtility.MaxVerticalErrorMeters ||
                                     !xzLocked && (placementErr.magnitude >
                                         CaveGroundPlacementUtility.MaxHorizontalErrorMeters ||
                                         !CaveGroundPlacementUtility.IsGroundPlacementAcceptable(caveRoot, ground));

                if (needsGroundFix)
                {
                    if (xzLocked)
                    {
                        if (CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(
                                caveRoot, ground, out var depthOnlyMsg))
                        {
                            changed = true;
                            parts.Add(depthOnlyMsg);
                        }
                    }
                    else if (CaveGroundPlacementUtility.TryRepairLockedGroundPlacement(caveRoot, ground, out var groundMsg))
                    {
                        changed = true;
                        parts.Add(groundMsg);
                    }
                    else if (CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(
                                 caveRoot, ground, out var snapMsg))
                    {
                        changed = true;
                        parts.Add(snapMsg);
                    }
                }
            }

            if (CaveCompactRouteUtility.NeedsBlockBudgetTrim(caveRoot))
            {
                var trimmed = CaveCompactRouteUtility.PrepareCompactRouteBlockBudgetForGrading(caveRoot);
                if (trimmed > 0)
                {
                    changed = true;
                    parts.Add($"trimmed {trimmed} block(s) to grader band (≤{CaveCompactRouteUtility.GradingBlockBudgetMax})");
                }
            }

            var shellReady = CaveCompactRouteUtility.EnsureGradingCompactShellReady(caveRoot);
            if (shellReady > 0)
            {
                changed = true;
                parts.Add($"grading shell ready ({shellReady} change(s))");
            }

            var renderersOff = CavePerformanceBudget.DisableSplineSubtreeRenderers(caveRoot);
            renderersOff += CavePerformanceBudget.ApplyBlockRendererPolicyOnly(caveRoot);
            renderersOff += CavePerformanceBudget.EnsureGradingTriangleBudget(caveRoot);
            var tris = CavePerformanceBudget.EstimateEnabledTriangleCount(caveRoot);
            if (renderersOff > 0)
            {
                changed = true;
                parts.Add($"perf budget (~{tris} tris, {renderersOff} renderer(s) off)");
            }

            CaveInvisibleColliderUtility.StripForAdventure(caveRoot);

            var probeAfter = CaveRouteProbeRunner.Run(caveRoot);
            actionTaken = parts.Count > 0
                ? string.Join("; ", parts) + $" — route probe {probeBefore.Issues.Count}→{probeAfter.Issues.Count} issue(s)."
                : $"Route shell OK — probe {probeAfter.Issues.Count} issue(s).";

            return changed;
        }

        static bool NeedsRouteShellRebuild(Transform caveRoot)
        {
            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
                return true;

            var floor = geometry.Find(CaveEnclosureShellBuilder.FloorRootName);
            if (floor == null)
                return true;

            var mf = floor.GetComponent<MeshFilter>();
            var mc = floor.GetComponent<MeshCollider>();
            return mf == null || mf.sharedMesh == null || mc == null || !mc.enabled;
        }

        static void EnsureRouteTerrainRenderer(Transform geometry)
        {
            if (geometry == null)
                return;

            foreach (var name in new[]
                     {
                         CaveEnclosureShellBuilder.FloorRootName,
                         CaveEnclosureShellBuilder.CeilingRootName
                     })
            {
                var surface = geometry.Find(name);
                if (surface == null)
                    continue;

                var mr = surface.GetComponent<MeshRenderer>();
                if (mr != null && !mr.enabled)
                {
                    CaveEditorUndo.RecordObject(mr, "Enable Route Terrain");
                    mr.enabled = true;
                }
            }
        }

        /// <summary>FDG HDPCG 2026 — keep third-person walk clearance; remove cardinal blocks inside the route column.</summary>
        public static int ClearRouteHeadroomIntruders(Transform caveRoot, CaveMazeLayout layout)
        {
            if (caveRoot == null || layout?.SolutionPath == null)
                return 0;

            var tunnel = CaveGeometryPaths.FindBlockTunnel(caveRoot);
            if (tunnel == null)
                return 0;

            var removed = 0;
            var minClear = CaveMazeLayout.MinWalkClearanceMeters + HeadroomProbePaddingMeters;
            var halfX = layout.PlatformSpan * 0.38f;
            var halfZ = layout.PlatformDepth * 0.38f;
            var halfY = minClear * 0.5f;

            foreach (var cell in layout.SolutionPath)
            {
                if (layout.IsJumpGap(cell.x, cell.y))
                    continue;

                var floor = caveRoot.TransformPoint(layout.GetFloorSurfaceLocal(cell.x, cell.y));
                var center = floor + Vector3.up * halfY;
                var halfExtents = new Vector3(halfX, halfY, halfZ);
                var hits = Physics.OverlapBox(
                    center,
                    halfExtents,
                    Quaternion.identity,
                    ~0,
                    QueryTriggerInteraction.Ignore);

                foreach (var col in hits)
                {
                    if (col == null || col.isTrigger)
                        continue;
                    if (!col.transform.IsChildOf(tunnel))
                        continue;
                    if (!col.gameObject.name.StartsWith("CaveBlock_"))
                        continue;
                    if (CaveColliderUtility.IsProtectedPlayCollider(col, caveRoot))
                        continue;

                    CaveEditorUndo.DestroyImmediate(col.gameObject);
                    removed++;
                }
            }

            return removed;
        }
    }
}
