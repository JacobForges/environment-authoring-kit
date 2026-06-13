using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Hub;
using Hub.Competition;
using UnityEngine;
using UnityEngine.Networking;

namespace Hub.Multiplayer
{
    public enum HubContentUpdateState
    {
        Checking,
        UpToDate,
        UpdateAvailable,
        Updated,
        Offline,
        Failed,
    }


    public enum HubManifestFetchSource
    {
        Baseline,
        Primary,
        GitHub,
        Fallback,
    }

    public readonly struct HubContentUpdateResult
    {
        public HubContentUpdateState State { get; }
        public string ContentVersion { get; }
        public int WorldSeed { get; }
        public string Message { get; }
        public HubManifestFetchSource FetchSource { get; }

        public HubContentUpdateResult(
            HubContentUpdateState state,
            string contentVersion,
            int worldSeed,
            string message,
            HubManifestFetchSource fetchSource = HubManifestFetchSource.Baseline)
        {
            State = state;
            ContentVersion = contentVersion ?? string.Empty;
            WorldSeed = worldSeed;
            Message = message ?? string.Empty;
            FetchSource = fetchSource;
        }
    }

    /// <summary>
    /// Menu-boot content updater — remote manifest, config JSON cache, map bundle download, lobby version gate.
    /// </summary>
    public static class HubContentUpdater
    {
        const string PrefsContentVersion = "Hub.ContentVersion";
        const string PrefsWorldSeed = "Hub.WorldSeed";
        const string PrefsPublishedUtc = "Hub.ContentPublishedUtc";
        const string PrefsBundleHash = "Hub.ContentBundleHash";

        const string EnvManifestUrl = "HUB_CONTENT_MANIFEST_URL";
        const int RequestTimeoutSeconds = 30;

        static HubContentManifest _resolved;
        static HubContentUpdateResult _lastResult;
        static HubManifestFetchSource _lastFetchSource;

        public static string LocalContentVersion => CurrentContentVersion;

        public static int LocalWorldSeed => CurrentWorldSeed;

        public static HubContentUpdateResult LastResult => _lastResult;

        public static string CurrentContentVersion
        {
            get
            {
                var cached = PlayerPrefs.GetString(PrefsContentVersion, string.Empty);
                if (!string.IsNullOrWhiteSpace(cached))
                    return cached.Trim();

                if (TryLoadBaseline(out var baseline))
                    return baseline.contentVersion?.Trim() ?? "1";

                return "1";
            }
        }

        public static int CurrentWorldSeed
        {
            get
            {
                if (PlayerPrefs.HasKey(PrefsWorldSeed))
                    return PlayerPrefs.GetInt(PrefsWorldSeed);

                if (CaveBuildWorldSessionManifest.TryLoad(out var world) && world.seed != 0)
                    return world.seed;

                if (TryLoadBaseline(out var baseline))
                    return baseline.worldSeed;

                return 0;
            }
        }

        public static bool IsReadyForMultiplayer(out string reason)
        {
            if (_lastResult.State == HubContentUpdateState.UpdateAvailable)
            {
                reason = "Map update available — restart from the menu to download, then try Play Online.";
                return false;
            }

            reason = null;
            return true;
        }

