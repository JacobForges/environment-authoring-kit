using EnvironmentAuthoringKit;
using EnvironmentAuthoringKit.Editor.Blockout;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor
{
    static class EnvironmentSceneUtility
    {
        public static EnvironmentRoot GetOrCreateRoot(SceneGroundInfo ground = null)
        {
            var existing = ActiveSceneUtility.FindInActiveScene<EnvironmentRoot>();
            if (existing != null)
            {
                if (ground != null)
                    AlignRootToGround(existing.transform, ground);
                return existing;
            }

            var go = new GameObject(EnvironmentRoot.DefaultName);
            if (ActiveSceneUtility.HasValidActiveScene)
                SceneManager.MoveGameObjectToScene(go, ActiveSceneUtility.ActiveScene);
            CaveEditorUndo.RegisterCreated(go, "Create Environment Root");
            var root = go.AddComponent<EnvironmentRoot>();
            if (go.GetComponent<GeneratedWorldMetadata>() == null)
                go.AddComponent<GeneratedWorldMetadata>();

            if (ground != null)
                AlignRootToGround(go.transform, ground);

            return root;
        }

        public static void AlignRootToGround(Transform root, SceneGroundInfo ground)
        {
            if (root == null || ground == null || !ground.HasAnchor)
                return;

            Undo.SetTransformParent(root, ground.Anchor, "Parent Environment To Ground");
            root.localPosition = EnvironmentKitSettings.PlaceUnderGroundSurface
                ? ground.Down * 0.05f
                : Vector3.zero;
            root.localRotation = Quaternion.identity;
        }

        public static Transform GetOrCreateChild(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            if (child != null)
                return child;

            var go = new GameObject(childName);
            CaveEditorUndo.RegisterCreated(go, "Create " + childName);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        public static void ClearGeneratedChildren(Transform root)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                CaveEditorUndo.DestroyImmediate(child.gameObject);
            }
        }

        /// <summary>Returns existing terrain in the active scene only. Does not create terrain unless explicitly allowed.</summary>
        public static UnityEngine.Terrain FindTerrainInActiveScene(
            Transform parent,
            SceneGroundInfo ground,
            bool allowCreate,
            int size = 256,
            float height = 80f)
        {
            if (ground != null && ground.HasTerrain && ActiveSceneUtility.IsInActiveScene(ground.Terrain.gameObject))
                return ground.Terrain;

            var terrain = ActiveSceneUtility.FindInActiveScene<UnityEngine.Terrain>();
            if (terrain != null)
                return terrain;

            if (!allowCreate || EnvironmentKitSettings.NeverCreateNewTerrain)
                return null;

            if (ground != null && ground.HasAnchor && EnvironmentKitSettings.SkipNewTerrainWhenGroundExists)
                return null;

            var heightmapRes = EnvironmentKitHardwareBudget.ClampHeightmapResolution(513);
            size = EnvironmentKitHardwareBudget.ClampTerrainSizeMeters(size);
            height = Mathf.Min(height, EnvironmentKitHardwareBudget.Active.TerrainMaxHeightMeters);
            var terrainData = new TerrainData
            {
                heightmapResolution = heightmapRes,
                size = new Vector3(size, height, size)
            };

            var go = UnityEngine.Terrain.CreateTerrainGameObject(terrainData);
            CaveEditorUndo.RegisterCreated(go, "Create Terrain");
            go.name = "GeneratedTerrain";
            if (ActiveSceneUtility.HasValidActiveScene)
                SceneManager.MoveGameObjectToScene(go, ActiveSceneUtility.ActiveScene);
            if (parent != null)
                go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            var col = go.GetComponent<TerrainCollider>();
            if (col != null)
                col.terrainData = terrainData;

            return go.GetComponent<UnityEngine.Terrain>();
        }

        public static void MarkSceneDirty()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return;

            foreach (var rootObject in scene.GetRootGameObjects())
                EditorUtility.SetDirty(rootObject);

            var environmentRoot = Object.FindAnyObjectByType<EnvironmentRoot>();
            if (environmentRoot != null)
                EditorUtility.SetDirty(environmentRoot.gameObject);
        }
    }
}
