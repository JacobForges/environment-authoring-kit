namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Prevents full queued pipeline completion from immediately starting another Cursor → rebuild cycle.
    /// </summary>
    public static class CaveBuildPipelineCompletion
    {
        static bool _fullPipelineJustFinished;
        static bool _postBuildCursorStartedForThisBuild;

        public static bool FullPipelineJustFinished => _fullPipelineJustFinished;

        public static void OnUserStartedBuild()
        {
            _fullPipelineJustFinished = false;
            _postBuildCursorStartedForThisBuild = false;
            LavaTubeCaveBuildPipeline.ResetResumeAfterAgentArmed();
        }

        public static void OnFullPipelineFinished()
        {
            _fullPipelineJustFinished = true;
            _postBuildCursorStartedForThisBuild = false;
        }

        public static bool ShouldSuppressPostBuildCursorInvoke() =>
            _postBuildCursorStartedForThisBuild;

        public static void MarkPostBuildCursorStarted() => _postBuildCursorStartedForThisBuild = true;

        /// <summary>Blocks Cursor agent from scheduling another Build Complete Cave after this run.</summary>
        public static bool ShouldBlockAutoRebuildAfterAgent() => _fullPipelineJustFinished;

        /// <summary>Allows post-agent auto-rebuild even when the prior run reached FinishQueued.</summary>
        public static void AllowAutoRebuildAfterAgent() => _fullPipelineJustFinished = false;
    }
}
