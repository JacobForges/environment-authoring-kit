using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Hub.Competition;
using UnityEngine;
namespace Hub.Multiplayer
{
    /// <summary>
    /// Runtime host/join + Vivox voice. Pseudocode implementation — wire NetworkManager in MainScene.
    /// See docs/MULTIPLAYER_PIPELINE_PSEUDOCODE.md
    /// </summary>
    public class PortfolioSessionOrchestrator : MonoBehaviour
    {
        Lobby _lobby;
        string _relayJoinCode;

        public bool JoinedDedicatedCloud { get; private set; }

        public async Task HostPortfolioSessionAsync()
        {
            if (HubNetworkReachability.ShouldSkipCloudServices)
                throw new InvalidOperationException(HubNetworkReachability.OfflineStatusMessage);

            await EnsureUgsSignedInAsync();

            var networkManager = RequireNetworkManager();
            var transport = PrepareNetworkForSession(networkManager);
            var maxConn = PortfolioMultiplayerConfig.ListenServerRelayConnections;
            var allocation = await RelayService.Instance.CreateAllocationAsync(maxConn);
            if (allocation == null)
                throw new InvalidOperationException("Relay allocation failed — try again in a moment.");

            _relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            if (string.IsNullOrWhiteSpace(_relayJoinCode))
                throw new InvalidOperationException("Relay join code missing — check Unity Relay dashboard.");

            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));

