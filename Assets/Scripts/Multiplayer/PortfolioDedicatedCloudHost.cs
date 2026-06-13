using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hub.Competition;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Hub.Multiplayer
{
    /// <summary>
    /// Free-tier dedicated cloud host — NGO StartServer + Relay + public Lobby heartbeat.
    /// Auto-starts on UNITY_SERVER builds, env HUB_DEDICATED_SERVER=1, or Editor menu Play Mode shortcut.
    /// </summary>
    public sealed class PortfolioDedicatedCloudHost : MonoBehaviour
    {
        const float LobbyHeartbeatSeconds = 25f;

        static PortfolioDedicatedCloudHost _instance;

        Lobby _lobby;
        float _heartbeatAt;
        bool _starting;

        public const string EditorPlayAsDedicatedPrefsKey = "Hub.EditorPlayAsDedicatedServer";

        public static bool IsDedicatedProcess
        {
            get
            {
#if UNITY_SERVER
                return true;
#endif
                if (string.Equals(
                        Environment.GetEnvironmentVariable("HUB_DEDICATED_SERVER"),
                        "1",
                        StringComparison.Ordinal))
                    return true;

#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(EditorPlayAsDedicatedPrefsKey, false);
#else
                return false;
#endif
            }
        }

        public static void EnsureAutoStart()
        {
            if (!IsDedicatedProcess || _instance != null)
                return;

            var go = new GameObject(nameof(PortfolioDedicatedCloudHost));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PortfolioDedicatedCloudHost>();
        }

        async void Start()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            await StartCloudHostAsync();
        }

        void Update()
        {
            if (_lobby == null || Time.unscaledTime < _heartbeatAt)
                return;

            _heartbeatAt = Time.unscaledTime + LobbyHeartbeatSeconds;
            _ = HeartbeatLobbyAsync();
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            if (_lobby == null)
                return;

            try
            {
                _ = LobbyService.Instance.DeleteLobbyAsync(_lobby.Id);
            }
            catch
            {
                // process exit
            }
        }

        public static async Task StartCloudHostAsync()
        {
            if (_instance != null)
            {
                await _instance.StartCloudHostInternalAsync();
                return;
            }

            var go = new GameObject(nameof(PortfolioDedicatedCloudHost));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PortfolioDedicatedCloudHost>();
            await _instance.StartCloudHostInternalAsync();
        }

        async Task StartCloudHostInternalAsync()
        {
            if (_starting)
                return;

            var nm = await WaitForNetworkManagerAsync();
            if (nm == null)
            {
                Debug.LogError("[DedicatedCloud] NetworkManager missing — run Game → Setup Portfolio Menu (MainScene).");
                return;
            }

            if (nm.IsListening)
                return;

            _starting = true;
            try
            {
                await HubUnityServices.EnsureSignedInAnonymouslyAsync();

                var maxConn = PortfolioMultiplayerConfig.DedicatedServerRelayConnections;
                var allocation = await RelayService.Instance.CreateAllocationAsync(maxConn);
                var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                var transport = nm.GetComponent<UnityTransport>();
                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));

                var seed = HubContentUpdater.LocalWorldSeed;
                var contentVersion = HubContentUpdater.LocalContentVersion;
                _lobby = await CreateDedicatedLobbyAsync(joinCode, seed, contentVersion);

                HubPlayerAuth.ApplyConnectionPayload(nm);
                AgentSecurityNetworkHooks.PrepareSession(nm);
                if (!nm.StartServer())
                {
                    Debug.LogError("[DedicatedCloud] StartServer failed.");
                    return;
                }

                CompetitionSeasonManager.EnsureLoaded();
                AgentSecurityLobbyBanSync.Bind(_lobby.Id, isHost: true);
                AgentSecurityLobbyBanSync.QueueMergeFromLobby(_lobby);
                AgentSecurityLobbyBanSync.QueuePublish();
                AgentSecurityGuard.Announce(
                    "Dedicated cloud server online — up to "
                    + PortfolioMultiplayerConfig.MaxPlayersDedicatedCloud
                    + " players via Play Online.");

                _heartbeatAt = Time.unscaledTime + LobbyHeartbeatSeconds;
                Debug.Log(
                    $"[DedicatedCloud] Live · lobby {PortfolioMultiplayerConfig.DedicatedLobbyName} · "
                    + $"Relay code {joinCode} · season {CompetitionSeasonManager.Active.seasonId}");
            }
            catch (Exception ex)
            {
                Debug.LogError("[DedicatedCloud] Start failed — " + ex.Message);
            }
            finally
            {
                _starting = false;
            }
        }

        static async Task<Lobby> CreateDedicatedLobbyAsync(
            string relayJoinCode,
            int worldSeed,
            string contentVersion)
        {
            var stale = await PortfolioSessionQueries.QueryLatestLobbyAsync(
                PortfolioMultiplayerConfig.DedicatedLobbyName);
            if (stale != null)
            {
                try
                {
                    await LobbyService.Instance.DeleteLobbyAsync(stale.Id);
                }
                catch
                {
                    // expired or foreign host
                }
            }

            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    {
                        PortfolioMultiplayerConfig.RelayJoinCodeLobbyKey,
                        new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode)
                    },
                    {
                        PortfolioMultiplayerConfig.WorldSeedLobbyKey,
                        new DataObject(DataObject.VisibilityOptions.Public, worldSeed.ToString())
                    },
                    {
                        PortfolioMultiplayerConfig.ContentVersionLobbyKey,
                        new DataObject(DataObject.VisibilityOptions.Public, contentVersion ?? "1")
                    },
                    {
                        PortfolioMultiplayerConfig.DedicatedLobbyFlagKey,
                        new DataObject(DataObject.VisibilityOptions.Public, "1")
                    },
                },
            };

            return await LobbyService.Instance.CreateLobbyAsync(
                PortfolioMultiplayerConfig.DedicatedLobbyName,
                PortfolioMultiplayerConfig.MaxPlayersDedicatedCloud,
                options);
        }

        static async Task<NetworkManager> WaitForNetworkManagerAsync()
        {
            for (var i = 0; i < 300; i++)
            {
                var nm = NetworkManager.Singleton;
                if (nm != null)
                    return nm;

                await Task.Yield();
            }

            return NetworkManager.Singleton;
        }

        async Task HeartbeatLobbyAsync()
        {
            if (_lobby == null)
                return;

            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(_lobby.Id);
            }
            catch (LobbyServiceException ex) when (ex.Reason == LobbyExceptionReason.LobbyNotFound)
            {
                Debug.LogWarning("[DedicatedCloud] Lobby expired — recreating…");
                await StartCloudHostInternalAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DedicatedCloud] Lobby heartbeat — " + ex.Message);
            }
        }
    }
}
