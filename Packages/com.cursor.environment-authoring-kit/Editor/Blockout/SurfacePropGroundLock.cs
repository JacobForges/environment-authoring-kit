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
        public static void Apply(SceneGroundInfo ground, Transform surfaceRoot)
        {
            if (ground?.Anchor == null || surfaceRoot == null)
                return;

            var envRoot = EnvironmentSceneUtility.GetOrCreateRoot(ground);
            EnvironmentSceneUtility.AlignRootToGround(envRoot.transform, ground);

            var vegRoot = surfaceRoot.Find(SurfaceIntelligentPropPlacer.VegetationLayerName);
            if (vegRoot == null)
                return;

            var mainTerrain = ground.Terrain;
            var locked = 0;
            var children = new Transform[vegRoot.childCount];
            for (var i = 0; i < vegRoot.childCount; i++)
                children[i] = vegRoot.GetChild(i);

            foreach (var child in children)
            {
                if (child == null)
                    continue;

                Transform parent = envRoot.transform;
                if (mainTerrain != null &&
                    SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(
                        mainTerrain,
                        child.position.x,
                        child.position.z,
                        out var tile) &&
                    tile != null &&
                    tile.transform != null)
                {
                    parent = tile.transform;
                }

                if (child.parent != parent)
                {
                    Undo.SetTransformParent(child, parent, "Lock surface prop to ground");
                    locked++;
                }
            }

            if (locked > 0)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Locked {locked} prop(s) to ground/terrain — moving Ground moves props.",
                    forceUnityConsole: true);
            }
        }
    }
}
#endif