            var seed = HubContentUpdater.LocalWorldSeed;
            var contentVersion = HubContentUpdater.LocalContentVersion;
            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new System.Collections.Generic.Dictionary<string, DataObject>
                {
                    {
                        PortfolioMultiplayerConfig.RelayJoinCodeLobbyKey,
                        new DataObject(DataObject.VisibilityOptions.Member, _relayJoinCode)
                    },
                    {
                        PortfolioMultiplayerConfig.WorldSeedLobbyKey,
                        new DataObject(DataObject.VisibilityOptions.Public, seed.ToString())
                    },
                    {
                        PortfolioMultiplayerConfig.ContentVersionLobbyKey,
                        new DataObject(DataObject.VisibilityOptions.Public, contentVersion)
                    },
                },
            };

            _lobby = await LobbyService.Instance.CreateLobbyAsync(
                PortfolioMultiplayerConfig.LobbyName,
                PortfolioMultiplayerConfig.MaxPlayersSession,
                options);

            JoinedDedicatedCloud = false;

            HubPlayerAuth.ApplyConnectionPayload(networkManager);
            AgentSecurityNetworkHooks.PrepareSession(networkManager);
            if (!networkManager.StartHost())
                throw new InvalidOperationException("StartHost failed — check NetworkManager and UnityTransport.");

            AgentSecurityGuard.Announce("Security guard online — host session monitored.");
            AgentSecurityLobbyBanSync.Bind(_lobby.Id, isHost: true);
            AgentSecurityLobbyBanSync.QueueMergeFromLobby(_lobby);
            AgentSecurityLobbyBanSync.QueuePublish();
            await PortfolioVivoxSupport.TryJoinLobbyVoiceAsync(_lobby.Id);
            Debug.Log($"[Multiplayer] Hosting {_lobby.Name} · join via Quick Join (no code)");
        }

        /// <returns>True when a listen-host lobby was joined; false when none exists.</returns>
        public async Task<bool> QuickJoinOrShowOfflineAsync()
        {
            if (HubNetworkReachability.ShouldSkipCloudServices)
                return false;

            await EnsureUgsSignedInAsync();

            var listen = await PortfolioSessionQueries.QueryLatestLobbyAsync(
                PortfolioMultiplayerConfig.LobbyName);
            if (listen == null)
                return false;

            await JoinLobbyAsync(listen.Id);
            return true;
        }

        public async Task JoinLobbyAsync(string lobbyId)
        {
            if (string.IsNullOrWhiteSpace(lobbyId))
                throw new InvalidOperationException("Lobby id missing — session may have closed.");

            _lobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);
            if (_lobby == null)
                throw new InvalidOperationException("Could not join that lobby — it may have closed.");

            JoinedDedicatedCloud = PortfolioSessionQueries.IsDedicatedCloudLobby(_lobby);
            if (_lobby.Data == null
                || !_lobby.Data.TryGetValue(PortfolioMultiplayerConfig.RelayJoinCodeLobbyKey, out var relayData)
                || relayData == null
                || string.IsNullOrWhiteSpace(relayData.Value))
                throw new InvalidOperationException("Lobby missing relay join code — host may still be starting.");

            ValidateLobbyContentOrThrow(_lobby);

            _relayJoinCode = relayData.Value.Trim();
            var joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode: _relayJoinCode);
            if (joinAllocation == null)
                throw new InvalidOperationException("Relay join failed — lobby may be full or expired.");

            var networkManager = RequireNetworkManager();
            var transport = PrepareNetworkForSession(networkManager);
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, "dtls"));

            HubPlayerAuth.ApplyConnectionPayload(networkManager);
            AgentSecurityNetworkHooks.PrepareSession(networkManager);
            if (!networkManager.StartClient())
                throw new InvalidOperationException("StartClient failed — check NetworkManager and UnityTransport.");

            if (string.IsNullOrEmpty(_lobby.Id))
                throw new InvalidOperationException("Joined lobby is invalid — try Play Online again.");

            AgentSecurityLobbyBanSync.Bind(_lobby.Id, isHost: false);
            AgentSecurityLobbyBanSync.QueueAnnounceLobbyBans(_lobby);
            await PortfolioVivoxSupport.TryJoinLobbyVoiceAsync(_lobby.Id);
            Debug.Log("[Multiplayer] Joined lobby");
        }

        static NetworkManager RequireNetworkManager()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                throw new InvalidOperationException("NetworkManager missing — run Game → Setup Portfolio Menu (MainScene).");

            EnsureNetworkConfig(networkManager);
            return networkManager;
        }

        static UnityTransport PrepareNetworkForSession(NetworkManager networkManager)
        {
            if (networkManager == null)
                throw new InvalidOperationException("NetworkManager missing.");

            PortfolioNetworkShutdown.PrepareForNewSession(networkManager);
            return RequireTransport(networkManager);
        }

        static UnityTransport RequireTransport(NetworkManager networkManager)
        {
            if (networkManager == null)
                throw new InvalidOperationException("NetworkManager missing.");

            EnsureNetworkConfig(networkManager);

            var transport = networkManager.GetComponent<UnityTransport>();
            if (transport == null)
                throw new InvalidOperationException(
                    "UnityTransport missing on NetworkManager — run Game → Setup Portfolio Menu (MainScene).");

            networkManager.NetworkConfig.NetworkTransport = transport;

            if (networkManager.NetworkConfig.PlayerPrefab == null)
                throw new InvalidOperationException("Assign PortfolioNetworkPlayer on NetworkManager.");

            return transport;
        }

        static void EnsureNetworkConfig(NetworkManager networkManager)
        {
            if (networkManager.NetworkConfig == null)
                networkManager.NetworkConfig = new NetworkConfig();
        }

        static async Task EnsureUgsSignedInAsync()
        {
            await HubUnityServices.EnsureSignedInAnonymouslyAsync();

            await CompetitionCloudPersistence.TrySyncAfterSignInAsync();
        }

        static void ValidateLobbyContentOrThrow(Lobby lobby)
        {
            if (lobby?.Data == null)
                return;

            string lobbyVersion = null;
            if (lobby.Data.TryGetValue(PortfolioMultiplayerConfig.ContentVersionLobbyKey, out var versionData)
                && versionData != null
                && !string.IsNullOrWhiteSpace(versionData.Value))
            {
                lobbyVersion = versionData.Value.Trim();
            }

            int lobbySeed = 0;
            if (lobby.Data.TryGetValue(PortfolioMultiplayerConfig.WorldSeedLobbyKey, out var seedData)
                && seedData != null
                && !string.IsNullOrWhiteSpace(seedData.Value)
                && int.TryParse(seedData.Value, out var parsedSeed))
            {
                lobbySeed = parsedSeed;
            }

            var localVersion = HubContentUpdater.LocalContentVersion;
            var localSeed = HubContentUpdater.LocalWorldSeed;
            var versionMismatch = !string.IsNullOrWhiteSpace(lobbyVersion)
                                  && !string.IsNullOrWhiteSpace(localVersion)
                                  && !string.Equals(lobbyVersion, localVersion, StringComparison.Ordinal);
            var seedMismatch = lobbySeed != 0 && localSeed != 0 && lobbySeed != localSeed;

            if (!versionMismatch && !seedMismatch)
                return;

            if (versionMismatch && seedMismatch)
            {
                throw new InvalidOperationException(
                    $"Map mismatch — host content {lobbyVersion} seed {lobbySeed}, "
                    + $"yours {localVersion} seed {localSeed}. Restart from the menu to check for updates.");
            }

            if (versionMismatch)
            {
                throw new InvalidOperationException(
                    $"Content version mismatch — host {lobbyVersion}, yours {localVersion}. "
                    + "Restart from the menu to check for updates.");
            }

            throw new InvalidOperationException(
                $"World seed mismatch — host {lobbySeed}, yours {localSeed}. "
                + "Download the latest map from the menu, then try Play Online again.");
        }

        void OnDestroy()
        {
            _ = PortfolioVivoxSupport.TryLeaveLobbyVoiceAsync();
            AgentSecurityLobbyBanSync.ClearBind();
            TryDeleteHostedLobbyInternal();
        }

        /// <summary>Best-effort lobby delete before process exit (called from graceful quit).</summary>
        public static void TryDeleteHostedLobbyForQuit()
        {
            var orchestrator = UnityEngine.Object.FindAnyObjectByType<PortfolioSessionOrchestrator>();
            orchestrator?.TryDeleteHostedLobbyInternal();
        }

        void TryDeleteHostedLobbyInternal()
        {
            if (_lobby == null)
                return;

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsHost)
                return;

            try
            {
                _ = LobbyService.Instance.DeleteLobbyAsync(_lobby.Id);
            }
            catch
            {
                /* session teardown */
            }

            _lobby = null;
        }

        void OnApplicationQuit()
        {
            _ = PortfolioVivoxSupport.TryLeaveLobbyVoiceAsync();
            PortfolioNetworkShutdown.SafeTeardown(NetworkManager.Singleton);
        }
    }
}
