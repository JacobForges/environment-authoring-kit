using System.IO;
using UnityEngine;

namespace Hub
{
    /// <summary>Cached content overrides — mirrors StreamingAssets layout under persistentDataPath/HubContent/.</summary>
    public static class HubContentPaths
    {
        public const string RootFolder = "HubContent";
        public const string ManifestFileName = "hub-content-manifest.json";
        public const string BaselineRelativePath = "Portfolio/" + ManifestFileName;
        public const string WorldFolder = "World";
        public const string WorldSessionManifestFile = "CaveBuildWorldSessionManifest.json";
        public const string MapBundleFileName = "map-bundle.zip";
        public const string BaselineWorldRelativePath = "Portfolio/" + WorldSessionManifestFile;

        public static string CacheRoot =>
            Path.Combine(Application.persistentDataPath, RootFolder);

        public static string CachedManifestPath =>
            Path.Combine(CacheRoot, ManifestFileName);

        public static string BaselineManifestPath =>
            Path.Combine(Application.streamingAssetsPath, BaselineRelativePath);

        public static string WorldCacheRoot =>
            Path.Combine(CacheRoot, WorldFolder);

        public static string CachedWorldSessionManifestPath =>
            Path.Combine(WorldCacheRoot, WorldSessionManifestFile);

        public static string CachedMapBundlePath =>
            Path.Combine(WorldCacheRoot, MapBundleFileName);

        public static string BaselineWorldSessionManifestPath =>
            Path.Combine(Application.streamingAssetsPath, BaselineWorldRelativePath);

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

        /// <summary>Downloaded world manifest, else shipped StreamingAssets copy.</summary>
        public static string ResolveWorldSessionManifestPath()
        {
            if (File.Exists(CachedWorldSessionManifestPath))
                return CachedWorldSessionManifestPath;

            if (File.Exists(BaselineWorldSessionManifestPath))
                return BaselineWorldSessionManifestPath;

            return null;
        }

        public static void EnsureCacheRoot()
        {
            Directory.CreateDirectory(CacheRoot);
        }

        public static void EnsureWorldCacheRoot()
        {
            EnsureCacheRoot();
            Directory.CreateDirectory(WorldCacheRoot);
        }
    }
}
