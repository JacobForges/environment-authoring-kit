using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Removes thin slab shells and legacy spline closure — compact routes use block rings + walk platforms only.</summary>
    public static class CaveCompactLayerPurge
    {
        /// <summary>Strip onion shells / legacy tubes without deleting committed Walkways floors.</summary>
        public static int PurgeShellLayersOnly(Transform caveRoot) => Purge(caveRoot, preserveWalkways: true);

        public static int Purge(Transform caveRoot, bool preserveWalkways = false)
        {
            if (caveRoot == null)
                return 0;

            preserveWalkways |= CaveBuildWorkflowCoordinator.ShouldPreserveWalkways;

            var removed = CaveBuildWorkflowCoordinator.ShouldRunDestructivePurge
                ? CaveLegacyGeometryPurge.Purge(caveRoot)
                : 0;

            removed += DestroyIfPresent(caveRoot, "SeamlessTunnel");
            removed += DestroyIfPresent(caveRoot, "OcclusionShell");

            var meshRoot = caveRoot.Find("SplineMesh");
            if (meshRoot != null)
            {
                removed += DestroyIfPresent(meshRoot, "MainCaveTube");
                removed += DestroyIfPresent(meshRoot, "MainCaveOuterShell");
                removed += DestroyIfPresent(meshRoot, "CaveMazeVolume");
                removed += DestroyIfPresent(meshRoot, "SkySeal");
                removed += DestroyIfPresent(meshRoot, "InteriorRibs");
            }

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry != null)
                removed += CaveEnclosureShellBuilder.PurgeLayerOffenders(geometry);

            // FDG HDPCG 2026 — blocks must live under BlockTunnel only (no stray onion under SplineMesh/root).
            removed += PurgeStrayBlockShells(caveRoot);

            CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);

            if (!preserveWalkways)
            {
                var walk = caveRoot.Find("Walkways");
                if (walk != null)
                {
                    var count = walk.childCount;
                    for (var i = count - 1; i >= 0; i--)
                        CaveEditorUndo.DestroyImmediate(walk.GetChild(i).gameObject);
                    removed += count;
                }
            }

            return removed;
        }

        /// <summary>Removes <c>CaveBlock_*</c> outside <see cref="CaveGeometryPaths.BlockTunnel"/> (visual_shell / geometry_integrity).</summary>
        public static int PurgeStrayBlockShells(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var removed = PurgeDuplicateRootBlockTunnel(caveRoot);
            var tunnel = CaveGeometryPaths.FindBlockTunnel(caveRoot);
            foreach (var t in caveRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || !t.name.StartsWith("CaveBlock_"))
                    continue;

                if (tunnel != null && t.IsChildOf(tunnel))
                    continue;

                CaveEditorUndo.DestroyImmediate(t.gameObject);
                removed++;
            }

            return removed;
        }

        /// <summary>
        /// Adventure compact routes parent blocks under <c>CaveGeometry/BlockTunnel</c>; legacy spline builds leave a
        /// duplicate <c>BlockTunnel</c> on the cave root (FDG HDPCG 2026 — single shell coordinate).
        /// </summary>
        static int PurgeDuplicateRootBlockTunnel(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            var canonical = geometry != null ? geometry.Find(CaveGeometryPaths.BlockTunnel) : null;
            if (canonical == null)
                return 0;

            var rootTunnel = caveRoot.Find(CaveGeometryPaths.BlockTunnel);
            if (rootTunnel == null || rootTunnel == canonical)
                return 0;

            CaveEditorUndo.DestroyImmediate(rootTunnel.gameObject);
            return 1;
        }

        public static bool IsCompactRoute(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
                return false;

            var platforms = geometry.Find(CaveAdventureBlockBuilder.PlatformsRootName);
            return platforms != null && platforms.childCount >= 4;
        }

        static int DestroyIfPresent(Transform parent, string childName)
        {
            if (parent == null)
                return 0;

            var child = parent.Find(childName);
            if (child == null)
                return 0;

            CaveEditorUndo.DestroyImmediate(child.gameObject);
            return 1;
        }

        static int DestroyNamedChildren(Transform parent, string namePrefix)
        {
            if (parent == null)
                return 0;

            var removed = 0;
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child == null || !child.name.StartsWith(namePrefix))
                    continue;

                CaveEditorUndo.DestroyImmediate(child.gameObject);
                removed++;
            }

            return removed;
        }
    }
}
