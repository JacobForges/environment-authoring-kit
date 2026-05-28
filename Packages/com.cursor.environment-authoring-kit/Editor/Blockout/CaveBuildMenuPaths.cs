namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Single place for Environment Kit menu paths — keeps the Window menu short.</summary>
    public static class CaveBuildMenuPaths
    {
        public const string Root = "Window/Environment Kit/";
        public const string Hub = Root + "Hub";
        public const string BuildComplete = Root + "Build Complete Cave Level (Active Scene)";
        public const string BuildSurfaceOnly = Root + "Build Surface World Only (Active Scene)";
        public const string TerrainGrader = Root + "Terrain Build Grader";
        public const string TerrainRegradeOnly = Root + "Terrain Build/Re-grade Only";
        public const string TerrainInvokeCursor = Root + "Terrain Build/Invoke Cursor Agent";
        public const string TerrainBeginWorkflow = Root + "Terrain Build/Run Full Terrain Cursor Workflow";
        public const string BuildCaveOnly = Root + "Build Cave Only — Align to Surface (Active Scene)";
        public const string RebuildMainScene = Root + "Rebuild Complete Cave (MainScene)";
        public const string CaveBuild = Root + "Cave Build/";
        public const string Advanced = CaveBuild + "Advanced/";
        public const string Cursor = CaveBuild + "Cursor/";
        public const string RepairOnly = CaveBuild + "Repair Only/";
        public const string Diagnostics = CaveBuild + "Diagnostics/";
        public const string MacBookAirBudget = CaveBuild + "Use MacBook Air (16GB) GPU Budget";
        public const string PlayMode = CaveBuild + "Play Mode/";
    }
}
