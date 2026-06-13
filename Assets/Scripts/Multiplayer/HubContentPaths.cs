using System.IO;
using UnityEngine;

namespace Hub.Multiplayer
{
    /// <summary>Cached content overrides — mirrors StreamingAssets layout under persistentDataPath/HubContent/.</summary>
    public static class HubContentPaths
    {
        public const string RootFolder = "HubContent";
        public const string ManifestFileName = "hub-content-manifest.json";
        public const string BaselineRelativePath = "Portfolio/" + ManifestFileName;

        public static string CacheRoot =>
            Path.Combine(Application.persistentDataPath, RootFolder);

        public static string CachedManifestPath =>
            Path.Combine(CacheRoot, ManifestFileName);

        public static string BaselineManifestPath =>
            Path.Combine(Application.streamingAssetsPath, BaselineRelativePath);

        /// <summary>Cached download if present, else StreamingAssets file.</summary>
        public static string ResolveConfigFile(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return null;

            var cached = Path.Combine(CacheRoot, relativePath.Replace('\\', '/'));
            if (File.Exists(cached))
                return cached;

            return Path.Combine(Application.streamingAssetsPath, relativePath.Replace('\\', '/'));
        }

        public static void EnsureCacheRoot()
        {
            Directory.CreateDirectory(CacheRoot);
        }
    }
}