        public static async Task<HubContentUpdateResult> CheckOnBootAsync()
        {
            await HubNetworkReachability.EnsureBootCheckAsync();

            _lastResult = new HubContentUpdateResult(
                HubContentUpdateState.Checking,
                CurrentContentVersion,
                CurrentWorldSeed,
                "Checking for updates…",
                HubManifestFetchSource.Baseline);

            var local = ResolveLocalManifest();

            if (HubNetworkReachability.OfflineMode)
            {
                ApplyResolvedManifest(local);
                CacheLocalState(local);
                _lastFetchSource = HubManifestFetchSource.Baseline;
                _lastResult = BuildResult(
                    HubContentUpdateState.Offline,
                    HubNetworkReachability.OfflineStatusMessage,
                    HubManifestFetchSource.Baseline);
                return _lastResult;
            }

            var urlChain = BuildManifestUrlChain(local);
            if (urlChain.Count == 0)
            {
                ApplyResolvedManifest(local);
                CacheLocalState(local);
                _lastFetchSource = HubManifestFetchSource.Baseline;
                _lastResult = BuildResult(
                    HubContentUpdateState.Offline,
                    "Up to date · offline manifest",
                    HubManifestFetchSource.Baseline);
                return _lastResult;
            }

            HubContentManifest remote = null;
            HubManifestFetchSource fetchSource = HubManifestFetchSource.Baseline;
            Exception lastError = null;

            for (var i = 0; i < urlChain.Count; i++)
            {
                var url = urlChain[i].url;
                var source = urlChain[i].source;
                try
                {
                    remote = await DownloadManifestAsync(url);
                    fetchSource = source;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Debug.LogWarning("[HubContent] Manifest fetch failed (" + source + ") — " + ex.Message);
                }
            }

            if (remote == null)
            {
                ApplyResolvedManifest(local);
                _lastFetchSource = HubManifestFetchSource.Baseline;
                _lastResult = BuildResult(
                    HubContentUpdateState.Offline,
                    "Offline — " + (lastError?.Message ?? "manifest fetch failed"),
                    HubManifestFetchSource.Baseline);
                return _lastResult;
            }

            _lastFetchSource = fetchSource;

            if (CompareManifestVersion(remote, local) <= 0)
            {
                ApplyResolvedManifest(local);
                CacheLocalState(local);
                _lastResult = BuildResult(
                    HubContentUpdateState.UpToDate,
                    ContentStatusPrefix(fetchSource),
                    fetchSource);
                return _lastResult;
            }

            try
            {
                await DownloadConfigFilesAsync(remote);
                await DownloadMapBundleAsync(remote);
                ApplyWorldSeedFromManifest(remote);
                WriteCachedManifest(remote);
                ApplyResolvedManifest(remote);
                CacheLocalState(remote);
                CompetitionSeasonManager.InvalidateCache();
                _lastResult = BuildResult(
                    HubContentUpdateState.Updated,
                    "Updated · map " + CurrentContentVersion + " · seed " + CurrentWorldSeed,
                    fetchSource);
                return _lastResult;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HubContent] Update download failed — " + ex.Message);
                _lastResult = new HubContentUpdateResult(
                    HubContentUpdateState.UpdateAvailable,
                    remote.contentVersion,
                    remote.worldSeed,
                    "Update available — download failed · " + ex.Message,
                    fetchSource);
                return _lastResult;
            }
        }

        public static string StatusLine(HubContentUpdateResult result)
        {
            switch (result.State)
            {
                case HubContentUpdateState.Checking:
                    return "Checking for updates…";
                case HubContentUpdateState.UpToDate:
                    return FormatReadyLine(result, ContentStatusPrefix(result.FetchSource));
                case HubContentUpdateState.Updated:
                {
                    var backupNote = BackupCatalogNote(result.FetchSource);
                    var prefix = string.IsNullOrEmpty(backupNote) ? "Updated" : "Updated · " + backupNote.TrimEnd('.');
                    return FormatReadyLine(result, prefix);
                }
                case HubContentUpdateState.UpdateAvailable:
                    return $"Update available · content {result.ContentVersion} · restart to retry";
                case HubContentUpdateState.Offline:
                    if (HubNetworkReachability.OfflineMode)
                        return HubNetworkReachability.OfflineStatusMessage;
                    return string.IsNullOrWhiteSpace(result.Message)
                        ? FormatReadyLine(result, "Offline")
                        : result.Message;
                case HubContentUpdateState.Failed:
                    return "Update check failed — using local content";
                default:
                    return result.Message;
            }
        }

        static string ContentStatusPrefix(HubManifestFetchSource source)
        {
            var backup = BackupCatalogNote(source);
            return string.IsNullOrEmpty(backup) ? "Up to date" : backup.TrimEnd(' ', '·');
        }

        static string BackupCatalogNote(HubManifestFetchSource source)
        {
            switch (source)
            {
                case HubManifestFetchSource.GitHub:
                    return "Using backup catalog (GitHub).";
                case HubManifestFetchSource.Fallback:
                    return "Using backup catalog.";
                default:
                    return string.Empty;
            }
        }

        static string FormatReadyLine(HubContentUpdateResult result, string prefix)
        {
            var seedNote = result.WorldSeed != 0 ? $" · seed {result.WorldSeed}" : string.Empty;
            return $"{prefix} · content {result.ContentVersion}{seedNote}";
        }

        static HubContentUpdateResult BuildResult(
            HubContentUpdateState state,
            string message,
            HubManifestFetchSource fetchSource = HubManifestFetchSource.Baseline)
        {
            return new HubContentUpdateResult(
                state,
                CurrentContentVersion,
                CurrentWorldSeed,
                message,
                fetchSource);
        }

