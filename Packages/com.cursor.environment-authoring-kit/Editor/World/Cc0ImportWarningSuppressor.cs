#if UNITY_EDITOR
using System;
using UnityEditor;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// Hides kit-driven reimport noise only while an explicit CC0 bulk reimport scope is active.
    /// Benign FBX/OBJ importer warnings are never hidden globally.
    /// </summary>
    static class Cc0ImportWarningSuppressor
    {
        static int _depth;

        public static bool IsActive => _depth > 0;

        public static IDisposable Begin() => new Scope();

        /// <summary>Benign third-party FBX/OBJ import noise — only hidden during explicit kit reimport scopes.</summary>
        public static bool IsBenignImportNoise(string condition, string stackTrace)
        {
            if (string.IsNullOrEmpty(condition))
                return false;

            if (condition.Contains("Self-intersecting polygon", StringComparison.OrdinalIgnoreCase) ||
                condition.Contains("self-intersecting and has been discarded", StringComparison.OrdinalIgnoreCase))
                return true;

            if (condition.Contains("MaterialLocation.External", StringComparison.Ordinal) &&
                condition.Contains("obsolete", StringComparison.OrdinalIgnoreCase))
                return true;

            if (condition.Contains("ImportFBX Warnings", StringComparison.OrdinalIgnoreCase))
                return true;

            if (condition.Contains("has no normals. Recalculating normals", StringComparison.OrdinalIgnoreCase))
                return true;

            if (condition.Contains("vertices with no weight and bone assigned", StringComparison.OrdinalIgnoreCase))
                return true;

            if (condition.Contains("Can't import tangents and binormals", StringComparison.OrdinalIgnoreCase))
                return true;

            if (condition.StartsWith("A polygon of Mesh", StringComparison.Ordinal) &&
                condition.Contains("self-intersecting", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(stackTrace) &&
                stackTrace.Contains("AssetDatabase", StringComparison.Ordinal) &&
                (condition.Contains(".fbx", StringComparison.OrdinalIgnoreCase) ||
                 condition.Contains(".obj", StringComparison.OrdinalIgnoreCase)) &&
                (condition.Contains("Mesh", StringComparison.Ordinal) ||
                 condition.Contains("ImportFBX", StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }

        public static bool ShouldSuppress(string condition, string stackTrace)
        {
            if (string.IsNullOrEmpty(condition))
                return false;

            if (IsBenignImportNoise(condition, stackTrace))
                return IsActive;

            if (!IsActive)
                return false;

            if (string.IsNullOrEmpty(stackTrace))
                return false;

            return stackTrace.Contains("Cc0PropRigFixUtility", StringComparison.Ordinal) ||
                   stackTrace.Contains("Cc0ContentImportUtility", StringComparison.Ordinal) ||
                   stackTrace.Contains("Cc0ModelImportPostprocessor", StringComparison.Ordinal) ||
                   stackTrace.Contains("HubModelImportRepair", StringComparison.Ordinal);
        }

        sealed class Scope : IDisposable
        {
            public Scope() => _depth++;

            public void Dispose()
            {
                if (_depth > 0)
                    _depth--;
            }
        }
    }
}
#endif
