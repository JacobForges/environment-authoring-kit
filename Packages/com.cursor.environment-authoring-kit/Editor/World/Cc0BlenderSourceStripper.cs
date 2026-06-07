#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// CC0 packs ship .blend sources; Hub uses FBX/OBJ only. Strips .blend so Unity never calls Blender.
    /// </summary>
    public static class Cc0BlenderSourceStripper
    {
        public const string Cc0Root = "Assets/EnvironmentKit/CC0Imports";

        [MenuItem("Window/Environment Kit/World/Strip Blender Sources (CC0Imports)")]
        public static void StripFromMenu()
        {
            var n = StripUnderCc0Imports();
            AssetDatabase.Refresh();
            Debug.Log($"[CC0] Removed {n} Blender .blend source(s). Unity will use FBX/OBJ only.");
        }

        /// <summary>Called after CC0 zip unpack / import — safe no-op if none remain.</summary>
        public static int StripUnderCc0Imports()
        {
            var fullRoot = Path.GetFullPath(Cc0Root);
            if (!Directory.Exists(fullRoot))
                return 0;

            var removed = 0;
            foreach (var blend in Directory.GetFiles(fullRoot, "*.blend", SearchOption.AllDirectories))
            {
                var rel = blend.Replace('\\', '/');
                var idx = rel.IndexOf("Assets/", System.StringComparison.Ordinal);
                var assetPath = idx >= 0 ? rel.Substring(idx) : null;
                if (!string.IsNullOrEmpty(assetPath) && AssetDatabase.DeleteAsset(assetPath))
                {
                    removed++;
                    continue;
                }

                File.Delete(blend);
                var meta = blend + ".meta";
                if (File.Exists(meta))
                    File.Delete(meta);
                removed++;
            }

            var blendsDir = Path.Combine(fullRoot, "quaternius-ultimate-animated-characters",
                "Ultimate Animated Character Pack - Nov 2019", "Blends");
            if (Directory.Exists(blendsDir))
            {
                Directory.Delete(blendsDir, true);
                var meta = blendsDir + ".meta";
                if (File.Exists(meta))
                    File.Delete(meta);
            }

            return removed;
        }
    }
}
#endif
