using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Suppresses per-object undo during bulk cave builds (blocks, shell) to avoid undo buffer overflow.
    /// </summary>
    static class CaveEditorUndo
    {
        static int _bulkDepth;

        public static bool IsBulkBuild => _bulkDepth > 0;

        public static void BeginBulkBuild()
        {
            _bulkDepth++;
            if (_bulkDepth == 1)
                Undo.IncrementCurrentGroup();
        }

        public static void EndBulkBuild()
        {
            _bulkDepth = Mathf.Max(0, _bulkDepth - 1);
        }

        public static void RegisterCreated(Object obj, string actionName)
        {
            if (!IsBulkBuild && obj != null)
                Undo.RegisterCreatedObjectUndo(obj, actionName);
        }

        public static void DestroyImmediate(Object obj)
        {
            if (obj == null)
                return;

            if (IsBulkBuild)
                Object.DestroyImmediate(obj);
            else
                Undo.DestroyObjectImmediate(obj);
        }

        public static void RecordObject(Object obj, string actionName)
        {
            if (!IsBulkBuild && obj != null)
                Undo.RecordObject(obj, actionName);
        }

        public static T GetOrAddComponent<T>(GameObject go) where T : Component
        {
            if (go == null)
                return null;

            var existing = go.GetComponent<T>();
            if (existing != null)
                return existing;

            return IsBulkBuild ? go.AddComponent<T>() : Undo.AddComponent<T>(go);
        }
    }
}
