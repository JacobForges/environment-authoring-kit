#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Registers Plan v4 project tags before authors assign or query them.</summary>
    public static class WorldProjectTagSetup
    {
        public static void EnsureBossStagePortalTag() => EnsureTag(HollowTitanLandmarkAuthor.PortalTag);

        public static void EnsureTag(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName) || TagExists(tagName))
                return;

            var asset = AssetDatabase.LoadMainAssetAtPath("ProjectSettings/TagManager.asset");
            if (asset == null)
            {
                Debug.LogWarning($"[World] TagManager missing — cannot register tag '{tagName}'.");
                return;
            }

            var so = new SerializedObject(asset);
            var tags = so.FindProperty("tags");
            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tagName;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static GameObject FindBossStagePortalObject()
        {
            EnsureBossStagePortalTag();

            var byName = GameObject.Find("BossStagePortal");
            if (byName != null)
                return byName;

            try
            {
                return GameObject.FindGameObjectWithTag(HollowTitanLandmarkAuthor.PortalTag);
            }
            catch
            {
                return null;
            }
        }

        public static void TrySetTag(GameObject go, string tagName)
        {
            if (go == null || string.IsNullOrWhiteSpace(tagName))
                return;

            EnsureTag(tagName);
            try
            {
                go.tag = tagName;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[World] Could not set tag '{tagName}' on {go.name}: {ex.Message}", go);
            }
        }

        static bool TagExists(string tagName)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath("ProjectSettings/TagManager.asset");
            if (asset == null)
                return false;

            var so = new SerializedObject(asset);
            var tags = so.FindProperty("tags");
            for (var i = 0; i < tags.arraySize; i++)
            {
                if (tags.GetArrayElementAtIndex(i).stringValue == tagName)
                    return true;
            }

            return false;
        }
    }
}
#endif
