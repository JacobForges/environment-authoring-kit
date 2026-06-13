using System;

namespace Hub.Multiplayer
{
    /// <summary>
    /// Remote + local content manifest — version, world seed, optional bundle URLs, config JSON paths.
    /// Baseline ships in StreamingAssets/Portfolio/hub-content-manifest.json; cached copy lives under persistentDataPath/HubContent/.
    /// See docs/MULTIPLAYER_FREE_SETUP.md (Content updater MVP).
    /// </summary>
    [Serializable]
    public sealed class HubContentManifest
    {
        public int version = 1;
        public string contentVersion = "1";
        public int worldSeed;
        public string publishedUtc;
        public string remoteManifestUrl;
        /// <summary>GitHub raw or Releases URL — tried after remoteManifestUrl when primary (GCS) is down.</summary>
        public string githubManifestUrl;
        /// <summary>Optional extra manifest URLs tried in order after githubManifestUrl.</summary>
        public string[] fallbackManifestUrls = Array.Empty<string>();
        public HubContentFileEntry[] configFiles = Array.Empty<HubContentFileEntry>();
        public string bundleUrl;
        public string bundleHash;
        public long bundleBytes;
        public string notes;
    }

    [Serializable]
    public sealed class HubContentFileEntry
    {
        public string relativePath;
        public string url;
    }
}
