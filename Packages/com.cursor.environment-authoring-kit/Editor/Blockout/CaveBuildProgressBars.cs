#if UNITY_EDITOR
namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Central gate — no floating Environment Kit progress bar during Hub / FullWorld builds.</summary>
    public static class CaveBuildProgressBars
    {
        public static bool AllowModalProgressBar =>
            !CaveBuildRunStatusPublisher.HasActiveSession &&
            !CaveBuildEditorResponsiveness.IsLongBuildActive &&
            !EnvironmentKitHubWindow.IsOpen;
    }
}
#endif
