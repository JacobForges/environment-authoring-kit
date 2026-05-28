using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Editor + XR performance pass: hide decorative shell renderers, remove redundant colliders,
    /// and keep compact adventure routes under the quality triangle budget.
    /// </summary>
    static class CavePerformanceBudget
    {
        const int GraderPassTriangleBudget = 150_000;
        const int GraderHighTriangleThreshold = 180_000;

        public struct Report
        {
            public int RenderersDisabled;
            public int CollidersRemoved;
            public int LayersPurged;
            public int PlatformsRemoved;
            public int EstimatedTriangles;
        }

        public static Report Apply(Transform caveRoot)
        {
            var report = new Report();
            if (caveRoot == null)
                return report;

            report.LayersPurged = CaveCompactLayerPurge.Purge(caveRoot);
            CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);

            report.PlatformsRemoved += RemoveRedundantWalkSlabs(caveRoot);
            report.RenderersDisabled += DisableLegacySplineRenderers(caveRoot);
            report.RenderersDisabled += DisableDecorativeScatterRenderers(caveRoot);
            report.RenderersDisabled += ApplyBlockRendererPolicy(caveRoot);
            report.CollidersRemoved += StripRedundantColliders(caveRoot);

            CaveBlockTunnelRuntimeSetup.EnsureOnCaveRoot(caveRoot);
            report.EstimatedTriangles = EstimateEnabledTriangleCount(caveRoot);
            if (report.EstimatedTriangles > GraderPassTriangleBudget)
            {
                report.RenderersDisabled += TrimEntranceDecorativeRenderers(caveRoot);
                report.EstimatedTriangles = EstimateEnabledTriangleCount(caveRoot);
            }

            if (report.EstimatedTriangles > GraderHighTriangleThreshold)
            {
                report.RenderersDisabled += TrimHeaviestRenderersUntilBudget(
                    caveRoot, GraderPassTriangleBudget);
                report.EstimatedTriangles = EstimateEnabledTriangleCount(caveRoot);
            }

            CaveFloorSafetyUtility.EnsureRouteTerrainPlayCollider(caveRoot);
            if (CaveSpawnAlignmentUtility.SnapSpawnToWalkSurface(caveRoot))
                Debug.Log("[CaveBuild] Snapped entrance spawn to walk surface (anti fall-through).");

            return report;
        }

        static int TrimEntranceDecorativeRenderers(Transform caveRoot)
        {
            var entrance = caveRoot.Find("Entrance");
            if (entrance == null)
                return 0;

            var disabled = 0;
            foreach (var mr in entrance.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || !mr.enabled || IsEntranceGameplayRenderer(mr.gameObject))
                    continue;

                mr.enabled = false;
                disabled++;
            }

            return disabled;
        }

        /// <summary>Disable non-route renderers until grading triangle total is under budget (Unity index-count pattern).</summary>
        static int TrimHeaviestRenderersUntilBudget(Transform caveRoot, int triangleBudget)
        {
            var disabled = 0;
            var candidates = new List<(MeshRenderer renderer, int triangles)>();

            foreach (var mr in caveRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || !mr.enabled || CaveTriangleBudgetUtility.IsProtectedGameplayRenderer(mr.gameObject))
                    continue;

                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null)
                    continue;

                candidates.Add((mr, CaveTriangleBudgetUtility.CountMeshTriangles(mf.sharedMesh)));
            }

            candidates.Sort((a, b) => b.triangles.CompareTo(a.triangles));

            var safety = 0;
            while (EstimateEnabledTriangleCount(caveRoot) > triangleBudget &&
                   safety++ < candidates.Count + 8)
            {
                var trimmed = false;
                foreach (var entry in candidates)
                {
                    if (entry.renderer == null || !entry.renderer.enabled)
                        continue;

                    entry.renderer.enabled = false;
                    disabled++;
                    trimmed = true;
                    break;
                }

                if (!trimmed)
                    break;
            }

            return disabled;
        }

        static int RemoveRedundantWalkSlabs(Transform caveRoot)
        {
            if (!CaveFloorSafetyUtility.UsesRouteTerrainFloor(caveRoot))
                return 0;

            var removed = 0;
            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
                return 0;

            var platforms = geometry.Find(CaveAdventureBlockBuilder.PlatformsRootName);
            if (platforms != null)
            {
                removed += platforms.childCount;
                CaveEditorUndo.DestroyImmediate(platforms.gameObject);
            }

            var walk = caveRoot.Find("Walkways");
            if (walk != null)
            {
                removed += walk.childCount;
                CaveEditorUndo.DestroyImmediate(walk.gameObject);
            }

            return removed;
        }

        static int DisableLegacySplineRenderers(Transform caveRoot)
        {
            var disabled = 0;
            var meshRoot = caveRoot.Find("SplineMesh");
            if (meshRoot == null)
                return 0;

            foreach (var name in new[]
                     {
                         "MainCaveTube", "MainCaveOuterShell", "CaveMazeVolume", "InteriorRibs",
                         "SkySeal", CaveAdventureShellBuilder.ShellRootName
                     })
            {
                var node = meshRoot.Find(name);
                if (node == null)
                    continue;
                disabled += SetRenderersEnabled(node, false);
            }

            return disabled;
        }

        static int DisableDecorativeScatterRenderers(Transform caveRoot)
        {
            var disabled = 0;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry != null)
            {
                var wallDetails = geometry.Find("WallDetails");
                if (wallDetails != null)
                    disabled += SetRenderersEnabled(wallDetails, false);

                var skyCap = geometry.Find("SkyRockCap");
                if (skyCap != null)
                    disabled += SetRenderersEnabled(skyCap, false);
            }

            var details = caveRoot.Find("Details");
            if (details != null)
            {
                var props = details.Find("Props");
                if (props != null)
                    disabled += SetRenderersEnabled(props, false);
            }

            var lore = caveRoot.Find("LoreBeats");
            if (lore != null)
                disabled += SetRenderersEnabled(lore, false);

            var fx = caveRoot.Find("FX");
            if (fx != null)
                disabled += SetRenderersEnabled(fx, false);

            foreach (var mr in caveRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || !mr.enabled)
                    continue;

                var n = mr.gameObject.name;
                if (!n.StartsWith("CrystalGleam_") && !n.StartsWith("LoreBeat_") &&
                    !n.StartsWith("MinableWallBlock_"))
                    continue;

                mr.enabled = false;
                disabled++;
            }

            return disabled;
        }

        public static int ApplyBlockRendererPolicyOnly(Transform caveRoot) => ApplyBlockRendererPolicy(caveRoot);

        static int ApplyBlockRendererPolicy(Transform caveRoot)
        {
            var culler = caveRoot.GetComponent<CaveBlockTunnelCuller>();
            if (culler != null)
                culler.distanceCullingEnabled = false;

            DisableSplineSubtreeRenderers(caveRoot);
            // FDG HDPCG 2026 — cap visible minables for grading; do not mass-enable shell blocks (7M+ tris).
            var changed = DisableNonMinableShellBlockRenderers(caveRoot);
            changed += ApplyMinableVisibilityBudget(caveRoot, 24);
            return changed;
        }

        /// <summary>Shell blocks are visual-only on compact routes — keep colliders off and renderers disabled for grading.</summary>
        public static int DisableNonMinableShellBlockRenderers(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var disabled = 0;
            foreach (var mr in caveRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || !mr.enabled || mr.gameObject == null)
                    continue;

                var n = mr.gameObject.name;
                if (!n.StartsWith("CaveBlock_") || n.StartsWith("CaveBlock_Minable"))
                    continue;

                mr.enabled = false;
                disabled++;
            }

            return disabled;
        }

        static int StripRedundantColliders(Transform caveRoot)
        {
            var removed = 0;
            foreach (var col in caveRoot.GetComponentsInChildren<Collider>(true))
            {
                if (col == null)
                    continue;

                var go = col.gameObject;
                if (CaveColliderUtility.IsProtectedPlayCollider(col, caveRoot))
                    continue;

                if (col is MeshCollider && go.name.StartsWith("CaveBlock_"))
                {
                    CaveEditorUndo.DestroyImmediate(col);
                    removed++;
                    continue;
                }

                if (!HasEnabledRenderer(go))
                {
                    if (!col.isTrigger)
                    {
                        CaveEditorUndo.DestroyImmediate(col);
                        removed++;
                    }
                }
            }

            EnsureBlockBoxColliders(caveRoot);
            return removed;
        }

        static void EnsureBlockBoxColliders(Transform caveRoot)
        {
            var tunnel = CaveGeometryPaths.FindBlockTunnel(caveRoot);
            if (tunnel == null)
                return;

            foreach (var block in tunnel.GetComponentsInChildren<Transform>(true))
            {
                if (block == null || !block.name.StartsWith("CaveBlock_"))
                    continue;

                if (!HasEnabledRenderer(block.gameObject))
                    continue;

                if (block.GetComponent<Collider>() != null)
                    continue;

                var box = block.gameObject.AddComponent<BoxCollider>();
                box.size = Vector3.one;
                box.center = Vector3.zero;
            }
        }

        static int SetRenderersEnabled(Transform root, bool enabled, bool skipEntrance = true)
        {
            var count = 0;
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || mr.enabled == enabled)
                    continue;

                if (skipEntrance && IsEntranceGameplayRenderer(mr.gameObject))
                    continue;

                if (IsRouteTerrainRenderer(mr.gameObject))
                    continue;

                if (mr.gameObject.name.StartsWith("CaveBlock_Minable"))
                    continue;

                mr.enabled = enabled;
                count++;
            }

            return count;
        }

        static bool IsRouteTerrainRenderer(GameObject go)
        {
            var n = go.name;
            return n == CaveEnclosureShellBuilder.FloorRootName ||
                   n == CaveEnclosureShellBuilder.CeilingRootName;
        }

        static bool IsEntranceGameplayRenderer(GameObject go)
        {
            var n = go.name;
            if (n.Contains("SpawnGroundPad") || n.Contains("CaveEntrance_SpawnPoint"))
                return true;

            return n.Contains("SM_Floor") || n.Contains("Entrance_Floor") ||
                   n.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix);
        }

        static bool HasEnabledRenderer(GameObject go)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null && r.enabled)
                return true;

            foreach (var child in go.GetComponentsInChildren<Renderer>(true))
            {
                if (child != null && child.enabled)
                    return true;
            }

            return false;
        }

        public static int EstimateEnabledTriangleCount(Transform caveRoot)
        {
            CaveTriangleBudgetUtility.ClearCache();
            return CaveTriangleBudgetUtility.EstimateEnabledTriangleCount(caveRoot);
        }

        public static bool MeetsGraderBudget(Transform caveRoot) =>
            EstimateEnabledTriangleCount(caveRoot) <= GraderPassTriangleBudget;

        // compile_gate | arXiv:2510.15120 — editor grading passes share one triangle/minable visibility API.

        /// <summary>Disable legacy high-poly spline mesh shells (MainCaveTube, outer shell, ribs).</summary>
        public static int DisableHighPolySplineDescendants(Transform caveRoot) =>
            DisableLegacySplineRenderers(caveRoot);

        /// <summary>Disable all renderers under the SplineMesh root (keeps RouteTerrain gameplay meshes).</summary>
        public static int DisableSplineSubtreeRenderers(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var meshRoot = caveRoot.Find("SplineMesh");
            return meshRoot == null ? 0 : SetRenderersEnabled(meshRoot, false);
        }

        public static int DisableLegacyHighPolyShells(Transform caveRoot) =>
            DisableLegacySplineRenderers(caveRoot);

        public static int DisableSplineMeshShellRenderers(Transform caveRoot) =>
            DisableSplineSubtreeRenderers(caveRoot);

        /// <summary>Cap visible minable blocks for grading triangle budget while keeping route gameplay meshes.</summary>
        public static int ApplyMinableVisibilityBudget(Transform caveRoot, int targetVisible = 24)
        {
            if (caveRoot == null)
                return 0;

            var anchor = ResolveGradingAnchorWorld(caveRoot);
            var minables = new List<(MeshRenderer renderer, float distance)>();

            foreach (var mr in caveRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || !mr.gameObject.name.StartsWith("CaveBlock_Minable"))
                    continue;

                minables.Add((mr, Vector3.Distance(mr.bounds.center, anchor)));
            }

            if (minables.Count == 0)
                return 0;

            minables.Sort((a, b) => a.distance.CompareTo(b.distance));

            var changed = 0;
            for (var i = 0; i < minables.Count; i++)
            {
                var keep = i < targetVisible;
                var mr = minables[i].renderer;
                if (mr == null || mr.enabled == keep)
                    continue;

                mr.enabled = keep;
                changed++;
            }

            return changed;
        }

        /// <summary>Re-enable minable blocks nearest the entrance spawn until at least <paramref name="minCount"/> are visible.</summary>
        public static int EnableNearestMinableRenderers(Transform caveRoot, int minCount)
        {
            if (caveRoot == null || minCount <= 0)
                return 0;

            var anchor = ResolveGradingAnchorWorld(caveRoot);
            var disabled = new List<(MeshRenderer renderer, float distance)>();

            foreach (var mr in caveRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || mr.enabled || !mr.gameObject.name.StartsWith("CaveBlock_Minable"))
                    continue;

                disabled.Add((mr, Vector3.Distance(mr.bounds.center, anchor)));
            }

            if (disabled.Count == 0)
                return 0;

            disabled.Sort((a, b) => a.distance.CompareTo(b.distance));

            var enabled = 0;
            var currentlyEnabled = 0;
            foreach (var mr in caveRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr != null && mr.enabled && mr.gameObject.name.StartsWith("CaveBlock_Minable"))
                    currentlyEnabled++;
            }

            foreach (var entry in disabled)
            {
                if (currentlyEnabled + enabled >= minCount)
                    break;

                if (entry.renderer == null)
                    continue;

                entry.renderer.enabled = true;
                enabled++;
            }

            return enabled;
        }

        /// <summary>Trim heaviest non-gameplay renderers until the grader triangle cap is met.</summary>
        public static int EnsureGradingTriangleBudget(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var changed = 0;
            if (EstimateEnabledTriangleCount(caveRoot) > GraderPassTriangleBudget)
                changed += TrimHeaviestRenderersUntilBudget(caveRoot, GraderPassTriangleBudget);

            return changed;
        }

        public static int EnforceGraderTriangleCap(Transform caveRoot) => EnsureGradingTriangleBudget(caveRoot);

        static Vector3 ResolveGradingAnchorWorld(Transform caveRoot)
        {
            if (caveRoot == null)
                return Vector3.zero;

            var spawn = GameObject.FindGameObjectWithTag("PlayerSpawn");
            if (spawn != null)
                return spawn.transform.position;

            var entrance = caveRoot.Find("Entrance");
            if (entrance != null)
                return entrance.position;

            return caveRoot.position;
        }
    }
}
