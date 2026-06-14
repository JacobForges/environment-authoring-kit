using System;

namespace Hub.Competition
{
    [Serializable]
    public sealed class AgentMissionDeliveryCheckpointEntry
    {
        public long utcUnix;
        public string utcIso;
        public int reviewAttempt;
        public string requestedPhrase;
        public string deliveredAnchorId;
        public string deliveredDisplayName;
        public string deliveredAliases;
        public string parentLocationId;
        public bool playerMarkedRight;
        public bool matchesRequested;
        public string actualItemDescription;
        public string previewImageFile;
        public string resultSummary;
    }
}
