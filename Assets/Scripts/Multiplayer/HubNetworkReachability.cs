using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Hub.Multiplayer
{
    /// <summary>
    /// Menu-boot network gate — skips UGS, Vivox, lobby chat, and remote content when offline.
    /// </summary>
    public static class HubNetworkReachability
    {
        const int ProbeTimeoutSeconds = 4;
        const string ProbeUrl = "https://cloudflare.com/cdn-cgi/trace";

        public const string OfflineStatusMessage = "No internet — solo & LAN host still work.";

        static bool _bootCheckComplete;

        /// <summary>True when the device has no usable internet at boot (solo still works).</summary>
        public static bool OfflineMode { get; private set; }

        public static bool BootCheckComplete => _bootCheckComplete;

        public static bool ShouldSkipCloudServices => OfflineMode;

        /// <summary>Run once on title menu boot before remote manifest / UGS checks.</summary>
        public static async Task EnsureBootCheckAsync()
        {
            if (_bootCheckComplete)
                return;

            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                OfflineMode = true;
                _bootCheckComplete = true;
                Debug.Log("[HubNetwork] Offline — Application.internetReachability NotReachable");
                return;
            }

            if (Application.internetReachability == NetworkReachability.ReachableViaLocalAreaNetwork)
            {
                OfflineMode = true;
                _bootCheckComplete = true;
                Debug.Log("[HubNetwork] Offline for cloud — local network only");
                return;
            }

            OfflineMode = !await ProbeInternetAsync();
            _bootCheckComplete = true;

            if (OfflineMode)
                Debug.Log("[HubNetwork] Offline — connectivity probe failed");
        }

        static async Task<bool> ProbeInternetAsync()
        {
            using var req = UnityWebRequest.Head(ProbeUrl);
            req.timeout = ProbeTimeoutSeconds;
            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            return req.result == UnityWebRequest.Result.Success;
        }
    }
}
