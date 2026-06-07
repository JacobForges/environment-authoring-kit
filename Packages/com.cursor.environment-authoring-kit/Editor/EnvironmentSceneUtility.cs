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
                if (ground != null && !SceneGroundResolver.IsInvalidFullWorldSurfaceGround(ground.Anchor))
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

            if (ground != null && !SceneGroundResolver.IsInvalidFullWorldSurfaceGround(ground.Anchor))
                AlignRootToGround(go.transform, ground);

            return root;
        }

        public static void AlignRootToGround(Transform root, SceneGroundInfo ground)
        {
            if (root == null || ground == null || !ground.HasAnchor)
                return;

            if (SceneGroundResolver.IsInvalidFullWorldSurfaceGround(ground.Anchor))
                return;

            if (!SceneGroundResolver.IsAxisAlignedGroundAnchor(ground.Anchor))
                return;

            if (root == ground.Anchor || root.IsChildOf(ground.Anchor))
                return;

            Undo.SetTransformParent(root, ground.Anchor, "Parent Environment To Ground");
            root.localPosition = EnvironmentKitSettings.PlaceUnderGroundSurface
                ? ground.Down * 0.05f
                : Vector3.zero;
            root.localRotation = Quaternion.identity;
        }

        public static Transform GetOrCreateChild(Transform parent, string childName)
        {
            // Unity fake-null: destroyed transforms must not be passed to Find/SetParent.
            if (!parent)
            {
                Debug.LogWarning(
                    $"[EnvironmentKit] GetOrCreateChild skipped — parent destroyed (child '{childName}').");
                return null;
            }

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

        /// <summary>Strip missing/destroyed Terrain refs and orphan TerrainColliders on kit roots.</summary>
        public static void RepairKitRootTerrainState()
        {
            foreach (var env in Object.FindObjectsByType<EnvironmentRoot>())
            {
                if (env == null)
                    continue;

                var go = env.gameObject;
                if (!IsLiveTerrain(go.GetComponent<UnityEngine.Terrain>()))
                {
                    foreach (var col in go.GetComponents<TerrainCollider>())
                    {
                        if (col != null)
                            CaveEditorUndo.DestroyImmediate(col);
                    }
                }
            }
        }

        internal static bool IsLiveTerrain(UnityEngine.Terrain terrain)
        {
            if (terrain == null)
                return false;

            try
            {
                return terrain.terrainData != null;
            }
            catch (MissingComponentException)
            {
                return false;
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
            if (ground != null && IsLiveTerrain(ground.Terrain) &&
                ActiveSceneUtility.IsInActiveScene(ground.Terrain.gameObject))
                return ground.Terrain;

            var terrain = Blockout.SurfaceTerrainTileExpansion.FindMainTerrainInScene();
            if (terrain == null)
            {
                foreach (var candidate in UnityEngine.Object.FindObjectsByType<UnityEngine.Terrain>())
                {
                    if (candidate == null ||
                        !ActiveSceneUtility.IsInActiveScene(candidate.gameObject) ||
                        candidate.name.StartsWith("SurfaceTerrainTile_", System.StringComparison.Ordinal))
                        continue;

                    terrain = candidate;
                    break;
                }
            }

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