        static void ApplyResolvedManifest(HubContentManifest manifest)
        {
            _resolved = manifest;
        }

        static void CacheLocalState(HubContentManifest manifest)
        {
            if (manifest == null)
                return;

            if (!string.IsNullOrWhiteSpace(manifest.contentVersion))
                PlayerPrefs.SetString(PrefsContentVersion, manifest.contentVersion.Trim());

            PlayerPrefs.SetInt(PrefsWorldSeed, manifest.worldSeed);

            if (!string.IsNullOrWhiteSpace(manifest.publishedUtc))
                PlayerPrefs.SetString(PrefsPublishedUtc, manifest.publishedUtc.Trim());

            if (!string.IsNullOrWhiteSpace(manifest.bundleHash))
                PlayerPrefs.SetString(PrefsBundleHash, manifest.bundleHash.Trim());

            PlayerPrefs.Save();
        }

        static HubContentManifest ResolveLocalManifest()
        {
            if (_resolved != null)
                return _resolved;

            if (TryLoadCached(out var cached))
                return cached;

            if (TryLoadBaseline(out var baseline))
                return baseline;

            return new HubContentManifest();
        }

        readonly struct ManifestUrlEntry
        {
            public readonly string url;
            public readonly HubManifestFetchSource source;

            public ManifestUrlEntry(string url, HubManifestFetchSource source)
            {
                this.url = url;
                this.source = source;
            }
        }

        static List<ManifestUrlEntry> BuildManifestUrlChain(HubContentManifest local)
        {
            var chain = new List<ManifestUrlEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string url, HubManifestFetchSource source)
            {
                if (string.IsNullOrWhiteSpace(url))
                    return;

                var trimmed = url.Trim();
                if (!seen.Add(trimmed))
                    return;

                chain.Add(new ManifestUrlEntry(trimmed, source));
            }

            var env = Environment.GetEnvironmentVariable(EnvManifestUrl);
            Add(env, HubManifestFetchSource.Primary);
            Add(local?.remoteManifestUrl, HubManifestFetchSource.Primary);
            Add(local?.githubManifestUrl, HubManifestFetchSource.GitHub);

            if (local?.fallbackManifestUrls != null)
            {
                foreach (var url in local.fallbackManifestUrls)
                    Add(url, HubManifestFetchSource.Fallback);
            }

