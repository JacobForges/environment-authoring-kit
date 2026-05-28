using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Creates or repairs SplineMesh tube objects (MainCaveTube, MainCaveOuterShell).</summary>
    static class CaveSplineTubeMeshUtility
    {
        const string GeneratedMeshFolder = "Assets/EnvironmentKit/Generated";

        public static bool EnsureTubeMesh(
            Transform meshRoot,
            string objectName,
            Mesh mesh,
            Material material,
            string meshAssetFileName)
        {
            if (meshRoot == null || mesh == null || material == null || string.IsNullOrEmpty(objectName))
                return false;

            if (TryGetHealthyTube(meshRoot, objectName, out _))
                return false;

            var savedMesh = SaveMeshAsset(mesh, meshAssetFileName);
            if (savedMesh == null)
                return false;

            PurgeNamedChildren(meshRoot, objectName);

            var go = new GameObject(objectName);
            CaveEditorUndo.RegisterCreated(go, objectName);
            go.transform.SetParent(meshRoot, false);

            var meshFilter = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();
            if (meshFilter == null || meshRenderer == null)
            {
                CaveEditorUndo.DestroyImmediate(go);
                return false;
            }

            try
            {
                meshFilter.sharedMesh = savedMesh;
            }
            catch (MissingComponentException)
            {
                CaveEditorUndo.DestroyImmediate(go);
                go = new GameObject(objectName);
                CaveEditorUndo.RegisterCreated(go, objectName);
                go.transform.SetParent(meshRoot, false);
                meshFilter = go.AddComponent<MeshFilter>();
                meshRenderer = go.AddComponent<MeshRenderer>();
                if (meshFilter == null || meshRenderer == null)
                {
                    CaveEditorUndo.DestroyImmediate(go);
                    return false;
                }

                meshFilter.sharedMesh = savedMesh;
            }

            meshRenderer.sharedMaterial = material;
            meshRenderer.enabled = true;
            go.isStatic = true;
            Debug.Log($"[CaveBuild] Repaired SplineMesh/{objectName} (fresh MeshFilter + saved mesh asset).");
            return true;
        }

        static bool TryGetHealthyTube(Transform meshRoot, string objectName, out Transform tube)
        {
            tube = meshRoot.Find(objectName);
            if (tube == null)
                return false;

            var meshFilter = tube.GetComponent<MeshFilter>();
            var meshRenderer = tube.GetComponent<MeshRenderer>();
            return meshFilter != null && meshFilter.sharedMesh != null && meshRenderer != null && meshRenderer.enabled;
        }

        static void PurgeNamedChildren(Transform parent, string objectName)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child == null || child.name != objectName)
                    continue;

                CaveEditorUndo.DestroyImmediate(child.gameObject);
            }
        }

        static Mesh SaveMeshAsset(Mesh mesh, string fileName)
        {
            EnsureGeneratedFolder();
            var path = $"{GeneratedMeshFolder}/{fileName}";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
                AssetDatabase.DeleteAsset(path);

            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        static void EnsureGeneratedFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
            if (!AssetDatabase.IsValidFolder(GeneratedMeshFolder))
                AssetDatabase.CreateFolder("Assets/EnvironmentKit", "Generated");
        }
    }
}
