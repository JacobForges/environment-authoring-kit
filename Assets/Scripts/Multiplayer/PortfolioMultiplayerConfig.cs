namespace Hub.Multiplayer
{
    /// <summary>
    /// UGS multiplayer caps — one session size for cloud + listen-server (free Relay ~50 avg CCU).
    /// </summary>
    public static class PortfolioMultiplayerConfig
    {
        public const string LobbyName = HubGameBranding.OnlineLobbySlug + "-Listen";

        public const string DedicatedLobbyName = HubGameBranding.OnlineLobbySlug + "-Cloud";

        public const string MenuChatLobbyName = "HubVerifiedMenuChat";

        public const string DedicatedLobbyFlagKey = "dedicatedCloud";

        public const int MenuChatMaxPlayers = 32;

        /// <summary>Unity Relay free tier — ~50 average concurrent users per month (billing meter).</summary>
        public const int RelayFreeTierAvgCcu = 50;

        /// <summary>Hard ceiling for one Relay allocation — stay under free-tier average in production.</summary>
        public const int RelayAllocationMaxConnections = 49;

        /// <summary>Max humans per session (cloud dedicated or listen-server host).</summary>
        public const int MaxPlayersSession = 24;

        public const int MaxPlayersDedicatedCloud = MaxPlayersSession;
        public const int MaxPlayersListenHost = MaxPlayersSession;
        public const int MaxPlayersMacHost = MaxPlayersSession;

        public static int DedicatedServerRelayConnections =>
            UnityEngine.Mathf.Min(MaxPlayersSession, RelayAllocationMaxConnections);

        public static int ListenServerRelayConnections =>
            UnityEngine.Mathf.Min(MaxPlayersSession - 1, RelayAllocationMaxConnections - 1);

        /// <summary>Shown in menu copy.</summary>
        public static int MaxPlayersAdvertised => MaxPlayersSession;
        public static int MaxPlayersAdvertisedOnline => MaxPlayersSession;

        public const int MaxPlayersRelayFreeTier = RelayFreeTierAvgCcu;
        public static int RelayMaxConnections => ListenServerRelayConnections;

        public const string RelayJoinCodeLobbyKey = "relayJoinCode";
        public const string WorldSeedLobbyKey = "worldSeed";
        public const string ContentVersionLobbyKey = "contentVersion";
    }
}