            return chain;
        }

        static async Task<HubContentManifest> DownloadManifestAsync(string url)
        {
            using var req = UnityWebRequest.Get(url);
            req.timeout = RequestTimeoutSeconds;
            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException(req.error ?? "manifest fetch failed");

            var text = req.downloadHandler?.text;
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("manifest empty");

            return JsonUtility.FromJson<HubContentManifest>(text);
        }

        static async Task DownloadConfigFilesAsync(HubContentManifest manifest)
        {
            if (manifest?.configFiles == null || manifest.configFiles.Length == 0)
                return;

            HubContentPaths.EnsureCacheRoot();

            foreach (var entry in manifest.configFiles)
            {
                if (entry == null
                    || string.IsNullOrWhiteSpace(entry.relativePath)
                    || string.IsNullOrWhiteSpace(entry.url))
                    continue;

                using var req = UnityWebRequest.Get(entry.url.Trim());
                req.timeout = RequestTimeoutSeconds;
                var op = req.SendWebRequest();
                while (!op.isDone)
                    await Task.Yield();

                if (req.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException(
                        "config " + entry.relativePath + " — " + (req.error ?? "download failed"));

                var rel = entry.relativePath.Replace('\\', '/').Trim();
                var dest = Path.Combine(HubContentPaths.CacheRoot, rel);
                var dir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(dest, req.downloadHandler.text);
                Debug.Log("[HubContent] Cached " + rel);
            }
        }

        static async Task DownloadMapBundleAsync(HubContentManifest manifest)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.bundleUrl))
                return;

            var url = manifest.bundleUrl.Trim();
            using var req = UnityWebRequest.Get(url);
            req.timeout = RequestTimeoutSeconds;
            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException("map bundle — " + (req.error ?? "download failed"));

            var bytes = req.downloadHandler?.data;
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException("map bundle empty");

            if (manifest.bundleBytes > 0 && bytes.Length != manifest.bundleBytes)
            {
                throw new InvalidOperationException(
                    "map bundle size mismatch — expected " + manifest.bundleBytes + ", got " + bytes.Length);
            }

            if (!string.IsNullOrWhiteSpace(manifest.bundleHash)
                && !VerifySha256(bytes, manifest.bundleHash.Trim()))
            {
                throw new InvalidOperationException("map bundle hash mismatch");
            }

            HubContentPaths.EnsureWorldCacheRoot();
            File.WriteAllBytes(HubContentPaths.CachedMapBundlePath, bytes);
            ExtractMapBundle(bytes);
            Debug.Log("[HubContent] Map bundle applied · " + bytes.Length + " bytes");
        }

        static bool VerifySha256(byte[] bytes, string expectedHex)
        {
            if (bytes == null || string.IsNullOrWhiteSpace(expectedHex))
                return false;

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes);
            var actual = BitConverter.ToString(hash).Replace("-", string.Empty);
            return string.Equals(actual, expectedHex, StringComparison.OrdinalIgnoreCase);
        }

        static void ExtractMapBundle(byte[] zipBytes)
        {
            HubContentPaths.EnsureCacheRoot();
            using var ms = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                var rel = entry.FullName.Replace('\\', '/').Trim();
                if (rel.Contains("..", StringComparison.Ordinal))
                    continue;

                if (!rel.StartsWith("World/", StringComparison.Ordinal)
                    && !rel.StartsWith("Competition/", StringComparison.Ordinal)
                    && rel != HubContentPaths.WorldSessionManifestFile)
                {
                    rel = HubContentPaths.WorldFolder + "/" + rel;
                }

                var dest = Path.Combine(HubContentPaths.CacheRoot, rel);
                var dir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                entry.ExtractToFile(dest, overwrite: true);
                Debug.Log("[HubContent] Extracted " + rel);
            }
        }

        static void ApplyWorldSeedFromManifest(HubContentManifest manifest)
        {
            if (manifest == null || manifest.worldSeed == 0)
                return;

            if (CaveBuildWorldSessionManifest.TryLoad(out var existing) && existing.seed == manifest.worldSeed)
                return;

            HubContentPaths.EnsureWorldCacheRoot();
            var world = new CaveBuildWorldSessionManifest
            {
                sceneName = "MainScene",
                seed = manifest.worldSeed,
                buildUtc = manifest.publishedUtc ?? DateTime.UtcNow.ToString("o"),
                notes = "Applied from hub-content-manifest " + manifest.contentVersion,
            };
            File.WriteAllText(
                HubContentPaths.CachedWorldSessionManifestPath,
                JsonUtility.ToJson(world, true) + "\n");
        }

        static void WriteCachedManifest(HubContentManifest manifest)
        {
            HubContentPaths.EnsureCacheRoot();
            File.WriteAllText(HubContentPaths.CachedManifestPath, JsonUtility.ToJson(manifest, true) + "\n");
        }

        static int CompareManifestVersion(HubContentManifest remote, HubContentManifest local)
        {
            if (remote == null)
                return -1;
            if (local == null)
                return 1;

            var remoteVer = remote.version;
            var localVer = local.version;
            if (remoteVer != localVer)
                return remoteVer.CompareTo(localVer);

            return CompareContentVersion(remote.contentVersion, local.contentVersion);
        }

        static int CompareContentVersion(string remote, string local)
        {
            remote = remote?.Trim() ?? string.Empty;
            local = local?.Trim() ?? string.Empty;
            if (remote == local)
                return 0;

            var remoteParts = remote.Split('.');
            var localParts = local.Split('.');
            var count = Math.Max(remoteParts.Length, localParts.Length);
            for (var i = 0; i < count; i++)
            {
                var r = i < remoteParts.Length && int.TryParse(remoteParts[i], out var rv) ? rv : 0;
                var l = i < localParts.Length && int.TryParse(localParts[i], out var lv) ? lv : 0;
                if (r != l)
                    return r.CompareTo(l);
            }

            return string.Compare(remote, local, StringComparison.Ordinal);
        }

        public static bool TryLoadBaseline(out HubContentManifest manifest)
        {
            manifest = null;
            return TryLoadFromPath(HubContentPaths.BaselineManifestPath, out manifest);
        }

        static bool TryLoadCached(out HubContentManifest manifest)
        {
            manifest = null;
            return TryLoadFromPath(HubContentPaths.CachedManifestPath, out manifest);
        }

        static bool TryLoadFromPath(string path, out HubContentManifest manifest)
        {
            manifest = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            try
            {
                manifest = JsonUtility.FromJson<HubContentManifest>(File.ReadAllText(path));
                return manifest != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
