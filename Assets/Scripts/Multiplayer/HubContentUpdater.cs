using System;
using System.IO;
using System.Threading.Tasks;
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

    public readonly struct HubContentUpdateResult
    {
        public HubContentUpdateState State { get; }
        public string ContentVersion { get; }
        public int WorldSeed { get; }
        public string Message { get; }

        public HubContentUpdateResult(
            HubContentUpdateState state,
            string contentVersion,
            int worldSeed,
            string message)
        {
            State = state;
            ContentVersion = contentVersion ?? string.Empty;
            WorldSeed = worldSeed;
            Message = message ?? string.Empty;
        }
    }

    /// <summary>
    /// Menu-boot content updater MVP — fetch remote manifest, cache config JSON, advertise version in lobby.
    /// Map bundles are listed but not downloaded yet (Addressables later).
    /// </summary>
    public static class HubContentUpdater
    {
        const string PrefsContentVersion = "Hub.ContentVersion";
        const string PrefsWorldSeed = "Hub.WorldSeed";
        const string PrefsPublishedUtc = "Hub.ContentPublishedUtc";

        const string EnvManifestUrl = "HUB_CONTENT_MANIFEST_URL";
        const int RequestTimeoutSeconds = 15;

        static HubContentManifest _resolved;

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

        public static async Task<HubContentUpdateResult> CheckOnBootAsync()
        {
            var local = ResolveLocalManifest();
            var remoteUrl = ResolveRemoteManifestUrl(local);

            if (string.IsNullOrWhiteSpace(remoteUrl))
            {
                ApplyResolvedManifest(local);
                CacheLocalState(local);
                return new HubContentUpdateResult(
                    HubContentUpdateState.Offline,
                    CurrentContentVersion,
                    CurrentWorldSeed,
                    "Up to date · offline manifest");
            }

            HubContentManifest remote;
            try
            {
                remote = await DownloadManifestAsync(remoteUrl);
            }
            catch (Exception ex)
            {
                ApplyResolvedManifest(local);
                return new HubContentUpdateResult(
                    HubContentUpdateState.Offline,
                    CurrentContentVersion,
                    CurrentWorldSeed,
                    "Offline — " + ex.Message);
            }

            if (remote == null)
            {
                ApplyResolvedManifest(local);
                return new HubContentUpdateResult(
                    HubContentUpdateState.Failed,
                    CurrentContentVersion,
                    CurrentWorldSeed,
                    "Update check failed — invalid manifest");
            }

            if (CompareManifestVersion(remote, local) <= 0)
            {
                ApplyResolvedManifest(local);
                CacheLocalState(local);
                return new HubContentUpdateResult(
                    HubContentUpdateState.UpToDate,
                    CurrentContentVersion,
                    CurrentWorldSeed,
                    "Up to date");
            }

            try
            {
                await DownloadConfigFilesAsync(remote);
                WriteCachedManifest(remote);
                ApplyResolvedManifest(remote);
                CacheLocalState(remote);
                return new HubContentUpdateResult(
                    HubContentUpdateState.Updated,
                    CurrentContentVersion,
                    CurrentWorldSeed,
                    "Update available — config cached");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HubContent] Config download failed — " + ex.Message);
                return new HubContentUpdateResult(
                    HubContentUpdateState.UpdateAvailable,
                    remote.contentVersion,
                    remote.worldSeed,
                    "Update available — download failed");
            }
        }

        public static string StatusLine(HubContentUpdateResult result)
        {
            switch (result.State)
            {
                case HubContentUpdateState.Checking:
                    return "Checking for updates…";
                case HubContentUpdateState.UpToDate:
                    return $"Up to date · content {result.ContentVersion}";
                case HubContentUpdateState.Updated:
                    return $"Updated · content {result.ContentVersion}";
                case HubContentUpdateState.UpdateAvailable:
                    return $"Update available · content {result.ContentVersion}";
                case HubContentUpdateState.Offline:
                    return string.IsNullOrWhiteSpace(result.Message)
                        ? $"Offline · content {result.ContentVersion}"
                        : result.Message;
                case HubContentUpdateState.Failed:
                    return "Update check failed — using local content";
                default:
                    return result.Message;
            }
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

            PlayerPrefs.Save();
        }

        static HubContentManifest ResolveLocalManifest()
        {
            if (TryLoadCached(out var cached))
                return cached;

            if (TryLoadBaseline(out var baseline))
                return baseline;

            return new HubContentManifest();
        }

        static string ResolveRemoteManifestUrl(HubContentManifest local)
        {
            var env = Environment.GetEnvironmentVariable(EnvManifestUrl);
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim();

            if (!string.IsNullOrWhiteSpace(local?.remoteManifestUrl))
                return local.remoteManifestUrl.Trim();

            return string.Empty;
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
