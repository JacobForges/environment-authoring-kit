#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Optional: registers CavePlaytestBot tag so legacy CompareTag paths stay quiet.</summary>
    public static class CavePlaytestBotTagSetup
    {
        public const string TagName = "CavePlaytestBot";

        [InitializeOnLoadMethod]
        static void EnsureOnLoad()
        {
            EditorApplication.delayCall += TryRegisterTag;
        }

        static void TryRegisterTag()
        {
            try
            {
                var tagManager = new SerializedObject(
                    AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
                var tags = tagManager.FindProperty("tags");
                for (var i = 0; i < tags.arraySize; i++)
                {
                    if (tags.GetArrayElementAtIndex(i).stringValue == TagName)
                        return;
                }

                tags.InsertArrayElementAtIndex(tags.arraySize);
                tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = TagName;
                tagManager.ApplyModifiedProperties();
            }
            catch
            {
                // Marker-based bot ID works without project tags.
            }
        }
    }
}
#endif
