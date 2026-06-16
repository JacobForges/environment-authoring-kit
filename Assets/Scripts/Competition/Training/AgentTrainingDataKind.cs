namespace Hub.Competition
{
    /// <summary>Dataset file names and kind labels for per-agent training data.</summary>
    public static class AgentTrainingDataKind
    {
        public const string GameplayEpisodePrefix = "episode_";
        public const string ChatFile = "chat.jsonl";
        public const string ReasoningJournal = "reasoning_cycles.jsonl";
        public const string ReasoningTrainExport = "reasoning_train.jsonl";
        public const string CatalogFile = "training_catalog.json";

        public const string Gameplay = "gameplay";
        public const string Chat = "chat";
        public const string Reasoning = "reasoning";
        public const string Mission = "mission";
        public const string Research = "research";
        public const string OwnerVoice = "owner_voice";

        public const string OwnerVoiceTrainFile = "owner_voice_train.jsonl";
        public const string OwnerVoiceProfileFile = "owner_voice.json";

        public const string CustomCommands = "custom_commands";
        public const string CustomCommandsFile = "custom_commands.json";
        public const string CustomCommandsTrainFile = "custom_commands_train.jsonl";
    }
}
