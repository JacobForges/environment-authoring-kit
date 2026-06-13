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

            var seed = HubContentUpdater.CurrentWorldSeed;
            var contentVersion = HubContentUpdater.CurrentContentVersion;
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

        /// <returns>True when a lobby was joined; false when no cloud or listen lobby exists.</returns>
        public async Task<bool> QuickJoinOrShowOfflineAsync()
        {
            await EnsureUgsSignedInAsync();

            var cloud = await PortfolioSessionQueries.QueryLatestLobbyAsync(
                PortfolioMultiplayerConfig.DedicatedLobbyName);
            if (cloud != null)
            {
                try
                {
                    await JoinLobbyAsync(cloud.Id);
                    return true;
                }
                catch (InvalidOperationException ex) when (IsRecoverableLobbyJoinFailure(ex))
                {
                    Debug.LogWarning("[Multiplayer] Cloud lobby unavailable — trying listen host. " + ex.Message);
                }
            }

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

            ValidateContentVersionOrThrow(_lobby);

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

        static bool IsRecoverableLobbyJoinFailure(InvalidOperationException ex)
        {
            var msg = ex.Message ?? string.Empty;
            return msg.Contains("relay", StringComparison.OrdinalIgnoreCase)
                   || msg.Contains("lobby", StringComparison.OrdinalIgnoreCase)
                   || msg.Contains("closed", StringComparison.OrdinalIgnoreCase)
                   || msg.Contains("expired", StringComparison.OrdinalIgnoreCase)
                   || msg.Contains("full", StringComparison.OrdinalIgnoreCase);
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
            await HubUnityServices.EnsureInitializedAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            await CompetitionCloudPersistence.TrySyncAfterSignInAsync();
        }

        static void ValidateContentVersionOrThrow(Lobby lobby)
        {
            if (lobby?.Data == null)
                return;

            if (lobby.Data.TryGetValue(PortfolioMultiplayerConfig.ContentVersionLobbyKey, out var versionData)
                && versionData != null
                && !string.IsNullOrWhiteSpace(versionData.Value))
            {
                var lobbyVersion = versionData.Value.Trim();
                var localVersion = HubContentUpdater.CurrentContentVersion;
                if (!string.IsNullOrWhiteSpace(localVersion)
                    && !string.Equals(lobbyVersion, localVersion, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Content version mismatch — host {lobbyVersion}, yours {localVersion}. "
                        + "Restart from the menu to check for updates.");
                }

                return;
            }

            ValidateWorldSeedOrThrow(lobby);
        }

        static void ValidateWorldSeedOrThrow(Lobby lobby)
        {
            if (lobby?.Data == null)
                return;

            if (!lobby.Data.TryGetValue(PortfolioMultiplayerConfig.WorldSeedLobbyKey, out var seedData)
                || seedData == null
                || string.IsNullOrWhiteSpace(seedData.Value))
                return;

            if (!int.TryParse(seedData.Value, out var lobbySeed) || lobbySeed == 0)
                return;

            var localSeed = HubContentUpdater.CurrentWorldSeed;
            if (localSeed != 0 && lobbySeed != localSeed)
            {
                throw new InvalidOperationException(
                    $"World version mismatch — host seed {lobbySeed}, your build seed {localSeed}. "
                    + "Use the same world build or check for updates from the menu.");
            }
        }

        void OnDestroy()
        {
            _ = PortfolioVivoxSupport.TryLeaveLobbyVoiceAsync();
            AgentSecurityLobbyBanSync.ClearBind();

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
        }

        void OnApplicationQuit()
        {
            _ = PortfolioVivoxSupport.TryLeaveLobbyVoiceAsync();
            PortfolioNetworkShutdown.SafeTeardown(NetworkManager.Singleton);
        }
    }
}
