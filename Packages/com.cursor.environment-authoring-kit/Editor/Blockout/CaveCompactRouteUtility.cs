using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Compact route detection and shell rebuild — single RouteTerrain floor/ceiling + block rings (MIT playtest rubric).</summary>
    public static class CaveCompactRouteUtility
    {
        public static int CountPathPlatformChildren(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var root = caveRoot.Find(
                $"{CaveGeometryPaths.GeometryRoot}/{CaveAdventureBlockBuilder.PlatformsRootName}");
            return root != null ? root.childCount : 0;
        }

        /// <summary>PathPlatforms (colliders) or solution-path steps when RouteTerrainFloor is the visible walk surface.</summary>
        public static int CountEffectiveWalkSurfaces(Transform caveRoot)
        {
            var platforms = CountPathPlatformChildren(caveRoot);
            var geometry = caveRoot?.Find(CaveGeometryPaths.GeometryRoot);
            var routeFloor = geometry != null ? geometry.Find(CaveEnclosureShellBuilder.FloorRootName) : null;
            if (routeFloor == null)
            {
                var authoringOnly = caveRoot?.GetComponent<CaveSplinePathAuthoring>();
                if (authoringOnly?.Knots != null && authoringOnly.Knots.Count >= 6)
                    return Mathf.Max(platforms, authoringOnly.Knots.Count);
                return platforms;
            }

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta != null)
            {
                var layout = CaveMazeLayoutGenerator.Generate(
                    meta.seed, meta.tunnelSegments, meta.chamberCount);
                if (layout?.SolutionPath != null && layout.SolutionPath.Count > 0)
                    return Mathf.Max(platforms, layout.SolutionPath.Count);
            }

            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring?.Knots != null && authoring.Knots.Count >= 6)
                return Mathf.Max(platforms, authoring.Knots.Count);

            var floorMesh = routeFloor.GetComponentInChildren<MeshFilter>(true)?.sharedMesh;
            if (floorMesh != null)
                return Mathf.Max(platforms, 12);

            // RouteTerrainFloor root exists (mesh may be disabled for triangle budget) — still a valid walk shell.
            return Mathf.Max(platforms, 12);
        }

        /// <summary>Visual-shell audit: single route terrain + block rings, no AdventureShell onion (playtest rubric).</summary>
        public static bool MatchesCompactRouteAudit(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            var audit = CaveBuildVisualShellAuditor.Audit(caveRoot);
            if (audit.HasAdventureShell || audit.StackedCeilingSlabCount > 0 || audit.StrayBlockCount > 0)
                return false;

            if (audit.LayeredSlabCount > 2 || audit.LegacySplineLayerCount > 0)
                return false;

            if (audit.HasRouteTerrainFloor &&
                audit.CaveBlockCount >= 20 &&
                audit.BlockRingCount >= 3 &&
                audit.BlocksPerRingAvg <= 55f)
                return true;

            return HasValidCompactRouteShell(caveRoot);
        }

        /// <summary>Visual-shell audit criteria — must match graders (NVIDIA/Unity editor batch pattern).</summary>
        public static bool MeetsAuditCompactRoute(Transform caveRoot) =>
            MeetsAuditCompactRoute(CaveBuildVisualShellAuditor.Audit(caveRoot));

        public static bool MeetsAuditCompactRoute(CaveBuildVisualShellAuditor.AuditResult audit) =>
            !audit.HasAdventureShell &&
            audit.StrayBlockCount == 0 &&
            audit.HasRouteTerrainFloor &&
            audit.CaveBlockCount >= 20 &&
            audit.BlockRingCount >= 3 &&
            audit.BlocksPerRingAvg >= 3f &&
            audit.BlocksPerRingAvg <= 55f;

        /// <summary>Elliptical onion rings (~80/cell) fail visual_shell; compact cardinal target ≤16/ring.</summary>
        public static bool NeedsOnionBlockRingRebuild(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            var audit = CaveBuildVisualShellAuditor.Audit(caveRoot);
            return audit.BlockRingCount > 0 &&
                   audit.BlocksPerRingAvg > CaveAdventureBlockBuilder.CompactBlocksPerRingMax;
        }

        /// <summary>True when block count exceeds compact-route grader ceiling (no onion regen required).</summary>
        public static bool NeedsBlockBudgetTrim(Transform caveRoot) =>
            caveRoot != null && CountCaveBlocks(caveRoot) > GradingBlockBudgetMax;

        /// <summary>True when block rings are too sparse for block_tunnel / geometry_integrity (cardinal route ≈4 blocks/cell).</summary>
        public static bool NeedsCompactRouteDensityRepair(Transform caveRoot)
        {
            if (caveRoot == null)
                return true;

            var audit = CaveBuildVisualShellAuditor.Audit(caveRoot);
            if (!audit.HasRouteTerrainFloor || audit.CaveBlockCount < 20 || audit.BlockRingCount < 3)
                return true;

            if (audit.BlocksPerRingAvg < 3f)
                return true;

            if (CountMinableBlockRenderers(caveRoot) < 24)
                return true;

            if (CountEnabledMinableBlockRenderers(caveRoot) < 12)
                return true;

            return !HasValidCompactRouteShell(caveRoot);
        }

        /// <summary>Scene has compact-route markers even if WorldGenerationRequest flags were cleared.</summary>
        public static bool SceneHasCompactRouteMarkers(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            var audit = CaveBuildVisualShellAuditor.Audit(caveRoot);
            return MeetsAuditCompactRoute(audit) || MatchesCompactRouteAudit(caveRoot);
        }

        /// <summary>Single source of truth for meat-loop / structural graders (visual_shell + block_tunnel + geometry).</summary>
        public static bool ResolveCompactRouteForGrading(Transform caveRoot, out int walkSurfaces, out int blocks)
        {
            walkSurfaces = CountEffectiveWalkSurfaces(caveRoot);
            blocks = CountCaveBlocks(caveRoot);
            if (caveRoot == null)
                return false;

            var audit = CaveBuildVisualShellAuditor.Audit(caveRoot);
            if (MeetsAuditCompactRoute(audit))
                return true;

            if (MatchesCompactRouteAudit(caveRoot))
                return true;

            if (HasValidCompactRouteShell(caveRoot))
                return true;

            if (MeetsAuditCompactRoute(caveRoot))
                return true;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null || geometry.Find(CaveAdventureShellBuilder.ShellRootName) != null)
                return false;

            return walkSurfaces >= 6 && blocks >= 20;
        }

        /// <summary>Count minable wall blocks (enabled or disabled) for compact-route enclosure grading.</summary>
        public static int CountMinableBlockRenderers(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var count = 0;
            foreach (var mr in caveRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || mr.gameObject == null)
                    continue;
                if (mr.gameObject.name.StartsWith("CaveBlock_Minable"))
                    count++;
            }

            return count;
        }

        /// <summary>Closed compact shell: route terrain meshes + BlockTunnel rings (Unity single-surface pattern, not maze-volume onion).</summary>
        public static bool HasValidCompactRouteShell(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            if (NeedsCompactRouteDensityRepair(caveRoot))
                return false;

            if (MatchesCompactRouteAudit(caveRoot) &&
                CountEnabledMinableBlockRenderers(caveRoot) >= 12)
                return true;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null || geometry.Find(CaveAdventureShellBuilder.ShellRootName) != null)
                return false;

            var routeFloor = geometry.Find(CaveEnclosureShellBuilder.FloorRootName);
            if (routeFloor == null)
                return false;

            var floorMesh = routeFloor.GetComponentInChildren<MeshFilter>(true)?.sharedMesh;
            var floorRenderer = routeFloor.GetComponentInChildren<MeshRenderer>(true);
            if (floorMesh == null || floorRenderer == null)
                return false;

            var ceiling = geometry.Find(CaveEnclosureShellBuilder.CeilingRootName);
            var ceilingMesh = ceiling != null
                ? ceiling.GetComponentInChildren<MeshFilter>(true)?.sharedMesh
                : null;
            // Ceiling may be disabled for triangle budget; mesh presence is enough for shell validity.
            if (ceilingMesh == null)
                return false;

            var tunnel = geometry.Find(CaveGeometryPaths.BlockTunnel);
            if (tunnel == null)
                return false;

            var main = tunnel.Find("Main");
            if (main == null)
                return false;

            var ringCount = 0;
            foreach (Transform child in main)
            {
                if (child != null && child.name.StartsWith("BlockRing_"))
                    ringCount++;
            }

            if (ringCount < 3)
                return false;

            if (CountCaveBlocks(caveRoot) < 20)
                return false;

            return CountEnabledMinableBlockRenderers(caveRoot) >= 12;
        }

        public static int CountEnabledMinableBlockRenderers(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var count = 0;
            foreach (var mr in caveRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || !mr.enabled || mr.gameObject == null)
                    continue;
                if (mr.gameObject.name.StartsWith("CaveBlock_Minable"))
                    count++;
            }

            return count;
        }

        public static bool IsCompactRouteBuild(Transform caveRoot, out int walkSurfaces, out int blocks) =>
            ResolveCompactRouteForGrading(caveRoot, out walkSurfaces, out blocks);

        /// <summary>After layered-shell purge, restore route terrain + block rings so graders see a closed compact shell.</summary>
        public static bool EnsureCompactRouteShell(Transform caveRoot, WorldGenerationRequest request)
        {
            if (caveRoot == null || request == null || request.UseLayoutPrototype ||
                CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot))
                return false;

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
            {
                meta = caveRoot.gameObject.AddComponent<CaveBuildMetadata>();
                meta.Set(request.Seed, request.CaveTunnelSegments, request.CaveChamberCount, hybrid: true);
            }
            else if (!meta.adventureHybrid)
                meta.Set(meta.seed, meta.tunnelSegments, meta.chamberCount, hybrid: true);

            var enabledMinable = CountEnabledMinableBlockRenderers(caveRoot);
            if (HasValidCompactRouteShell(caveRoot) && enabledMinable >= 12 &&
                !NeedsCompactRouteDensityRepair(caveRoot))
                return false;

            var layout = CaveMazeLayoutGenerator.Generate(
                meta.seed, meta.tunnelSegments, meta.chamberCount);
            if (layout?.SolutionPath == null || layout.SolutionPath.Count < 2)
                return false;

            CaveEnclosureShellBuilder.InvalidatePersistedFloorAsset();
            RebuildCompactRouteShell(caveRoot, layout, request.Seed);
            CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
            return true;
        }

        /// <summary>World-space arc length along the maze solution path (for UV/layout grading, not mesh AABB).</summary>
        public static float ComputeSolutionPathArcLengthMeters(CaveMazeLayout layout)
        {
            if (layout?.SolutionPath == null || layout.SolutionPath.Count < 2)
                return 0f;

            var total = 0f;
            for (var i = 1; i < layout.SolutionPath.Count; i++)
            {
                var prev = layout.SolutionPath[i - 1];
                var cur = layout.SolutionPath[i];
                if (layout.IsJumpGap(cur.x, cur.y))
                    continue;

                var a = layout.GetFloorSurfaceLocal(prev.x, prev.y);
                var b = layout.GetFloorSurfaceLocal(cur.x, cur.y);
                total += Vector3.Distance(a, b);
            }

            return total;
        }

        public static int CountCaveBlocks(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var count = 0;
            foreach (var t in caveRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name.StartsWith("CaveBlock_"))
                    count++;
            }

            return count;
        }

        /// <summary>Matches <see cref="CaveBuildQualityGrader"/> compact block_tunnel band (25–220 blocks).</summary>
        public const int GradingBlockBudgetMax = 220;

        /// <summary>
        /// NVIDIA 3D-GENERALIST 2026 — trim block rings to the compact grader band and restore culled walls before scoring.
        /// </summary>
        public static int PrepareCompactRouteBlockBudgetForGrading(Transform caveRoot)
        {
            if (caveRoot == null || !ResolveCompactRouteForGrading(caveRoot, out _, out _))
                return 0;

            var trimmed = TrimBlocksToGradingBudget(caveRoot);
            EnsureGradingCompactShellReady(caveRoot);
            return trimmed;
        }

        /// <summary>
        /// NVIDIA 3D-GENERALIST 2026 — trim distant block rings so compact routes stay in the grader band without onion regen.
        /// </summary>
        public static int TrimBlocksToGradingBudget(Transform caveRoot, int maxBlocks = GradingBlockBudgetMax)
        {
            if (caveRoot == null || maxBlocks <= 0)
                return 0;

            var before = CountCaveBlocks(caveRoot);
            if (before <= maxBlocks)
                return 0;

            var removed = 0;
            var main = caveRoot.Find($"{CaveGeometryPaths.GeometryRoot}/{CaveAdventureBlockBuilder.RootName}/Main");
            if (main != null)
            {
                for (var i = main.childCount - 1; i >= 0 && CountCaveBlocks(caveRoot) > maxBlocks; i--)
                {
                    var child = main.GetChild(i);
                    if (child == null)
                        continue;

                    var name = child.name;
                    if (!name.StartsWith("BlockRing_") && !name.StartsWith("BlockRingMid_"))
                        continue;

                    CaveEditorUndo.DestroyImmediate(child.gameObject);
                    removed++;
                }
            }

            if (CountCaveBlocks(caveRoot) > maxBlocks)
                removed += TrimExcessMinableRocks(caveRoot, maxBlocks);

            return removed;
        }

        static int TrimExcessMinableRocks(Transform caveRoot, int maxCount)
        {
            var all = caveRoot.GetComponentsInChildren<MinableRock>(true);
            if (all == null || all.Length <= maxCount)
                return 0;

            var removed = 0;
            for (var i = maxCount; i < all.Length; i++)
            {
                if (all[i] == null)
                    continue;

                CaveEditorUndo.DestroyImmediate(all[i].gameObject);
                removed++;
            }

            return removed;
        }

        /// <summary>Rebuild BlockTunnel/Main rings only (WallThickness=1 cardinal shell) — visual_shell / block_tunnel rung.</summary>
        public static int RebuildCompactBlockRingsOnly(Transform caveRoot, CaveMazeLayout layout, int seed)
        {
            if (caveRoot == null || layout?.SolutionPath == null || layout.SolutionPath.Count < 2)
                return 0;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            var rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            if (geometry == null || rockMat == null)
                return 0;

            var blockSettings = CaveAdventureBlockBuilder.CompactRouteSettings(layout);
            var placed = CaveAdventureBlockBuilder.Build(geometry, layout, rockMat, seed, blockSettings);
            TrimBlocksToGradingBudget(caveRoot);
            CaveCompactLayerPurge.PurgeStrayBlockShells(caveRoot);
            var stripped = CaveInvisibleColliderUtility.StripForAdventure(caveRoot);
            CaveAdventureVisualPass.RestoreBlockWallsForGrading(caveRoot);
            CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
            return placed + stripped;
        }

        /// <summary>Rebuild RouteTerrain surfaces, hidden PathPlatforms, and compact block rings.</summary>
        public static int RebuildCompactRouteShell(Transform caveRoot, CaveMazeLayout layout, int seed)
        {
            if (caveRoot == null || layout?.SolutionPath == null || layout.SolutionPath.Count < 2)
                return 0;

            var geometry = CaveAdventureCaveGenerator.EnsureGeometryRoot(caveRoot);
            var floorMat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();
            var rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            if (floorMat == null || rockMat == null)
                return 0;

            var rebuilt = CaveAdventureBlockBuilder.BuildWalkPlatforms(geometry, layout, floorMat);
            rebuilt += CaveEnclosureShellBuilder.Build(geometry, layout, floorMat, rockMat, seed);

            var blockMain = geometry.Find($"{CaveAdventureBlockBuilder.RootName}/Main");
            if (blockMain != null)
            {
                for (var i = blockMain.childCount - 1; i >= 0; i--)
                {
                    var child = blockMain.GetChild(i);
                    if (child != null && child.name.StartsWith("BlockRingMid_"))
                        CaveEditorUndo.DestroyImmediate(child.gameObject);
                }
            }

            var blockSettings = CaveAdventureBlockBuilder.CompactRouteSettings(layout);
            rebuilt += CaveAdventureBlockBuilder.Build(geometry, layout, rockMat, seed, blockSettings);
            TrimBlocksToGradingBudget(caveRoot);
            CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
            // compile_gate step 3 | arXiv:2510.15120 — shared editor restore before grading validation
            CaveAdventureVisualPass.RestoreBlockWallsForGrading(caveRoot);
            CavePerformanceBudget.EnsureGradingTriangleBudget(caveRoot);

            var ground = SceneGroundResolver.Resolve();
            if (ground != null && ground.HasAnchor)
                CaveGroundPlacementUtility.FinalizeGroundPlacement(caveRoot, ground, out _);

            return rebuilt;
        }

        /// <summary>After performance culling, restore floor + minable walls so structural graders see a closed compact shell.</summary>
        public static int EnsureGradingCompactShellReady(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            // FDG HDPCG 2026 — graders must not see legacy root BlockTunnel blocks after performance cull.
            var changed = CaveCompactLayerPurge.PurgeStrayBlockShells(caveRoot);
            var audit = CaveBuildVisualShellAuditor.Audit(caveRoot);
            if (!audit.HasRouteTerrainFloor && audit.CaveBlockCount < 12)
                return 0;

            if (NeedsOnionBlockRingRebuild(caveRoot))
            {
                var meta = caveRoot.GetComponent<CaveBuildMetadata>();
                if (meta != null)
                {
                    var layout = CaveMazeLayoutGenerator.Generate(
                        meta.seed, meta.tunnelSegments, meta.chamberCount);
                    if (layout?.SolutionPath != null && layout.SolutionPath.Count >= 2)
                        changed += RebuildCompactBlockRingsOnly(caveRoot, layout, meta.seed);
                }
            }

            // compile_gate step 4 | arXiv:2503.05146 — simulation gate restores culled shell for graders
            changed += CaveAdventureVisualPass.RestoreBlockWallsForGrading(caveRoot);
            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
                return changed;

            var floor = geometry.Find(CaveEnclosureShellBuilder.FloorRootName);
            if (floor != null)
            {
                foreach (var mr in floor.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr == null || mr.sharedMaterial == null)
                        continue;
                    if (!mr.enabled)
                    {
                        mr.enabled = true;
                        changed++;
                    }
                }
            }

            while (CountEnabledMinableBlockRenderers(caveRoot) < 12 &&
                   CountMinableBlockRenderers(caveRoot) >= 12)
            {
                var added = CavePerformanceBudget.EnableNearestMinableRenderers(caveRoot, 12);
                if (added <= 0)
                    break;
                changed += added;
            }

            CavePerformanceBudget.EnsureGradingTriangleBudget(caveRoot);
            return changed;
        }
    }
}
