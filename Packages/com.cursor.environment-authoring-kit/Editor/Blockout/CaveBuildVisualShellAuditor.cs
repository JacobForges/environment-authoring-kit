using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Detects onion slab shells, legacy spline layers, and flat-platform stacking that numeric rubrics miss.</summary>
    public static class CaveBuildVisualShellAuditor
    {
        public struct AuditResult
        {
            public int LayeredSlabCount;
            public int LegacySplineLayerCount;
            public int VisibleFlatPlatformCount;
            public int BlockRingCount;
            public int SolutionPathSteps;
            public int CaveBlockCount;
            public float BlocksPerRingAvg;
            public bool HasRouteTerrainFloor;
            public bool HasSingleRouteCeiling;
            public int StackedCeilingSlabCount;
            public bool HasLayoutWalkFloor;
            public bool HasAdventureShell;
            public int StrayBlockCount;
            public List<string> Issues;

            public int ComputeScore(bool compactRoute, bool layoutPrototype)
            {
                var score = 100;

                if (HasAdventureShell)
                    return 0;

                if (StrayBlockCount > 0)
                    score -= Mathf.Min(40, StrayBlockCount * 8);

                if (StackedCeilingSlabCount > 0)
                    score -= Mathf.Min(80, 40 + StackedCeilingSlabCount * 12);

                if (LayeredSlabCount > 0)
                    score -= Mathf.Min(70, 25 + LayeredSlabCount * 8);

                if (LegacySplineLayerCount > 0)
                    score -= Mathf.Min(55, 20 + LegacySplineLayerCount * 10);

                if (VisibleFlatPlatformCount > 0)
                    score -= Mathf.Min(45, 15 + VisibleFlatPlatformCount * 6);

                if (layoutPrototype)
                {
                    if (HasLayoutWalkFloor && LayeredSlabCount == 0 && LegacySplineLayerCount == 0)
                        score = Mathf.Max(score, 92);
                    if (HasSingleRouteCeiling || StackedCeilingSlabCount > 0)
                        score -= 25;
                    return Mathf.Clamp(score, 0, 100);
                }

                if (compactRoute)
                {
                    if (!HasRouteTerrainFloor)
                        score -= 20;
                    if (!HasSingleRouteCeiling)
                        score -= 25;
                }

                if (BlockRingCount > 0 && BlocksPerRingAvg > 16f)
                    score -= Mathf.Min(50, Mathf.RoundToInt((BlocksPerRingAvg - 12f) * 2.5f));

                if (compactRoute && BlockRingCount > 0)
                {
                    var allowedRings = SolutionPathSteps > 0 ? SolutionPathSteps + 2 : 28;
                    var extraRings = BlockRingCount - allowedRings;
                    if (extraRings > 0)
                        score -= Mathf.Min(35, extraRings * 4);
                }

                // Material validity is graded in the materials stage — do not double-penalize here.
                return Mathf.Clamp(score, 0, 100);
            }

            public void CollectIssues(bool compactRoute, bool layoutPrototype)
            {
                Issues ??= new List<string>();
                Issues.Clear();
                if (HasAdventureShell)
                    Issues.Add("AdventureShell slab stack present — delete layered shell geometry.");

                if (StrayBlockCount > 0)
                    Issues.Add($"{StrayBlockCount} stray CaveBlock(s) outside BlockTunnel — run Remove Cave Layered Shells.");

                if (StackedCeilingSlabCount > 0)
                    Issues.Add($"{StackedCeilingSlabCount} stacked ceiling slab(s) (PathCeiling_, per-cell Ceiling_, extra RouteTerrainCeiling children…).");

                if (LayeredSlabCount > 0)
                    Issues.Add($"{LayeredSlabCount} layered slab/ceiling piece(s) (Floor_, PathCeiling_, Outer_, SkySeal…).");

                if (LegacySplineLayerCount > 0)
                    Issues.Add($"{LegacySplineLayerCount} legacy spline/tube layer(s) (MainCaveTube, CaveMazeVolume, SeamlessTunnel…).");

                if (VisibleFlatPlatformCount > 0)
                    Issues.Add($"{VisibleFlatPlatformCount} visible flat PathPlatforms still enabled (onion walk slabs).");

                if (!layoutPrototype && compactRoute && !HasRouteTerrainFloor)
                    Issues.Add("Missing RouteTerrainFloor — run Build Complete Cave Level.");

                if (!layoutPrototype && compactRoute && !HasSingleRouteCeiling)
                    Issues.Add("Missing single RouteTerrainCeiling mesh — run Build Complete Cave Level.");

                if (BlocksPerRingAvg > 16f)
                    Issues.Add($"Block rings overdense ({BlocksPerRingAvg:F0} blocks/ring avg) — use WallThickness=1 compact route.");

                if (SolutionPathSteps > 0 && BlockRingCount > SolutionPathSteps + 2)
                    Issues.Add(
                        $"Too many block rings ({BlockRingCount}) for path ({SolutionPathSteps}) — one ring per route cell only.");
            }
        }

        public static AuditResult Audit(Transform caveRoot)
        {
            var result = new AuditResult { Issues = new List<string>() };
            if (caveRoot == null)
                return result;

            result.SolutionPathSteps = ResolveSolutionPathSteps(caveRoot);

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            result.HasAdventureShell = geometry != null &&
                                      geometry.Find(CaveAdventureShellBuilder.ShellRootName) != null;
            result.HasRouteTerrainFloor = geometry != null &&
                                          geometry.Find(CaveEnclosureShellBuilder.FloorRootName) != null;
            result.HasLayoutWalkFloor = geometry != null &&
                                        geometry.Find(CaveLayoutPrototypeGenerator.FlatFloorRootName) != null;

            var ceilingRoot = geometry != null ? geometry.Find(CaveEnclosureShellBuilder.CeilingRootName) : null;
            if (ceilingRoot != null)
            {
                var ceilingMesh = ceilingRoot.GetComponentInChildren<MeshFilter>(true)?.sharedMesh;
                var ceilingRenderer = ceilingRoot.GetComponent<MeshRenderer>();
                // Triangle budget may disable the renderer; mesh presence is enough for compact-route grading.
                result.HasSingleRouteCeiling =
                    ceilingMesh != null ||
                    (ceilingRenderer != null && ceilingRenderer.enabled);
                if (!result.HasSingleRouteCeiling)
                {
                    var childRenderers = ceilingRoot.GetComponentsInChildren<MeshRenderer>(true);
                    if (childRenderers.Length > 1)
                        result.StackedCeilingSlabCount += childRenderers.Length - 1;
                }
            }

            if (geometry != null)
            {
                var extraCeilings = 0;
                foreach (Transform child in geometry)
                {
                    if (child == null || child.name != CaveEnclosureShellBuilder.CeilingRootName)
                        continue;
                    extraCeilings++;
                }

                if (extraCeilings > 1)
                    result.StackedCeilingSlabCount += extraCeilings - 1;
            }

            foreach (var t in caveRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null)
                    continue;

                var n = t.name;
                if (IsStackedCeilingSlabName(n))
                {
                    var mr = t.GetComponent<MeshRenderer>();
                    if (mr != null && mr.enabled)
                        result.StackedCeilingSlabCount++;
                }

                if (IsLayeredSlabName(n) && !IsCompactRouteSurfaceName(n))
                {
                    var mr = t.GetComponent<MeshRenderer>();
                    if (mr != null && mr.enabled)
                        result.LayeredSlabCount++;
                }

                if (IsLegacySplineLayerName(n))
                {
                    var mr = t.GetComponent<MeshRenderer>();
                    if (mr != null && mr.enabled)
                        result.LegacySplineLayerCount++;
                }

                if (n.StartsWith("BlockRing_") && t.parent != null &&
                    t.parent.name == "Main")
                    result.BlockRingCount++;

                if (n.StartsWith("CaveBlock_"))
                    result.CaveBlockCount++;
            }

            if (geometry != null)
            {
                var platforms = geometry.Find(CaveAdventureBlockBuilder.PlatformsRootName);
                if (platforms != null)
                {
                    foreach (var mr in platforms.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (mr != null && mr.enabled)
                            result.VisibleFlatPlatformCount++;
                    }
                }
            }

            if (result.BlockRingCount > 0)
            {
                // FDG HDPCG 2026 — density metric is per route ring, not total CaveBlock_* / ring count
                // (entrance/decoration blocks under BlockTunnel must not inflate visual_shell).
                result.BlocksPerRingAvg = ComputeBlocksPerRingAverage(caveRoot, result.BlockRingCount);
                if (result.BlocksPerRingAvg <= 0.01f && result.CaveBlockCount > 0)
                    result.BlocksPerRingAvg = result.CaveBlockCount / (float)result.BlockRingCount;
            }

            result.StrayBlockCount = CountStrayBlocks(caveRoot);
            return result;
        }

        static bool IsStackedCeilingSlabName(string n) =>
            !IsCompactRouteSurfaceName(n) &&
            (n.StartsWith("PathCeiling_") || n.StartsWith("Ceiling_") || n.StartsWith("Cavern_Ceiling"));

        static bool IsLayeredSlabName(string n) =>
            n.StartsWith("Floor_") || n.StartsWith("Cavern_") || n.StartsWith("Entrance_Shaft_") ||
            n.StartsWith("Outer_") || n == "SkySeal" || n.Contains("CeilingCover");

        static bool IsCompactRouteSurfaceName(string n) =>
            n == CaveEnclosureShellBuilder.FloorRootName || n == CaveEnclosureShellBuilder.CeilingRootName;

        static bool IsLegacySplineLayerName(string n) =>
            n == "MainCaveTube" || n == "MainCaveOuterShell" || n == "CaveMazeVolume" ||
            n.StartsWith("TunnelRing_") || n.StartsWith("TunnelSegment_");

        /// <summary>Counts <c>CaveBlock_*</c> under <c>BlockTunnel/Main/BlockRing_*</c> only.</summary>
        static float ComputeBlocksPerRingAverage(Transform caveRoot, int blockRingCount)
        {
            if (caveRoot == null || blockRingCount <= 0)
                return 0f;

            var tunnel = CaveGeometryPaths.FindBlockTunnel(caveRoot);
            var main = tunnel != null ? tunnel.Find("Main") : null;
            if (main == null)
                return 0f;

            var inRings = 0;
            foreach (Transform ring in main)
            {
                if (ring == null || !ring.name.StartsWith("BlockRing_"))
                    continue;

                foreach (var t in ring.GetComponentsInChildren<Transform>(true))
                {
                    if (t != null && t.name.StartsWith("CaveBlock_"))
                        inRings++;
                }
            }

            return inRings / (float)blockRingCount;
        }

        static int CountStrayBlocks(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var tunnel = CaveGeometryPaths.FindBlockTunnel(caveRoot);
            var count = 0;
            foreach (var t in caveRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || !t.name.StartsWith("CaveBlock_"))
                    continue;

                if (tunnel != null && t.IsChildOf(tunnel))
                    continue;

                count++;
            }

            return count;
        }

        static int ResolveSolutionPathSteps(Transform caveRoot)
        {
            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
                return 0;

            var layout = CaveMazeLayoutGenerator.Generate(
                meta.seed, meta.tunnelSegments, meta.chamberCount);
            return layout?.SolutionPath?.Count ?? 0;
        }
    }
}
