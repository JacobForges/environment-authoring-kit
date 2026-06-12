#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// After surface props are placed, parent them to Ground / terrain tiles so moving Ground moves props.
    /// </summary>
    public static class SurfacePropGroundLock
    {
        public static void Apply(SceneGroundInfo ground, Transform surfaceRoot) =>
            ResnapAndLockAll(ground, surfaceRoot, log: true);

        /// <summary>After grid snap or terrain moves — parent props to tiles and raycast Y to heightfield.</summary>
        public static int ResnapAndLockAll(SceneGroundInfo ground, Transform surfaceRoot, bool log = false)
        {
            if (ground?.Anchor == null || surfaceRoot == null)
                return 0;

            var envRoot = EnvironmentSceneUtility.GetOrCreateRoot(ground);
            EnvironmentSceneUtility.AlignRootToGround(envRoot.transform, ground);

            var vegRoot = surfaceRoot.Find(SurfaceIntelligentPropPlacer.VegetationLayerName);
            if (vegRoot == null)
                return 0;

            var mainTerrain = ground.Terrain;
            var locked = 0;
            var resnapped = 0;
            var rejected = 0;
            var children = CollectVegetationTransforms(vegRoot);

            foreach (var child in children)
            {
                if (child == null)
                    continue;

                if (mainTerrain == null ||
                    !SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(
                        mainTerrain,
                        child.position.x,
                        child.position.z,
                        out var tile) ||
                    tile == null)
                {
                    rejected++;
                    CaveEditorUndo.DestroyImmediate(child.gameObject);
                    continue;
                }

                var expectedY = tile.SampleHeight(child.position) + tile.transform.position.y;
                var delta = child.position.y - expectedY;

                var parent = tile.transform;
                if (child.parent != parent)
                {
                    child.SetParent(parent, true);
                    locked++;
                }

                // Terrain grid moves after placement — always resnap Y when a tile exists; only void = no tile.
                if (Mathf.Abs(delta) > 0.02f)
                {
                    var pos = child.position;
                    pos.y = expectedY;
                    child.position = pos;
                    resnapped++;
                }
            }

            if (log && (locked > 0 || resnapped > 0 || rejected > 0))
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Prop ground lock — parented={locked} resnapped={resnapped} void-rejected={rejected}.",
                    forceUnityConsole: true);
            }

            return locked + resnapped;
        }

        static Transform[] CollectVegetationTransforms(Transform vegRoot)
        {
            var list = new System.Collections.Generic.List<Transform>();
            for (var i = 0; i < vegRoot.childCount; i++)
                list.Add(vegRoot.GetChild(i));

            foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>())
            {
                if (terrain?.transform == null)
                    continue;

                for (var i = 0; i < terrain.transform.childCount; i++)
                {
                    var c = terrain.transform.GetChild(i);
                    if (c != null && IsVegetationInstance(c))
                        list.Add(c);
                }
            }

            return list.ToArray();
        }

        static bool IsVegetationInstance(Transform t)
        {
            if (t == null)
                return false;

            var n = t.name;
            return n.Contains("Surface_", System.StringComparison.Ordinal) ||
                   n.StartsWith("G-G", System.StringComparison.Ordinal) ||
                   n.StartsWith("G-C", System.StringComparison.Ordinal) ||
                   n.StartsWith("G-T", System.StringComparison.Ordinal) ||
                   n.StartsWith("B-", System.StringComparison.Ordinal) ||
                   n.StartsWith("K-", System.StringComparison.Ordinal) ||
                   n.IndexOf("grass", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   n.IndexOf("tree", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   n.IndexOf("bush", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
#endif
