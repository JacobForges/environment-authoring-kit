#if UNITY_EDITOR
namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// When active, terrain/props place one tile per editor queue step with immediate Scene flush —
    /// required for demo timelapse and to avoid batch heightmap commits that stall or crash Unity.
    /// </summary>
    public static class CaveBuildLivePlacementPolicy
    {
        /// <summary>True when the operator should see each tile appear before the next is generated.</summary>
        public static bool Active
        {
            get
            {
                if (CaveBuildDemoAutoRecorder.IsRecording ||
                    CaveBuildDemoAutoRecorder.KeepSceneViewLiveForRecording)
                    return true;

                if (CaveBuildSessionConfig.HasFinalizedActive &&
                    CaveBuildSessionConfig.IsFloatingIslandsDemo())
                    return true;

                var settings = CaveBuildCursorSettings.LoadOrCreate();
                settings.LoadFromPrefs();
                return settings.showLiveScenePlacement &&
                       (CaveBuildLiveSceneFeedback.SessionActive ||
                        CaveBuildEditorResponsiveness.IsLongBuildActive);
            }
        }

        public static void CommitVisibleTerrainTile(UnityEngine.Terrain tile, string label)
        {
            if (tile == null)
                return;

            CaveBuildTerrainHeightmapMemory.FlushPendingTileHeightmap(tile);
            CaveBuildLiveSceneFeedback.NotifyTerrainTile(
                tile,
                label,
                ping: true,
                forceCamera: CaveBuildDemoAutoRecorder.IsRecording);
            CaveBuildLiveSceneFlushUtility.FlushWorldView(tile, forceRepaint: true);
        }
    }
}
#endif
