using UnityEngine;

namespace Hub.Competition
{
    public enum ListenSttMode
    {
        CloudOnly = 0,
        HybridLocalFirst = 1,
        HybridCloudPreferred = 2,
        LocalPreferred = 3,
    }

    public struct AgentListenProfile
    {
        public AgentIntelligenceTier IntelligenceTier;
        public ListenSttMode SttMode;
        public int CaptureClipSeconds;
        public bool LocalWhisperAvailable;
        public bool CloudAvailable;
        public int CommandsSucceeded;
        public int CheckpointsUntilNext;
        public string TierTitle;
        public string ProgressHint;
    }

    /// <summary>Listen / STT tier — offline Whisper from Rookie (3s clips); length grows when you Train Agent.</summary>
    public static class AgentListenProgression
    {
        public static AgentListenProfile Evaluate(string agentId)
        {
            var intel = AgentIntelligenceProgression.Evaluate(agentId);
            var mem = string.IsNullOrEmpty(agentId)
                ? new AgentPersonalityMemoryState()
                : AgentPersonalityMemory.LoadCommitted(agentId);

            var local = HubLocalWhisperSpeechToText.IsAvailable;
            var cloud = HubCloudSpeechToText.IsConfigured;
            var mode = ResolveMode(intel.Tier, local, cloud);
            var clip = ClipSecondsFor(intel.Tier);
            var checkpointsUntil = CheckpointsUntilNext(intel.TrainCheckpointCount, intel.Tier);

            return new AgentListenProfile
            {
                IntelligenceTier = intel.Tier,
                SttMode = mode,
                CaptureClipSeconds = clip,
                LocalWhisperAvailable = local,
                CloudAvailable = cloud,
                CommandsSucceeded = mem.listenCommandsSucceeded,
                CheckpointsUntilNext = checkpointsUntil,
                TierTitle = TitleFor(intel.Tier, mode, local, cloud),
                ProgressHint = HintFor(intel, mode, local, cloud, checkpointsUntil),
            };
        }

        public static void RecordSuccess(string agentId)
        {
            if (string.IsNullOrEmpty(agentId))
                return;

            var state = AgentPersonalityMemory.Load(agentId);
            state.listenCommandsSucceeded = Mathf.Max(0, state.listenCommandsSucceeded + 1);
            AgentPersonalityMemory.Save(agentId, state);
        }

        static ListenSttMode ResolveMode(AgentIntelligenceTier tier, bool local, bool cloud)
        {
            if (local)
                return ListenSttMode.LocalPreferred;

            if (cloud)
                return ListenSttMode.CloudOnly;

            return ListenSttMode.CloudOnly;
        }

        static int ClipSecondsFor(AgentIntelligenceTier tier) => tier switch
        {
            AgentIntelligenceTier.Rookie => 3,
            AgentIntelligenceTier.Apprentice => 5,
            AgentIntelligenceTier.Partner => 6,
            AgentIntelligenceTier.Guide => 8,
            AgentIntelligenceTier.Veteran => 10,
            AgentIntelligenceTier.Lead => 11,
            _ => 12,
        };

        static int CheckpointsUntilNext(int trainCount, AgentIntelligenceTier tier)
        {
            return AgentIntelligenceProgression.TrainsUntilNextTier(trainCount, tier);
        }

        static string TitleFor(AgentIntelligenceTier tier, ListenSttMode mode, bool local, bool cloud)
        {
            if (!local && !cloud)
                return "Listen — no STT";

            if (local && !cloud)
                return $"Listen · local Whisper ({tier})";

            if (!local && cloud)
                return $"Listen · cloud ({tier})";

            return mode switch
            {
                ListenSttMode.HybridCloudPreferred => $"Listen · hybrid cloud ({tier})",
                ListenSttMode.LocalPreferred => $"Listen · local ({tier})",
                _ => $"Listen · hybrid ({tier})",
            };
        }

        static string HintFor(
            AgentIntelligenceProfile intel,
            ListenSttMode mode,
            bool local,
            bool cloud,
            int checkpointsUntil)
        {
            if (!local && !cloud)
                return "Bundle Whisper (Hub → Competition → Bundle Whisper for Standalone), then rebuild.";

            if (local && intel.TrainCheckpointCount <= 0)
                return "Rookie — 3s Listen. Train Agent to tier up (speaking alone does not).";

            if (checkpointsUntil > 0)
                return $"Train Agent {checkpointsUntil} more time(s) for longer Listen clips.";

            if (local)
                return $"Offline Whisper — {ClipSecondsFor(intel.Tier)}s clips at {intel.TierTitle}.";

            return intel.ProgressHint;
        }
    }
}
