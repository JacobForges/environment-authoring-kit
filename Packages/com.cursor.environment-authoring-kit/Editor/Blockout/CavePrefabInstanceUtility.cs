using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Repairs lava-tube prefab instances when renderers were stripped or components went missing.</summary>
    static class CavePrefabInstanceUtility
    {
        public static int RestoreMissingPrefabVisuals(Transform root)
        {
            if (root == null)
                return 0;

            var restored = 0;
            foreach (var source in root.GetComponentsInChildren<CavePrefabSource>(true))
            {
                if (source == null)
                    continue;

                if (CaveRendererVisibility.HasVisibleRenderer(source.gameObject, true))
                    continue;

                if (TryRestoreInstance(source))
                    restored++;
            }

            return restored;
        }

        static bool TryRestoreInstance(CavePrefabSource source)
        {
            var go = source.gameObject;
            var path = source.AssetPath;
            if (string.IsNullOrEmpty(path))
                return false;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[CavePrefabInstanceUtility] Missing prefab asset: {path}");
                return false;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                if (!CaveEditorUndo.IsBulkBuild)
                    CaveEditorUndo.RecordObject(go, "Restore Prefab Visual");

                PrefabUtility.RevertObjectOverride(go.transform, InteractionMode.AutomatedAction);
                CaveSceneMaterialRepair.ApplyModuleMaterials(go, go.transform.localScale);
                return CaveRendererVisibility.HasVisibleRenderer(go, true);
            }

            if (CaveEditorUndo.IsBulkBuild)
                return false;

            return ReplaceWithFreshInstance(go, prefab, path);
        }

        static bool ReplaceWithFreshInstance(GameObject broken, GameObject prefab, string assetPath)
        {
            var t = broken.transform;
            var parent = t.parent;
            var localPosition = t.localPosition;
            var localRotation = t.localRotation;
            var localScale = t.localScale;
            var siblingIndex = t.GetSiblingIndex();

            CaveEditorUndo.DestroyImmediate(broken);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (instance == null)
                return false;

            CaveEditorUndo.RegisterCreated(instance.transform, "Restore Prefab Visual");
            instance.transform.SetSiblingIndex(siblingIndex);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation;
            instance.transform.localScale = localScale;

            var source = instance.GetComponent<CavePrefabSource>();
            if (source == null)
                source = instance.AddComponent<CavePrefabSource>();
            source.SetAssetPath(assetPath);
            CaveSceneMaterialRepair.ApplyModuleMaterials(instance, localScale);
            return CaveRendererVisibility.HasVisibleRenderer(instance, true);
        }
    }
}
